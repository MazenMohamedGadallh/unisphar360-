using Microsoft.AspNetCore.Http;

namespace UniSphere.Api.Services;

/// <summary>A user-facing validation failure while storing a research PDF.</summary>
public sealed class ResearchFileException : Exception
{
    public ResearchFileException(string message) : base(message) { }
}

/// <summary>
/// Stores research PDFs outside the executable/wwwroot paths, under
/// App_Data/research by default. Files are renamed to a random GUID and only
/// that file name is stored in the database; absolute server paths are never
/// persisted or returned to clients.
/// </summary>
public sealed class ResearchFileStorage
{
    public const long MaxPdfBytes = 20L * 1024 * 1024; // 20 MB
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();
    private static readonly HashSet<string> PdfExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf" };

    private readonly string _root;
    private readonly ILogger<ResearchFileStorage> _logger;

    public ResearchFileStorage(
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ILogger<ResearchFileStorage> logger)
    {
        var configured = configuration["Research:StoragePath"];
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "App_Data", "research")
            : Path.GetFullPath(configured);
        _logger = logger;
    }

    /// <summary>Saves a PDF and returns the generated file name (not a path).</summary>
    public async Task<string> SavePdfAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length <= 0)
            throw new ResearchFileException("Choose a PDF file to upload.");
        if (file.Length > MaxPdfBytes)
            throw new ResearchFileException("PDF files must be 20 MB or smaller.");
        if (!string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            throw new ResearchFileException("Only PDF files are accepted (application/pdf).");

        Directory.CreateDirectory(_root);
        var fileName = StoredFileStorage.NewFileName(".pdf");
        var fullPath = Path.Combine(_root, fileName);

        try
        {
            await using var input = file.OpenReadStream();

            // Trust the file contents, not the extension or MIME type alone.
            var header = new byte[PdfMagic.Length];
            var headerRead = 0;
            while (headerRead < header.Length)
            {
                var read = await input.ReadAsync(
                    header.AsMemory(headerRead, header.Length - headerRead), cancellationToken);
                if (read == 0) break;
                headerRead += read;
            }

            if (headerRead != PdfMagic.Length ||
                !header.AsSpan(0, headerRead).SequenceEqual(PdfMagic))
                throw new ResearchFileException("The uploaded file is not a valid PDF.");

            await using var output = new FileStream(
                fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true);
            await output.WriteAsync(header.AsMemory(0, headerRead), cancellationToken);
            await input.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);

            return fileName;
        }
        catch
        {
            TryDelete(fullPath);
            throw;
        }
    }

    /// <summary>Resolves a stored file name to an absolute path, rejecting any
    /// value that is not a bare *.pdf file name (path traversal guard).</summary>
    public string? ResolvePath(string? fileName) =>
        StoredFileStorage.ResolvePath(_root, fileName, PdfExtensions);

    /// <summary>Best-effort delete; a missing/unreadable file never fails the API call.</summary>
    public void Delete(string? fileName)
    {
        var path = ResolvePath(fileName);
        if (path is not null) TryDelete(path);
    }

    private void TryDelete(string path) =>
        StoredFileStorage.TryDelete(path, _logger, "a research PDF file");
}
