using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

public class GraduateProfileService(AppDbContext db)
{
    private static readonly string[] EntryKinds = ["Experience", "Project", "Certification", "Education"];

    internal static readonly (string Field, string Label, int Weight)[] CompletionItems =
    [
        ("Headline", "Professional headline", 15),
        ("CareerSummary", "Career summary", 15),
        ("PreferredJobTitles", "Target job titles", 10),
        ("PreferredLocations", "Preferred locations", 5),
        ("GitHubUrl", "GitHub link", 5),
        ("CareerGoal", "Career goal", 10),
        ("Skills:3", "At least 3 skills", 20),
        ("Cv:1", "Uploaded CV", 10),
        ("Experience:1", "Work experience entry", 10)
    ];

    public async Task<GraduateProfile> GetOrCreateProfileEntityAsync(int userId)
    {
        var profile = await db.GraduateProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        if (profile is null)
        {
            profile = new GraduateProfile { UserId = userId, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
            db.GraduateProfiles.Add(profile);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // A parallel request from the same page load created it first.
                db.Entry(profile).State = EntityState.Detached;
                profile = await db.GraduateProfiles.FirstAsync(p => p.UserId == userId);
            }
        }
        return profile;
    }

    public async Task<GraduateProfileDto> GetProfileAsync(int userId)
    {
        var profile = await GetOrCreateProfileEntityAsync(userId);
        return await BuildDtoAsync(profile);
    }

    public async Task<GraduateProfileDto> UpdateAsync(int userId, GraduateProfileUpdateRequest req)
    {
        var profile = await GetOrCreateProfileEntityAsync(userId);

        if (req.Headline is not null) profile.Headline = nullIfEmpty(req.Headline);
        if (req.CareerSummary is not null) profile.CareerSummary = nullIfEmpty(req.CareerSummary);
        if (req.LinkedInUrl is not null) profile.LinkedInUrl = nullIfEmpty(req.LinkedInUrl);
        if (req.GitHubUrl is not null) profile.GitHubUrl = nullIfEmpty(req.GitHubUrl);
        if (req.PortfolioUrl is not null) profile.PortfolioUrl = nullIfEmpty(req.PortfolioUrl);
        if (req.PreferredJobTitles is not null) profile.PreferredJobTitles = nullIfEmpty(req.PreferredJobTitles);
        if (req.PreferredIndustries is not null) profile.PreferredIndustries = nullIfEmpty(req.PreferredIndustries);
        if (req.PreferredLocations is not null) profile.PreferredLocations = nullIfEmpty(req.PreferredLocations);
        if (req.WorkModes is not null) profile.WorkModes = req.WorkModes;
        if (req.EmploymentType is not null) profile.EmploymentType = req.EmploymentType;
        if (req.ExperienceLevel is not null) profile.ExperienceLevel = req.ExperienceLevel;
        if (req.Languages is not null) profile.Languages = nullIfEmpty(req.Languages);
        if (req.CareerGoal is not null) profile.CareerGoal = nullIfEmpty(req.CareerGoal);
        if (req.CareerChallenges is not null)
            profile.CareerChallenges = nullIfEmpty(req.CareerChallenges) is { } c ? c[..Math.Min(c.Length, 1500)] : null;
        profile.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return await BuildDtoAsync(profile);
    }

    // Career calibration onboarding

    public async Task<OnboardingStatusDto> GetOnboardingAsync(int userId)
    {
        var p = await GetOrCreateProfileEntityAsync(userId);
        return new OnboardingStatusDto(
            p.HasCompletedOnboarding, p.TargetRole, p.ExperienceLevel, p.PrimaryStruggle,
            p.PreferredWorkMode, p.PreferredCity,
            [.. CareerCatalog.RoleSkills.Keys], CareerCatalog.ExperienceLevels,
            CareerCatalog.Struggles, CareerCatalog.CalibrationWorkModes);
    }

