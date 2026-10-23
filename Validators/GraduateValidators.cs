using FluentValidation;
using UniSphere.Api.Dtos;

namespace UniSphere.Api.Validators;

public class GraduateProfileUpdateRequestValidator : AbstractValidator<GraduateProfileUpdateRequest>
{
    private const int MaxText = 2000, MaxList = 300, MaxUrl = 300;

    public GraduateProfileUpdateRequestValidator()
    {
        RuleFor(x => x.Headline).MaximumLength(120).When(x => x.Headline is not null);
        RuleFor(x => x.CareerSummary).MaximumLength(MaxText).When(x => x.CareerSummary is not null);
        RuleFor(x => x.LinkedInUrl).Must(BeUrl).WithMessage("LinkedIn URL must start with http(s)://")
            .MaximumLength(MaxUrl).When(x => !string.IsNullOrWhiteSpace(x.LinkedInUrl));
        RuleFor(x => x.GitHubUrl).Must(BeUrl).WithMessage("GitHub URL must start with http(s)://")
            .MaximumLength(MaxUrl).When(x => !string.IsNullOrWhiteSpace(x.GitHubUrl));
        RuleFor(x => x.PortfolioUrl).Must(BeUrl).WithMessage("Portfolio URL must start with http(s)://")
            .MaximumLength(MaxUrl).When(x => !string.IsNullOrWhiteSpace(x.PortfolioUrl));
        RuleFor(x => x.PreferredJobTitles).MaximumLength(MaxList).When(x => x.PreferredJobTitles is not null);
        RuleFor(x => x.PreferredIndustries).MaximumLength(MaxList).When(x => x.PreferredIndustries is not null);
        RuleFor(x => x.PreferredLocations).MaximumLength(MaxList).When(x => x.PreferredLocations is not null);
        RuleFor(x => x.WorkModes).MaximumLength(100).When(x => x.WorkModes is not null);
        RuleFor(x => x.EmploymentType).MaximumLength(50).When(x => x.EmploymentType is not null);
        RuleFor(x => x.ExperienceLevel).MaximumLength(50).When(x => x.ExperienceLevel is not null);
        RuleFor(x => x.Languages).MaximumLength(MaxList).When(x => x.Languages is not null);
        RuleFor(x => x.CareerGoal).MaximumLength(500).When(x => x.CareerGoal is not null);
    }

    internal static bool BeUrl(string? url) =>
        string.IsNullOrWhiteSpace(url)
        || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}

public class GraduateSkillCreateRequestValidator : AbstractValidator<GraduateSkillCreateRequest>
{
    public GraduateSkillCreateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Level).MaximumLength(20).When(x => x.Level is not null);
    }
}

public class ProfileEntryCreateRequestValidator : AbstractValidator<ProfileEntryCreateRequest>
{
    public ProfileEntryCreateRequestValidator()
    {
        RuleFor(x => x.Kind).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Organization).MaximumLength(120).When(x => x.Organization is not null);
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null);
        RuleFor(x => x.Period).MaximumLength(60).When(x => x.Period is not null);
    }
}

public class ApplicationCreateRequestValidator : AbstractValidator<ApplicationCreateRequest>
{
    private static readonly string[] Statuses = ["Applied", "Interview", "Offer", "Rejected", "Withdrawn"];

    public ApplicationCreateRequestValidator()
    {
        RuleFor(x => x.JobTitle).NotEmpty().MaximumLength(120)
            .When(x => x.JobListingId is null);
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(120)
            .When(x => x.JobListingId is null);
        RuleFor(x => x.Status)
            .Must(s => string.IsNullOrWhiteSpace(s) || Statuses.Contains(s.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Status must be one of: Applied, Interview, Offer, Rejected, Withdrawn.");
        RuleFor(x => x.SourceUrl).Must(GraduateProfileUpdateRequestValidator.BeUrl)
            .WithMessage("Source URL must start with http(s)://")
            .When(x => !string.IsNullOrWhiteSpace(x.SourceUrl));
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
    }
}

public class ApplicationUpdateRequestValidator : AbstractValidator<ApplicationUpdateRequest>
{
    private static readonly string[] Statuses = ["Applied", "Interview", "Offer", "Rejected", "Withdrawn"];

    public ApplicationUpdateRequestValidator()
    {
        RuleFor(x => x.Status)
            .Must(s => string.IsNullOrWhiteSpace(s) || Statuses.Contains(s.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Status must be one of: Applied, Interview, Offer, Rejected, Withdrawn.")
            .When(x => x.Status is not null);
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
    }
}

public class RoadmapItemCreateRequestValidator : AbstractValidator<RoadmapItemCreateRequest>
{
    public RoadmapItemCreateRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Category).MaximumLength(40).When(x => x.Category is not null);
        RuleFor(x => x.Status).MaximumLength(20);
        RuleFor(x => x.Notes).MaximumLength(500).When(x => x.Notes is not null);
    }
}

public class RoadmapItemUpdateRequestValidator : AbstractValidator<RoadmapItemUpdateRequest>
{
    public RoadmapItemUpdateRequestValidator()
    {
        RuleFor(x => x.Title).MaximumLength(200).When(x => x.Title is not null);
        RuleFor(x => x.Category).MaximumLength(40).When(x => x.Category is not null);
        RuleFor(x => x.Status).MaximumLength(20);
        RuleFor(x => x.Notes).MaximumLength(500).When(x => x.Notes is not null);
    }
}

public class InterviewSessionCreateRequestValidator : AbstractValidator<InterviewSessionCreateRequest>
{
    public InterviewSessionCreateRequestValidator()
    {
        RuleFor(x => x.TargetRole).MaximumLength(80);
    }
}

public class InterviewAnswerRequestValidator : AbstractValidator<InterviewAnswerRequest>
{
    public InterviewAnswerRequestValidator()
    {
        RuleFor(x => x.AnswerText).NotEmpty().MaximumLength(10000);
    }
}
