using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Models;
using UniSphere.Api.Services;

namespace UniSphere.Api.Data;

public static class DbSeeder
{
    public const string DemoStudentOldEmail = "gehad@unisphere.edu";
    public const string DemoStudentEmail = "mazenmohamed@unisphere.edu";

    public static void Seed(AppDbContext db)
    {
        // In-place rename for existing databases: the demo student was
        // created as gehad@unisphere.edu in older seeds. Update that row
        // (same Id, so grades/attendance/plans/notifications stay linked)
        // instead of inserting a duplicate.
        var legacy = db.Users.FirstOrDefault(u => u.Email == DemoStudentOldEmail);
        if (legacy is not null && !db.Users.Any(u => u.Email == DemoStudentEmail))
        {
            legacy.Email = DemoStudentEmail;
            legacy.FirstName = "Mazen";
            legacy.LastName = "Mohamed";
            legacy.FullName = "Mazen Mohamed";
            db.SaveChanges();
        }

        // The graduation ceremony event was added after many databases already
        // had users (the seeder early-returns once users exist), so refresh it
        // idempotently here instead of only inside the fresh-seed path.
        // Only patch LEGACY databases (ones that already have events): the
        // fresh-seed path below inserts the ceremony with the full event list,
        // so running this too would double-count it.
        if (db.Events.Any() && !db.Events.Any(e => e.Title == "Graduation Ceremony 2026"))
        {
            db.Events.Add(new EventItem
            {
                Title = "Graduation Ceremony 2026",
                Location = "Grand Hall, Main Campus",
                StartsAtUtc = DateTime.UtcNow.Date.AddDays(180).AddHours(9),
                Description = "Annual graduation ceremony for approved graduates. Seats are limited."
            });
            db.SaveChanges();
        }

        // Legacy-database patch: earlier seeds created the demo graduate
        // without a UniversityId, which left /api/university/me permanently
        // 404 for that account. Link it to Cairo University when empty.
        var legacyGraduate = db.Users.FirstOrDefault(u => u.Email == "graduate@unisphere.edu");
        if (legacyGraduate is not null && legacyGraduate.UniversityId is null)
        {
            var cairo = db.Universities.FirstOrDefault(u => u.Name == "Cairo University");
            if (cairo is not null)
            {
                legacyGraduate.UniversityId = cairo.Id;
                db.SaveChanges();
            }
        }

        // Roles (AppDbContext.OnModelCreating seeds these via HasData on
        // EnsureCreated, so only insert when the table is still empty —
        // otherwise a fresh database crashes with UNIQUE constraint on Roles.Id).
        if (!db.Roles.Any())
        {
            db.Roles.AddRange(
                new RoleItem { Id = 1, Name = "Student" },
                new RoleItem { Id = 2, Name = "Graduate" },
                new RoleItem { Id = 3, Name = "Doctor" },
                new RoleItem { Id = 4, Name = "Admin" }
            );
        }

        // Universities (seeded before users/courses so ids are known).
        if (!db.Universities.Any())
        {
            db.Universities.AddRange(
                new University { Name = "El Shorouk Academy" },
                new University { Name = "Helwan University" },
                new University { Name = "Cairo University" },
                new University { Name = "Ain Shams University" },
                new University { Name = "Other" }
            );
            db.SaveChanges();
        }
        var universities = db.Universities.ToDictionary(u => u.Name);
        var elShorouk = EnsureUniversity(db, "El Shorouk Academy");
        var helwan = EnsureUniversity(db, "Helwan University");
        db.SaveChanges();

        if (db.Users.Any())
        {
            EnsureExistingInstallation(db, elShorouk, helwan);
            // Demo jobs are idempotent (Source == "Demo" guard), so also run
            // them for databases created before the career platform existed.
            SeedDemoJobs(db);
            if (legacyGraduate is not null)
                SeedGraduateCareerDemo(db, legacyGraduate.Id);
            return;
        }

        // Demo accounts
        var user = new User
        {
            FirstName = "Mazen",
            LastName = "Mohamed",
            FullName = "Mazen Mohamed",
            PhoneNumber = "01012345678",
            Email = "mazenmohamed@unisphere.edu",
            Gender = "Male",
            Age = 20,
            Major = "Computer Engineering",
            Department = "Computer Engineering",
            AcademicYear = 3,
            UniversityId = elShorouk.Id,
            Role = UserRole.Student,
            PasswordHash = PasswordService.Hash("Password123!")
        };
        var admin = new User
        {
            FirstName = "UniSphere",
            LastName = "Admin",
            FullName = "UniSphere Admin",
            PhoneNumber = "01000000001",
            Email = "admin@unisphere.edu",
            Gender = "Male",
            Age = 35,
            Major = "Administration",
            Department = "Administration",
            Role = UserRole.Admin,
            PasswordHash = PasswordService.Hash("Admin123!")
        };
        var graduate = new User
        {
            FirstName = "Omar",
            LastName = "Hassan",
            FullName = "Omar Hassan",
            PhoneNumber = "01112345678",
            Email = "graduate@unisphere.edu",
            Gender = "Male",
            Age = 24,
            Major = "Computer Engineering",
            Department = "Computer Science",
            GraduatedFrom = "Cairo University, Faculty of Engineering",
            GraduationYear = 2023,
            UniversityId = universities["Cairo University"].Id,
            Role = UserRole.Graduate,
            PasswordHash = PasswordService.Hash("Graduate123!")
        };
        var doctor = new User
        {
            FirstName = "Khaled",
            LastName = "Mansour",
            FullName = "Dr. Khaled Mansour",
            PhoneNumber = "01212345678",
            Email = "doctor@unisphere.edu",
            Address = "12 Faculty Road, El Shorouk, Cairo",
            Gender = "Male",
            Age = 45,
            Major = "Computer Engineering",
            Department = "Computer Science",
            Specialization = "Artificial Intelligence",
            UniversityId = elShorouk.Id,
            Role = UserRole.Doctor,
            PasswordHash = PasswordService.Hash("Doctor123!")
        };
        var helwanStudent = new User
        {
            FirstName = "Salma",
            LastName = "Adel",
            FullName = "Salma Adel",
            PhoneNumber = "01098765432",
            Email = "helwan.student@unisphere.edu",
            Gender = "Female",
            Age = 20,
            Major = "Computer Science",
            Department = "Computer Science",
            AcademicYear = 3,
            UniversityId = helwan.Id,
            Role = UserRole.Student,
            PasswordHash = PasswordService.Hash("Password123!")
        };
        db.Users.AddRange(user, admin, graduate, doctor, helwanStudent);

        // Modules
        db.Modules.AddRange(
            new ModuleItem { Key = "academic-advisor", Title = "Academic Advisor", Subtitle = "Plan - Analyze - Improve", Icon = "school", BackgroundColor = "#EDEBFF", AccentColor = "#4A3FD0", SortOrder = 1 },
            new ModuleItem { Key = "career-assistant", Title = "Career Assistant", Subtitle = "CV - Jobs - Skills", Icon = "work", BackgroundColor = "#E6F8EE", AccentColor = "#1E9E5E", SortOrder = 2 },
            new ModuleItem { Key = "research-hub", Title = "Research Hub", Subtitle = "Search - Summarize - Ask", Icon = "search", BackgroundColor = "#FFE8E6", AccentColor = "#E0524A", SortOrder = 3 },
            new ModuleItem { Key = "student-services", Title = "Student Services", Subtitle = "FAQs - Events - Support", Icon = "account_balance", BackgroundColor = "#E5F0FF", AccentColor = "#2F7BE0", SortOrder = 4 },
            new ModuleItem { Key = "analytics-admin", Title = "Analytics & Admin", Subtitle = "Insights - Reports - Trends", Icon = "bar_chart", BackgroundColor = "#FFF4D9", AccentColor = "#E09A1A", SortOrder = 5 }
        );

        // Events
        var now = DateTime.UtcNow.Date;
        db.Events.AddRange(
            new EventItem { Title = "AI Workshop", Location = "Faculty of Engineering", StartsAtUtc = now.AddDays(3).AddHours(10), Description = "Hands-on workshop on machine learning basics.", UniversityId = elShorouk.Id },
            new EventItem { Title = "Tech Club Meeting", Location = "Student Activities Center", StartsAtUtc = now.AddDays(5).AddHours(16), Description = "Weekly meeting of the Tech Club.", UniversityId = elShorouk.Id },
            new EventItem { Title = "Career Fair", Location = "Main Campus", StartsAtUtc = now.AddDays(10).AddHours(9), Description = "Meet employers and apply for internships.", UniversityId = elShorouk.Id },
            new EventItem { Title = "Graduation Ceremony 2026", Location = "Grand Hall, Main Campus", StartsAtUtc = now.AddDays(180).AddHours(9), Description = "Annual graduation ceremony for approved graduates. Seats are limited." },
            new EventItem { Title = "Helwan Tech Day", Location = "Faculty of Computers and Artificial Intelligence", StartsAtUtc = now.AddDays(7).AddHours(11), Description = "Student projects, research demos and employer talks.", UniversityId = helwan.Id }
        );

        // Courses
        db.Courses.AddRange(
            new Course { Code = "CS350", Title = "Machine Learning", Description = "Supervised and unsupervised learning.", Track = "AI", Credits = 3 },
            new Course { Code = "CS351", Title = "Deep Learning", Description = "Neural networks, CNNs and transformers.", Track = "AI", Credits = 3 },
            new Course { Code = "CS362", Title = "Natural Language Processing", Description = "Text processing, LLMs and RAG.", Track = "AI", Credits = 3 },
            new Course { Code = "CS340", Title = "Mobile App Development", Description = "Building apps with Flutter.", Track = "Software", Credits = 3 },
            new Course { Code = "CS240", Title = "Web Development", Description = "HTML, CSS, JavaScript and backend basics.", Track = "Software", Credits = 3 },
            new Course { Code = "CS330", Title = "Database Systems", Description = "SQL, PostgreSQL and data modeling.", Track = "CS", Credits = 3 },
            new Course { Code = "CS430", Title = "Advanced Databases", Description = "Query optimization, NoSQL and distributed data.", Track = "CS", Credits = 3 },
            new Course { Code = "CS210", Title = "Data Structures & Algorithms", Description = "Lists, trees, graphs and complexity.", Track = "CS", Credits = 3 },
            new Course { Code = "CS120", Title = "Discrete Mathematics", Description = "Logic, sets, combinatorics and graphs.", Track = "CS", Credits = 3 },
            new Course { Code = "CS320", Title = "Computer Networks", Description = "TCP/IP, routing and network design.", Track = "IT", Credits = 3 },
            new Course { Code = "CS420", Title = "Cloud Computing", Description = "Virtualization, containers and cloud services.", Track = "IT", Credits = 3 }
        );

        db.SaveChanges();
        var c = db.Courses.ToDictionary(x => x.Code);

        // University scope: every seeded catalog course belongs to El Shorouk.
        foreach (var course in c.Values)
            course.UniversityId = elShorouk.Id;

        // Mazen's six current courses are Computer Engineering, year 3.
        foreach (var code in new[] { "CS350", "CS210", "CS330", "CS320", "CS240", "CS120" })
        {
            c[code].Department = "Computer Engineering";
            c[code].AcademicYear = 3;
            c[code].Semester = "Fall 2025";
        }
        db.SaveChanges();

        // Notifications
        db.Notifications.AddRange(
            new Notification { UserId = user.Id, Title = "Registration open", Body = "Course registration for next term is now open." },
            new Notification { UserId = user.Id, Title = "AI Workshop reminder", Body = "Your AI Workshop starts in 3 days." },
            new Notification { UserId = user.Id, Title = "New job match", Body = "A new internship matches your profile." }
        );

        // Enrollments
        db.Enrollments.AddRange(
            new Enrollment { UserId = user.Id, CourseId = c["CS350"].Id, GradeLetter = "B+", GradePoints = 3.3, AttendancePercent = 95, Status = "Good" },
            new Enrollment { UserId = user.Id, CourseId = c["CS210"].Id, GradeLetter = "B", GradePoints = 3.0, AttendancePercent = 88, Status = "Needs Attention" },
            new Enrollment { UserId = user.Id, CourseId = c["CS330"].Id, GradeLetter = "A-", GradePoints = 3.7, AttendancePercent = 96, Status = "Excellent" },
            new Enrollment { UserId = user.Id, CourseId = c["CS320"].Id, GradeLetter = "C+", GradePoints = 2.3, AttendancePercent = 78, Status = "Needs Attention" },
            new Enrollment { UserId = user.Id, CourseId = c["CS240"].Id, GradeLetter = "B+", GradePoints = 3.4, AttendancePercent = 90, Status = "Good" },
            new Enrollment { UserId = user.Id, CourseId = c["CS120"].Id, GradeLetter = "B", GradePoints = 3.0, AttendancePercent = 85, Status = "Needs Attention" }
        );

        // Semester records
        db.SemesterRecords.AddRange(
            new SemesterRecord { UserId = user.Id, Label = "2 semesters ago", IsCurrent = false, Gpa = 2.78, AttendanceRate = 82, CompletedCourses = 6, TotalCourses = 26 },
            new SemesterRecord { UserId = user.Id, Label = "Last semester", IsCurrent = false, Gpa = 2.96, AttendanceRate = 87, CompletedCourses = 12, TotalCourses = 26 },
            new SemesterRecord { UserId = user.Id, Label = "Current semester", IsCurrent = true, Gpa = 3.16, AttendanceRate = 92, CompletedCourses = 18, TotalCourses = 26 }
        );
        db.SemesterRecords.Add(new SemesterRecord { UserId = graduate.Id, Label = "Final", IsCurrent = true, Gpa = 3.4, AttendanceRate = 94, CompletedCourses = 26, TotalCourses = 26 });

        // Study planner
        var weekStart = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
        var template = new (int Day, string Title, int Hours, string? Note)[]
        {
            (0, "Data Structures", 2, null),
            (1, "Machine Learning", 3, null),
            (2, "Database Systems", 2, null),
            (3, "Web Development", 2, null),
            (4, "Computer Networks", 2, null),
            (5, "Revision & practice", 3, null),
            (6, "Free Day", 0, "Rest & Recharge"),
        };
        for (var week = -1; week <= 2; week++)
        {
            foreach (var t in template)
            {
                db.StudyPlanItems.Add(new StudyPlanItem
                {
                    UserId = user.Id,
                    Date = weekStart.AddDays(week * 7 + t.Day),
                    Title = t.Title,
                    Hours = t.Hours,
                    Note = t.Note,
                    IsDone = week < 0 || (week == 0 && t.Day == 0)
                });
            }
        }

        // Activities
        var utc = DateTime.UtcNow;
        db.Activities.AddRange(
            new ActivityItem { UserId = user.Id, Kind = "quiz", Message = "You completed the quiz for Machine Learning", CreatedAtUtc = utc.AddHours(-2) },
            new ActivityItem { UserId = user.Id, Kind = "recommendation", Message = "New recommendation available: Deep Learning", CreatedAtUtc = utc.AddHours(-5) },
            new ActivityItem { UserId = user.Id, Kind = "attendance", Message = "Your attendance has been updated", CreatedAtUtc = utc.AddHours(-25) },
            new ActivityItem { UserId = user.Id, Kind = "announcement", Message = "New announcement: Exam schedule released", CreatedAtUtc = utc.AddHours(-49) }
        );

        // Announcements
        db.Announcements.AddRange(
            new Announcement { Title = "Welcome to UniSphere 360+", Body = "Your academic journey starts here. Explore your dashboard and connect with your advisors.", Category = "general", CreatedByUserId = admin.Id, UniversityId = elShorouk.Id },
            new Announcement { Title = "Midterm Exam Schedule", Body = "Midterm exams will begin next month. Check your schedule for exact dates and times.", Category = "academic", CreatedByUserId = admin.Id, UniversityId = elShorouk.Id },
            new Announcement { Title = "Career Fair Registration Open", Body = "Register now for the annual career fair. Meet top employers and explore internship opportunities.", Category = "career", CreatedByUserId = admin.Id, UniversityId = elShorouk.Id },
            new Announcement { Title = "Helwan CS Welcome", Body = "Welcome to the Helwan University Computer Science workspace. Course materials and announcements will appear here.", Category = "general", CreatedByUserId = admin.Id, UniversityId = helwan.Id }
        );

        // Research papers
        db.ResearchPapers.AddRange(
            new ResearchPaper { Title = "Attention Is All You Need", Authors = "Vaswani et al.", Abstract = "The dominant sequence transduction models are based on complex recurrent or convolutional neural networks. We propose a new simple network architecture, the Transformer, based solely on attention mechanisms.", Url = "https://arxiv.org/abs/1706.03762", Source = "arxiv", PublishedDate = new DateTime(2017, 6, 12) },
            new ResearchPaper { Title = "BERT: Pre-training of Deep Bidirectional Transformers", Authors = "Devlin et al.", Abstract = "We introduce a new language representation model called BERT, which stands for Bidirectional Encoder Representations from Transformers.", Url = "https://arxiv.org/abs/1810.04805", Source = "arxiv", PublishedDate = new DateTime(2018, 10, 11) },
            new ResearchPaper { Title = "Deep Residual Learning for Image Recognition", Authors = "He et al.", Abstract = "We present a residual learning framework to ease the training of networks that are substantially deeper than those used previously.", Url = "https://arxiv.org/abs/1512.03385", Source = "arxiv", PublishedDate = new DateTime(2015, 12, 10) }
        );

        // Course materials for Mazen's university courses (3-4 per course,
        // mixed types) plus 3 upcoming items (exams/announcement).
        var nowUtc = DateTime.UtcNow;
        void Material(string code, string title, string type, string description, string content, int daysAgo = 0)
        {
            db.CourseMaterials.Add(new CourseMaterial
            {
                CourseId = c[code].Id,
                Title = title,
                Type = type,
                Description = description,
                Content = content,
                UploadedByUserId = doctor.Id,
                CreatedAtUtc = nowUtc.AddDays(-daysAgo)
            });
        }

        Material("CS350", "Supervised Learning Summary", "Summary", "Key concepts of regression and classification.", "Supervised learning maps inputs to labeled outputs.\n\nKey points:\n- Linear and logistic regression\n- Decision trees and forests\n- Train/validation/test splits", 6);
        Material("CS350", "Lecture 4: Neural Network Basics", "Lecture", "Perceptrons, activation functions and backpropagation.", "A perceptron computes w.x + b followed by a non-linearity.\nBackpropagation applies the chain rule to update weights.", 4);
        Material("CS350", "Assignment 2: Build a Classifier", "Assignment", "Train and evaluate a classifier on the provided dataset.", "Tasks:\n1. Load the dataset\n2. Train logistic regression and k-NN\n3. Report accuracy and confusion matrix", 2);
        Material("CS210", "Data Structures Cheat Sheet", "Summary", "Lists, trees, graphs and complexity in one page.", "Arrays O(1) access.\nLinked lists O(n) traversal.\nBST average O(log n).\nGraphs: BFS vs DFS.", 7);
        Material("CS210", "Lecture 6: Graph Traversals", "Lecture", "BFS and DFS with worked examples.", "BFS explores level by level using a queue.\nDFS dives deep using a stack or recursion.", 3);
        Material("CS210", "Assignment 3: Shortest Paths", "Assignment", "Implement Dijkstra on the campus graph.", "Submit code plus a short report comparing runtimes.", 1);
        Material("CS330", "SQL Joins Summary", "Summary", "Inner, left, right and full joins visualized.", "INNER JOIN keeps matches.\nLEFT JOIN keeps all left rows.\nAggregation with GROUP BY and HAVING.", 5);
        Material("CS330", "Lecture 5: Indexing", "Lecture", "B-trees and query planning.", "An index trades write cost for read speed.\nCheck EXPLAIN plans before optimizing.", 2);
        Material("CS330", "Midterm Exam: Databases", "Exam", "Covers SQL, normalization and indexing. Bring your ID.", "Chapters 1-6.\nDuration: 90 minutes.\nNo phones allowed.", 0);
        Material("CS320", "OSI Model Summary", "Summary", "All seven layers with examples.", "7 Application, 6 Presentation, 5 Session, 4 Transport, 3 Network, 2 Data Link, 1 Physical.", 6);
        Material("CS320", "Lecture 7: TCP vs UDP", "Lecture", "Reliability vs speed trade-offs.", "TCP: ordered, reliable, slower handshake.\nUDP: fire-and-forget, ideal for streaming.", 3);
        Material("CS320", "Assignment 4: Subnetting", "Assignment", "Subnet the lab network into 6 subnets.", "Show your work for full credit.", 1);
        Material("CS240", "Flexbox & Grid Summary", "Summary", "Modern CSS layout in one page.", "Flexbox for one dimension.\nGrid for two dimensions.\nMedia queries for responsiveness.", 5);
        Material("CS240", "Lecture 8: REST APIs", "Lecture", "Resources, verbs and status codes.", "GET reads, POST creates, PUT replaces, PATCH updates, DELETE removes.", 2);
        Material("CS240", "Project Milestone 2", "Assignment", "Client-side routing plus API integration due Friday.", "Deliverable: working demo plus README.", 0);
        Material("CS120", "Logic & Proofs Summary", "Summary", "Propositional logic and proof techniques.", "Direct proof, contrapositive, contradiction, induction.", 4);
        Material("CS120", "Lecture 5: Combinatorics", "Lecture", "Counting principles with examples.", "Permutations P(n,k). Combinations C(n,k). Pigeonhole principle.", 1);
        Material("CS120", "Final Exam: Discrete Math", "Exam", "Cumulative final covering logic, sets and graphs.", "Chapters 1-9.\nDuration: 120 minutes.", 0);
        Material("CS330", "Lab Sessions Moved to Hall B", "Announcement", "All database labs move to Hall B starting Monday.", "Please check the new seating plan on the department board.", 0);

        SeedDemoJobs(db);
        SeedGraduateCareerDemo(db, graduate.Id);

        db.SaveChanges();
    }