    /// <summary>Saves the answers and mirrors them into the preference fields job matching already reads.</summary>
    public async Task<OnboardingStatusDto> CompleteOnboardingAsync(int userId, OnboardingRequest req)
    {
        static string Pick(string[] allowed, string? value, string field) =>
            allowed.FirstOrDefault(a => a.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"{field} must be one of: {string.Join(", ", allowed)}.");

        var role = req.TargetRole?.Trim() ?? "";
        var city = nullIfEmpty(req.PreferredCity);
        if (role.Length is 0 or > 80 || role.Contains(','))
            throw new ArgumentException("Target role is required (one role, max 80 characters).");
        if (city is not null && (city.Length > 60 || city.Contains(',')))
            throw new ArgumentException("Preferred city must be a single city name (max 60 characters).");

        var level = Pick(CareerCatalog.ExperienceLevels, req.ExperienceLevel, "Experience level");
        var struggle = Pick(CareerCatalog.Struggles, req.PrimaryStruggle, "Primary struggle");
        var mode = Pick(CareerCatalog.CalibrationWorkModes, req.PreferredWorkMode, "Work mode");

        var p = await GetOrCreateProfileEntityAsync(userId);
        // The newest answer goes first; hand-entered preferences are kept (a replaced answer is not).
        p.PreferredJobTitles = Prepend(p.PreferredJobTitles, role, p.TargetRole);
        if (city is not null) p.PreferredLocations = Prepend(p.PreferredLocations, city, p.PreferredCity);
        p.WorkModes = mode;
        p.TargetRole = role;
        p.ExperienceLevel = level;
        p.PrimaryStruggle = struggle;
        p.PreferredWorkMode = mode;
        p.PreferredCity = city;
        p.HasCompletedOnboarding = true;
        p.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return await GetOnboardingAsync(userId);
    }

    private static string Prepend(string? csv, string value, string? replaced)
    {
        var rest = (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !x.Equals(value, StringComparison.OrdinalIgnoreCase)
                        && !x.Equals(replaced, StringComparison.OrdinalIgnoreCase));
        var joined = string.Join(", ", rest.Prepend(value));
        return joined.Length <= 300 ? joined : joined[..300];
    }

    // Skills

