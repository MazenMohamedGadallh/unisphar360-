using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

public sealed record ReadinessDimension(string Name, int? Percent, string Explanation, string Route);
public sealed record ReadinessCriterion(string Name, bool Met, string Detail, string Route);

public sealed record ReadinessSnapshot(
    DateTime CreatedAtUtc, IReadOnlyList<ReadinessDimension> Dimensions, IReadOnlyList<ReadinessCriterion> Criteria,
    IReadOnlyList<string> Strengths, bool CareerReady);

public sealed record MissionDto(
    string Key, string Title, string Description, string Criteria, int Xp, string Route, string Status, DateTime? CompletedAtUtc);

public sealed record JourneyLevelDto(int Level, string Title, string Badge, string Status, IReadOnlyList<MissionDto> Missions);

public sealed record XpEntryDto(string Reason, int Amount, DateTime AtUtc);

public sealed record JourneyDto(
    int CareerLevel, int Xp, int CompletedMissions, int TotalMissions,
    IReadOnlyList<JourneyLevelDto> Levels, MissionDto? NextMission, IReadOnlyList<XpEntryDto> History);

public class CareerJourneyService(AppDbContext db, CareerEvidenceService evidence)
{
    private sealed record Mission(
        string Key, int Level, string Title, string Description, string Criteria, int Xp, string Route,
        Func<CareerEvidence, bool> Done);

    public static readonly (string Title, string Badge)[] Levels =
    [
        ("Know yourself", "Profile Pioneer"), ("Build your CV", "CV Ready"), ("Upgrade your skills", "Skill Explorer"),
        ("Prove your skills", "Challenge Master"), ("Build your portfolio", "Portfolio Builder"),
        ("Master interviews", "Interview Achiever"), ("Find your opportunity", "Job Hunter"), ("Become career ready", "Career Ready")
    ];

    private static bool Completed(CareerEvidence e, string type) => e.CompletedSessions.Any(s => s.InterviewType == type);

    private static readonly Mission[] Missions =
    [
        new("profile", 1, "Complete your career profile", "Tell us where you are starting from.", "Finish onboarding.", 50, "/graduate/onboarding", e => e.Profile.HasCompletedOnboarding),
        new("target", 1, "Choose a target role", "Everything else is measured against it.", "A target role is saved on your profile.", 25, "/graduate/profile", e => !string.IsNullOrWhiteSpace(e.Profile.TargetRole)),
        new("interests", 1, "Describe your direction", "Say which industries or goal interest you.", "Preferred industries or a career goal is saved.", 25, "/graduate/profile", e => !string.IsNullOrWhiteSpace(e.Profile.PreferredIndustries) || !string.IsNullOrWhiteSpace(e.Profile.CareerGoal)),
        new("dna", 1, "Generate your Career DNA", "An assessment built from your own evidence.", "A Career DNA report exists.", 50, "/graduate/profile", e => e.Profile.CareerDnaJson != null),

        new("cv-upload", 2, "Upload a readable CV", "PDF or DOCX with selectable text.", "Text was extracted from your CV.", 40, "/graduate/cv", e => e.CvText.Length >= 200),
        new("cv-analyze", 2, "Run an ATS analysis", "Review what a screening system would see.", "One AI analysis completed.", 60, "/graduate/cv", e => e.Analyses.Count > 0),
        new("cv-improve", 2, "Improve your CV", "Fix the highest-priority findings and analyze again.", "A later analysis scores higher than your first, or an analysis reaches 85.", 100, "/graduate/cv",
            e => e.Analyses.Count > 0 && (e.Analyses.Any(a => a.ScorePercent >= 85) || e.Analyses.Skip(1).Any(a => a.ScorePercent > e.Analyses[0].ScorePercent))),

        new("skill-plan", 3, "Pick priority skills", "Add gaps from Skill Gap Analysis to your plan.", "A skill item is on your plan.", 30, "/graduate/skill-gap", e => e.Plan.Any(p => p.Category == "Skill")),
        new("skill-learn", 3, "Complete a learning mission", "Finish a planned skill and note what you did.", "A skill plan item is completed with a note of at least 30 characters.", 60, "/graduate/roadmap",
            e => e.Plan.Any(p => p.Category == "Skill" && p.Status == "Completed" && (p.Notes?.Trim().Length ?? 0) >= 30)),
        new("skill-quiz", 3, "Pass a knowledge quiz", "Show your progress on the role curriculum.", "One quiz level passed.", 60, "/graduate/roadmap", e => e.QuizLevelsPassed >= 1),

        new("challenge-1", 4, "Pass a practical challenge", "Prove one skill in the Skill Proof Lab.", "Score 70 or more on a challenge.", 100, "/graduate/skill-lab", e => e.VerifiedSkills.Count >= 1),
        new("challenge-2", 4, "Prove a second skill", "Breadth matters to employers.", "Challenges passed in two different skills.", 100, "/graduate/skill-lab", e => e.VerifiedSkills.Count >= 2),

        new("project-1", 5, "Document your first project", "Describe the work and your own contribution.", "One project passes the portfolio checks.", 80, "/graduate/portfolio", e => e.ValidProjects >= 1),
        new("project-2", 5, "Add a second project", "Show range.", "Two projects pass the portfolio checks.", 80, "/graduate/portfolio", e => e.ValidProjects >= 2),
        new("project-link", 5, "Link your evidence", "Let a reviewer open the work.", "A valid project has a GitHub or demo link.", 40, "/graduate/portfolio",
            e => e.Projects.Any(p => PortfolioRules.IsValid(p) && (p.GitHubUrl != null || p.DemoUrl != null))),

        new("interview-hr", 6, "Finish an HR interview", "Answer every question and read the feedback.", "A fully graded HR session.", 70, "/graduate/interview", e => Completed(e, "HR")),
        new("interview-tech", 6, "Finish a technical interview", "Practise explaining your technical choices.", "A fully graded technical session.", 80, "/graduate/interview", e => Completed(e, "Technical")),
        new("interview-improve", 6, "Work on a weak area", "Run a recovery drill, or reach a strong answer.", "A drill finished, or an answer scored 8 or more.", 60, "/graduate/interview",
            e => e.RecoveryDrillDone || e.Questions.Any(q => q.FeedbackScore >= 8)),

        new("job-save", 7, "Save a relevant job", "Shortlist an opportunity.", "At least one saved job.", 30, "/graduate/jobs", e => e.SavedJobs > 0),
        new("job-tailor", 7, "Prepare a tailored application", "Use Prepare for this job on a listing.", "A job-specific preparation session exists.", 60, "/graduate/jobs", e => e.Sessions.Any(s => s.JobListingId != null)),
        new("job-apply", 7, "Record an application", "Track what you actually sent.", "At least one application is recorded.", 60, "/graduate/applications", e => e.Applications.Count > 0),

        new("report", 8, "Run the readiness assessment", "Take a snapshot of where you stand.", "A career report has been generated.", 50, "/graduate/reports", e => e.ReportCount > 0),
        new("ready", 8, "Meet every readiness criterion", "CV, verified skills, portfolio, interviews and profile.", "All criteria on the report are met.", 200, "/graduate/reports", e => Criteria(e).All(c => c.Met))
    ];

