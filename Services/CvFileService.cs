using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using UniSphere.Api.Data;
using UniSphere.Api.Dtos;
using UniSphere.Api.Models;

namespace UniSphere.Api.Services;

/// <summary>
/// CV upload, storage, text extraction and deletion.
/// Files live under {ContentRoot}/uploads/cv/{userId}/ with random storage
/// names (original names are only kept in the DB for display). Extraction is
/// best-effort per format; failures surface as real ParseStatus values.
/// </summary>
public class CvFileService(AppDbContext db, IWebHostEnvironment env, ILogger<CvFileService> logger)
{
    public const int MaxBytes = 10 * 1024 * 1024;
    public const int MaxExtractedChars = 15000;
    private static readonly string[] AllowedExtensions = [".pdf", ".docx", ".txt"];

    private string RootDir => Path.Combine(env.ContentRootPath, "uploads", "cv");

    public async Task<CvDocumentDto> UploadAsync(int userId, string originalFileName, string? contentType, Stream content)
    {
        var ext = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            throw new CvUploadException($"Unsupported CV format. Allowed: {string.Join(", ", AllowedExtensions)}");
        if (content.CanSeek && content.Length > MaxBytes)
            throw new CvUploadException("CV file is larger than the 10 MB limit.");

        var userDir = Path.Combine(RootDir, userId.ToString());
        Directory.CreateDirectory(userDir);
        var storedName = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{RandomNumberGenerator.GetInt32(100000, 999999)}{ext}";
        var storagePath = Path.Combine(userDir, storedName);

        await using (var fs = File.Create(storagePath))
        {
            await content.CopyToAsync(fs);
            fs.Position = 0;
            if (fs.Length > MaxBytes)
            {
                fs.Close();
                File.Delete(storagePath);
                throw new CvUploadException("CV file is larger than the 10 MB limit.");
            }
        }

        var (extractedText, parseStatus, parseError) = ExtractText(storagePath, ext);

        var doc = new CvDocument
        {
            UserId = userId,
            FileName = SanitizeFileName(originalFileName),
            ContentType = contentType ?? GuessContentType(ext),
            SizeBytes = new FileInfo(storagePath).Length,
            StoragePath = storagePath,
            ParseStatus = parseStatus,
            ExtractedText = extractedText,
            ParseError = parseError,
            UploadedAtUtc = DateTime.UtcNow
        };
        db.CvDocuments.Add(doc);
        await db.SaveChangesAsync();

        logger.LogInformation("CV uploaded: user {UserId}, doc {DocId}, parse {Status}, {Chars} chars",
            userId, doc.Id, parseStatus, extractedText?.Length ?? 0);

        return await ToDtoAsync(doc);
    }

    public async Task<bool> DeleteAsync(int userId, int cvId)
    {
        var doc = await db.CvDocuments.FirstOrDefaultAsync(c => c.UserId == userId && c.Id == cvId);
        if (doc is null) return false;

        db.CvDocuments.Remove(doc);
        await db.SaveChangesAsync();
        TryDeleteFile(doc.StoragePath);
        return true;
    }

