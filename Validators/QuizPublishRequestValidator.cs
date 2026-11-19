using FluentValidation;
using UniSphere.Api.Dtos;

namespace UniSphere.Api.Validators;

/// <summary>Validates the exact request the publish-quiz feature posts.
/// Database-dependent checks (course exists, doctor assigned, material in
/// course) are enforced again server-side in QuizPublishService — the client
/// is never trusted for ownership or titles.</summary>
public class QuizPublishRequestValidator : AbstractValidator<QuizPublishRequest>
{
    private const int MaxQuestionTextLength = 1000;
    private const int MaxOptionLength = 300;
    private const int MaxExplanationLength = 2000;
    private const int MaxReferenceLength = 200;

    public QuizPublishRequestValidator()
    {
        RuleFor(x => x.CourseId).GreaterThan(0);

        RuleFor(x => x.Title)
            .Must(t => (t ?? "").Trim().Length is >= 3 and <= 150)
            .WithMessage("Title must contain 3-150 characters (trimmed).");

        RuleFor(x => x.Language)
            .Must(l => l is "en" or "ar")
            .WithMessage("Language must be 'en' or 'ar'.");

        RuleFor(x => x.Marks)
            .InclusiveBetween(1, 1000)
            .When(x => x.Marks.HasValue)
            .WithMessage("Marks, when provided, must be between 1 and 1000.");

        // Exam window (all optional; absent = always open). The close date
        // is the hard server-side submit deadline, so it must lie in the
        // future and after the open date. DurationMinutes is the advertised
        // soft time budget shown to students, not auto-enforced.
        RuleFor(x => x.ClosesAtUtc)
            .Must(c => c!.Value > DateTime.UtcNow)
            .When(x => x.ClosesAtUtc.HasValue)
            .WithMessage("The exam closing date must be in the future.");

        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(1, 600)
            .When(x => x.DurationMinutes.HasValue)
            .WithMessage("Duration, when provided, must be between 1 and 600 minutes.");

        RuleFor(x => x)
            .Must(r => !r.OpensAtUtc.HasValue
                       || !r.ClosesAtUtc.HasValue
                       || r.OpensAtUtc.Value < r.ClosesAtUtc.Value)
            .WithMessage("The open date must come before the close date.");

        RuleFor(x => x.Questions)
            .Must(q => q is { Count: >= 1 and <= 30 })
            .WithMessage("A quiz must contain between 1 and 30 questions.");

        RuleForEach(x => x.Questions).ChildRules(q =>
        {
            q.RuleFor(x => x.Question)
                .Must(s => !string.IsNullOrWhiteSpace(s))
                .WithMessage("Question text is required.")
                .MaximumLength(MaxQuestionTextLength);
            q.RuleFor(x => x.Options)
                .Must(o => o is { Count: >= 2 and <= 6 })
                .WithMessage("Each question needs between 2 and 6 options.");
            q.RuleForEach(x => x.Options)
                .Must(o => !string.IsNullOrWhiteSpace(o))
                .WithMessage("Options must be non-empty.")
                .MaximumLength(MaxOptionLength);
            q.RuleFor(x => x.CorrectIndex)
                .Must((quest, idx) => quest.Options is not null
                    && idx >= 0 && idx < quest.Options.Count)
                .WithMessage("CorrectIndex must point at one of the options.");
            q.RuleFor(x => x.Explanation)
                .MaximumLength(MaxExplanationLength);
            q.RuleFor(x => x.Reference)
                .MaximumLength(MaxReferenceLength);
        });
    }
}