    public static IReadOnlyList<ReadinessCriterion> Criteria(CareerEvidence e)
    {
        var avg = e.InterviewAverage;
        return
        [
            new("CV score of 70 or more", e.CvScore >= 70, e.CvScore is { } s ? $"Latest AI analysis: {s}/100." : "No AI analysis yet.", "/graduate/cv"),
            new("Two skills proven in challenges", e.VerifiedSkills.Count >= 2, $"{e.VerifiedSkills.Count} proven.", "/graduate/skill-lab"),
            new("Two complete portfolio projects", e.ValidProjects >= 2, $"{e.ValidProjects} pass the checks.", "/graduate/portfolio"),
            new("HR and technical interviews, average 6/10", Completed(e, "HR") && Completed(e, "Technical") && avg >= 6,
                avg is { } a ? $"Average answer score {a:0.0}/10." : "No graded answers yet.", "/graduate/interview"),
            new("Profile at least 80% complete", e.ProfileCompletion >= 80, $"{e.ProfileCompletion}% complete.", "/graduate/profile")
        ];
    }

    /// <summary>Separate dimensions on purpose; null means "not assessed", never zero.</summary>
    public static ReadinessSnapshot Snapshot(CareerEvidence e, int evidencedPercent)
    {
        var avg = e.InterviewAverage;
        var verified = CareerCatalog.RoleSkills.TryGetValue(e.RoleKey, out var required)
            ? required.Count(r => e.VerifiedSkills.Any(v => CareerCatalog.MatchesSkill(r, v))) * 100 / required.Length : 0;
        ReadinessDimension[] dimensions =
        [
            new("Profile completeness", e.ProfileCompletion, "Weighted share of profile fields filled in.", "/graduate/profile"),
            new("CV readiness", e.CvScore, e.CvScore is null ? "Run an AI analysis to assess this." : "Score of your latest AI analysis.", "/graduate/cv"),
            new("Technical evidence", verified, $"Share of {e.RoleKey} skills proven in challenges. {evidencedPercent}% appear in your CV, portfolio or challenges.", "/graduate/skill-gap"),
            new("Portfolio readiness", Math.Min(e.ValidProjects, 3) * 100 / 3, "Complete projects out of a target of three.", "/graduate/portfolio"),
            new("Interview preparation", avg is null ? null : (int)Math.Round(avg.Value * 10), avg is null ? "No graded answers yet." : "Average AI estimate over your graded answers.", "/graduate/interview"),
            new("Job alignment", e.TopJobMatch, "Average match score of your three best current listings.", "/graduate/jobs")
        ];
        var criteria = Criteria(e);
        var strengths = new List<string>();
        if (e.VerifiedSkills.Count > 0) strengths.Add($"Proven in challenges: {string.Join(", ", e.VerifiedSkills)}.");
        if (e.ValidProjects > 0) strengths.Add($"{e.ValidProjects} documented portfolio project(s).");
        strengths.AddRange(criteria.Where(c => c.Met).Select(c => c.Name + "."));
        return new ReadinessSnapshot(DateTime.UtcNow, dimensions, criteria, strengths, criteria.All(c => c.Met));
    }

