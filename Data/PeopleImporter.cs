using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Models;
using UniSphere.Api.Services;

namespace UniSphere.Api.Data;

/// <summary>
/// On-demand importer for people CSVs: students-*.csv and doctors-*.csv.
/// The university comes from the FILE NAME (same slug rule CatalogImporter
/// uses), never from a CSV cell. People are upserted by Email
/// (case-insensitive) inside that university. Runs only when --import-people
/// is passed on the command line (see Program.cs); never at startup.
///
/// Safety properties:
///  - The initial password comes from the user-secret
///    Import:InitialPassword when set (callers must never log it); when it
///    is missing or empty, new accounts are bootstrapped with the built-in
///    DefaultInitialPassword and only the COUNT of such users is reported
///    — the value and its hashes are never logged anywhere.
///  - Existing users are never demoted or re-passworded: the update path
///    touches contact/profile fields only, and an email that exists in a
///    DIFFERENT university is skipped so uniqueness and isolation hold.
///  - Passed enrollments carry GradePoints 2.0 / letter "C": the app marks a
///    course Passed at GradePoints >= 2.0 (UniversityController.CourseStatus,
///    DoctorController.CourseStudents), so this is the honest pass boundary.
///  - Instructors never replace an existing assignment for a term.
///  - dry-run: counts only, not a single SaveChanges is issued.
/// </summary>
public static class PeopleImporter
{
    public const string InstructorTerm = "Fall 2025";
    public const string PassedGradeLetter = "C";
    public const double PassedGradePoints = 2.0;
    private const string CurrentSemester = "Current";

    /// <summary>Bootstrap password given to NEW accounts only when the
    /// user-secret Import:InitialPassword is not set. The value itself is
    /// never logged; only the number of users that received it is.</summary>
    public const string DefaultInitialPassword = "Password123!";