    /// <summary>Demo job catalog (Source="Demo") — clearly-labeled seed content so
    /// the search/matching/UI experience works before the first live sync.
    /// Live rows (Source="Remotive") arrive via GET /api/grad/jobs/sync.
    /// Demo rows never claim a fake posting URL: their "Open" link goes to a
    /// real job-board search matching the title and location.</summary>
    public static void SeedDemoJobs(AppDbContext db)
    {
        // Rewrite legacy demo links (old seeds pointed at example.com/*, which is
        // not a real job board). Idempotent: only touches the placeholder URLs.
        var stale = db.JobListings
            .Where(j => j.Source == "Demo" && j.Url.Contains("://example.com/"))
            .ToList();
        foreach (var j in stale)
            j.Url = DemoJobLink(j.Title, j.Location, j.Remote);
        if (stale.Count > 0) db.SaveChanges();

        // Applications created from the stale demo rows copied their URL — point
        // those at the corrected listing link (placeholder pattern only, so any
        // user-entered external URLs are never touched).
        var fixedListingIds = stale.Select(j => j.Id).ToHashSet();
        var staleApps = db.JobApplications
            .Where(a => a.SourceUrl != null && a.SourceUrl.Contains("://example.com/jobs/"))
            .ToList();
        foreach (var app in staleApps)
        {
            var linked = db.JobListings.FirstOrDefault(l => l.Id == app.JobListingId);
            if (linked is not null && !linked.Url.Contains("://example.com/"))
                app.SourceUrl = linked.Url;
            else
                app.SourceUrl = null; // orphaned demo link: drop it instead of pointing somewhere fake
        }
        if (staleApps.Count > 0) db.SaveChanges();

        if (db.JobListings.Any(j => j.Source == "Demo")) return;

        var utc = DateTime.UtcNow;
        (string Title, string Company, string Location, bool Remote, string Category, string? Salary, string Desc, int DaysAgo)[] jobs =
        [
            ("Backend Developer (.NET)", "Cobalt Systems", "Cairo, Egypt", false, "Technical", "EGP 28000 - 34000",
             "We are hiring a Backend Developer with strong C# and ASP.NET skills for a services platform used by thousands of users. You will design REST APIs with .NET (C#), write SQL queries against PostgreSQL, and collaborate in Agile ceremonies. Git, unit testing and Docker are part of our daily workflow. Excellent communication and problem-solving skills required.",
             1),
            ("Full-Stack Developer (React + Node)", "Nile Cloud", "Remote", true, "Technical", "USD 1200 - 1600",
             "Join our remote web team as a Full-Stack Developer. You will build dashboards in React and Node.js, maintain JavaScript and TypeScript codebases, integrate REST endpoints and SQL data stores, and ship with GitHub Actions CI/CD. HTML/CSS attention to detail and Git hygiene are expected.",
             2),
            ("Machine Learning Engineer", "Horizon Analytics", "Remote", true, "Data", "USD 1500 - 2200",
             "As Machine Learning Engineer you will productionize models built with Python, TensorFlow and PyTorch. Strong SQL and data munging skills, Docker deployments and Git-based workflows are required. You will champion MLOps best practices and machine learning pipelines for the analytics platform.",
             3),
            ("Data Analyst (Power BI)", "Delta Retail Group", "Cairo, Egypt", false, "Data", "EGP 18000 - 24000",
             "Support business decisions as Data Analyst: build Power BI dashboards, write SQL, and analyze sales with Excel and Python. Clear communication and teamwork with product owners, strong time management, statistics basics and an eye for data quality.",
             4),
            ("DevOps Engineer", "Skyline Telecom", "Dubai, UAE", false, "Technical", null,
             "Own our Kubernetes clusters: Docker, CI/CD pipelines (GitHub Actions/Jenkins), Azure and AWS infrastructure, Linux administration, monitoring and incident response. Git and infrastructure-as-code habits required; AWS experience preferred.",
             5),
            ("Frontend Developer (Angular)", "Sahara Digital", "Remote", true, "Technical", "USD 900 - 1300",
             "Craft user-facing experiences with Angular, TypeScript, HTML/CSS and JavaScript. You will collaborate with designers, work in Agile sprints, write clean Git history and deliver responsive components used by millions.",
             6),
            ("Mobile Developer (Flutter)", "Zayn Apps", "Cairo, Egypt", true, "Technical", "EGP 22000 - 30000",
             "Ship cross-platform apps with Flutter. Strong Dart, native Android/iOS basics, REST integrations, Git and unit testing for mobile builds. Problem-solving mindset and teamwork in a small product squad.",
             7),
            ("QA Engineer", "Pyramid Software", "Cairo, Egypt", false, "Technical", "EGP 15000 - 20000",
             "Lead quality for our web platform: design test plans, automate with Selenium and write unit tests and xUnit/integration suites. Agile/Scrum ceremonies, testing-first mindset, sharp communication for reporting regressions.",
             8),
            ("Data Scientist", "Fawry Analytics", "Cairo, Egypt", false, "Data", "EGP 25000 - 32000",
             "Own models end to end as Data Scientist: Python, pandas for data analysis, statistics, SQL, machine learning frameworks (scikit-learn, TensorFlow), plus dashboarding in Power BI. Present insights with strong communication.",
             9),
            ("Junior Java Backend Engineer", "Meridian Bank", "Cairo, Egypt", false, "Fintech", "EGP 20000 - 26000",
             "Build securefinancial services with Java and Spring Boot. SQL (MySQL) schema design, Git-based reviews, unit testing and Agile rituals. Interest in fintech security and Apache Kafka a plus.",
             10),
            ("NLP Engineer (LLMs)", "Cortex Labs", "Remote", true, "Data", "USD 2000 - 2800",
             "Work on Arabic/English NLP: Python, transformer fine-tuning, TensorFlow/PyTorch, deep learning and machine learning pipelines, SQL query tooling for dataset curation, Docker deployments. NLP research background preferred.",
             11),
            ("Technical Support Engineer", "Nimbus Helpdesk", "Remote", true, "Support", "USD 600 - 900",
             "Help enterprise customers debug integrations. Linux basics, SQL queries for troubleshooting, clear written communication, time management and empathy. SQL,Git and REST knowledge helpful; growth into engineering roles possible.",
             12),
        ];

        foreach (var j in jobs)
        {
            var skills = string.Join(",", Services.CareerCatalog.DetectSkills($"{j.Title} {j.Desc}"));
            db.JobListings.Add(new JobListing
            {
                Source = "Demo",
                SourceId = $"demo-{j.Title}-{j.Company}",
                Title = j.Title,
                CompanyName = j.Company,
                Location = j.Location,
                Remote = j.Remote,
                Category = j.Category,
                Salary = j.Salary,
                Url = DemoJobLink(j.Title, j.Location, j.Remote),
                Description = j.Desc,
                KeySkillsCsv = skills,
                PublishedAtUtc = utc.AddDays(-j.DaysAgo),
                FetchedAtUtc = utc
            });
        }
        // Self-committing: the legacy-database path (early-return branch) has
        // no outer SaveChanges, so persist the demo rows here.
        db.SaveChanges();
    }

