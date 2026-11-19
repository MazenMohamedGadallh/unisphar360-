using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Models;
using UniSphere.Api.Services;

namespace UniSphere.Api.Controllers;

/// <summary>
/// Authenticated course-material attachment downloads. The endpoint accepts
/// only a material id — never a file name or path. Authorization is decided
/// from the authenticated user:
///  - Admin: global access under the existing admin rules.
///  - Doctor: must be assigned to the material's course (same university).
///  - Student: must be enrolled in the material's course and in the same
///    university as that course.
/// Every authenticated caller who fails those checks receives 404, never a
/// 403 that would reveal a foreign material exists.
/// </summary>
[Authorize]
[ApiController]
[Route("api/materials")]
public class MaterialFilesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly DoctorAccessService _access;
    private readonly CourseMaterialFileStorage _files;

    public MaterialFilesController(
        AppDbContext db, DoctorAccessService access, CourseMaterialFileStorage files)
    {
        _db = db;
        _access = access;
        _files = files;
    }

    [HttpGet("{id:int}/file")]
    public async Task<IActionResult> Download(int id)
    {
        var me = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == User.GetUserId() && !u.IsDeleted && u.IsActive);
        if (me is null) return Unauthorized();

        var material = await _db.CourseMaterials.AsNoTracking()
            .Include(m => m.Course)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (material is null || string.IsNullOrWhiteSpace(material.StoredFileName))
            return NotFound();

        var allowed = me.Role switch
        {
            UserRole.Admin => true,
            UserRole.Doctor => (await _access.AuthorizeCourseAsync(me.Id, material.CourseId)).IsAllowed,
            UserRole.Student => await EnrolledStudentMayDownloadAsync(me, material),
            _ => false
        };
        if (!allowed) return NotFound();

        var physicalPath = _files.ResolvePath(material.StoredFileName);
        if (physicalPath is null || !System.IO.File.Exists(physicalPath))
            return NotFound();

        var contentType = CourseMaterialFileStorage.ContentTypeFor(material.StoredFileName)
            ?? "application/octet-stream";
        var downloadName = string.IsNullOrWhiteSpace(material.OriginalFileName)
            ? $"material-{material.Id}{Path.GetExtension(material.StoredFileName)}"
            : material.OriginalFileName;

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(
            physicalPath, contentType, downloadName,
            enableRangeProcessing: true);
    }

    private async Task<bool> EnrolledStudentMayDownloadAsync(User student, CourseMaterial material)
    {
        if (!student.UniversityId.HasValue || material.Course is null)
            return false;
        if (material.Course.UniversityId != student.UniversityId.Value)
            return false;

        return await _db.Enrollments.AsNoTracking()
            .AnyAsync(e => e.UserId == student.Id && e.CourseId == material.CourseId);
    }
}
