using System.Text.RegularExpressions;

namespace UniSphere.Api.Services;

public sealed record CvParseEvidence(
    IReadOnlyList<string> Sections,
    IReadOnlyList<string> TechnicalSkills,
    IReadOnlyList<string> SoftSkills,
    bool HasContact,
    bool HasSummary,
    bool HasEducation,
    bool HasExperience,
    bool HasProjects,
    bool HasCertifications,
    int EvidenceLength,
    IReadOnlyList<string> Risks);

public static class CvSectionParser
{
    private static readonly string[] TechnicalTokens =
    [
        "C#", ".NET", "ASP.NET", "Blazor", "SQL", "Python", "Java", "JavaScript", "TypeScript",
        "React", "Angular", "Vue", "Node.js", "Docker", "Kubernetes", "Azure", "AWS", "Git",
        "REST", "API", "HTML", "CSS", "Machine Learning", "Data Analysis", "Power BI", "TensorFlow",
        "MongoDB", "PostgreSQL", "MySQL", "Linux", "Agile", "Scrum"
    ];

    private static readonly string[] SoftTokens =
    ["communication", "teamwork", "leadership", "problem solving", "analytical", "time management",
        "adaptability", "collaboration", "presentation", "critical thinking"];

    public static CvParseEvidence Parse(string text)
    {
        var normalized = Normalize(text);
        var lower = normalized.ToLowerInvariant();

        bool ContainsSection(params string[] names) => names.Any(n =>
            Regex.IsMatch(lower,
                $@"(?m)^\s*{Regex.Escape(n)}\b[^\r\n]*(:|$)",
                RegexOptions.IgnoreCase));

        var sections = new List<string>();
        void Add(string name, bool present) { if (present) sections.Add(name); }

        var hasContact = Regex.IsMatch(normalized, @"(?i)([\w.-]+@[\w.-]+\.[A-Za-z]{2,}|(?:\+?\d{1,3}[\s-]?)?(?:\(?\d{2,4}\)?[\s-]?){2,3}\d{3,4})");
        var hasSummary = ContainsSection("summary", "professional summary", "profile", "objective");
        var hasEducation = ContainsSection("education", "academic background", "qualifications");
        var hasExperience = ContainsSection("experience", "work experience", "employment", "internship", "internships");
        var hasProjects = ContainsSection("projects", "project experience", "portfolio");
        var hasCertifications = ContainsSection("certifications", "certificates", "licenses");

        Add("Contact", hasContact); Add("Summary", hasSummary); Add("Education", hasEducation);
        Add("Experience", hasExperience); Add("Projects", hasProjects); Add("Certifications", hasCertifications);

        // Whole tokens only: "Java" must not match "JavaScript", nor "Git" match "digital".
        bool Mentions(string token) => Regex.IsMatch(lower,
            $@"(?<![a-z0-9]){Regex.Escape(token.ToLowerInvariant())}(?![a-z0-9#+])");
        var technical = TechnicalTokens.Where(Mentions).Distinct().ToArray();
        var soft = SoftTokens.Where(Mentions).Distinct().ToArray();

        var risks = new List<string>();
        if (!hasContact) risks.Add("Contact details could not be detected.");
        if (!hasSummary) risks.Add("A professional summary section is missing or unclear.");
        if (!hasEducation) risks.Add("Education details are missing or not clearly sectioned.");
        if (!hasExperience) risks.Add("Work or internship experience is missing or not clearly sectioned.");
        if (!hasProjects) risks.Add("A project section was not detected.");
        if (normalized.Length < 600) risks.Add("The extracted CV text is unusually short; formatting or extraction may have omitted content.");

        return new CvParseEvidence(sections, technical, soft, hasContact, hasSummary, hasEducation,
            hasExperience, hasProjects, hasCertifications, normalized.Length, risks);
    }

    private static string Normalize(string text) => Regex.Replace(text ?? "", @"\r\n?", "\n").Trim();
}