    /// <summary>Legitimate, relevant destination for a demo listing: a live
    /// LinkedIn job search for the title in the listing's location (remote
    /// listings get LinkedIn's remote-work filter). Real job board, no fake
    /// postings, always relevant to the concrete role.</summary>
    public static string DemoJobLink(string title, string location, bool remote) =>
        remote
            ? "https://www.linkedin.com/jobs/search/?keywords=" + Uri.EscapeDataString(title) + "&location=Remote&f_WT=2"
            : "https://www.linkedin.com/jobs/search/?keywords=" + Uri.EscapeDataString(title) + "&location=" + Uri.EscapeDataString(location);

    /// <summary>Seeds realistic starter career data owned by the demo graduate
    /// account so the module is explorable immediately (login demo-account data).</summary>
    public static void SeedGraduateCareerDemo(AppDbContext db, int graduateUserId)
    {
        // Recompute the guard from skills: some early databases already hold an
        // EMPTY GraduateProfile row (auto-created when the pages were opened
        // before the seeding fix). Seed whenever skills are missing, even then.
        var existingProfile = db.GraduateProfiles.FirstOrDefault(p => p.UserId == graduateUserId);
        if (existingProfile is not null && db.GraduateSkills.Any(s => s.UserId == graduateUserId)) return;
        var utc = DateTime.UtcNow;
        var demoProfile = new GraduateProfile
        {
            UserId = graduateUserId,
            Headline = "Backend Engineer | .NET, APIs & Data",
            CareerSummary = "Computer Engineering graduate (Cairo University, 2023). Two years building backend services and internal tooling; passionate about clean APIs and data pipelines.",
            LinkedInUrl = "https://www.linkedin.com/in/omar-hassan-demo",
            GitHubUrl = "https://github.com/omar-hassan-demo",
            PreferredJobTitles = "Backend Developer, .NET Developer, Software Engineer",
            PreferredIndustries = "Tech, Fintech",
            PreferredLocations = "Cairo, Egypt",
            WorkModes = "Hybrid,Remote",
            EmploymentType = "Full-time",
            ExperienceLevel = "Entry-level",
            Languages = "Arabic (Native), English (Fluent)",
            CareerGoal = "Land a backend engineering role on a product team within 6 months."
        };
        if (existingProfile is null)
        {
            db.GraduateProfiles.Add(demoProfile);
        }
        else
        {
            // Upgrade the empty legacy row in place instead of duplicating
            // (GraduateProfiles has a unique index on UserId).
            existingProfile.Headline = demoProfile.Headline;
            existingProfile.CareerSummary = demoProfile.CareerSummary;
            existingProfile.LinkedInUrl = demoProfile.LinkedInUrl;
            existingProfile.GitHubUrl = demoProfile.GitHubUrl;
            existingProfile.PreferredJobTitles = demoProfile.PreferredJobTitles;
            existingProfile.PreferredIndustries = demoProfile.PreferredIndustries;
            existingProfile.PreferredLocations = demoProfile.PreferredLocations;
            existingProfile.WorkModes = demoProfile.WorkModes;
            existingProfile.EmploymentType = demoProfile.EmploymentType;
            existingProfile.ExperienceLevel = demoProfile.ExperienceLevel;
            existingProfile.Languages = demoProfile.Languages;
            existingProfile.CareerGoal = demoProfile.CareerGoal;
        }

        db.GraduateSkills.AddRange(
            new GraduateSkill { UserId = graduateUserId, Name = "C#", Level = "Advanced", Source = "Profile" },
            new GraduateSkill { UserId = graduateUserId, Name = "ASP.NET", Level = "Advanced", Source = "Profile" },
            new GraduateSkill { UserId = graduateUserId, Name = "SQL", Level = "Advanced", Source = "Profile" },
            new GraduateSkill { UserId = graduateUserId, Name = "Git", Level = "Intermediate", Source = "Profile" },
            new GraduateSkill { UserId = graduateUserId, Name = "Docker", Level = "Beginner", Source = "Profile" },
            new GraduateSkill { UserId = graduateUserId, Name = "Communication", Level = "Advanced", Source = "Profile" },
            new GraduateSkill { UserId = graduateUserId, Name = "Teamwork", Level = "Advanced", Source = "Profile" }
        );

        db.ProfileEntries.AddRange(
            new ProfileEntry { UserId = graduateUserId, Kind = "Experience", Title = "Backend Engineering Intern", Organization = "TechNova (Cairo)", Description = "Built REST endpoints in ASP.NET for the billing service; wrote integration tests and cut p95 latency by 30%.", Period = "Jul 2022 - Dec 2022" },
            new ProfileEntry { UserId = graduateUserId, Kind = "Project", Title = "Graduation Project: Real-time Event Pipeline", Organization = "Cairo University", Description = "Designed a streaming pipeline with ASP.NET, Redis and PostgreSQL ingesting 2k events/sec; led a 4-person team.", Period = "2023" },
            new ProfileEntry { UserId = graduateUserId, Kind = "Certification", Title = "AWS Cloud Practitioner", Organization = "Amazon Web Services", Description = "Cloud fundamentals certification, verified credential.", Period = "2024" }
        );

        db.RoadmapItems.AddRange(
            new RoadmapItem { UserId = graduateUserId, Title = "Refresh advanced SQL (window functions)", Category = "Skill", Status = "InProgress", TargetDate = DateTime.UtcNow.Date.AddDays(14), SortOrder = 1 },
            new RoadmapItem { UserId = graduateUserId, Title = "Publish two Azure/AWS side projects", Category = "Project", Status = "NotStarted", TargetDate = DateTime.UtcNow.Date.AddDays(45), SortOrder = 2 },
            new RoadmapItem { UserId = graduateUserId, Title = "Submit application to Fawry backend role", Category = "Application", Status = "Completed", TargetDate = DateTime.UtcNow.Date.AddDays(-3), SortOrder = 3, CompletedAtUtc = DateTime.UtcNow.Date.AddDays(-3) }
        );

        if (!db.GraduateSettings.Any(s => s.UserId == graduateUserId))
            db.GraduateSettings.Add(new GraduateSettings { UserId = graduateUserId, NotifyNewMatches = true, NotifyFollowUps = true, MatchAlertThresholdPercent = 75 });

        // Tracker starter rows + one saved demo job. Query the table (not
        // ChangeTracker.Local): on reboots the demo jobs may already be
        // persisted but untracked, and the starter rows must still link up.
        var savedSource = db.JobListings.FirstOrDefault(j => j.Title.Contains("Data Analyst"))
            ?? db.JobListings.Local.FirstOrDefault(j => j.Title.Contains("Data Analyst"));
        var appliedSource = db.JobListings.FirstOrDefault(j => j.Title.Contains("Backend Developer (.NET)"))
            ?? db.JobListings.Local.FirstOrDefault(j => j.Title.Contains("Backend Developer (.NET)"));
        if (savedSource is not null)
        {
            db.SavedJobs.Add(new SavedJob { UserId = graduateUserId, JobListingId = savedSource.Id, SavedAtUtc = utc.AddDays(-2) });
            db.JobApplications.Add(new JobApplication
            {
                UserId = graduateUserId, JobListingId = savedSource.Id,
                CompanyName = savedSource.CompanyName, JobTitle = savedSource.Title, SourceUrl = savedSource.Url,
                Status = "Applied", Notes = "Applied via career portal; waiting on HR reply.",
                FollowUpDate = DateTime.UtcNow.Date.AddDays(3), AppliedAtUtc = DateTime.UtcNow.Date.AddDays(-2)
            });
        }
        if (appliedSource is not null)
        {
            db.JobApplications.Add(new JobApplication
            {
                UserId = graduateUserId, JobListingId = appliedSource.Id,
                CompanyName = appliedSource.CompanyName, JobTitle = appliedSource.Title, SourceUrl = appliedSource.Url,
                Status = "Interview", Notes = "Technical interview scheduled with the platform team.",
                FollowUpDate = DateTime.UtcNow.Date.AddDays(5), AppliedAtUtc = DateTime.UtcNow.Date.AddDays(-9)
            });
        }
        // Self-committing: called from the legacy early-return path too.
        db.SaveChanges();
    }