    private static readonly Regex EmailRegex =
        new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    public static PeopleImportReport ImportAll(
        AppDbContext db,
        string contentRoot,
        string initialPassword,
        bool dryRun = false,
        ILogger? logger = null)
    {
        // Password policy: the user-secret Import:InitialPassword overrides
        // everything when set. When it is missing/empty, NEW accounts are
        // bootstrapped with the built-in default; only the count of those
        // users is reported (the value and its hashes are never logged).
        var useDefaultPassword = string.IsNullOrWhiteSpace(initialPassword);
        var password = useDefaultPassword ? DefaultInitialPassword : initialPassword;

        var report = new PeopleImportReport();
        var dataDirectory = Path.Combine(contentRoot, "Data");
        if (!Directory.Exists(dataDirectory))
        {
            report.Reasons.Add("Data directory not found: " + dataDirectory);
            return report;
        }

        var files = Directory.GetFiles(dataDirectory, "*.csv")
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                return name.StartsWith("students-", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("doctors-", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0)
        {
            report.Reasons.Add("No students-*.csv or doctors-*.csv files found.");
            return report;
        }

        // ONE decision map for both run modes: each course's current occupant
        // is the doctor-module CourseAssignment (primary), or, failing that,
        // the legacy CourseInstructors row of the run's term. Occupants are
        // stored by normalized email because dry-run doctors are planned
        // users without an id; email identity makes dry-run decisions take
        // exactly the same branch as a real run, so the counters match.
        // Cross-university rows can never collide: courses are keyed by id
        // and a run's course dictionary is university-scoped.
        var occupancy = new Dictionary<int, (string Email, string Name)>();
        foreach (var x in db.CourseAssignments.AsNoTracking()
            .Join(db.Users.AsNoTracking(), a => a.DoctorUserId, u => u.Id,
                (a, u) => new { a.CourseId, u.Email, u.FullName }))
            occupancy[x.CourseId] = (x.Email.ToLowerInvariant(), x.FullName);
        foreach (var x in db.CourseInstructors.AsNoTracking()
            .Where(ci => ci.Term == InstructorTerm)
            .Join(db.Users.AsNoTracking(), ci => ci.DoctorUserId, u => u.Id,
                (ci, u) => new { ci.CourseId, u.Email, u.FullName }))
            if (!occupancy.ContainsKey(x.CourseId))
                occupancy[x.CourseId] = (x.Email.ToLowerInvariant(), x.FullName);

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var kind = fileName.StartsWith("doctors-", StringComparison.OrdinalIgnoreCase)
                ? "doctor" : "student";
            var slug = fileName[(kind == "doctor" ? "doctors-".Length : "students-".Length)..]
                .Replace(".csv", "", StringComparison.OrdinalIgnoreCase);
            var universityName = CultureInfo.InvariantCulture.TextInfo
                .ToTitleCase(slug.Replace('-', ' ').Trim());
            if (string.IsNullOrWhiteSpace(universityName))
            {
                report.Reasons.Add($"{fileName}: could not derive a university name from the file name.");
                continue;
            }

            var university = db.Universities.FirstOrDefault(u => u.Name == universityName);
            if (university is null)
            {
                university = new University { Name = universityName };
                db.Universities.Add(university);
                if (!dryRun) db.SaveChanges();
            }

            var fileStats = new PeopleFileStats(fileName, universityName);
            report.Files.Add(fileStats);

            var rows = ParsePeopleFile(file);
            if (rows.Count == 0)
            {
                fileStats.InvalidRows++;
                report.Reasons.Add($"{fileName}: no data rows found.");
                continue;
            }

            var courses = db.Courses
                .Where(c => c.UniversityId == university.Id)
                .ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

            // Pending rows this run added to the context but has not saved yet
            // (needed so a duplicate code inside one file cannot slip past the
            // unique indexes and blow up SaveChanges).
            var pendingEnrollments = new HashSet<(User User, Course Course)>();
            var pendingLinks = new HashSet<(User Doctor, Course Course)>();
            // Emails planned earlier in this run (dry-run users are not saved,
            // so the DB query cannot see them; the map keeps later rows in the
            // same file on the update path instead of double-counting).
            var plannedByEmail = new Dictionary<string, User>(StringComparer.Ordinal);

            foreach (var (lineNumber, row) in rows)
            {
                var get = (string key) => (row.TryGetValue(key, out var v) ? v : "").Trim();
                var email = get("Email").ToLowerInvariant();

                // ---- row validation: one bad row never aborts the run ----
                var invalidReason = kind == "student"
                    ? ValidateStudentRow(get, email)
                    : ValidateDoctorRow(get, email);
                if (invalidReason is not null)
                {
                    fileStats.InvalidRows++;
                    report.Reasons.Add($"{fileName} line {lineNumber}: {invalidReason}");
                    continue;
                }

                // ---- identity upsert by lower(email) inside the university ----
                User user;
                var inDb = db.Users.FirstOrDefault(u => u.Email.ToLower() == email);
                if (inDb is not null)
                {
                    if (inDb.UniversityId != university.Id)
                    {
                        report.Reasons.Add(
                            $"{fileName} line {lineNumber}: email already exists in a different university — row skipped (identity protected).");
                        fileStats.SkippedRows++;
                        continue;
                    }
                    user = inDb;
                    if (ApplyProfileUpdates(user, kind, get)) fileStats.UpdatedUsers++;
                    else fileStats.ExistingUsers++;
                }
                else if (plannedByEmail.TryGetValue(email, out var planned))
                {
                    user = planned;
                    if (ApplyProfileUpdates(user, kind, get)) fileStats.UpdatedUsers++;
                    else fileStats.ExistingUsers++;
                }
                else
                {
                    user = BuildUser(kind, get, email, university.Id, password);
                    plannedByEmail[email] = user;
                    if (!dryRun)
                    {
                        db.Users.Add(user);
                        db.SaveChanges();
                        AddWelcomeBundle(db, user, kind);
                    }
                    if (useDefaultPassword) fileStats.UsersWithDefaultPassword++;
                    fileStats.CreatedUsers++;
                }

                // ---- course links ----
                var codeLists = kind == "student"
                    ? new[]
                    {
                        (get("CurrentCourses"), false),
                        (get("PassedCourses"), true)
                    }
                    : new[] { (get("CourseCodes"), true) };

                foreach (var (codesValue, passed) in codeLists)
                {
                    foreach (var token in SplitCodes(codesValue))
                    {
                        if (!courses.TryGetValue(token, out var course))
                        {
                            report.Reasons.Add(
                                $"{fileName} line {lineNumber}: unknown course code '{token}' for {universityName} — link skipped.");
                            fileStats.UnknownCourseCodes++;
                            continue;
                        }

                        if (kind == "doctor")
                            UpsertInstructorLink(db, report, fileStats, fileName, lineNumber,
                                user, course, courses, pendingLinks, occupancy, dryRun);
                        else
                            UpsertEnrollment(db, report, fileStats, fileName, lineNumber,
                                user, course, passed, pendingEnrollments, dryRun);
                    }
                }
            }

            if (!dryRun) db.SaveChanges();
            if (fileStats.UsersWithDefaultPassword > 0)
                report.Reasons.Add(
                    $"{fileName}: {fileStats.UsersWithDefaultPassword} new user(s) bootstrapped with the default password (value not logged).");
            logger?.LogInformation(
                "People {Mode} done for {File}: created {Created}, updated {Updated}, existing {Existing}, invalid {Invalid}",
                dryRun ? "dry-run" : "import",
                fileName,
                fileStats.CreatedUsers, fileStats.UpdatedUsers, fileStats.ExistingUsers, fileStats.InvalidRows);
        }

        return report;
    }