    /// <summary>
    /// Pays XP for missions whose evidence now exists. A level opens only when the previous one is complete.
    /// The unique (UserId, Key) index makes every reward one-time, also under parallel requests.
    /// </summary>
    public async Task<JourneyDto> GetAsync(int userId)
    {
        var e = await evidence.GatherAsync(userId);
        var paid = await db.XpTransactions.AsNoTracking().Where(x => x.UserId == userId).ToDictionaryAsync(x => x.Key);

        var open = true;
        for (var level = 1; level <= Levels.Length && open; level++)
        {
            foreach (var m in Missions.Where(m => m.Level == level && !paid.ContainsKey(m.Key) && m.Done(e)))
            {
                var tx = new XpTransaction { UserId = userId, Key = m.Key, Amount = m.Xp, Reason = m.Title };
                db.XpTransactions.Add(tx);
                try
                {
                    await db.SaveChangesAsync();
                    paid[m.Key] = tx;
                }
                catch (DbUpdateException)
                {
                    db.Entry(tx).State = EntityState.Detached;
                    paid[m.Key] = await db.XpTransactions.AsNoTracking().FirstAsync(x => x.UserId == userId && x.Key == m.Key);
                }
            }
            open = Missions.Where(m => m.Level == level).All(m => paid.ContainsKey(m.Key));
        }

        var levels = new List<JourneyLevelDto>();
        var previousDone = true;
        foreach (var (info, index) in Levels.Select((l, i) => (l, i + 1)))
        {
            var mine = Missions.Where(m => m.Level == index).ToList();
            var done = mine.All(m => paid.ContainsKey(m.Key));
            var status = done ? "completed" : previousDone ? "active" : "locked";
            levels.Add(new JourneyLevelDto(index, info.Title, info.Badge, status,
                [.. mine.Select(m => new MissionDto(m.Key, m.Title, m.Description, m.Criteria, m.Xp, m.Route,
                    paid.ContainsKey(m.Key) ? "completed" : status == "locked" ? "locked" : "active",
                    paid.GetValueOrDefault(m.Key)?.CreatedAtUtc))]));
            previousDone = previousDone && done;
        }

        var quizXp = await db.GraduateQuestProgress.Where(p => p.UserId == userId).Select(p => p.Xp).FirstOrDefaultAsync();
        var history = paid.Values.Select(x => new XpEntryDto(x.Reason, x.Amount, x.CreatedAtUtc))
            .OrderByDescending(x => x.AtUtc).ToList();
        if (quizXp > 0) history.Add(new XpEntryDto("Knowledge quizzes and recovery drills (total)", quizXp, DateTime.UtcNow));

        return new JourneyDto(
            levels.FirstOrDefault(l => l.Status == "active")?.Level ?? Levels.Length,
            paid.Values.Sum(x => x.Amount) + quizXp,
            paid.Count, Missions.Length, levels,
            levels.SelectMany(l => l.Missions).FirstOrDefault(m => m.Status == "active"),
            history);
    }

    public async Task<ReadinessSnapshot> CurrentReadinessAsync(int userId)
    {
        var e = await evidence.GatherAsync(userId);
        return Snapshot(e, (await evidence.SkillGapAsync(e, null, null)).EvidencedPercent);
    }

    public async Task<ReadinessSnapshot> SaveReportAsync(int userId)
    {
        var snapshot = await CurrentReadinessAsync(userId);
        db.CareerReports.Add(new CareerReport { UserId = userId, ReportJson = JsonSerializer.Serialize(snapshot) });
        await db.SaveChangesAsync();
        return snapshot;
    }

    public async Task<IReadOnlyList<ReadinessSnapshot>> ReportHistoryAsync(int userId) =>
        [.. (await db.CareerReports.AsNoTracking().Where(r => r.UserId == userId)
                .OrderByDescending(r => r.Id).Take(20).ToListAsync())
            .Select(r => JsonSerializer.Deserialize<ReadinessSnapshot>(r.ReportJson)!)];
}
