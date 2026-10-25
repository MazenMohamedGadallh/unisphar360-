using System.IO.Compression;
using Microsoft.AspNetCore.Http;

namespace UniSphere.Api.Services;

/// <summary>A user-facing validation failure while storing a course-material file.</summary>
public sealed class MaterialFileException : Exception
{
    public MaterialFileException(string message) : base(message) { }
}

/// <summary>Metadata for one successfully stored course-material attachment.</summary>
public sealed record StoredMaterialFile(
    string StoredFileName,
    string OriginalFileName,
    long SizeBytes,
    string ContentType);

/// <summary>
/// Stores doctor course-material attachments outside wwwroot, under
/// App_Data/materials by default. Files are renamed to a random GUID plus the
/// validated extension; only that bare name is stored in the database. The
/// declared client MIME type is ignored in favour of an extension allow-list
/// and real byte-signature checks (PDF, OOXML ZIP and legacy OLE).
/// </summary>
public sealed class CourseMaterialFileStorage
{
    public const int DefaultMaxFileSizeMb = 20;
    public const long TransportMaxBytes = 25L * 1024 * 1024; // 20 MB + multipart headroom

    public static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".ppt", ".pptx"
    };

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation"
    };

    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] OleMagic = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private readonly string _root;
    private readonly ILogger<CourseMaterialFileStorage> _logger;

    public CourseMaterialFileStorage(
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ILogger<CourseMaterialFileStorage> logger)
    {
        var configured = configuration["Uploads:StoragePath"];
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "App_Data", "materials")
            : Path.GetFullPath(configured);

        var configuredMb = DefaultMaxFileSizeMb;
        if (int.TryParse(configuration["Uploads:MaxFileSizeMb"], out var parsedMb) && parsedMb > 0)
            configuredMb = parsedMb;

        // The HTTP/SignalR ceiling is 25 MiB; never advertise a larger max.
        MaxFileBytes = Math.Clamp(configuredMb, 1, 25) * 1024L * 1024L;
        _logger = logger;
    }

    public long MaxFileBytes { get; }

    public int MaxFileSizeMb => (int)(MaxFileBytes / (1024L * 1024L));

    /// <summary>Saves and validates one attachment, returning its generated
    /// stored name and display metadata. Invalid content is deleted before the
    /// exception leaves this method.</summary>
    public async Task<StoredMaterialFile> SaveAsync(
        IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length <= 0)
            throw new MaterialFileException("Choose a non-empty file to upload.");
        if (file.Length > MaxFileBytes)
            throw new MaterialFileException($"Files must be {MaxFileSizeMb} MB or smaller.");

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
            throw new MaterialFileException(
                "Only PDF, Word (.doc/.docx) and PowerPoint (.ppt/.pptx) files are accepted.");

        extension = extension.ToLowerInvariant();
        var originalFileName = SanitizeOriginalFileName(file.FileName, extension);
        var contentType = ContentTypes[extension];

        Directory.CreateDirectory(_root);
        var temporaryPath = Path.Combine(_root, $"{Guid.NewGuid():N}.upload");
        var storedFileName = StoredFileStorage.NewFileName(extension);
        var finalPath = Path.Combine(_root, storedFileName);

        try
        {
            await using (var input = file.OpenReadStream())
            await using (var output = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            if (!await IsSignatureValidAsync(temporaryPath, extension, cancellationToken))
                throw new MaterialFileException(
                    "The uploaded file content does not match its file extension.");

            File.Move(temporaryPath, finalPath);
            return new StoredMaterialFile(storedFileName, originalFileName, file.Length, contentType);
        }
        catch
        {
            StoredFileStorage.TryDelete(temporaryPath, _logger, "an incomplete material upload");
            StoredFileStorage.TryDelete(finalPath, _logger, "an invalid material upload");
            throw;
        }
    }

    /// <summary>Resolves a stored bare GUID+extension name to an absolute path
    /// inside the material folder; path traversal and unknown extensions fail.</summary>
    public string? ResolvePath(string? storedFileName) =>
        StoredFileStorage.ResolvePath(_root, storedFileName, AllowedExtensions);

    /// <summary>Best-effort delete; a missing or unreadable file never fails the API call.</summary>
    public void Delete(string? storedFileName)
    {
        var path = ResolvePath(storedFileName);
        StoredFileStorage.TryDelete(path, _logger, "a course-material attachment");
    }

    public static string? ContentTypeFor(string? storedFileName)
    {
        if (string.IsNullOrWhiteSpace(storedFileName)) return null;
        var extension = Path.GetExtension(storedFileName);
        return !string.IsNullOrWhiteSpace(extension)
            && ContentTypes.TryGetValue(extension, out var contentType)
                ? contentType
                : null;
    }

    private static async Task<bool> IsSignatureValidAsync(
        string path, string extension, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);

        switch (extension)
        {
            case ".pdf":
                return await StartsWithAsync(stream, PdfMagic, cancellationToken);

            case ".doc":
            case ".ppt":
                return await StartsWithAsync(stream, OleMagic, cancellationToken);

            case ".docx":
            case ".pptx":
                if (!await StartsWithAsync(stream, ZipMagic, cancellationToken))
                    return false;

                stream.Position = 0;
                var requiredPrefix = extension == ".docx" ? "word/" : "ppt/";
                try
                {
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
                    return archive.Entries.Any(e =>
                        e.FullName.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase));
                }
                catch (InvalidDataException)
                {
                    return false;
                }

            default:
                return false;
        }
    }

    private static async Task<bool> StartsWithAsync(
        Stream stream, byte[] magic, CancellationToken cancellationToken)
    {
        var buffer = new byte[magic.Length];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(total, buffer.Length - total), cancellationToken);
            if (read == 0) break;
            total += read;
        }

        return total == magic.Length && buffer.AsSpan().SequenceEqual(magic);
    }

    private static string SanitizeOriginalFileName(string? clientName, string extension)
    {
        var raw = (clientName ?? "").Replace('\\', '/');
        var leaf = raw[(raw.LastIndexOf('/') + 1)..];

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(leaf
            .Where(c => !char.IsControl(c) && c != '/' && c != '\\' && !invalid.Contains(c))
            .ToArray())
            .Trim();

        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = "material" + extension;
        if (string.IsNullOrWhiteSpace(Path.GetExtension(cleaned)))
            cleaned += extension;

        const int maxLength = 200;
        if (cleaned.Length > maxLength)
        {
            var extensionPart = Path.GetExtension(cleaned);
            var stem = Path.GetFileNameWithoutExtension(cleaned);
            var maxStem = Math.Max(1, maxLength - extensionPart.Length);
            cleaned = stem[..Math.Min(stem.Length, maxStem)] + extensionPart;
        }

        return cleaned;
    }
}
