using FluentValidation.TestHelper;
using UniSphere.Api.Dtos;
using UniSphere.Api.Validators;
using Xunit;

namespace UniSphere.Api.Tests;

/// <summary>Validates the exact request the Exam Assistant UI posts,
/// including the doctor's normal selection (10 questions, mixed, English)
/// and every invalid-input variant the UI must never silently accept.</summary>
public class ExamAssistantGenerationRequestValidatorTests
{
    private readonly ExamAssistantGenerationRequestValidator _validator = new();

    private static ExamAssistantGenerationRequest ValidLikeTheUi(
        int count = 10, string difficulty = "mixed", string language = "en") =>
        new(1, [2], count, difficulty, language);

    [Fact]
    public void TheDoctorsUsualSelection_IsValid()
    {
        var result = _validator.TestValidate(ValidLikeTheUi());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void MissingCourses_OrMaterials_AreRejected()
    {
        _validator.TestValidate(ValidLikeTheUi() with { CourseId = 0 })
            .ShouldHaveValidationErrorFor(r => r.CourseId);
        _validator.TestValidate(ValidLikeTheUi() with { MaterialIds = [] })
            .ShouldHaveValidationErrorFor(r => r.MaterialIds);
    }

    [Fact]
    public void QuestionCount_OutsideRange_IsRejected()
    {
        _validator.TestValidate(ValidLikeTheUi(count: 0))
            .ShouldHaveValidationErrorFor(r => r.QuestionCount);
        _validator.TestValidate(ValidLikeTheUi(count: 31))
            .ShouldHaveValidationErrorFor(r => r.QuestionCount);
    }

    [Theory]
    [InlineData("weird")]
    [InlineData("")]
    public void UnknownDifficulty_IsRejected(string difficulty)
    {
        _validator.TestValidate(ValidLikeTheUi(difficulty: difficulty))
            .ShouldHaveValidationErrorFor(r => r.Difficulty);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("")]
    public void UnknownLanguage_IsRejected(string language)
    {
        _validator.TestValidate(ValidLikeTheUi(language: language))
            .ShouldHaveValidationErrorFor(r => r.Language);
    }
}