    /// <summary>Adds missing foundation data to an existing database without
    /// deleting or recreating anything (safe to run on every startup).</summary>
    private static void EnsureExistingInstallation(
        AppDbContext db, University elShorouk, University helwan)
    {
        var admin = db.Users.FirstOrDefault(u => u.Email == "admin@unisphere.edu");

        // Phase 1: give the demo doctor a locked profile address on older
        // installations so the read-only profile page has something to show.
        var demoDoctor = db.Users.FirstOrDefault(u => u.Email == "doctor@unisphere.edu");
        if (demoDoctor is not null && string.IsNullOrWhiteSpace(demoDoctor.Address))
            demoDoctor.Address = "12 Faculty Road, El Shorouk, Cairo";

        var helwanStudent = db.Users.FirstOrDefault(u => u.Email == "helwan.student@unisphere.edu");
        if (helwanStudent is null)
        {
            helwanStudent = new User
            {
                FirstName = "Salma",
                LastName = "Adel",
                FullName = "Salma Adel",
                PhoneNumber = "01098765432",
                Email = "helwan.student@unisphere.edu",
                Gender = "Female",
                Age = 20,
                Major = "Computer Science",
                Department = "Computer Science",
                AcademicYear = 3,
                UniversityId = helwan.Id,
                Role = UserRole.Student,
                PasswordHash = PasswordService.Hash("Password123!")
            };
            db.Users.Add(helwanStudent);
        }

        var graduate = db.Users.FirstOrDefault(u => u.Email == "graduate@unisphere.edu");
        if (graduate is not null)
        {
            graduate.UniversityId ??= helwan.Id;
            if (string.IsNullOrWhiteSpace(graduate.GraduatedFrom))
                graduate.GraduatedFrom = "Helwan University, Faculty of Computers and Artificial Intelligence";
        }

        // Backfill legacy rows so scoped queries never expose unowned data.
        // Skip a code that already exists in El Shorouk to protect the
        // (UniversityId, Code) unique index on older installations.
        var elShoroukCodes = db.Courses
            .Where(c => c.UniversityId == elShorouk.Id)
            .Select(c => c.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var course in db.Courses.Where(c => c.UniversityId == null).ToList())
        {
            if (elShoroukCodes.Add(course.Code))
                course.UniversityId = elShorouk.Id;
        }
        foreach (var evt in db.Events.Where(e => e.UniversityId == null))
            evt.UniversityId = elShorouk.Id;
        foreach (var announcement in db.Announcements.Where(a => a.UniversityId == null))
            announcement.UniversityId = elShorouk.Id;

        if (!db.Events.Any(e => e.UniversityId == helwan.Id))
        {
            db.Events.Add(new EventItem
            {
                Title = "Helwan Tech Day",
                Location = "Faculty of Computers and Artificial Intelligence",
                StartsAtUtc = DateTime.UtcNow.Date.AddDays(7).AddHours(11),
                Description = "Student projects, research demos and employer talks.",
                UniversityId = helwan.Id
            });
        }

        if (admin is not null && !db.Announcements.Any(a => a.UniversityId == helwan.Id))
        {
            db.Announcements.Add(new Announcement
            {
                Title = "Helwan CS Welcome",
                Body = "Welcome to the Helwan University Computer Science workspace. Course materials and announcements will appear here.",
                Category = "general",
                CreatedByUserId = admin.Id,
                UniversityId = helwan.Id
            });
        }

        // Existing databases that predate the demo catalog block in the
        // fresh-seed path have users but no El Shorouk courses, leaving the
        // demo student/doctor/exam workspaces empty. Restore them additively.
        EnsureDemoCatalogCourses(db, elShorouk);

        db.SaveChanges();
    }

