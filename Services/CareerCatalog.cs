using System.Text.RegularExpressions;

namespace UniSphere.Api.Services;

/// <summary>
/// Static career taxonomy used by matching and skill-gap analysis.
/// Matching is deterministic: weights and rules are documented here so a score
/// can always be reproduced and reasoned about (no LLM-generated scores, no
/// randomness). Skill detection uses regex variants so both English and
/// common spellings appear consistently.
/// </summary>
public static class CareerCatalog
{
    /// <summary>Canonical skill name -> regex variants detected in job descriptions / CV text.</summary>
    public static readonly Dictionary<string, string> SkillRegexes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["C#"] = @"\bc#|csharp|\bnet\b|\.net\b",
        ["ASP.NET"] = @"asp\.?net",
        ["Java"] = @"\bjava\b(?!script)",
        ["Spring Boot"] = @"spring\s?boot",
        ["JavaScript"] = @"java\s?script|\bjs\b",
        ["TypeScript"] = @"type\s?script",
        ["React"] = @"\breact\b",
        ["Angular"] = @"angular",
        ["Vue.js"] = @"\bvue(\.js)?\b",
        ["Node.js"] = @"node\.?js|\bexpress\b",
        ["Python"] = @"\bpython\b",
        ["TensorFlow"] = @"tensorflow",
        ["PyTorch"] = @"py\s?torch",
        ["Machine Learning"] = @"machine\s?learning|\bml\b",
        ["Deep Learning"] = @"deep\s?learning",
        ["NLP"] = @"natural\s?language|\bnlp\b",
        ["Data Analysis"] = @"data\s?analysis|analytics|pandas",
        ["Statistics"] = @"statistic",
        ["SQL"] = @"\bsql\b(?!ite)|postgre|mysql|\bmariadb\b",
        ["PostgreSQL"] = @"postgre",
        ["NoSQL"] = @"@nosql\b|.mongo",
        ["Git"] = @"\bgit\b",
        ["Docker"] = @"docker",
        ["Kubernetes"] = @"kubernetes|\bk8s\b",
        ["AWS"] = @"\baws\b|amazon\s?web",
        ["Azure"] = @"azure",
        ["Linux"] = @"linux|unix",
        ["CI/CD"] = @"ci\s?/?\s?cd|jenkins|github\s?actions",
        ["Testing"] = @"unit\s?test|\bxunit\b|\bpytest\b|selenium|qa\b|testing",
        ["Flutter"] = @"flutter",
        ["Mobile Development"] = @"mobile|android|ios",
        ["HTML/CSS"] = @"\bhtml\b|css3?|tailwind",
        ["PHP"] = @"\bphp\b|laravel",
        ["Go"] = @"\bgo(lang)?\b|\bgolang\b",
        ["C/C++"] = @"\bc\+\+|\bclang\b",
        ["Communication"] = @"communication",
        ["Teamwork"] = @"team\s?work|collaborat|team\s?player",
        ["Problem Solving"] = @"problem\s?solving|problem\s?solver",
        ["Time Management"] = @"time\s?manag",
        ["Agile/Scrum"] = @"agile|scrum|kanban",
        ["Power BI / Tableau"] = @"power\s?bi|tableau",
        ["Excel"] = @"\bexcel\b",
    };

    /// <summary>Target roles -> required skills (ordering = priority).</summary>
    public static readonly Dictionary<string, string[]> RoleSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Machine Learning Engineer"] = ["Python", "Machine Learning", "Deep Learning", "TensorFlow", "SQL", "Git", "Docker"],
        ["Data Scientist"] = ["Python", "Data Analysis", "Statistics", "SQL", "Machine Learning", "Power BI / Tableau", "Communication"],
        ["Data Analyst"] = ["SQL", "Excel", "Data Analysis", "Power BI / Tableau", "Python", "Communication"],
        ["Backend Developer"] = ["C#", "ASP.NET", "SQL", "Git", "Docker", "Testing", "Azure"],
        ["Full-Stack Developer"] = ["JavaScript", "React", "Node.js", "SQL", "Git", "HTML/CSS", "CI/CD"],
        ["Frontend Developer"] = ["JavaScript", "React", "TypeScript", "HTML/CSS", "Git"],
        ["Mobile Developer"] = ["Flutter", "Mobile Development", "Javascript", "Git"],
        ["DevOps Engineer"] = ["Docker", "Kubernetes", "AWS", "CI/CD", "Linux", "Git"],
        ["QA Engineer"] = ["Testing", "Git", "Agile/Scrum", "JavaScript"],
        ["Software Engineer"] = ["C#", "Java", "Python", "SQL", "Git", "Testing", "Agile/Scrum"],
        ["NLP Engineer"] = ["Python", "NLP", "Machine Learning", "Deep Learning", "Git"]
    };

    /// <summary>Common skills offered for the default profile bootstrap.</summary>
    public static readonly string[] DefaultSkills = ["Communication", "Teamwork", "Problem Solving", "Time Management"];

    /// <summary>Curated interview question bank per role family (deterministic fallback
    /// when no AI provider is configured — real questions, collected per role skill set).</summary>
    public static readonly Dictionary<string, string[]> InterviewQuestions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Technical"] =
        [
            "Walk me through a project you built end to end and the part you are proudest of.",
            "How do you test your code, and what do you do when a test fails in production?",
            "Describe a bug that took you a long time to fix. What made it hard and what did you learn?",
            "How do you decide between building something yourself and using a library or service?",
            "A requirement changed in the middle of your project. How did you adapt?"
        ],
        ["Behavioral"] =
        [
            "Tell me about a time you disagreed with a teammate. How did you resolve it?",
            "Describe a deadline you nearly missed and what you changed to recover.",
            "Give an example of feedback you received that improved your work.",
            "Tell me about a time you had to learn a new tool quickly to deliver a task.",
            "What achievement are you most proud of since graduating, and why?"
        ],
        ["Role specific"] =
        [
            "Which technologies are you strongest in, and demonstrate it with a short example.",
            "Why do you want to work as {role}, and what do you already know about us?",
            "Where do you want to be professionally in three years?",
            "What would your first 90 days in {role} look like?"
        ]
    };

    /// <summary>Detect canonical skills present in a text block (case-insensitive regex).</summary>
    public static List<string> DetectSkills(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        foreach (var (name, pattern) in SkillRegexes)
        {
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
                result.Add(name);
        }
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    /// <summary>True when the rule matches the skill name (loose, forgiving matching).</summary>
    public static bool MatchesSkill(string canonicalSkill, string candidate) =>
        SkillRegexes.TryGetValue(canonicalSkill, out var pattern)
            ? Regex.IsMatch(candidate, pattern, RegexOptions.IgnoreCase)
            : string.Equals(canonicalSkill, candidate, StringComparison.OrdinalIgnoreCase);
}
