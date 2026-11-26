using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniSphere.Api.Dtos;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/graduation")]
public class GraduationController : ControllerBase
{
    private readonly GraduationService _graduation;
    private readonly IValidator<GraduationApplyRequest> _applyValidator;
    private readonly IValidator<ReviewGraduationApplicationRequest> _reviewValidator;

    public GraduationController(
        GraduationService graduation,
        IValidator<GraduationApplyRequest> applyValidator,
        IValidator<ReviewGraduationApplicationRequest> reviewValidator)
    {
        _graduation = graduation;
        _applyValidator = applyValidator;
        _reviewValidator = reviewValidator;
    }

    [HttpGet("overview")]
    public async Task<ActionResult<GraduationOverviewResponse>> Overview()
    {
        var overview = await _graduation.GetOverviewAsync(User.GetUserId());
        return overview is null ? NotFound("No account for this user.") : overview;
    }

    [HttpPost("apply")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Apply(GraduationApplyRequest request)
    {
        var validation = await _applyValidator.ValidateAsync(request);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));

        var result = await _graduation.ApplyAsync(User.GetUserId(), request);
        return result.Outcome switch
        {
            GraduationService.GraduationApplyOutcome.Applied => Ok(result.Application),
            GraduationService.GraduationApplyOutcome.ActiveApplicationExists
                => Conflict(new { Message = "You already have an active graduation application.", result.Application }),
            GraduationService.GraduationApplyOutcome.AlreadyGraduated
                => Conflict("This account has already graduated."),
            GraduationService.GraduationApplyOutcome.NotStudent
                => Forbid("Only students can apply for graduation."),
            GraduationService.GraduationApplyOutcome.UserNotFound
                => NotFound("No active account for this user."),
            _
                => UnprocessableEntity(new
                {
                    Title = "Not eligible to apply for graduation.",
                    BlockingReasons = result.BlockingReasons
                })
        };
    }

    [HttpGet("application")]
    public async Task<ActionResult<GraduationApplicationSummaryDto>> Application()
    {
        var overview = await _graduation.GetOverviewAsync(User.GetUserId());
        if (overview is null) return NotFound("No account for this user.");
        if (overview.Application is null)
            return NotFound("No graduation application for this user.");
        return overview.Application;
    }

    [HttpPost("application/withdraw")]
    public async Task<IActionResult> Withdraw()
    {
        var result = await _graduation.WithdrawAsync(User.GetUserId());
        return result.Outcome switch
        {
            GraduationService.GraduationWithdrawOutcome.Withdrawn => Ok(result.Application),
            _ => NotFound("No active graduation application to withdraw.")
        };
    }

    [HttpGet("ceremony")]
    public async Task<ActionResult<GraduationCeremonyDto>> Ceremony()
    {
        var ceremony = await _graduation.GetNextCeremonyAsync();
        return ceremony is null
            ? NotFound("No upcoming graduation ceremony has been scheduled yet.")
            : ceremony;
    }

    [HttpGet("certificate")]
    public async Task<ActionResult<GraduationCertificateDto>> Certificate()
    {
        var result = await _graduation.GetCertificateAsync(User.GetUserId());
        return result.Outcome switch
        {
            GraduationService.GraduationCertificateOutcome.Issued => Ok(result.Certificate),
            _ => NotFound("No conferred degree found for this user. Certificates unlock after your application is approved.")
        };
    }

    [HttpGet("applications")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PagedResultDto<GraduationApplicationAdminDto>>> Applications(
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
        => Ok(await _graduation.GetApplicationsAsync(status, page, pageSize));

    [HttpPost("applications/{applicationId:int}/review")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<GraduationApplicationSummaryDto>> Review(
        int applicationId, ReviewGraduationApplicationRequest request)
    {
        var validation = await _reviewValidator.ValidateAsync(request);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));

        var result = await _graduation.ReviewAsync(
            applicationId, request.Approve, request.Note, request.GraduationYear, User.GetUserId());

        return result.Outcome switch
        {
            GraduationService.GraduationReviewOutcome.NotFound
                => NotFound($"Graduation application {applicationId} not found."),
            GraduationService.GraduationReviewOutcome.AlreadyFinalized
                => Conflict(new { Message = "This application already has a final decision.", result.Application }),
            // Approved and Rejected both return the updated application.
            _ => Ok(result.Application)
        };
    }
}
