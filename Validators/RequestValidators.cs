using System.Text.RegularExpressions;
using FluentValidation;
using UniSphere.Api.Models;
using UniSphere.Api.Services;
using UniSphere.Api.Controllers;
using UniSphere.Api.Dtos;

namespace UniSphere.Api.Validators;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    // Egyptian mobile (010/011/012/015, with optional 0 or +20) or a general
    // international number as fallback.
    private const string EgyptianPhone = @"^(\+20|0)?1[0125]\d{8}$";
    private const string InternationalPhone = @"^\+?[1-9]\d{7,14}$";
    private const string PasswordPattern = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z0-9]).{8,}$";

    private static bool IsStudent(RegisterRequest x) => IsRole(x, UserType.Student);
    private static bool IsDoctor(RegisterRequest x) => IsRole(x, UserType.Doctor);
    private static bool IsGraduate(RegisterRequest x) => IsRole(x, UserType.Graduate);
    private static bool IsRole(RegisterRequest x, UserType type) =>
        RegistrationRules.TryParseUserType(x.Role, out var parsed) && parsed == type;

    public RegisterRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().WithMessage("First name is required.").MaximumLength(100);
        RuleFor(x => x.MiddleName).MaximumLength(100).When(x => !string.IsNullOrEmpty(x.MiddleName));
        RuleFor(x => x.LastName).NotEmpty().WithMessage("Last name is required.").MaximumLength(100);

        RuleFor(x => x.PhoneNumber).NotEmpty().WithMessage("Phone number is required.")
            .Must(p => p is not null && (Regex.IsMatch(p.Trim(), EgyptianPhone) || Regex.IsMatch(p.Trim(), InternationalPhone)))
            .WithMessage("Enter a valid phone number (e.g. 01012345678 or +201012345678).");

        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.").EmailAddress().WithMessage("Enter a valid email address.").MaximumLength(200);
        RuleFor(x => x.Gender).NotEmpty().WithMessage("Gender is required.")
            .Must(g => string.Equals(g?.Trim(), "Male", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(g?.Trim(), "Female", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Gender must be Male or Female.");
        RuleFor(x => x.Age)
            .Must((req, age) =>
            {
                // An unknown role is reported by the Role rule; don't double-report here.
                if (!RegistrationRules.TryParseUserType(req.Role, out var type)) return true;
                return age >= RegistrationRules.GetMinAge(type) && age <= RegistrationRules.MaxAge;
            })
            .WithMessage(req => RegistrationRules.TryParseUserType(req.Role, out var type)
                ? $"{RegistrationRules.MinAgeMessage(type)} Maximum age is {RegistrationRules.MaxAge}."
                : "Age is invalid for the selected role.");

        RuleFor(x => x.Role).NotEmpty().WithMessage("Role is required.")
            .Must(r => RegistrationRules.TryParseUserType(r, out _))
            .WithMessage("Role must be Student, Doctor or Graduate. Admin accounts can't be created through registration.");

        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.")
            .Matches(PasswordPattern).WithMessage("Password must be at least 8 characters and include an upper-case letter, a lower-case letter, a digit and a symbol.");
        RuleFor(x => x.ConfirmPassword).NotEmpty().WithMessage("Please confirm your password.")
            .Equal(x => x.Password).WithMessage("Passwords do not match.");

        When(x => IsStudent(x), () =>
        {
            RuleFor(x => x.AcademicYear).NotNull().WithMessage("Academic year is required for students.")
                .InclusiveBetween(1, 5).WithMessage("Academic year must be between 1 and 5.");
            RuleFor(x => x.Department).NotEmpty().WithMessage("Department is required for students.").MaximumLength(200);
        });

        When(x => IsDoctor(x), () =>
        {
            RuleFor(x => x.Specialization).NotEmpty().WithMessage("Specialization is required for doctors.").MaximumLength(200);
        });

        When(x => IsGraduate(x), () =>
        {
            RuleFor(x => x.GraduatedFrom).NotEmpty().WithMessage("Graduated from is required for graduates.").MaximumLength(200);
            RuleFor(x => x.GraduationYear).NotNull().WithMessage("Graduation year is required for graduates.")
                .InclusiveBetween(1950, DateTime.UtcNow.Year).WithMessage($"Graduation year must be between 1950 and {DateTime.UtcNow.Year}.");
            RuleFor(x => x.Department).NotEmpty().WithMessage("Department is required for graduates.").MaximumLength(200);
        });
    }
}

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.").EmailAddress();
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.");
    }
}

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Current password is required.");
        RuleFor(x => x.NewPassword).NotEmpty().WithMessage("New password is required.").MinimumLength(8).WithMessage("New password must be at least 8 characters.");
    }
}