    // ------------------------------------------------------------------
    // Building / updating users
    // ------------------------------------------------------------------

    private static string BuildFullName(string first, string middle, string last)
    {
        var parts = new[] { first, middle, last }.Where(p => p.Length > 0);
        return string.Join(" ", parts);
    }

    private static User BuildUser(string kind, Func<string, string> get,
        string email, int universityId, string password)
    {
        var first = get("FirstName");
        var middle = get("MiddleName");
        var last = get("LastName");
        var departmentName = get("DepartmentName");

        var user = new User
        {
            FirstName = first,
            MiddleName = middle.Length == 0 ? null : middle,
            LastName = last,
            FullName = BuildFullName(first, middle, last),
            Email = email,
            PhoneNumber = get("Phone"),
            Address = get("Address"),
            Gender = get("Gender"),
            Age = int.Parse(get("Age"), CultureInfo.InvariantCulture),
            Department = departmentName,
            Major = departmentName,
            UniversityId = universityId,
            Role = kind == "doctor" ? UserRole.Doctor : UserRole.Student,
            IsActive = true,
            IsDeleted = false,
            CreatedAtUtc = DateTime.UtcNow,
            PasswordHash = PasswordService.Hash(password),
            AcademicYear = kind == "student"
                ? int.Parse(get("AcademicYear"), CultureInfo.InvariantCulture)
                : null,
            Specialization = kind == "doctor"
                ? (get("Specialization").Length == 0 ? null : get("Specialization"))
                : null
        };
        return user;
    }

    /// <summary>Update path: contact/profile fields only. Names (first,
    /// middle, last, FullName) are identity data and are NEVER rewritten, so
    /// an account matched by email keeps the names it was registered with;
    /// CSV rows with a repeated email that carry different names just touch
    /// the contact fields. PasswordHash, Role, IsActive, IsDeleted,
    /// UniversityId and created fields are never touched either, so
    /// pre-existing accounts stay intact.</summary>
    private static bool ApplyProfileUpdates(User user, string kind, Func<string, string> get)
    {
        var departmentName = get("DepartmentName");

        var changed = false;

        if (!string.Equals(user.PhoneNumber, get("Phone"), StringComparison.Ordinal)) { user.PhoneNumber = get("Phone"); changed = true; }
        if (!string.Equals(user.Address, get("Address"), StringComparison.Ordinal)) { user.Address = get("Address"); changed = true; }
        if (!string.Equals(user.Gender, get("Gender"), StringComparison.Ordinal)) { user.Gender = get("Gender"); changed = true; }
        var newAge = int.Parse(get("Age"), CultureInfo.InvariantCulture);
        if (user.Age != newAge) { user.Age = newAge; changed = true; }
        if (!string.Equals(user.Department, departmentName, StringComparison.Ordinal)) { user.Department = departmentName; changed = true; }
        if (!string.Equals(user.Major, departmentName, StringComparison.Ordinal)) { user.Major = departmentName; changed = true; }
        if (kind == "student")
        {
            var newYear = int.Parse(get("AcademicYear"), CultureInfo.InvariantCulture);
            if (user.AcademicYear != newYear) { user.AcademicYear = newYear; changed = true; }
        }
        else
        {
            var newSpec = get("Specialization");
            var spec = newSpec.Length == 0 ? null : newSpec;
            if (!string.Equals(user.Specialization, spec, StringComparison.Ordinal)) { user.Specialization = spec; changed = true; }
        }
        return changed;
    }

    /// <summary>Exactly what registration gives a new account so dashboards
    /// are not blank: welcome notification + activity, and a current-semester
    /// record for students only (register creates it for Student/Graduate).</summary>
    private static void AddWelcomeBundle(AppDbContext db, User user, string kind)
    {
        if (kind == "student")
            db.SemesterRecords.Add(new SemesterRecord
            { UserId = user.Id, Label = "Current semester", IsCurrent = true });
        db.Notifications.Add(new Notification
        { UserId = user.Id, Title = "Welcome to UniSphere 360+", Body = "Your account is ready. Explore your dashboard." });
        db.Activities.Add(new ActivityItem
        { UserId = user.Id, Kind = "announcement", Message = "Welcome to UniSphere 360+" });
    }

