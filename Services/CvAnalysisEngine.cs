using UniSphere.Api.Dtos;

namespace UniSphere.Api.Services;

public sealed class CvAnalysisEngine(GraduateAiService ai)
{
    public async Task<(CvAnalysisEvidence Result, string ProviderName, string Model)> AnalyzeAsync(
        string cvText, string? targetRole, CancellationToken ct, string? jobDescription = null)
    {
        var (result, provider, model) = await AnalyzeCoreAsync(cvText, targetRole, ct);
        var breakdown = Breakdown(CvSectionParser.Parse(cvText));
        return (result with { ScoreBreakdown = breakdown, JobFit = Fit(cvText, jobDescription) }, provider, model);
    }

    private async Task<(CvAnalysisEvidence Result, string ProviderName, string Model)> AnalyzeCoreAsync(
        string cvText, string? targetRole, CancellationToken ct)
    {
        var evidence = CvSectionParser.Parse(cvText);

        try
        {
            var (aiData, meta) = await ai.AnalyzeCvAsync(0, cvText, targetRole, ct);
            var score = ReconcileScore(aiData.ScorePercent, evidence);
            var strengths = Merge(aiData.Strengths,
                evidence.TechnicalSkills.Take(8).Select(s => $"Evidence found for {s}.").ToArray());
            var weaknesses = Merge(aiData.Weaknesses, evidence.Risks);
            var suggestions = Merge(aiData.Suggestions, BuildSuggestions(evidence));

            return (new CvAnalysisEvidence(
                aiData.OverallAssessment,
                score,
                strengths,
                weaknesses,
                suggestions,
                aiData.Rewrites.Select(r => new KeywordRewrite(r.Original, r.Rewritten, r.Why)).ToArray(),
                Merge(aiData.Keywords,
                    evidence.TechnicalSkills.Take(12).Select(s => s).ToArray()),
                evidence.TechnicalSkills,
                evidence.SoftSkills,
                evidence.Sections,
                evidence.Risks,
                meta.ProviderName,
                meta.Model,
                false) { AiScore = aiData.ScorePercent }, meta.ProviderName, meta.Model);
        }
        catch (ChatProviderException)
        {
            var fallbackScore = CalculateDeterministicScore(evidence);
            var (assessment, weaknesses, suggestions) = DeterministicReport(evidence);
            return (new CvAnalysisEvidence(
                assessment,
                fallbackScore,
                evidence.TechnicalSkills.Take(8).Select(s => $"Evidence found for {s}.").ToArray(),
                weaknesses,
                suggestions,
                Array.Empty<KeywordRewrite>(),
                evidence.TechnicalSkills.Take(12).ToArray(),
                evidence.TechnicalSkills,
                evidence.SoftSkills,
                evidence.Sections,
                evidence.Risks,
                "deterministic",
                "cv-rules-v1",
                true), "deterministic", "cv-rules-v1");
        }
    }

    private static int ReconcileScore(int aiScore, CvParseEvidence evidence)
    {
        var deterministic = CalculateDeterministicScore(evidence);
        return Math.Clamp((aiScore + deterministic) / 2, 0, 100);
    }

    private static int CalculateDeterministicScore(CvParseEvidence evidence) =>
        Math.Clamp(Breakdown(evidence).Sum(c => c.Points), 0, 100);

    /// <summary>The rules half of the score, one line per criterion so the graduate can see why.</summary>
    public static CvScoreItem[] Breakdown(CvParseEvidence e)
    {
        static CvScoreItem Section(string name, bool found, int max) =>
            new(name, found ? max : 0, max, found ? "Detected in the CV." : "Not detected in the extracted text.");
        var skills = Math.Min(10, e.TechnicalSkills.Count);
        return
        [
            new("Readable text", 20, 20, $"{e.EvidenceLength} characters were extracted."),
            Section("Contact details", e.HasContact, 10),
            Section("Professional summary", e.HasSummary, 10),
            Section("Education", e.HasEducation, 15),
            Section("Experience", e.HasExperience, 20),
            Section("Projects", e.HasProjects, 10),
            Section("Certifications", e.HasCertifications, 5),
            new("Technical skills", skills, 10, $"{e.TechnicalSkills.Count} recognised technical skill(s), one point each up to 10."),
            new("Soft skills", e.SoftSkills.Count > 0 ? 5 : 0, 5, $"{e.SoftSkills.Count} recognised soft skill(s).")
        ];
    }

    /// <summary>Job-specific fit, kept apart from the general score. "Not evidenced" is not "does not have".</summary>
    public static CvJobFit? Fit(string cvText, string? jobDescription)
    {
        var required = CareerCatalog.DetectSkills(jobDescription);
        if (required.Count == 0) return null;
        var found = required.Where(s => CareerCatalog.MatchesSkill(s, cvText)).ToArray();
        return new CvJobFit((int)Math.Round(found.Length * 100.0 / required.Count), found, [.. required.Except(found)]);
    }

    private static (string Assessment, string[] Weaknesses, string[] Suggestions) DeterministicReport(CvParseEvidence evidence)
    {
        var assessment = $"Deterministic ATS review: {evidence.Sections.Count} key section(s) detected, " +
            $"with {evidence.TechnicalSkills.Count} technical skill token(s) and {evidence.SoftSkills.Count} soft-skill token(s) found. " +
            "This fallback did not use a generative AI provider.";
        return (assessment, evidence.Risks.Count > 0 ? evidence.Risks.ToArray() : ["No critical parsing risks detected."], BuildSuggestions(evidence));
    }

    private static string[] BuildSuggestions(CvParseEvidence evidence)
    {
        var result = new List<string>();
        if (!evidence.HasSummary) result.Add("Add a concise professional summary tied to the target role.");
        if (!evidence.HasExperience) result.Add("Clarify internships or practical work experience with measurable outcomes.");
        if (!evidence.HasProjects) result.Add("Add selected projects that demonstrate the technical skills claimed in the CV.");
        if (!evidence.HasCertifications) result.Add("Add relevant certifications only when they appear in the source document.");
        if (evidence.TechnicalSkills.Count == 0) result.Add("Include technical skills supported by the CV's projects or experience.");
        if (evidence.Risks.Count == 0) result.Add("Keep the final section under consistent headings and preserve machine-readable text.");
        return result.Take(8).ToArray();
    }

    private static string[] Merge(IEnumerable<string> first, IEnumerable<string> second)
    {
        return first.Concat(second)
            .Select(v => v.Trim())
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }
}

public sealed record CvAnalysisEvidence(
    string OverallAssessment,
    int ScorePercent,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Weaknesses,
    IReadOnlyList<string> Suggestions,
    IReadOnlyList<KeywordRewrite> Rewrites,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> TechnicalSkills,
    IReadOnlyList<string> SoftSkills,
    IReadOnlyList<string> Sections,
    IReadOnlyList<string> ParsingRisks,
    string ProviderName,
    string Model,
    bool IsDeterministicFallback)
{
    public int? AiScore { get; init; }
    public IReadOnlyList<CvScoreItem>? ScoreBreakdown { get; init; }
    public CvJobFit? JobFit { get; init; }
}

public sealed record CvScoreItem(string Criterion, int Points, int Max, string Reason);

public sealed record CvJobFit(int FitPercent, IReadOnlyList<string> Evidenced, IReadOnlyList<string> NotEvidenced);