public class CreateStudyItemRequestValidator : AbstractValidator<CreateStudyItemRequest>
{
    public CreateStudyItemRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required.").MaximumLength(200);
        RuleFor(x => x.Hours).InclusiveBetween(0, 12).WithMessage("Hours must be between 0 and 12.");
        RuleFor(x => x.Date).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(500).When(x => !string.IsNullOrEmpty(x.Note));
    }
}

public class CreateEventRequestValidator : AbstractValidator<CreateEventRequest>
{
    public CreateEventRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required.").MaximumLength(200);
        RuleFor(x => x.Location).NotEmpty().WithMessage("Location is required.").MaximumLength(200);
        RuleFor(x => x.StartsAtUtc).NotEmpty();
        RuleFor(x => x.Description).MaximumLength(1000).When(x => !string.IsNullOrEmpty(x.Description));
    }
}

public class PatchUserRoleRequestValidator : AbstractValidator<PatchUserRoleRequest>
{
    public PatchUserRoleRequestValidator()
    {
        RuleFor(x => x.Role).NotEmpty().WithMessage("Role is required.")
            .Must(r => Enum.TryParse<UserRole>(r?.Trim(), ignoreCase: true, out _))
            .WithMessage("Role must be Student, Graduate, Doctor or Admin.");
    }
}

public class SearchQueryValidator : AbstractValidator<SearchQuery>
{
    public SearchQueryValidator()
    {
        RuleFor(x => x.Q).NotEmpty().WithMessage("Search text is required.")
            .MinimumLength(2).WithMessage("Type at least 2 characters to search.")
            .MaximumLength(100).WithMessage("Search text is too long.");
        RuleFor(x => x.Take).InclusiveBetween(1, 50).WithMessage("Take must be between 1 and 50.");
        RuleFor(x => x.Type)
            .Must(t => string.IsNullOrWhiteSpace(t) || SearchController.KnownTypes.Contains(t.Trim().ToLowerInvariant()))
            .WithMessage("Unknown search type filter.");
    }
}

public class SendChatMessageRequestValidator : AbstractValidator<SendChatMessageRequest>
{
    public SendChatMessageRequestValidator()
    {
        RuleFor(x => x.Provider).NotEmpty().WithMessage("Provider is required.").MaximumLength(50);
        RuleFor(x => x.Content).NotEmpty().WithMessage("Message is required.")
            .MaximumLength(2000).WithMessage("Message must be at most 2000 characters.");
    }
}

public class RenameConversationRequestValidator : AbstractValidator<RenameConversationRequest>
{
    public RenameConversationRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required.")
            .MaximumLength(100).WithMessage("Title must be at most 100 characters.");
    }
}

public class CreateMaterialRequestValidator : AbstractValidator<CreateMaterialRequest>
{
    public static readonly string[] AllowedTypes =
        { "Summary", "Lecture", "Assignment", "Exam", "Announcement" };

    public CreateMaterialRequestValidator()
    {
        RuleFor(x => x.CourseId).GreaterThan(0).WithMessage("CourseId must be positive.");
        RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required.").MaximumLength(200);
        RuleFor(x => x.Type).NotEmpty().WithMessage("Type is required.")
            .Must(t => AllowedTypes.Contains(t?.Trim() ?? "", StringComparer.OrdinalIgnoreCase))
            .WithMessage("Type must be Summary, Lecture, Assignment, Exam or Announcement.");
        RuleFor(x => x.Description).MaximumLength(2000).When(x => !string.IsNullOrEmpty(x.Description));
        RuleFor(x => x.Content).MaximumLength(20000).When(x => !string.IsNullOrEmpty(x.Content));
        RuleFor(x => x.FileUrl).MaximumLength(500)
            .Must(u => string.IsNullOrEmpty(u) || Uri.TryCreate(u, UriKind.Absolute, out _))
            .When(x => !string.IsNullOrEmpty(x.FileUrl))
            .WithMessage("FileUrl must be a valid absolute URL.");
    }
}

public class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().WithMessage("FullName is required.").MaximumLength(200);
        RuleFor(x => x.Major).NotEmpty().WithMessage("Major is required.").MaximumLength(200);
        RuleFor(x => x.AvatarUrl).MaximumLength(500).When(x => !string.IsNullOrEmpty(x.AvatarUrl));
    }
}

public class CreateEnrollmentRequestValidator : AbstractValidator<CreateEnrollmentRequest>
{
    public CreateEnrollmentRequestValidator()
    {
        RuleFor(x => x.CourseId).GreaterThan(0).WithMessage("CourseId must be positive.");
    }
}

public class UpdateGradeRequestValidator : AbstractValidator<UpdateGradeRequest>
{
    public UpdateGradeRequestValidator()
    {
        RuleFor(x => x.GradePoints).InclusiveBetween(0, 4.0).When(x => x.GradePoints.HasValue).WithMessage("Grade points must be between 0 and 4.0.");
        RuleFor(x => x.AttendancePercent).InclusiveBetween(0, 100).When(x => x.AttendancePercent.HasValue).WithMessage("Attendance must be between 0 and 100.");
    }
}