    /// <summary>
    /// Additive repair for older databases: recreate the demo catalog that
    /// only the fresh-seed path used to insert. Missing rows are added,
    /// existing rows are never overwritten or removed.
    /// </summary>
    private static void EnsureDemoCatalogCourses(AppDbContext db, University elShorouk)
    {
        var demoCatalog = new (string Code, string Title, string Description, string Track)[]
        {
            ("CS350", "Machine Learning", "Supervised and unsupervised learning.", "AI"),
            ("CS351", "Deep Learning", "Neural networks, CNNs and transformers.", "AI"),
            ("CS362", "Natural Language Processing", "Text processing, LLMs and RAG.", "AI"),
            ("CS340", "Mobile App Development", "Building apps with Flutter.", "Software"),
            ("CS240", "Web Development", "HTML, CSS, JavaScript and backend basics.", "Software"),
            ("CS330", "Database Systems", "SQL, PostgreSQL and data modeling.", "CS"),
            ("CS430", "Advanced Databases", "Query optimization, NoSQL and distributed data.", "CS"),
            ("CS210", "Data Structures & Algorithms", "Lists, trees, graphs and complexity.", "CS"),
            ("CS120", "Discrete Mathematics", "Logic, sets, combinatorics and graphs.", "CS"),
            ("CS320", "Computer Networks", "TCP/IP, routing and network design.", "IT"),
            ("CS420", "Cloud Computing", "Virtualization, containers and cloud services.", "IT")
        };

        var existing = db.Courses
            .Where(c => c.UniversityId == elShorouk.Id)
            .ToList()
            .GroupBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var (code, title, description, track) in demoCatalog)
        {
            if (existing.ContainsKey(code)) continue;
            var course = new Course
            {
                Code = code,
                Title = title,
                Description = description,
                Track = track,
                Credits = 3,
                UniversityId = elShorouk.Id
            };
            db.Courses.Add(course);
            existing[code] = course;
        }