    private static IEnumerable<string> SplitCodes(string value) =>
        value.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => v.Length > 0);

    // ------------------------------------------------------------------
    // Links
    // ------------------------------------------------------------------

    private static void UpsertEnrollment(
        AppDbContext db, PeopleImportReport report, PeopleFileStats stats,
        string fileName, int lineNumber, User user, Course course, bool passed,
        HashSet<(User, Course)> pending, bool dryRun)
    {
        if (pending.Contains((user, course)) ||
            (!dryRun && db.Enrollments.Any(e => e.UserId == user.Id && e.CourseId == course.Id)))
        {
            report.Reasons.Add(
                $"{fileName} line {lineNumber}: '{course.Code}' already enrolled for {user.Email} — duplicate link skipped.");
            stats.EnrollmentsSkippedExisting++;
            return;
        }

        var enrollment = new Enrollment
        {
            UserId = user.Id,
            CourseId = course.Id,
            Semester = CurrentSemester,
            GradeLetter = passed ? PassedGradeLetter : "",
            GradePoints = passed ? PassedGradePoints : 0,
            AttendancePercent = 100,
            Status = "Good"
        };
        if (!dryRun) db.Enrollments.Add(enrollment);
        pending.Add((user, course));
        stats.EnrollmentsCreated++;
    }

    /// <summary>Doctor links are created exactly the way the integrated
    /// doctor module and DbSeeder's dual-write expect: a doctor-module
    /// CourseAssignment (no term, unique per doctor+course, AssignedByUserId
    /// = the doctor's own id) plus the legacy per-term CourseInstructors row
    /// the enrollment endpoints still read. A course held by a different
    /// doctor (assignment first, then the legacy row of
    /// <see cref="InstructorTerm"/>) is never replaced — the row is skipped
    /// and reported. The occupancy map is in-run, so a dry run decides every
    /// course exactly like a real run and reports identical counts.</summary>
    private static void UpsertInstructorLink(
        AppDbContext db, PeopleImportReport report, PeopleFileStats stats,
        string fileName, int lineNumber, User doctor, Course course,
        Dictionary<string, Course> courses, HashSet<(User, Course)> pending,
        Dictionary<int, (string Email, string Name)> occupancy, bool dryRun)
    {
        var identity = doctor.Email.ToLowerInvariant();
        occupancy.TryGetValue(course.Id, out var held);

        // A different doctor holds the course: never replaced, only reported.
        if (!string.IsNullOrEmpty(held.Email) && held.Email != identity)
        {
            report.Reasons.Add(
                $"{fileName} line {lineNumber}: '{course.Code}' already has instructor {held.Name} — NOT replaced.");
            stats.LinksSkippedDifferentInstructor++;
            return;
        }

        // The same doctor's course once more (this run, or in the database
        // for a real run): duplicate link.
        if (pending.Contains((doctor, course)) ||
            held.Email == identity ||
            (!dryRun && (db.CourseInstructors.Any(ci =>
                ci.CourseId == course.Id && ci.DoctorUserId == doctor.Id && ci.Term == InstructorTerm) ||
                db.CourseAssignments.Any(a =>
                    a.CourseId == course.Id && a.DoctorUserId == doctor.Id))))
        {
            report.Reasons.Add(
                $"{fileName} line {lineNumber}: '{course.Code}' is already assigned to {doctor.FullName} — duplicate link skipped.");
            stats.LinksSkippedSelf++;
            return;
        }

        if (!dryRun)
        {
            // Doctor-module course assignment (what the module reads).
            db.CourseAssignments.Add(new CourseAssignment
            {
                DoctorUserId = doctor.Id,
                CourseId = course.Id,
                AssignedByUserId = doctor.Id,
                AssignedAtUtc = DateTime.UtcNow,
                DisplayTitle = null
            });
            // Legacy per-term row (what the pre-module endpoints still read);
            // written together so both views stay in sync, like DbSeeder.
            db.CourseInstructors.Add(new CourseInstructor
            {
                CourseId = course.Id,
                DoctorUserId = doctor.Id,
                Term = InstructorTerm,
                CreatedAtUtc = DateTime.UtcNow,
                DisplayTitle = null
            });
        }
        occupancy[course.Id] = (identity, doctor.FullName);
        pending.Add((doctor, course));
        stats.LinksCreated++;
    }

    // ------------------------------------------------------------------
    // Row validation (returns a reason or null)
    // ------------------------------------------------------------------

    private static string? ValidateStudentRow(Func<string, string> get, string email)
    {
        if (email.Length == 0) return "missing Email.";
        if (!EmailRegex.IsMatch(email)) return $"bad email '{email}'.";
        if (get("FirstName").Length == 0 || get("LastName").Length == 0) return "empty FirstName/LastName.";
        if (get("Gender").Length == 0) return "empty Gender.";
        if (!int.TryParse(get("Age"), NumberStyles.None, CultureInfo.InvariantCulture, out _)) return "invalid Age.";
        if (get("DepartmentName").Length == 0) return "empty DepartmentName.";
        if (!int.TryParse(get("AcademicYear"), NumberStyles.None, CultureInfo.InvariantCulture, out _)) return "invalid AcademicYear.";
        return null;
    }

    private static string? ValidateDoctorRow(Func<string, string> get, string email)
    {
        if (email.Length == 0) return "missing Email.";
        if (!EmailRegex.IsMatch(email)) return $"bad email '{email}'.";
        if (get("FirstName").Length == 0 || get("LastName").Length == 0) return "empty FirstName/LastName.";
        if (get("Gender").Length == 0) return "empty Gender.";
        if (!int.TryParse(get("Age"), NumberStyles.None, CultureInfo.InvariantCulture, out _)) return "invalid Age.";
        if (get("DepartmentName").Length == 0) return "empty DepartmentName.";
        if (get("CourseCodes").Length == 0) return "empty CourseCodes.";
        return null;
    }

    // ------------------------------------------------------------------
    // CSV parsing — private copy of CatalogImporter's parser so the catalog
    // importer file is never touched by this feature. Identical semantics:
    // UTF-8 with/without BOM, RFC4180 quoted fields with escaped quotes and
    // commas inside quotes, every row mapped through the header names.
    // ------------------------------------------------------------------

    private static List<(int Line, Dictionary<string, string> Row)> ParsePeopleFile(string path)
    {
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(
            stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
            lines.Add(line);
        if (lines.Count == 0) return [];

        var headers = ParseCsvLine(lines[0].TrimStart('\uFEFF'))
            .Select(h => h.Trim())
            .ToList();
        var rows = new List<(int, Dictionary<string, string>)>();

        for (var i = 1; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var values = ParseCsvLine(lines[i]);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var c = 0; c < headers.Count; c++)
                row[headers[c]] = c < values.Count ? values[c] : "";
            rows.Add((i + 1, row));
        }
        return rows;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else inQuotes = false;
                }
                else current.Append(ch);
            }
            else if (ch == '"') inQuotes = true;
            else if (ch == ',')
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else current.Append(ch);
        }
        values.Add(current.ToString());
        return values;
    }
}