    public async Task<GraduateSkillDto> AddSkillAsync(int userId, GraduateSkillCreateRequest req)
    {
        var name = req.Name.Trim();
        var existing = await db.GraduateSkills
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Name.ToLower() == name.ToLower());
        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(req.Level)) existing.Level = req.Level!.Trim();
            else if (string.Equals(existing.Source, "System", StringComparison.OrdinalIgnoreCase))
            {
                // The user typed the same skill the system bootstrapped - promote it to claimed.
                existing.Source = "Profile";
                existing.Level ??= "Intermediate";
            }
            await db.SaveChangesAsync();
            return new GraduateSkillDto(existing.Id, existing.Name, existing.Level, existing.Source);
        }

        var skill = new GraduateSkill
        {
            UserId = userId,
            Name = name,
            Level = nullIfEmpty(req.Level),
            Source = "Profile",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.GraduateSkills.Add(skill);
        await db.SaveChangesAsync();
        return new GraduateSkillDto(skill.Id, skill.Name, skill.Level, skill.Source);
    }

    public async Task<bool> RemoveSkillAsync(int userId, int id) =>
        await db.GraduateSkills
            .Where(s => s.UserId == userId && s.Id == id)
            .ExecuteDeleteAsync() > 0;

    // Profile entries

    public async Task<ProfileEntryDto> AddEntryAsync(int userId, ProfileEntryCreateRequest req)
    {
        var entry = new ProfileEntry
        {
            UserId = userId,
            Kind = NormalizeKind(req.Kind),
            Title = req.Title.Trim(),
            Organization = nullIfEmpty(req.Organization),
            Description = nullIfEmpty(req.Description),
            Period = nullIfEmpty(req.Period),
            CreatedAtUtc = DateTime.UtcNow
        };
        db.ProfileEntries.Add(entry);
        await db.SaveChangesAsync();
        return ToDto(entry);
    }

    public async Task<bool> UpdateEntryAsync(int userId, int id, ProfileEntryCreateRequest req)
    {
        var entry = await db.ProfileEntries.FirstOrDefaultAsync(e => e.UserId == userId && e.Id == id);
        if (entry is null) return false;
        entry.Kind = NormalizeKind(req.Kind);
        entry.Title = req.Title.Trim();
        entry.Organization = nullIfEmpty(req.Organization);
        entry.Description = nullIfEmpty(req.Description);
        entry.Period = nullIfEmpty(req.Period);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RemoveEntryAsync(int userId, int id) =>
        await db.ProfileEntries.Where(e => e.UserId == userId && e.Id == id).ExecuteDeleteAsync() > 0;

    private static string NormalizeKind(string kind) =>
        EntryKinds.FirstOrDefault(k => string.Equals(k, kind, StringComparison.OrdinalIgnoreCase)) ?? "Experience";

    public async Task<ProfileCompletionDto> GetCompletionAsync(int userId)
    {
        var profile = await GetOrCreateProfileEntityAsync(userId);
        var skillsCount = await db.GraduateSkills.CountAsync(s => s.UserId == userId);
        var hasCv = await db.CvDocuments.AnyAsync(c => c.UserId == userId);
        var hasExperience = await db.ProfileEntries.AnyAsync(e => e.UserId == userId && e.Kind == "Experience");

        var missing = new List<string>();
        int total = 0, earned = 0;
        foreach (var (field, label, weight) in CompletionItems)
        {
            total += weight;
            var ok = field switch
            {
                "Skills:3" => skillsCount >= 3,
                "Cv:1" => hasCv,
                "Experience:1" => hasExperience,
                "Headline" => !string.IsNullOrWhiteSpace(profile.Headline),
                "CareerSummary" => !string.IsNullOrWhiteSpace(profile.CareerSummary),
                "PreferredJobTitles" => !string.IsNullOrWhiteSpace(profile.PreferredJobTitles),
                "PreferredLocations" => !string.IsNullOrWhiteSpace(profile.PreferredLocations),
                "GitHubUrl" => !string.IsNullOrWhiteSpace(profile.GitHubUrl),
                "CareerGoal" => !string.IsNullOrWhiteSpace(profile.CareerGoal),
                _ => false
            };
            if (ok) earned += weight;
            else missing.Add(label);
        }
        return new ProfileCompletionDto(total == 0 ? 0 : earned * 100 / total,
            [.. missing],
            [.. missing.Take(3).Select(m => $"Add your {m}")]);
    }

    internal async Task<GraduateProfileDto> BuildDtoAsync(GraduateProfile profile)
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == profile.UserId);
        var university = user.UniversityId is null
            ? null
            : await db.Universities.AsNoTracking().FirstOrDefaultAsync(un => un.Id == user.UniversityId);

        var skills = await db.GraduateSkills.AsNoTracking()
            .Where(s => s.UserId == profile.UserId)
            .OrderBy(s => s.Name).ToListAsync();
        var entries = await db.ProfileEntries.AsNoTracking()
            .Where(e => e.UserId == profile.UserId)
            .OrderBy(e => e.CreatedAtUtc).ToListAsync();

        var completion = await GetCompletionAsync(profile.UserId);

        return new GraduateProfileDto(
            profile.UserId,
            user.FullName,
            user.FirstName,
            user.AvatarUrl,
            user.Major,
            user.Department,
            university?.Name,
            user.GraduationYear,
            user.GraduatedFrom,
            profile.Headline, profile.CareerSummary,
            profile.LinkedInUrl, profile.GitHubUrl, profile.PortfolioUrl,
            profile.PreferredJobTitles, profile.PreferredIndustries, profile.PreferredLocations,
            profile.WorkModes, profile.EmploymentType, profile.ExperienceLevel,
            profile.Languages, profile.CareerGoal,
            completion.Percent, completion.MissingFields,
            [.. skills.Select(s => new GraduateSkillDto(s.Id, s.Name, s.Level, s.Source))],
            [.. entries.Select(ToDto)],
            profile.TargetRole, profile.PrimaryStruggle, profile.PreferredWorkMode,
            profile.PreferredCity, profile.HasCompletedOnboarding, profile.CareerChallenges);
    }

    internal static ProfileEntryDto ToDto(ProfileEntry e) =>
        new(e.Id, e.Kind, e.Title, e.Organization, e.Description, e.Period);

    private static string? nullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
