using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace UniSphere.Api.Tests;

/// <summary>
/// Integration coverage for the Phase 2 Research Hub API
/// (Controllers/ResearchHubController.cs): upload with server-authoritative
/// identity, magic-byte PDF validation, cross-university browse visibility,
/// draft isolation, ownership rules, role rules and filter combinations.
/// Tests share one throw-away database (the factory) and run sequentially;
/// every paper created here is deleted again, so no rows or PDF files leak.
/// upload endpoints are rate limited to 10 uploads/10min per user - the
/// doctor stays far below that across the whole class.
/// </summary>
public class ResearchHubApiTests : IClassFixture<ApiFactory>
{
    private const string PdfBytes =
        "%PDF-1.4\n1 0 obj\n<> endobj\ntrailer<>\n%%EOF\n";
    private const string FieldTag = "Quantum Integration Tests";
    private const int YearTag = 2026;

    private readonly ApiFactory _factory;
    public ResearchHubApiTests(ApiFactory factory) => _factory = factory;

    private HttpClient Doctor => _factory.DoctorClient;
    private HttpClient Admin => _factory.AdminClient;
    private HttpClient Student => _factory.StudentClient;

    [Fact]
    public async Task Upload_Browse_Details_Download_Lifecycle()
    {
        // Upload (published) - author/university must come from the token.
        var create = await Doctor.PostAsync("/api/research-hub",
            Multipart("Integration Lifecycle Paper", isPublished: true));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var paper = await create.Content.ReadFromJsonAsync<ResearchHubItem>();
        Assert.NotNull(paper);
        Assert.Equal("Dr. Khaled Mansour", paper!.AuthorName);
        Assert.Equal("El Shorouk Academy", paper.AuthorUniversity);
        Assert.True(paper.IsPublished);
        Assert.True(paper.HasPdf);
        // No private author data or server paths leak in the payload.
        var raw = await create.Content.ReadAsStringAsync();
        Assert.DoesNotContain("doctor@unisphere.edu", raw);
        Assert.DoesNotContain("App_Data", raw);
        Assert.DoesNotContain("\\\\", raw);

        // Mine lists it (including drafts -> published here).
        var mine = await Doctor.GetFromJsonAsync<List<ResearchHubItem>>("/api/research-hub/mine");
        Assert.Contains(mine!, m => m.Id == paper.Id);

        // Cross-university browse returns it with paging envelope.
        var browse = await Doctor.GetFromJsonAsync<ResearchHubPage>("/api/research-hub");
        Assert.Contains(browse!.Items, i => i.Id == paper.Id);
        Assert.Equal(20, browse.PageSize);

        // Details (any doctor).
        var details = await Doctor.GetAsync($"/api/research-hub/{paper.Id}");
        details.EnsureSuccessStatusCode();
        var dto = await details.Content.ReadFromJsonAsync<ResearchHubItem>();
        Assert.Equal("Integration Lifecycle Paper", dto!.Title);

        // Download: real PDF magic bytes, sanitized attachment name.
        var pdf = await Doctor.GetAsync($"/api/research-hub/{paper.Id}/pdf");
        pdf.EnsureSuccessStatusCode();
        Assert.True(AssertPdf(pdf), await ReadPdfHead(pdf));
        var dispositionName = pdf.Content.Headers.ContentDisposition?.FileNameStar
            ?? pdf.Content.Headers.ContentDisposition?.FileName;
        Assert.False(string.IsNullOrWhiteSpace(dispositionName), "no Content-Disposition filename");
        var disposition = dispositionName!.ToLowerInvariant().Trim('"');
        Assert.Contains("integration-lifecycle-paper", disposition);

        // Delete -> row gone + stored file removed.
        var del = await Doctor.DeleteAsync($"/api/research-hub/{paper.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        var afterDel = await Doctor.GetAsync($"/api/research-hub/{paper.Id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDel.StatusCode);
        AssertHasNoPdfFiles();
    }

    [Fact]
    public async Task Ownership_And_Draft_Visibility()
    {
        // Start as a draft so the visibility flows are all exercised.
        var create = await Doctor.PostAsync("/api/research-hub",
            Multipart("Ownership Visibility Paper", isPublished: false));
        create.EnsureSuccessStatusCode();
        var paper = await create.Content.ReadFromJsonAsync<ResearchHubItem>();
        Assert.NotNull(paper);
        Assert.False(paper!.IsPublished);

        // A second doctor (fresh registration -> no university assigned).
        var other = await OtherDoctorClient();

        // Drafts are invisible and un-editable for anyone else, without 403s.
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.GetAsync($"/api/research-hub/{paper.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.GetAsync($"/api/research-hub/{paper.Id}/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.PutAsync($"/api/research-hub/{paper.Id}/publish", JsonBool(false))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.DeleteAsync($"/api/research-hub/{paper.Id}")).StatusCode);

        // Publish (with PDF) succeeds; the paper becomes cross-university.
        var publish = await Doctor.PutAsync($"/api/research-hub/{paper.Id}/publish", JsonBool(true));
        Assert.Equal(HttpStatusCode.NoContent, publish.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync($"/api/research-hub/{paper.Id}")).StatusCode);