/// <summary>Counts from one import run. Reasons carry file + line + why for
/// every skipped row/link so nothing is silently dropped.</summary>
public sealed class PeopleImportReport
{
    public List<PeopleFileStats> Files { get; } = [];
    public List<string> Reasons { get; } = [];

    public int CreatedUsers => Files.Sum(f => f.CreatedUsers);
    public int UpdatedUsers => Files.Sum(f => f.UpdatedUsers);
    public int ExistingUsers => Files.Sum(f => f.ExistingUsers);
    public int SkippedRows => Files.Sum(f => f.SkippedRows);
    public int InvalidRows => Files.Sum(f => f.InvalidRows);
    public int EnrollmentsCreated => Files.Sum(f => f.EnrollmentsCreated);
    public int EnrollmentsSkippedExisting => Files.Sum(f => f.EnrollmentsSkippedExisting);
    public int UnknownCourseCodes => Files.Sum(f => f.UnknownCourseCodes);
    public int LinksCreated => Files.Sum(f => f.LinksCreated);
    public int LinksSkippedSelf => Files.Sum(f => f.LinksSkippedSelf);
    public int LinksSkippedDifferentInstructor => Files.Sum(f => f.LinksSkippedDifferentInstructor);
    public int UsersWithDefaultPassword => Files.Sum(f => f.UsersWithDefaultPassword);
}

public sealed class PeopleFileStats(string file, string university)
{
    public string File { get; } = file;
    public string University { get; } = university;
    public int CreatedUsers { get; set; }
    public int UpdatedUsers { get; set; }
    public int ExistingUsers { get; set; }
    public int SkippedRows { get; set; }
    public int InvalidRows { get; set; }
    public int EnrollmentsCreated { get; set; }
    public int EnrollmentsSkippedExisting { get; set; }
    public int UnknownCourseCodes { get; set; }
    public int LinksCreated { get; set; }
    public int LinksSkippedSelf { get; set; }
    public int LinksSkippedDifferentInstructor { get; set; }
    public int UsersWithDefaultPassword { get; set; }
}