    public async Task<IReadOnlyList<CvDocumentDto>> ListAsync(int userId)
    {
        var docs = await db.CvDocuments.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.UploadedAtUtc).ToListAsync();
        var analysisIds = await db.CvAnalyses.AsNoTracking()
            .GroupBy(a => a.CvDocumentId)
            .Select(g => new { g.Key, Id = g.Max(x => x.Id) })
            .ToDictionaryAsync(x => x.Key, x => x.Id);
        return [.. docs.Select(d =>
        {
            // TryGetValue leaves a 0 default when absent -> surface null.
            int? analysisId = analysisIds.TryGetValue(d.Id, out var aid) ? aid : null;
            return new CvDocumentDto(
                d.Id, d.FileName, d.ContentType, d.SizeBytes, d.ParseStatus, d.ParseError, d.UploadedAtUtc,
                analysisId.HasValue, analysisId, (d.ExtractedText ?? "").Length);
        })];
    }

    public async Task<CvDocument?> GetAsync(int userId, int cvId) =>
        await db.CvDocuments.FirstOrDefaultAsync(c => c.UserId == userId && c.Id == cvId);

    /// <summary>Attach user-typed CV text (e.g., when extraction failed or the
    /// user wants to edit it). Replaces existing manual text.</summary>
    public async Task<CvDocumentDto> SaveManualTextAsync(int userId, int cvId, string text)
    {
        var doc = await db.CvDocuments.FirstOrDefaultAsync(c => c.UserId == userId && c.Id == cvId)
            ?? throw new CvUploadException("CV not found for this account.");
        doc.ManualText = text.Trim();
        doc.ParseStatus = string.IsNullOrWhiteSpace(doc.ExtractedText) ? "Manual" : doc.ParseStatus;
        await db.SaveChangesAsync();
        return await ToDtoAsync(doc);
    }

    public async Task<string?> GetEffectiveTextAsync(CvDocument doc) =>
        !string.IsNullOrWhiteSpace(doc.ManualText)
            ? doc.ManualText
            : string.IsNullOrWhiteSpace(doc.ExtractedText) ? null : doc.ExtractedText;

    private async Task<CvDocumentDto> ToDtoAsync(CvDocument d)
    {
        var lastAnalysis = await db.CvAnalyses.AsNoTracking()
            .Where(a => a.CvDocumentId == d.Id)
            .OrderByDescending(a => a.CreatedAtUtc).FirstOrDefaultAsync();
        return new CvDocumentDto(
            d.Id, d.FileName, d.ContentType, d.SizeBytes, d.ParseStatus, d.ParseError, d.UploadedAtUtc,
            lastAnalysis is not null, lastAnalysis?.Id, (d.ExtractedText ?? "").Length);
    }

    // ---------------- extraction ----------------

    internal (string? Text, string Status, string? Error) ExtractText(string path, string ext)
    {
        try
        {
            var text = ext switch
            {
                ".txt" => File.ReadAllText(path, Encoding.UTF8),
                ".docx" => ExtractDocx(path),
                ".pdf" => ExtractPdf(path),
                _ => throw new NotSupportedException(ext)
            };
            text = CleanText(text ?? "");
            return text.Length >= MinUsefulChars
                ? (text.Length > MaxExtractedChars ? text[..MaxExtractedChars] : text, "Parsed", null)
                : (null, "Failed", $"Extracted almost no text ({text.Length} characters). Use manual text entry instead.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "CV text extraction failed for {Path}", path);
            return (null, "Failed", "Automatic text extraction failed. You can still enter your CV text manually.");
        }
    }

    private const int MinUsefulChars = 80;

    /// <summary>DOCX = zip; concatenate w:t runs from word/document.xml.</summary>
    internal static string ExtractDocx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("word/document.xml")
            ?? throw new InvalidDataException("Not a Word document (word/document.xml missing).");
        using var reader = new StreamReader(entry.Open());
        var xml = reader.ReadToEnd();

        var sb = new StringBuilder();
        foreach (Match m in Regex.Matches(xml, @"<w:t[^>]*>(.*?)</w:t>", RegexOptions.Singleline))
        {
            var decoded = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
            sb.Append(decoded);
            sb.Append(' ');
        }
        return sb.ToString();
    }

    /// <summary>PDF best-effort: inflate FlateDecode streams and pull text shown
    /// with Tj/TJ operators. Works for many simple text-based PDFs; scanned or
    /// encrypted PDFs fail gracefully into ParseStatus=Failed.</summary>
    internal static string ExtractPdf(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var pdf = Encoding.Latin1.GetString(bytes);
        if (!pdf.StartsWith("%PDF", StringComparison.Ordinal))
            throw new InvalidDataException("Not a valid PDF file.");

        var sb = new StringBuilder();
        var offset = 0;
        while (true)
        {
            var streamStart = pdf.IndexOf("stream", offset, StringComparison.Ordinal);
            if (streamStart < 0) break;
            var dataStart = streamStart + "stream".Length;
            if (dataStart < pdf.Length && pdf[dataStart] == '\r') dataStart++;
            if (dataStart < pdf.Length && pdf[dataStart] == '\n') dataStart++;
            var dataEnd = pdf.IndexOf("endstream", dataStart, StringComparison.Ordinal);
            if (dataEnd < 0) break;
            offset = dataEnd + 9;

            try
            {
                byte[] content = new byte[dataEnd - dataStart];
                Array.Copy(bytes, dataStart, content, 0, content.Length);
                if (TryInflate(content, out var decoded))
                {
                    var ascii = Encoding.Latin1.GetString(decoded);
                    foreach (Match m in Regex.Matches(ascii, "\\((?:\\\\.|[^\\\\)])*\\)\\s*(Tj|TJ|')\\s*", RegexOptions.Singleline))
                        sb.Append(ParsePdfString(m.Value));
                    sb.Append(' ');
                }
            }
            catch { /* ignore undecodable streams */ }
        }

        // Fallback: some PDFs embed plain text runs (not standards-perfect).
        if (sb.Length < MinUsefulChars)
        {
            foreach (Match m in Regex.Matches(pdf, @"\[((?:\\.|[^\\])*)\]\s*TJ|\((?:\\.|[^\\)])*\)\s*Tj"))
                sb.Append(ParsePdfString(m.Value));
            sb.Append(' ');
        }
        return sb.ToString();
    }

    private static bool TryInflate(byte[] input, out byte[] output)
    {
        // zlib (RFC1950, starts with 0x78) → ZLibStream; raw Deflate (RFC1951) → DeflateStream.
        var attempts = new Func<Stream>[]
        {
            () => new ZLibStream(new MemoryStream(input), CompressionMode.Decompress),
            () => new DeflateStream(new MemoryStream(input), CompressionMode.Decompress)
        };
        foreach (var open in attempts)
        {
            try
            {
                using var stream = open();
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                if (ms.Length > 0)
                {
                    output = ms.ToArray();
                    return true;
                }
            }
            catch { /* next attempt */ }
        }
        output = [];
        return false;
    }

    private static string ParsePdfString(string raw)
    {
        if (raw.StartsWith('['))
        {
            raw = raw.TrimStart('[').TrimEnd();
            var idx = raw.IndexOf(']');
            if (idx >= 0) raw = raw[..idx];
        }
        raw = raw.TrimEnd();
        raw = Regex.Replace(raw, "\\s*(Tj|TJ|'|\")\\s*$", "");
        if (!raw.StartsWith('(')) return "";
        raw = raw.TrimStart('(');
        if (raw.EndsWith(')')) raw = raw[..^1];
        raw = raw.Replace("\\(", "(").Replace("\\)", ")").Replace("\\\\", "\\").Replace(@"\n", "\n").Replace(@"\r", "").Replace(@"\t", "\t");
        return raw;
    }

    internal static string CleanText(string text)
    {
        text = text.Replace('\r', '\n').Replace('\0', ' ');
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        text = Regex.Replace(text, @"[ \t]{3,}", "  ");
        return text.Trim();
    }

    private static void TryDeleteFile(string? path)
    {
        try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
        catch { /* storage cleanup is best-effort */ }
    }

    internal static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        if (clean.Length == 0) clean = "cv";
        return clean.Length > 120 ? clean[..120] : clean;
    }

    private static string GuessContentType(string ext) => ext switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "text/plain"
    };
}

[Serializable]
public class CvUploadException(string message) : Exception(message);
