namespace UniSphere.Api.Dtos;

public record ResearchHubItemDto(
    int Id, string Title, string Abstract, string Authors, string Keywords,
    int Year, string Field, string? DoiOrUrl, bool IsPublished,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc,
    int AuthorDoctorUserId, string AuthorName,
    string AuthorUniversity, string AuthorDepartment,
    bool HasPdf);

public record ResearchHubFiltersDto(
    IReadOnlyList<UniversityDto> Universities,
    IReadOnlyList<string> Fields,
    IReadOnlyList<int> Years);

public record ResearchHubPageDto(
    IReadOnlyList<ResearchHubItemDto> Items, int Total, int Page, int PageSize);

public record ResearchUploadRequest(
    string Title, string Abstract, string Authors, string Keywords,
    int Year, string Field, string? DoiOrUrl, bool IsPublished);

public record SetResearchPublishRequest(bool IsPublished);
