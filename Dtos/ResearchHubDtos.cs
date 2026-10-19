namespace UniSphere.Api.Dtos;

// ---- Research Hub (Phase 2). Author identity (doctor + university) is
// always resolved server-side from the JWT claims; browse output never
// includes the author's email, phone or any other private data.

/// <summary>One paper in the cross-university Browse list (published only)
/// or the doctor's own list (published + drafts).</summary>
public record ResearchHubItemDto(
    int Id, string Title, string Abstract, string Authors, string Keywords,
    int Year, string Field, string? DoiOrUrl, bool IsPublished,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc,
    int AuthorDoctorUserId, string AuthorName,
    string AuthorUniversity, string AuthorDepartment,
    bool HasPdf);

/// <summary>Filter options for the Browse tab.</summary>
public record ResearchHubFiltersDto(
    IReadOnlyList<UniversityDto> Universities,
    IReadOnlyList<string> Fields,
    IReadOnlyList<int> Years);

/// <summary>Server-side paging result for the Browse tab (20 per page).</summary>
public record ResearchHubPageDto(
    IReadOnlyList<ResearchHubItemDto> Items, int Total, int Page, int PageSize);

/// <summary>Multipart form fields for POST /api/research-hub (create) and
/// PUT /api/research-hub/{id} (edit). The file is validated by magic bytes
/// (%PDF-), max 20 MB, never trusted by extension or MIME type. DoctorUserId
/// and UniversityId are NOT part of the payload — the server sets them.</summary>
public record ResearchUploadRequest(
    string Title, string Abstract, string Authors, string Keywords,
    int Year, string Field, string? DoiOrUrl, bool IsPublished);

/// <summary>PUT /api/research-hub/{id}/publish body.</summary>
public record SetResearchPublishRequest(bool IsPublished);
