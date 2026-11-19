using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Xunit;

namespace UniSphere.Api.Tests;

/// <summary>
/// End-to-end coverage for doctor course-material attachments:
/// allowed extensions + real byte signatures, the 20 MB server limit,
/// server-authoritative course/university authorization, traversal-like
/// client file names, authenticated downloads, replacement/deletion disk
/// cleanup, and legacy FileUrl-only materials. Every material created here is
/// deleted again, and the factory uses a throw-away Uploads:StoragePath.
/// </summary>
public class MaterialFilesTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _doctor;
    private readonly HttpClient _student;
    private readonly HttpClient _admin;
    private readonly List<int> _createdMaterialIds = new();

    private int _courseId;
    private int _universityId;

    public MaterialFilesTests(ApiFactory factory)
    {
        _factory = factory;
        _doctor = factory.DoctorClient;
        _student = factory.StudentClient;
        _admin = factory.AdminClient;
    }

    public async Task InitializeAsync()
    {
        var coursesResponse = await _doctor.GetAsync("/api/doctor/courses");
        coursesResponse.EnsureSuccessStatusCode();
        var courses = await coursesResponse.Content.ReadFromJsonAsync<List<DoctorCourseRow>>();
        Assert.NotNull(courses);
        Assert.NotEmpty(courses!);
        _courseId = courses[0].Id;

        var profile = await _doctor.GetFromJsonAsync<DoctorProfileRow>("/api/doctor/profile");
        Assert.NotNull(profile);
        Assert.True(profile!.UniversityId.HasValue);
        _universityId = profile.UniversityId.Value;
    }

    public async Task DisposeAsync()
    {
        // Best-effort cleanup even when an assertion failed mid-test.
        foreach (var id in _createdMaterialIds)
        {
            try { await _doctor.DeleteAsync($"/api/doctor/materials/{id}"); }
            catch { /* best effort */ }
        }
    }

    private static readonly byte[] PdfBytes =
        "%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer<<>>\n%%EOF\n"u8.ToArray();

    private static readonly byte[] OleHeader =
        [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".doc")]
    [InlineData(".docx")]
    [InlineData(".ppt")]
    [InlineData(".pptx")]
    public async Task ValidAttachment_IsStoredAndDownloaded(string extension)
    {
        var (bytes, contentType) = ValidFile(extension);
        var material = await CreateMaterialAsync();
        var before = MaterialFiles();

        var upload = await UploadAsync(_doctor, material.Id, bytes, $"lecture{extension}");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var uploaded = await upload.Content.ReadFromJsonAsync<UploadedMaterialRow>();
        Assert.NotNull(uploaded);
        Assert.True(uploaded!.HasUploadedFile);
        Assert.Equal($"lecture{extension}", uploaded.UploadedFileName);
        Assert.Equal(bytes.Length, uploaded.UploadedFileSizeBytes);

        var files = MaterialFiles();
        Assert.Equal(before.Length + 1, files.Length);
        Assert.Contains(files, f => f.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        Assert.Contains($"lecture{extension}", uploaded.UploadedFileName, StringComparison.Ordinal);

        var download = await _student.GetAsync($"/api/materials/{material.Id}/file");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(contentType, download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains($"lecture{extension}",
            download.Content.Headers.ContentDisposition?.ToString() ?? "");
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());

        var remove = await _doctor.DeleteAsync($"/api/doctor/materials/{material.Id}/file");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        Assert.Empty(MaterialFiles());
    }

    [Theory]
    [InlineData("notes.txt", "pdf")]
    [InlineData("fake.pdf", "text")]
    [InlineData("fake.pdf", "zip")]
    [InlineData("fake.docx", "plain-zip")]
    [InlineData("fake.pptx", "docx-zip")]
    [InlineData("fake.doc", "zip")]
    [InlineData("fake.ppt", "pdf")]
    [InlineData("empty.pdf", "empty")]
    public async Task InvalidAttachment_IsRejectedWithoutLeavingAFile(
        string fileName, string kind)
    {
        var bytes = InvalidFile(kind);
        var material = await CreateMaterialAsync();
        var before = MaterialFiles().Length;

        var upload = await UploadAsync(_doctor, material.Id, bytes, fileName);
        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
        Assert.Equal(before, MaterialFiles().Length);

        var download = await _student.GetAsync($"/api/materials/{material.Id}/file");
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
    }

    [Fact]
    public async Task OversizeAttachment_IsRejectedByTheServerLimit()
    {
        var bytes = new byte[20 * 1024 * 1024 + 1];
        PdfBytes.CopyTo(bytes, 0);
        var material = await CreateMaterialAsync();
        var before = MaterialFiles().Length;

        var upload = await UploadAsync(_doctor, material.Id, bytes, "too-large.pdf");
        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
        Assert.Equal(before, MaterialFiles().Length);
    }

    [Fact]
    public async Task TraversalLikeFileName_IsSanitizedAndStoredInsideTheMaterialRoot()
    {
        var material = await CreateMaterialAsync();

        var upload = await UploadAsync(_doctor, material.Id, PdfBytes, "../../evil.pdf");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var uploaded = await upload.Content.ReadFromJsonAsync<UploadedMaterialRow>();
        Assert.NotNull(uploaded);
        Assert.Equal("evil.pdf", uploaded!.UploadedFileName);

        var files = MaterialFiles();
        Assert.Single(files);
        Assert.Matches("^[0-9a-f]{32}\\.pdf$", Path.GetFileName(files[0]));
        Assert.StartsWith(
            Path.GetFullPath(_factory.MaterialsPath) + Path.DirectorySeparatorChar,
            Path.GetFullPath(files[0]), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.GetFullPath(
            Path.Combine(_factory.MaterialsPath, "..", "evil.pdf"))));
    }

    [Fact]
    public async Task Replacement_DeletesThePreviousStoredFile()
    {
        var material = await CreateMaterialAsync();
        var first = await UploadAsync(_doctor, material.Id, PdfBytes, "first.pdf");
        first.EnsureSuccessStatusCode();
        var oldPath = Assert.Single(MaterialFiles());

        var secondBytes = PdfBytes.Concat("\n% replacement\n"u8.ToArray()).ToArray();
        var second = await UploadAsync(_doctor, material.Id, secondBytes, "second.pdf");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var uploaded = await second.Content.ReadFromJsonAsync<UploadedMaterialRow>();
        Assert.Equal("second.pdf", uploaded!.UploadedFileName);

        var newPath = Assert.Single(MaterialFiles());
        Assert.NotEqual(oldPath, newPath);
        Assert.False(File.Exists(oldPath));
        Assert.True(File.Exists(newPath));
    }

    [Fact]
    public async Task DeleteAttachment_RemovesFileAndClearsMetadata()
    {
        var material = await CreateMaterialAsync();
        (await UploadAsync(_doctor, material.Id, PdfBytes, "remove-me.pdf")).EnsureSuccessStatusCode();
        var storedPath = Assert.Single(MaterialFiles());

        var remove = await _doctor.DeleteAsync($"/api/doctor/materials/{material.Id}/file");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        Assert.False(File.Exists(storedPath));
        Assert.Empty(MaterialFiles());

        var list = await _doctor.GetFromJsonAsync<List<DoctorMaterialRow>>(
            $"/api/doctor/courses/{_courseId}/materials");
        var row = Assert.Single(list!, m => m.Id == material.Id);
        Assert.False(row.HasUploadedFile);
        Assert.Null(row.UploadedFileName);
    }

    [Fact]
    public async Task DeleteMaterial_RemovesAttachedFile()
    {
        var material = await CreateMaterialAsync();
        (await UploadAsync(_doctor, material.Id, PdfBytes, "delete-material.pdf")).EnsureSuccessStatusCode();
        var storedPath = Assert.Single(MaterialFiles());

        var delete = await _doctor.DeleteAsync($"/api/doctor/materials/{material.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.False(File.Exists(storedPath));
        Assert.Empty(MaterialFiles());
    }

    [Fact]
    public async Task AdminMaterialDelete_RemovesAttachedFile()
    {
        var material = await CreateMaterialAsync();
        (await UploadAsync(_doctor, material.Id, PdfBytes, "admin-delete.pdf")).EnsureSuccessStatusCode();
        var storedPath = Assert.Single(MaterialFiles());

        var delete = await _admin.DeleteAsync($"/api/university/materials/{material.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.False(File.Exists(storedPath));
        Assert.Empty(MaterialFiles());
    }

    [Fact]
    public async Task MissingPhysicalFile_Returns404AndDoesNotBreakMaterialDelete()
    {
        var material = await CreateMaterialAsync();
        (await UploadAsync(_doctor, material.Id, PdfBytes, "missing.pdf")).EnsureSuccessStatusCode();
        var storedPath = Assert.Single(MaterialFiles());

        File.Delete(storedPath);
        var download = await _student.GetAsync($"/api/materials/{material.Id}/file");
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);

        var delete = await _doctor.DeleteAsync($"/api/doctor/materials/{material.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task FileUrlOnlyMaterial_KeepsWorkingWithoutAnAttachment()
    {
        const string url = "https://example.com/legacy-notes.pdf";
        var material = await CreateMaterialAsync(url);

        Assert.False(material.HasUploadedFile);
        Assert.Equal(url, material.FileUrl);

        var detail = await _student.GetFromJsonAsync<MaterialDetailRow>(
            $"/api/university/me/materials/{material.Id}");
        Assert.NotNull(detail);
        Assert.False(detail!.HasUploadedFile);
        Assert.Equal(url, detail.FileUrl);
        Assert.Null(detail.UploadedFileName);

        var download = await _student.GetAsync($"/api/materials/{material.Id}/file");
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
    }

    [Fact]
    public async Task Authorization_IsEnforcedForUploadsDeletesAndDownloads()
    {
        var material = await CreateMaterialAsync();
        (await UploadAsync(_doctor, material.Id, PdfBytes, "secured.pdf")).EnsureSuccessStatusCode();
        var storedPath = Assert.Single(MaterialFiles());

        // Anonymous: authentication is required before authorization.
        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync($"/api/materials/{material.Id}/file")).StatusCode);

        // Same-university doctor who is not assigned to the course.
        var sameUniDoctor = await RegisterAndLoginAsync(DoctorPayload(
            $"unassigned-{Guid.NewGuid():N}@example.com", _universityId));
        Assert.Equal(HttpStatusCode.NotFound,
            (await UploadAsync(sameUniDoctor, material.Id, PdfBytes, "hijack.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await sameUniDoctor.DeleteAsync($"/api/doctor/materials/{material.Id}/file")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await sameUniDoctor.GetAsync($"/api/materials/{material.Id}/file")).StatusCode);

        // Doctor from a different university.
        var universities = await _factory.CreateClient()
            .GetFromJsonAsync<List<UniversityRow>>("/api/auth/universities");
        var foreignUniversity = universities!.First(u => u.Id != _universityId).Id;
        var foreignDoctor = await RegisterAndLoginAsync(DoctorPayload(
            $"foreign-doctor-{Guid.NewGuid():N}@example.com", foreignUniversity));
        Assert.Equal(HttpStatusCode.NotFound,
            (await UploadAsync(foreignDoctor, material.Id, PdfBytes, "foreign.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await foreignDoctor.GetAsync($"/api/materials/{material.Id}/file")).StatusCode);

        // Same-university student who is not enrolled in the course.
        var nonEnrolledStudent = await RegisterAndLoginAsync(StudentPayload(
            $"not-enrolled-{Guid.NewGuid():N}@example.com", _universityId));
        Assert.Equal(HttpStatusCode.NotFound,
            (await nonEnrolledStudent.GetAsync($"/api/materials/{material.Id}/file")).StatusCode);

        // Student from a different university.
        var foreignStudent = await RegisterAndLoginAsync(StudentPayload(
            $"foreign-student-{Guid.NewGuid():N}@example.com", foreignUniversity));
        Assert.Equal(HttpStatusCode.NotFound,
            (await foreignStudent.GetAsync($"/api/materials/{material.Id}/file")).StatusCode);

        // Students and admins cannot use the doctor-only write endpoints.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await UploadAsync(_student, material.Id, PdfBytes, "student.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await UploadAsync(_admin, material.Id, PdfBytes, "admin.pdf")).StatusCode);

        // The assigned doctor and the admin can download; the file survived all
        // of the rejected writes/deletes above.
        Assert.Equal(HttpStatusCode.OK,
            (await _doctor.GetAsync($"/api/materials/{material.Id}/file")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await _admin.GetAsync($"/api/materials/{material.Id}/file")).StatusCode);
        Assert.True(File.Exists(storedPath));
        Assert.Single(MaterialFiles());
    }

    // ---- Helpers ----

    private async Task<MaterialRow> CreateMaterialAsync(string? fileUrl = null)
    {
        var response = await _doctor.PostAsJsonAsync("/api/doctor/materials", new
        {
            courseId = _courseId,
            title = $"Attachment test {Guid.NewGuid():N}",
            type = "Lecture",
            description = "",
            content = "",
            fileUrl,
            deadlineUtc = (DateTime?)null
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var material = await response.Content.ReadFromJsonAsync<MaterialRow>();
        Assert.NotNull(material);
        _createdMaterialIds.Add(material!.Id);
        return material;
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client, int materialId, byte[] bytes, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        // Deliberately claim a generic MIME type: the server must inspect the
        // extension and the actual bytes, not trust the client header.
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return await client.PostAsync($"/api/doctor/materials/{materialId}/file", form);
    }

    private async Task<HttpClient> RegisterAndLoginAsync(object payload)
    {
        var anonymous = _factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/auth/register", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<TokenRow>();
        Assert.NotNull(auth);
        return _factory.AuthedClient(auth!.Token);
    }

    private static object DoctorPayload(string email, int universityId) => new
    {
        firstName = "Material",
        middleName = (string?)null,
        lastName = "Tester",
        phoneNumber = "01012345678",
        email,
        gender = "Male",
        age = 40,
        role = "Doctor",
        password = "DoctorPass123!",
        confirmPassword = "DoctorPass123!",
        academicYear = (int?)null,
        department = "Computer Science",
        specialization = "Software Engineering",
        graduatedFrom = (string?)null,
        graduationYear = (int?)null,
        universityId,
        address = "1 Test Street"
    };

    private static object StudentPayload(string email, int universityId) => new
    {
        firstName = "Material",
        middleName = (string?)null,
        lastName = "Student",
        phoneNumber = "01012345678",
        email,
        gender = "Female",
        age = 20,
        role = "Student",
        password = "Password123!",
        confirmPassword = "Password123!",
        academicYear = 3,
        department = "Computer Science",
        specialization = (string?)null,
        graduatedFrom = (string?)null,
        graduationYear = (int?)null,
        universityId,
        address = "1 Test Street"
    };

    private (byte[] Bytes, string ContentType) ValidFile(string extension)
    {
        return extension switch
        {
            ".pdf" => (PdfBytes, "application/pdf"),
            ".doc" => (OleFile(), "application/msword"),
            ".ppt" => (OleFile(), "application/vnd.ms-powerpoint"),
            ".docx" => (ZipFile("word/document.xml"),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
            ".pptx" => (ZipFile("ppt/presentation.xml"),
                "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
            _ => throw new ArgumentOutOfRangeException(nameof(extension))
        };
    }

    private static byte[] InvalidFile(string kind) => kind switch
    {
        "empty" => Array.Empty<byte>(),
        "text" => "this is definitely not a PDF"u8.ToArray(),
        "pdf" => PdfBytes,
        "zip" => ZipFile("[Content_Types].xml"),
        "plain-zip" => ZipFile("[Content_Types].xml"),
        "docx-zip" => ZipFile("word/document.xml"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static byte[] OleFile() =>
        OleHeader.Concat("legacy office payload"u8.ToArray()).ToArray();

    private static byte[] ZipFile(params string[] entryNames)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in entryNames)
            {
                var entry = archive.CreateEntry(name);
                using var entryStream = entry.Open();
                entryStream.Write(Encoding.UTF8.GetBytes("<test/>"));
            }
        }
        return stream.ToArray();
    }

    private string[] MaterialFiles() =>
        Directory.Exists(_factory.MaterialsPath)
            ? Directory.GetFiles(_factory.MaterialsPath)
            : Array.Empty<string>();

    private sealed record DoctorCourseRow(int Id, string Code, string Title);

    private sealed record DoctorProfileRow(int Id, int? UniversityId);

    private sealed record UniversityRow(int Id, string Name);

    private sealed record TokenRow(string Token);

    private sealed record MaterialRow(
        int Id, int CourseId, string Title, string Type, string Description,
        string Content, string? FileUrl, DateTime CreatedAt,
        string? InstructorName = null,
        bool HasUploadedFile = false,
        string? UploadedFileName = null,
        long? UploadedFileSizeBytes = null);

    private sealed record UploadedMaterialRow(
        int Id, bool HasUploadedFile, string? UploadedFileName,
        long? UploadedFileSizeBytes);

    private sealed record DoctorMaterialRow(
        int Id, bool HasUploadedFile, string? UploadedFileName);

    private sealed record MaterialDetailRow(
        int Id, int CourseId, string Title, string Type, string Description,
        string Content, string? FileUrl, DateTime CreatedAt,
        string? InstructorName = null,
        bool HasUploadedFile = false,
        string? UploadedFileName = null,
        long? UploadedFileSizeBytes = null);
}
