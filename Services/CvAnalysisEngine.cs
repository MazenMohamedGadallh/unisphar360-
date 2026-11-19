using UniSphere.Api.Dtos;

namespace UniSphere.Api.Services;

public sealed class CvAnalysisEngine(GraduateAiService ai)
{
    public async Task<(CvAnalysisEvidence Result, string ProviderName, string Model)> AnalyzeAsync(
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
                false), meta.ProviderName, meta.Model);
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

    private static int CalculateDeterministicScore(CvParseEvidence evidence)
    {
        var score = 20;
        if (evidence.HasContact) score += 10;
        if (evidence.HasSummary) score += 10;
        if (evidence.HasEducation) score += 15;
        if (evidence.HasExperience) score += 20;
        if (evidence.HasProjects) score += 10;
        if (evidence.HasCertifications) score += 5;
        score += Math.Min(10, evidence.TechnicalSkills.Count);
        if (evidence.SoftSkills.Count > 0) score += 5;
        return Math.Clamp(score, 0, 100);
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
    bool IsDeterministicFallback);