public class SendNotificationRequestValidator : AbstractValidator<SendNotificationRequest>
{
    public SendNotificationRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(1000);
        RuleFor(x => x).Must(x => x.UserId.HasValue || x.RoleId.HasValue)
            .WithMessage("Either UserId or RoleId must be specified.");
    }
}

public class CreateAnnouncementRequestValidator : AbstractValidator<CreateAnnouncementRequest>
{
    public CreateAnnouncementRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(2000);
    }
}

public class CreatePaperRequestValidator : AbstractValidator<CreatePaperRequest>
{
    public CreatePaperRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(500);
    }
}

// ---- Doctor module ----

public class DoctorMaterialValidator : AbstractValidator<CreateDoctorMaterialRequest>
{
    public static readonly string[] AllowedTypes = CreateMaterialRequestValidator.AllowedTypes;

    public DoctorMaterialValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required.").MaximumLength(200);
        RuleFor(x => x.Type).NotEmpty().WithMessage("Type is required.")
            .Must(t => AllowedTypes.Contains(t?.Trim() ?? "", StringComparer.OrdinalIgnoreCase))
            .WithMessage("Type must be Summary, Lecture, Assignment, Exam or Announcement.");
        RuleFor(x => x.Description).MaximumLength(2000).When(x => !string.IsNullOrEmpty(x.Description));
        RuleFor(x => x.Content).MaximumLength(200000).When(x => !string.IsNullOrEmpty(x.Content));
        RuleFor(x => x.FileUrl).MaximumLength(500)
            .Must(u => string.IsNullOrEmpty(u)
                || u.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase)
                || Uri.TryCreate(u, UriKind.Absolute, out _))
            .When(x => !string.IsNullOrEmpty(x.FileUrl))
            .WithMessage("FileUrl must be an uploaded file path or a valid absolute URL.");
    }
}

public class UpdateDoctorMaterialRequestValidator : AbstractValidator<UpdateDoctorMaterialRequest>
{
    public UpdateDoctorMaterialRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required.").MaximumLength(200);
        RuleFor(x => x.Type).NotEmpty().WithMessage("Type is required.")
            .Must(t => DoctorMaterialValidator.AllowedTypes.Contains(t?.Trim() ?? "", StringComparer.OrdinalIgnoreCase))
            .WithMessage("Type must be Summary, Lecture, Assignment, Exam or Announcement.");
        RuleFor(x => x.Description).MaximumLength(2000).When(x => !string.IsNullOrEmpty(x.Description));
        RuleFor(x => x.Content).MaximumLength(200000).When(x => !string.IsNullOrEmpty(x.Content));
        RuleFor(x => x.FileUrl).MaximumLength(500)
            .Must(u => string.IsNullOrEmpty(u)
                || u.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase)
                || Uri.TryCreate(u, UriKind.Absolute, out _))
            .When(x => !string.IsNullOrEmpty(x.FileUrl))
            .WithMessage("FileUrl must be an uploaded file path or a valid absolute URL.");
    }
}

public class CreateCourseAnnouncementRequestValidator : AbstractValidator<CreateCourseAnnouncementRequest>
{
    public CreateCourseAnnouncementRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required.").MaximumLength(200);
        RuleFor(x => x.Body).NotEmpty().WithMessage("Body is required.").MaximumLength(5000);
        RuleFor(x => x.Category).MaximumLength(50).When(x => !string.IsNullOrEmpty(x.Category));
    }
}

public class CreateCourseAssignmentRequestValidator : AbstractValidator<CreateCourseAssignmentRequest>
{
    public CreateCourseAssignmentRequestValidator()
    {
        RuleFor(x => x.DoctorUserId).GreaterThan(0).WithMessage("DoctorUserId must be positive.");
        RuleFor(x => x.CourseId).GreaterThan(0).WithMessage("CourseId must be positive.");
    }
}

public class QuizGenerationRequestValidator : AbstractValidator<QuizGenerationRequest>
{
    public QuizGenerationRequestValidator()
    {
        RuleFor(x => x.CourseId).GreaterThan(0).WithMessage("CourseId must be positive.");
        RuleFor(x => x.QuestionCount).InclusiveBetween(1, 15).WithMessage("Question count must be between 1 and 15.");
    }
}

public class SummarizeRequestValidator : AbstractValidator<SummarizeRequest>
{
    public SummarizeRequestValidator()
    {
        RuleFor(x => x.CourseId).GreaterThan(0);
        RuleFor(x => x.MaterialId).GreaterThan(0);
    }
}

public class LectureQuestionRequestValidator : AbstractValidator<LectureQuestionRequest>
{
    public LectureQuestionRequestValidator()
    {
        RuleFor(x => x.CourseId).GreaterThan(0).WithMessage("CourseId must be positive.");
        RuleFor(x => x.Question).NotEmpty().WithMessage("Question is required.").MaximumLength(1000);
    }
}
