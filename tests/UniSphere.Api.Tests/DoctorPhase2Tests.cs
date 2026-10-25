using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;
using UniSphere.Api.Services;
using Xunit;

namespace UniSphere.Api.Tests;

/// <summary>
/// Phase 2 acceptance tests for the doctor research hub: server-derived
/// ownership, cross-university Browse, draft privacy, authenticated PDF
/// download, PDF magic-byte/size checks, owner-only mutations, audit logging
/// and rejection of student access.
/// </summary>
public class DoctorPhase2Tests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _doctor;
    private readonly HttpClient _student;
    private readonly HttpClient _admin;

    public DoctorPhase2Tests(ApiFactory factory)
    {
        _factory = factory;
        _doctor = factory.DoctorClient;
        _student = factory.StudentClient;
        _admin = factory.AdminClient;
    }

    // ---- Storage / ownership ----

    [Fact]
    public async Task Upload_ValidPdf_UsesJwtOwnerAndUniversity_AndStoresOnlyAFileName()
    {
        var title = UniqueTitle("Ownership");
        using var form = BuildForm(title, pdf: PdfBytes(), published: true,
            field: "Artificial Intelligence", year: 2026);
        var response = await _doctor.PostAsync("/api/doctor/research", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var (created, raw) = await ReadJsonWithRawAsync<DoctorResearchDto>(response);

        Assert.True(created.Id > 0);
        Assert.Equal(title, created.Title);
        Assert.True(created.HasPdf);
        Assert.True(created.IsPublished);
        Assert.False(raw.Contains("pdfPath", StringComparison.OrdinalIgnoreCase));
        Assert.False(raw.Contains("App_Data", StringComparison.OrdinalIgnoreCase));
        Assert.False(raw.Contains("unisphere-test-research", StringComparison.OrdinalIgnoreCase));

        var owner = await GetDoctorAsync();
        var (rowOwnerId, rowUniversityId, pdfName, auditFound) = await GetRowAsync(created.Id);

        Assert.Equal(owner.Id, rowOwnerId);
        Assert.Equal(owner.UniversityId, rowUniversityId);
        Assert.Matches("^[0-9a-f]{32}\\.pdf$", pdfName);
        Assert.DoesNotContain(Path.DirectorySeparatorChar.ToString(), pdfName);
        Assert.DoesNotContain("/", pdfName);
        Assert.True(auditFound);

        var path = ResolvePdfPath(pdfName);
        Assert.True(File.Exists(path));
        Assert.False(path.Contains("wwwroot", StringComparison.OrdinalIgnoreCase));
        Assert.False(path.Contains(
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Upload_IgnoresClientSuppliedOwnerUniversityAndRoleFields()
    {
        var title = UniqueTitle("MassAssignment");
        using var form = BuildForm(title, pdf: PdfBytes(), published: true,
            field: "Artificial Intelligence", year: 2026);
        form.Add(new StringContent("9999"), "DoctorUserId");
        form.Add(new StringContent("9999"), "UniversityId");
        form.Add(new StringContent("Admin"), "Role");

        var response = await _doctor.PostAsync("/api/doctor/research", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await ReadJsonAsync<DoctorResearchDto>(response);

        var owner = await GetDoctorAsync();
        var (rowOwnerId, rowUniversityId, _, _) = await GetRowAsync(created.Id);
        Assert.Equal(owner.Id, rowOwnerId);
        Assert.Equal(owner.UniversityId, rowUniversityId);
    }

    // ---- Cross-university Browse + download ----

    [Fact]
    public async Task Browse_IsCrossUniversity_AndDownloadWorks_ButForeignEditsAre404()
    {
        var title = UniqueTitle("CrossUniversity");
        var paper = await CreatePaperAsync(_doctor, title, published: true,
            field: "Artificial Intelligence", year: 2026);
        var other = await CreateOtherUniversityDoctorAsync();

        // B's own list is owner-scoped.
        var myListResponse = await other.GetAsync("/api/doctor/research");
        Assert.Equal(HttpStatusCode.OK, myListResponse.StatusCode);
        var myList = await ReadJsonAsync<List<DoctorResearchDto>>(myListResponse);
        Assert.DoesNotContain(myList, p => p.Id == paper.Id);

        // B finds A's published paper from a different university.
        var browseResponse = await other.GetAsync(
            $"/api/doctor/research/browse?search={Uri.EscapeDataString(title)}");
        Assert.Equal(HttpStatusCode.OK, browseResponse.StatusCode);
        var (page, browseRaw) = await ReadJsonWithRawAsync<PagedResultDto<ResearchBrowseItemDto>>(browseResponse);
        Assert.Equal(1, page.Total);
        var item = Assert.Single(page.Items);
        Assert.Equal(title, item.Title);
        Assert.Contains("Khaled", item.AuthorName);
        Assert.Equal("El Shorouk Academy", item.UniversityName);
        Assert.True(item.HasPdf);

        // Admins may browse published papers too.
        var adminBrowse = await _admin.GetAsync(
            $"/api/doctor/research/browse?search={Uri.EscapeDataString(title)}");
        Assert.Equal(HttpStatusCode.OK, adminBrowse.StatusCode);
        var adminPage = await ReadJsonAsync<PagedResultDto<ResearchBrowseItemDto>>(adminBrowse);
        Assert.Equal(1, adminPage.Total);

        // No private author data or storage paths are exposed.
        var owner = await GetDoctorAsync();
        Assert.False(browseRaw.Contains(owner.Email, StringComparison.OrdinalIgnoreCase));
        Assert.False(browseRaw.Contains(owner.PhoneNumber, StringComparison.OrdinalIgnoreCase));
        Assert.False(browseRaw.Contains("pdfPath", StringComparison.OrdinalIgnoreCase));
        Assert.False(browseRaw.Contains("App_Data", StringComparison.OrdinalIgnoreCase));

        var detailResponse = await other.GetAsync($"/api/doctor/research/browse/{paper.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var (detail, detailRaw) = await ReadJsonWithRawAsync<ResearchDetailDto>(detailResponse);
        Assert.Equal(title, detail.Title);
        Assert.False(string.IsNullOrWhiteSpace(detail.Abstract));
        Assert.False(string.IsNullOrWhiteSpace(detail.AuthorName));
        Assert.Equal("El Shorouk Academy", detail.UniversityName);
        Assert.False(detailRaw.Contains(owner.Email, StringComparison.OrdinalIgnoreCase));
        Assert.False(detailRaw.Contains(owner.PhoneNumber, StringComparison.OrdinalIgnoreCase));

        // B can download the PDF, but cannot edit or delete A's paper.
        var pdfResponse = await other.GetAsync($"/api/doctor/research/{paper.Id}/pdf");
        Assert.Equal(HttpStatusCode.OK, pdfResponse.StatusCode);
        Assert.Equal("application/pdf", pdfResponse.Content.Headers.ContentType?.MediaType);
        var bytes = await pdfResponse.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes));

        using var hijackForm = BuildForm("Hijacked title", published: false,
            field: "Hijacked", year: 2026);
        var put = await other.PutAsync($"/api/doctor/research/{paper.Id}", hijackForm);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);

        var delete = await other.DeleteAsync($"/api/doctor/research/{paper.Id}");
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        // The paper is untouched.
        var ownerDetail = await _doctor.GetAsync($"/api/doctor/research/{paper.Id}");
        Assert.Equal(HttpStatusCode.OK, ownerDetail.StatusCode);
        var ownerPaper = await ReadJsonAsync<DoctorResearchDto>(ownerDetail);
        Assert.Equal(title, ownerPaper.Title);
        Assert.True(ownerPaper.IsPublished);
    }

    [Fact]
    public async Task Draft_IsPrivateUntilPublished()
    {
        var title = UniqueTitle("DraftPrivacy");
        var paper = await CreatePaperAsync(_doctor, title, published: false,
            field: "Artificial Intelligence", year: 2026);
        var other = await CreateOtherUniversityDoctorAsync();

        // Hidden from Browse, detail and download for another doctor.
        var browse = await other.GetAsync(
            $"/api/doctor/research/browse?search={Uri.EscapeDataString(title)}");
        var page = await ReadJsonAsync<PagedResultDto<ResearchBrowseItemDto>>(browse);
        Assert.Equal(0, page.Total);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.GetAsync($"/api/doctor/research/browse/{paper.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.GetAsync($"/api/doctor/research/{paper.Id}/pdf")).StatusCode);

        // Owner sees the draft in My Research, its detail and its PDF.
        var mineResponse = await _doctor.GetAsync("/api/doctor/research");
        var mine = await ReadJsonAsync<List<DoctorResearchDto>>(mineResponse);
        Assert.Contains(mine, p => p.Id == paper.Id && !p.IsPublished);
        Assert.Equal(HttpStatusCode.OK,
            (await _doctor.GetAsync($"/api/doctor/research/{paper.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await _doctor.GetAsync($"/api/doctor/research/{paper.Id}/pdf")).StatusCode);

        // Publishing (without replacing the PDF) makes it visible.
        var oldFileName = (await GetRowAsync(paper.Id)).PdfName;
        using var publishForm = BuildForm(title, published: true,
            field: "Artificial Intelligence", year: 2026);
        var publish = await _doctor.PutAsync($"/api/doctor/research/{paper.Id}", publishForm);
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);

        var after = await ReadJsonAsync<DoctorResearchDto>(publish);
        Assert.True(after.IsPublished);
        Assert.Equal(oldFileName, (await GetRowAsync(paper.Id)).PdfName);

        var browseAfter = await other.GetAsync(
            $"/api/doctor/research/browse?search={Uri.EscapeDataString(title)}");
        var pageAfter = await ReadJsonAsync<PagedResultDto<ResearchBrowseItemDto>>(browseAfter);
        Assert.Equal(1, pageAfter.Total);
        Assert.Equal(HttpStatusCode.OK,
            (await other.GetAsync($"/api/doctor/research/browse/{paper.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await other.GetAsync($"/api/doctor/research/{paper.Id}/pdf")).StatusCode);
    }

    // ---- PDF validation ----

    [Fact]
    public async Task NonPdf_AndWrongMimeType_AreRejected_WithoutCreatingRows()
    {
        var before = await CountMyPapersAsync();

        using (var badMagic = BuildForm(UniqueTitle("BadMagic"), pdf: Encoding.ASCII.GetBytes("this is not a pdf")))
        {
            var response = await _doctor.PostAsync("/api/doctor/research", badMagic);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(body.Contains("PDF", StringComparison.OrdinalIgnoreCase));
        }

        using (var wrongMime = BuildForm(UniqueTitle("WrongMime"), pdf: PdfBytes(),
                   contentType: "text/plain", fileName: "paper.pdf"))
        {
            var response = await _doctor.PostAsync("/api/doctor/research", wrongMime);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Equal(before, await CountMyPapersAsync());
    }

    [Fact]
    public async Task OversizedPdf_IsRejected()
    {
        var before = await CountMyPapersAsync();
        var bytes = new byte[ResearchFileStorage.MaxPdfBytes + 1];
        Encoding.ASCII.GetBytes("%PDF-").CopyTo(bytes, 0);

        using var form = BuildForm(UniqueTitle("Oversized"), pdf: bytes);
        var response = await _doctor.PostAsync("/api/doctor/research", form);

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest
            or HttpStatusCode.RequestEntityTooLarge,
            $"Expected 400/413, got {(int)response.StatusCode}.");
        Assert.Equal(before, await CountMyPapersAsync());
    }

    // ---- Update / delete file lifecycle ----

    [Fact]
    public async Task Update_WithNewPdf_ReplacesOldFile_AndKeepsOwnership()
    {
        var title = UniqueTitle("ReplacePdf");
        var paper = await CreatePaperAsync(_doctor, title);
        var oldName = (await GetRowAsync(paper.Id)).PdfName;
        var oldPath = ResolvePdfPath(oldName);
        Assert.True(File.Exists(oldPath));

        var newTitle = title + " revised";
        using var form = BuildForm(newTitle, pdf: PdfBytes("replacement"));
        var response = await _doctor.PutAsync($"/api/doctor/research/{paper.Id}", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await ReadJsonAsync<DoctorResearchDto>(response);
        Assert.Equal(newTitle, updated.Title);
        var (ownerId, universityId, newName, _) = await GetRowAsync(paper.Id);
        var owner = await GetDoctorAsync();
        Assert.Equal(owner.Id, ownerId);
        Assert.Equal(owner.UniversityId, universityId);
        Assert.NotEqual(oldName, newName);
        Assert.False(File.Exists(oldPath));
        Assert.True(File.Exists(ResolvePdfPath(newName)));
    }

    [Fact]
    public async Task Delete_RemovesPaperAndPdf_AndOwnerScopes404()
    {
        var title = UniqueTitle("Delete");
        var paper = await CreatePaperAsync(_doctor, title);
        var pdfName = (await GetRowAsync(paper.Id)).PdfName;
        var pdfPath = ResolvePdfPath(pdfName);
        Assert.True(File.Exists(pdfPath));

        var other = await CreateOtherUniversityDoctorAsync();
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.DeleteAsync($"/api/doctor/research/{paper.Id}")).StatusCode);
        Assert.True(File.Exists(pdfPath));

        var delete = await _doctor.DeleteAsync($"/api/doctor/research/{paper.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.False(File.Exists(pdfPath));
        Assert.Equal(HttpStatusCode.NotFound,
            (await _doctor.GetAsync($"/api/doctor/research/{paper.Id}")).StatusCode);

        var browse = await other.GetAsync(
            $"/api/doctor/research/browse?search={Uri.EscapeDataString(title)}");
        var page = await ReadJsonAsync<PagedResultDto<ResearchBrowseItemDto>>(browse);
        Assert.Equal(0, page.Total);
    }

    // ---- Filters ----

    [Fact]
    public async Task Browse_FiltersByFieldAndYear_AndFiltersEndpointListsOptions()
    {
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var aTitle = $"AI paper {stamp}";
        var bTitle = $"Data paper {stamp}";

        await CreatePaperAsync(_doctor, aTitle, published: true,
            field: "Artificial Intelligence", year: 2026);

        var other = await CreateOtherUniversityDoctorAsync();
        await CreatePaperAsync(other, bTitle, published: true,
            field: "Data Science", year: 2024);

        var filtered = await other.GetAsync(
            "/api/doctor/research/browse?field=" + Uri.EscapeDataString("Data Science") +
            "&year=2024&search=" + Uri.EscapeDataString(stamp));
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        var page = await ReadJsonAsync<PagedResultDto<ResearchBrowseItemDto>>(filtered);
        Assert.Equal(1, page.Total);
        Assert.Equal(bTitle, Assert.Single(page.Items).Title);

        var wrongField = await other.GetAsync(
            "/api/doctor/research/browse?field=" + Uri.EscapeDataString("Artificial Intelligence") +
            "&search=" + Uri.EscapeDataString(bTitle));
        var empty = await ReadJsonAsync<PagedResultDto<ResearchBrowseItemDto>>(wrongField);
        Assert.Equal(0, empty.Total);

        var filtersResponse = await other.GetAsync("/api/doctor/research/browse/filters");
        Assert.Equal(HttpStatusCode.OK, filtersResponse.StatusCode);
        var filters = await ReadJsonAsync<ResearchFiltersDto>(filtersResponse);
        Assert.Contains(filters.Universities, u => u.Name == "El Shorouk Academy");
        Assert.Contains(filters.Universities, u => u.Name == "Helwan University");
        Assert.Contains("Artificial Intelligence", filters.Fields);
        Assert.Contains("Data Science", filters.Fields);
        Assert.Contains(2026, filters.Years);
        Assert.Contains(2024, filters.Years);
    }

    // ---- Authorization ----

    [Fact]
    public async Task Students_Get403_AndAnonymous_Get401_OnDoctorResearchEndpoints()
    {
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _student.GetAsync("/api/doctor/research")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _student.GetAsync("/api/doctor/research/browse")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _student.GetAsync("/api/doctor/research/browse/filters")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _student.GetAsync("/api/doctor/research/1/pdf")).StatusCode);

        using (var form = BuildForm(UniqueTitle("StudentUpload")))
        {
            var post = await _student.PostAsync("/api/doctor/research", form);
            Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        }

        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/doctor/research")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/doctor/research/1/pdf")).StatusCode);
    }

    // ---- Helpers ----

    private static string UniqueTitle(string prefix) =>
        $"{prefix} {Guid.NewGuid():N}";

    private static byte[] PdfBytes(string marker = "phase2") =>
        Encoding.ASCII.GetBytes(
            "%PDF-1.4\n" +
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n" +
            "2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n" +
            $"trailer\n<< /Root 1 0 R /Marker ({marker}) >>\n%%EOF\n");

    private static MultipartFormDataContent BuildForm(
        string title,
        byte[]? pdf = null,
        bool published = true,
        string field = "Artificial Intelligence",
        int year = 2026,
        string contentType = "application/pdf",
        string fileName = "paper.pdf")
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(title), "Title");
        form.Add(new StringContent("A detailed abstract describing the research problem, method and findings."), "Abstract");
        form.Add(new StringContent("Dr. Khaled Mansour"), "Authors");
        form.Add(new StringContent("research, ai"), "Keywords");
        form.Add(new StringContent(year.ToString()), "Year");
        form.Add(new StringContent(field), "Field");
        form.Add(new StringContent("https://example.org/paper"), "DoiOrUrl");
        form.Add(new StringContent(published ? "true" : "false"), "IsPublished");

        if (pdf is not null)
        {
            var file = new ByteArrayContent(pdf);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(file, "Pdf", fileName);
        }
        return form;
    }

    private static async Task<DoctorResearchDto> CreatePaperAsync(
        HttpClient client,
        string title,
        bool published = true,
        string field = "Artificial Intelligence",
        int year = 2026)
    {
        using var form = BuildForm(title, pdf: PdfBytes(), published: published, field: field, year: year);
        var response = await client.PostAsync("/api/doctor/research", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadJsonAsync<DoctorResearchDto>(response);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        var value = JsonSerializer.Deserialize<T>(raw,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(value);
        return value!;
    }

    private static async Task<(T Value, string Raw)> ReadJsonWithRawAsync<T>(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        var value = JsonSerializer.Deserialize<T>(raw,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(value);
        return (value!, raw);
    }

    private async Task<HttpClient> CreateOtherUniversityDoctorAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var helwan = await db.Universities.SingleAsync(u => u.Name == "Helwan University");
        var email = $"doctor.b.{Guid.NewGuid():N}@unisphere.edu";

        db.Users.Add(new User
        {
            FirstName = "Sara",
            LastName = "Helwan",
            FullName = "Dr. Sara Helwan",
            Email = email,
            PhoneNumber = "01198765432",
            Address = "Helwan, Cairo",
            Gender = "Female",
            Age = 40,
            Major = "Computer Science",
            Department = "Computer Science",
            UniversityId = helwan.Id,
            Role = UserRole.Doctor,
            PasswordHash = PasswordService.Hash("Doctor123!")
        });
        await db.SaveChangesAsync();

        var token = await _factory.LoginAsync(email, "Doctor123!");
        return _factory.AuthedClient(token);
    }

    private async Task<(int Id, int? UniversityId, string Email, string PhoneNumber)> GetDoctorAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doctor = await db.Users.AsNoTracking()
            .SingleAsync(u => u.Email == "doctor@unisphere.edu");
        return (doctor.Id, doctor.UniversityId, doctor.Email, doctor.PhoneNumber);
    }

    private async Task<(int OwnerId, int? UniversityId, string PdfName, bool AuditFound)>
        GetRowAsync(int paperId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.DoctorResearches.AsNoTracking()
            .SingleAsync(r => r.Id == paperId);
        var auditFound = await db.AuditLogs.AsNoTracking()
            .AnyAsync(a => a.UserId == row.DoctorUserId &&
                           a.Path.StartsWith("/api/doctor/research") &&
                           (a.StatusCode == StatusCodes.Status201Created ||
                            a.StatusCode == StatusCodes.Status200OK ||
                            a.StatusCode == StatusCodes.Status204NoContent));
        return (row.DoctorUserId, row.UniversityId, row.PdfPath, auditFound);
    }

    private string ResolvePdfPath(string fileName)
    {
        using var scope = _factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<ResearchFileStorage>();
        var path = storage.ResolvePath(fileName);
        Assert.NotNull(path);
        return path!;
    }

    private async Task<int> CountMyPapersAsync()
    {
        var response = await _doctor.GetAsync("/api/doctor/research");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var papers = await ReadJsonAsync<List<DoctorResearchDto>>(response);
        return papers.Count;
    }
}