        // The demo student's current courses are Computer Engineering, year 3.
        foreach (var code in new[] { "CS350", "CS210", "CS330", "CS320", "CS240", "CS120" })
        {
            if (!existing.TryGetValue(code, out var course)) continue;
            if (string.IsNullOrWhiteSpace(course.Department))
                course.Department = "Computer Engineering";
            course.AcademicYear ??= 3;
            if (string.IsNullOrWhiteSpace(course.Semester))
                course.Semester = "Fall 2025";
        }

        db.SaveChanges();
    }

    /// <summary>Demo enrollments that can only be created after the CSV
    /// catalog importer has inserted the Helwan courses.</summary>
    public static void EnsurePostCatalog(AppDbContext db)
    {
        // Part 1: assign the El Shorouk demo doctor to the courses he already
        // publishes materials for. Idempotent: only missing assignments are
        // added, keyed by (course, doctor, term).
        var doctor = db.Users.FirstOrDefault(u => u.Email == "doctor@unisphere.edu");
        if (doctor is not null && doctor.UniversityId.HasValue)
        {
            var doctorCourseCodes = new[] { "CS350", "CS210", "CS330", "CS320", "CS240", "CS120" };
            var doctorCourses = db.Courses
                .Where(c => c.UniversityId == doctor.UniversityId)
                .Where(c => doctorCourseCodes.Contains(c.Code))
                .Select(c => new { c.Id, c.Semester })
                .ToList();
            var existingAssignments = db.CourseInstructors
                .Where(ci => ci.DoctorUserId == doctor.Id)
                .Select(ci => new { ci.CourseId, ci.Term })
                .ToList();

            // The doctor module reads CourseAssignments; keep the legacy
            // CourseInstructors rows mirrored there (idempotent on reruns).
            var existingAssignmentCourseIds = db.CourseAssignments
                .Where(a => a.DoctorUserId == doctor.Id)
                .Select(a => a.CourseId)
                .ToHashSet();

            foreach (var course in doctorCourses)
            {
                var term = string.IsNullOrWhiteSpace(course.Semester) ? "Fall 2025" : course.Semester;
                if (!existingAssignments.Any(a => a.CourseId == course.Id && a.Term == term))
                    db.CourseInstructors.Add(new CourseInstructor
                    {
                        CourseId = course.Id,
                        DoctorUserId = doctor.Id,
                        Term = term
                    });

                if (existingAssignmentCourseIds.Add(course.Id))
                {
                    db.CourseAssignments.Add(new CourseAssignment
                    {
                        CourseId = course.Id,
                        DoctorUserId = doctor.Id,
                        AssignedByUserId = doctor.Id,
                        AssignedAtUtc = DateTime.UtcNow
                    });
                }
            }
        }

        // The demo student's six current-course enrollments also belong to the
        // post-catalog phase: older databases have the student but never got
        // these rows. Additive only - existing enrollments keep their grades.
        var demoStudent = db.Users.FirstOrDefault(u => u.Email == DemoStudentEmail);
        if (demoStudent is not null && demoStudent.UniversityId.HasValue)
        {
            var demoGrades = new Dictionary<string, (string Letter, double Points, int Attendance, string Status)>
            {
                ["CS350"] = ("B+", 3.3, 95, "Good"),
                ["CS210"] = ("B", 3.0, 88, "Needs Attention"),
                ["CS330"] = ("A-", 3.7, 96, "Excellent"),
                ["CS320"] = ("C+", 2.3, 78, "Needs Attention"),
                ["CS240"] = ("B+", 3.4, 90, "Good"),
                ["CS120"] = ("B", 3.0, 85, "Needs Attention")
            };
            var demoCodes = demoGrades.Keys.ToArray();
            var demoCourses = db.Courses
                .Where(c => c.UniversityId == demoStudent.UniversityId)
                .Where(c => demoCodes.Contains(c.Code))
                .Select(c => new { c.Id, c.Code })
                .ToList();
            var demoEnrolled = db.Enrollments
                .Where(e => e.UserId == demoStudent.Id)
                .Select(e => e.CourseId)
                .ToHashSet();

            foreach (var course in demoCourses.Where(c => !demoEnrolled.Contains(c.Id)))
            {
                var grade = demoGrades[course.Code];
                db.Enrollments.Add(new Enrollment
                {
                    UserId = demoStudent.Id,
                    CourseId = course.Id,
                    Semester = "Current",
                    GradeLetter = grade.Letter,
                    GradePoints = grade.Points,
                    AttendancePercent = grade.Attendance,
                    Status = grade.Status
                });
            }
        }

        var student = db.Users.FirstOrDefault(u => u.Email == "helwan.student@unisphere.edu");
        if (student is null) return;

        var courseIds = db.Courses
            .Where(c => c.UniversityId == student.UniversityId)
            .Where(c => c.Code == "CS 111" || c.Code == "CS 112")
            .Select(c => c.Id)
            .ToList();

        var existing = db.Enrollments
            .Where(e => e.UserId == student.Id)
            .Select(e => e.CourseId)
            .ToHashSet();

        foreach (var courseId in courseIds.Where(id => !existing.Contains(id)))
        {
            db.Enrollments.Add(new Enrollment
            {
                UserId = student.Id,
                CourseId = courseId,
                Semester = "Current",
                GradeLetter = "",
                GradePoints = 0,
                AttendancePercent = 100,
                Status = "Good"
            });
        }

        if (!db.SemesterRecords.Any(r => r.UserId == student.Id))
        {
            db.SemesterRecords.Add(new SemesterRecord
            {
                UserId = student.Id,
                Label = "Current semester",
                IsCurrent = true,
                Gpa = 0,
                AttendanceRate = 100,
                CompletedCourses = 0,
                TotalCourses = courseIds.Count
            });
        }

        db.SaveChanges();
    }

    private static University EnsureUniversity(AppDbContext db, string name)
    {
        var university = db.Universities.FirstOrDefault(u => u.Name == name);
        if (university is not null) return university;
        university = new University { Name = name };
        db.Universities.Add(university);
        return university;
    }
}