        // Edit metadata without re-uploading; ownership is kept (hasPdf).
        using var editForm = Multipart("Ownership Visibility Paper v2", isPublished: false);
        var update = await Doctor.PutAsync($"/api/research-hub/{paper.Id}", editForm);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var edited = await update.Content.ReadFromJsonAsync<ResearchHubItem>();
        Assert.Equal("Ownership Visibility Paper v2", edited!.Title);
        Assert.True(edited.HasPdf);

        // Unpublish hides it again (draft), owner still sees/downloads it.
        await Doctor.PutAsync($"/api/research-hub/{paper.Id}/publish", JsonBool(false));
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.GetAsync($"/api/research-hub/{paper.Id}")).StatusCode);
        var browse = await other.GetFromJsonAsync<ResearchHubPage>("/api/research-hub");
        Assert.DoesNotContain(browse!.Items, i => i.Id == paper.Id);
        var mine = await Doctor.GetFromJsonAsync<List<ResearchHubItem>>("/api/research-hub/mine");
        Assert.Contains(mine!, m => m.Id == paper.Id && !m.IsPublished);

        // Delete from draft state works and removes the file.
        Assert.Equal(HttpStatusCode.NoContent,
            (await Doctor.DeleteAsync($"/api/research-hub/{paper.Id}")).StatusCode);
        AssertHasNoPdfFiles();
    }

    [Fact]
    public async Task Upload_Validation_Rejects_BadContent()
    {
        // Fake PDF (plain text) -> magic-byte rejection, not a validation dump.
        var fake = await Doctor.PostAsync("/api/research-hub",
            Multipart("Magic Byte Rejection", isPublished: true, fakePdf: true));
        Assert.Equal(HttpStatusCode.BadRequest, fake.StatusCode);
        Assert.Contains("not a valid PDF", await fake.Content.ReadAsStringAsync());

        // Disallowed characters (<, > in the title) -> ValidationProblemDetails.
        var htmlTitle = await Doctor.PostAsync("/api/research-hub",
            Multipart("<script>alert(1)</script>", isPublished: true));
        Assert.Equal(HttpStatusCode.BadRequest, htmlTitle.StatusCode);
        var htmlErrors = await ParseValidationErrors(htmlTitle);
        Assert.True(htmlErrors.ContainsKey("title") || htmlErrors.ContainsKey("Title"));

        // Year out of range -> validation error, no row stored.
        var badYear = await Doctor.PostAsync("/api/research-hub",
            Multipart("Too Old Paper", isPublished: true, yearOverride: 1899));
        Assert.Equal(HttpStatusCode.BadRequest, badYear.StatusCode);
        var yearErrors = await ParseValidationErrors(badYear);
        Assert.True(yearErrors.ContainsKey("year") || yearErrors.ContainsKey("Year"));

        var mine = await Doctor.GetFromJsonAsync<List<ResearchHubItem>>("/api/research-hub/mine");
        Assert.DoesNotContain(mine!, m => m.Title.Contains("alert(1)"));
    }

    [Fact]
    public async Task Admin_And_Student_Permissions()
    {
        // Admin: can browse + download published papers...
        var browse = await Admin.GetAsync("/api/research-hub");
        Assert.Equal(HttpStatusCode.OK, browse.StatusCode);
        // ...but never treats Doctor-only ownership endpoints as its own.
        Assert.Equal(HttpStatusCode.Forbidden, (await Admin.GetAsync("/api/research-hub/mine")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Admin.PostAsync("/api/research-hub", Multipart("Admin Paper"))).StatusCode);

        // Student: everything is off limits.
        Assert.Equal(HttpStatusCode.Forbidden, (await Student.GetAsync("/api/research-hub")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Student.GetAsync("/api/research-hub/mine")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Student.PostAsync("/api/research-hub", Multipart("Student Paper"))).StatusCode);
    }

    [Fact]
    public async Task Browse_Filters_Combinations()
    {
        var create = await Doctor.PostAsync("/api/research-hub",
            Multipart("Filtered Quantum Paper", isPublished: true));
        create.EnsureSuccessStatusCode();
        var paper = await create.Content.ReadFromJsonAsync<ResearchHubItem>();
        Assert.NotNull(paper);

        // Search hits the title; the filter combination narrows correctly.
        var byTitle = await Doctor.GetFromJsonAsync<ResearchHubPage>(
            "/api/research-hub?q=" + Uri.EscapeDataString("Filtered Quantum Paper"));
        Assert.Contains(byTitle!.Items, i => i.Id == paper!.Id);

        var byFieldYear = await Doctor.GetFromJsonAsync<ResearchHubPage>(
            $"/api/research-hub?field={Uri.EscapeDataString(FieldTag)}&year={YearTag}");
        Assert.Contains(byFieldYear!.Items, i => i.Id == paper!.Id);

        var wrongYear = await Doctor.GetFromJsonAsync<ResearchHubPage>(
            $"/api/research-hub?field={Uri.EscapeDataString(FieldTag)}&year=1999");
        Assert.DoesNotContain(wrongYear!.Items, i => i.Id == paper!.Id);

        // University filter (doctor belongs to university 1).
        var uni1 = await Doctor.GetFromJsonAsync<ResearchHubPage>("/api/research-hub?universityId=1");
        Assert.Contains(uni1!.Items, i => i.Id == paper!.Id);
        var uni2 = await Doctor.GetFromJsonAsync<ResearchHubPage>("/api/research-hub?universityId=2");
        Assert.DoesNotContain(uni2!.Items, i => i.Id == paper!.Id);

        // Filters endpoint surfaces our field among its options.
        var filters = await Doctor.GetFromJsonAsync<ResearchHubFilters>("/api/research-hub/filters");
        Assert.Contains(filters!.Fields, f => f == FieldTag);
        Assert.Contains(filters.Universities, u => u.Id == 1);

        await Doctor.DeleteAsync($"/api/research-hub/{paper!.Id}");
        AssertHasNoPdfFiles();
    }

    [Fact]
    public async Task Doctor_Without_University_Cannot_Upload()
    {
        var other = await OtherDoctorClient();

        // Browsing works for every doctor; uploading is blocked until an
        // admin assigns a university (server-side rule honored exactly once).
        var browse = await other.GetAsync("/api/research-hub");
        Assert.Equal(HttpStatusCode.OK, browse.StatusCode);

        var create = await other.PostAsync("/api/research-hub",
            Multipart("No University Paper", isPublished: true));
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Contains("has no university assigned", await create.Content.ReadAsStringAsync());

        var mine = await other.GetFromJsonAsync<List<ResearchHubItem>>("/api/research-hub/mine");
        Assert.Empty(mine!);
    }

    // ---- helpers ----

    private static MultipartFormDataContent Multipart(string title, bool isPublished = false,
        bool fakePdf = false, int? yearOverride = null, string? fieldOverride = null)
    {
        var form = new MultipartFormDataContent();
        AddText(form, "title", title);
        AddText(form, "abstract", "Abstract used by the Research Hub integration tests.");
        AddText(form, "authors", "Khaled Mansour");
        AddText(form, "keywords", "integration; research; hub");
        AddText(form, "year", (yearOverride ?? YearTag).ToString());
        AddText(form, "field", fieldOverride ?? FieldTag);
        AddText(form, "doiOrUrl", "https://doi.org/10.1234/test-suite");
        AddText(form, "isPublished", isPublished ? "true" : "false");

        var name = fakePdf ? "fake.txt" : "paper.pdf";
        var contentType = fakePdf ? "text/plain" : "application/pdf";
        var bytes = fakePdf ? Encoding.ASCII.GetBytes("plain text is NOT a pdf") : Encoding.ASCII.GetBytes(PdfBytes);
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(file, "pdfFile", name);
        return form;
    }

    private static void AddText(MultipartFormDataContent form, string name, string value)
    {
        var c = new StringContent(value, Encoding.UTF8);
        c.Headers.ContentType = null;
        form.Add(c, name);
    }

    private static StringContent JsonBool(bool value) =>
        new(JsonSerializer.Serialize(new { isPublished = value }), Encoding.UTF8, "application/json");

    private async Task<HttpClient> OtherDoctorClient()
    {
        var email = $"research-doc-{Guid.NewGuid():N}@unisphere.edu";
        var register = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new Dictionary<string, object?>
            {
                ["firstName"] = "Second", ["lastName"] = "Researcher",
                ["phoneNumber"] = "01299999999", ["email"] = email,
                ["gender"] = "Male", ["age"] = 38, ["role"] = "Doctor",
                ["specialization"] = "Quantum Computing", ["department"] = "Computer Science",
                ["password"] = "Password123!", ["confirmPassword"] = "Password123!"
            });
        Assert.True(register.IsSuccessStatusCode,
            "doctor registration failed: " + await register.Content.ReadAsStringAsync());
        return _factory.AuthedClient(await _factory.LoginAsync(email, "Password123!"));
    }

    private static async Task<Dictionary<string, JsonElement>> ParseValidationErrors(HttpResponseMessage resp)
    {
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var result = new Dictionary<string, JsonElement>();
        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in errors.EnumerateObject())
                result[prop.Name.ToLowerInvariant()] = prop.Value.Clone();
        }
        return result;
    }

    private static bool AssertPdf(HttpResponseMessage resp)
    {
        var bytes = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        return bytes.Length >= 5 && Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-";
    }

    private static async Task<string> ReadPdfHead(HttpResponseMessage resp)
        => await resp.Content.ReadAsStringAsync() ?? "";

    private static string FindResearchDir()
    {
        // App_Data/research lives next to the API project - walk up from the
        // test bin directory until UniSphere.Api.csproj is found.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "UniSphere.Api.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "App_Data", "research");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AssertHasNoPdfFiles()
    {
        var dir = FindResearchDir();
        var files = Directory.GetFiles(dir, "*.pdf");
        Assert.True(files.Length == 0,
            "research directory should be empty, contains: " + string.Join(", ", files));
    }

    private record ResearchHubItem(int Id, string Title, string Abstract, string Authors,
        string Keywords, int Year, string Field, string? DoiOrUrl, bool IsPublished,
        DateTime CreatedAtUtc, DateTime UpdatedAtUtc, int AuthorDoctorUserId,
        string AuthorName, string AuthorUniversity, string AuthorDepartment, bool HasPdf);

    private record ResearchHubPage(IReadOnlyList<ResearchHubItem> Items, int Total, int Page, int PageSize);

    private record ResearchHubFilters(IReadOnlyList<UniversityMinimal> Universities,
        IReadOnlyList<string> Fields, IReadOnlyList<int> Years);

    private record UniversityMinimal(int Id, string Name, string? Logo);
}
