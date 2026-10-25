using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Models;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

[Authorize(Roles = "Student")]
[ApiController]
[Route("api/[controller]")]
public class CareerController : ControllerBase
{
    private readonly AppDbContext _db;
    public CareerController(AppDbContext db) => _db = db;

    /// <summary>Career recommendations based on the student's major and completed courses.</summary>
    [HttpGet("recommendations")]
    public async Task<ActionResult<IReadOnlyList<object>>> GetRecommendations()
    {
        var userId = User.GetUserId();
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return NotFound();

        // Get completed courses
        var completedCourses = await _db.Enrollments.AsNoTracking()
            .Where(e => e.UserId == userId && !string.IsNullOrEmpty(e.GradeLetter))
            .Include(e => e.Course)
            .Select(e => e.Course!.Title)
            .ToListAsync();

        var major = user.Major.ToLowerInvariant();
        var recommendations = new List<object>();

        // AI / Data Science career path
        if (major.Contains("ai") || major.Contains("data") || major.Contains("machine") || major.Contains("computer"))
        {
            recommendations.Add(new
            {
                Title = "Machine Learning Engineer",
                Description = "Design and implement ML models for production systems. Requires strong programming, statistics, and data modeling skills.",
                RequiredSkills = "Python, TensorFlow/PyTorch, SQL, Statistics, Cloud (AWS/GCP)",
                RecommendedCourses = "CS350 (Machine Learning), CS351 (Deep Learning), CS362 (NLP)",
                InternshipTypes = "ML Engineering Intern, Data Science Intern, AI Research Intern",
                MatchConfidence = completedCourses.Any(c => c.Contains("Machine Learning") || c.Contains("Deep Learning")) ? "High" : "Medium"
            });
            recommendations.Add(new
            {
                Title = "Data Scientist",
                Description = "Analyze complex datasets to extract insights and build predictive models. Strong analytical and communication skills needed.",
                RequiredSkills = "Python, R, SQL, Statistics, Data Visualization, Communication",
                RecommendedCourses = "CS350 (Machine Learning), CS330 (Database Systems), CS120 (Discrete Math)",
                InternshipTypes = "Data Science Intern, Business Intelligence Intern, Analytics Intern",
                MatchConfidence = "Medium"
            });
        }

        // Software Engineering career path
        if (major.Contains("software") || major.Contains("computer") || major.Contains("web"))
        {
            recommendations.Add(new
            {
                Title = "Full-Stack Developer",
                Description = "Build end-to-end web applications. Requires proficiency in frontend, backend, databases, and deployment.",
                RequiredSkills = "JavaScript/TypeScript, React/Angular, Node.js/.NET, SQL, Git, CI/CD",
                RecommendedCourses = "CS240 (Mobile App Dev), CS240 (Web Dev), CS330 (Database Systems)",
                InternshipTypes = "Software Engineering Intern, Full-Stack Developer Intern, QA Intern",
                MatchConfidence = completedCourses.Any(c => c.Contains("Web") || c.Contains("Mobile")) ? "High" : "Medium"
            });
        }

        // Always add generic recommendations
        recommendations.Add(new
        {
            Title = "Research Assistant",
            Description = "Work with professors on cutting-edge research. Great for students considering graduate studies.",
            RequiredSkills = "Academic writing, Literature review, Data analysis, Critical thinking",
            RecommendedCourses = "CS362 (NLP), CS430 (Advanced Databases), CS420 (Cloud Computing)",
            InternshipTypes = "Research Intern, Lab Assistant, Teaching Assistant",
            MatchConfidence = "Medium"
        });

        return Ok(recommendations);
    }

    /// <summary>Skill gap analysis based on target career vs completed courses.</summary>
    [HttpGet("skill-gaps")]
    public async Task<ActionResult<object>> GetSkillGaps()
    {
        var userId = User.GetUserId();
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return NotFound();

        var completedCourses = await _db.Enrollments.AsNoTracking()
            .Where(e => e.UserId == userId)
            .Include(e => e.Course)
            .Select(e => e.Course!.Title)
            .ToListAsync();

        var allCourses = await _db.Courses.AsNoTracking()
            .Where(c => user.UniversityId.HasValue && c.UniversityId == user.UniversityId)
            .EligibleForStudent(user)
            .Select(c => c.Title)
            .ToListAsync();
        var pendingCourses = allCourses.Except(completedCourses).ToList();

        return Ok(new
        {
            CompletedCourses = completedCourses,
            PendingCourses = pendingCourses,
            Recommendation = pendingCourses.Any()
                ? $"Consider taking {pendingCourses.First()} to broaden your skill set."
                : "You have explored a wide range of courses. Consider advanced topics or internships."
        });
    }
}
