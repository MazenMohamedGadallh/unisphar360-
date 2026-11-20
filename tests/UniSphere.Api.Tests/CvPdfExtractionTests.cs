using System.IO.Compression;
using System.Text;
using UniSphere.Api.Services;
using Xunit;

namespace UniSphere.Api.Tests;

/// <summary>CV PDFs from ReportLab (and others) wrap content streams in
/// /Filter [/ASCII85Decode /FlateDecode]; the extractor must read them.</summary>
public class CvPdfExtractionTests
{
    [Fact]
    public void ExtractPdf_reads_ascii85_wrapped_flate_streams_with_line_breaks()
    {
        const string content =
            "BT /F1 12 Tf 72 720 Td (Mazen Mohamed) Tj 0 -14 Td (PROFILE) Tj " +
            "T* (AI and machine learning enthusiast \\(Cairo\\)) Tj ET";
        var path = WritePdf(Ascii85(Deflate(Encoding.Latin1.GetBytes(content))),
            "/Filter [/ASCII85Decode /FlateDecode]");
        try
        {
            var text = CvFileService.ExtractPdf(path);

            Assert.Contains("Mazen Mohamed", text);
            Assert.Contains("AI and machine learning enthusiast (Cairo)", text);
            // Td/T* start new lines so section headings stay on their own line.
            Assert.Contains("\nPROFILE\n", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExtractPdf_still_reads_plain_flate_streams()
    {
        var path = WritePdf(Encoding.Latin1.GetString(Deflate(Encoding.Latin1.GetBytes(
            "BT 72 720 Td (Plain flate text) Tj ET"))), "/Filter /FlateDecode");
        try
        {
            Assert.Contains("Plain flate text", CvFileService.ExtractPdf(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] Deflate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(data);
        return ms.ToArray();
    }

    /// <summary>Adobe ASCII85 encoder (test-side mirror of the decoder).</summary>
    private static string Ascii85(byte[] data)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < data.Length; i += 4)
        {
            var n = Math.Min(4, data.Length - i);
            uint v = 0;
            for (var j = 0; j < 4; j++) v = (v << 8) | (j < n ? data[i + j] : (byte)0);
            if (n == 4 && v == 0) { sb.Append('z'); continue; }
            var chars = new char[5];
            for (var k = 4; k >= 0; k--) { chars[k] = (char)('!' + v % 85); v /= 85; }
            sb.Append(chars, 0, n + 1);
        }
        return sb.Append("~>").ToString();
    }

    private static string WritePdf(string streamData, string filter)
    {
        var pdf = $"%PDF-1.4\n1 0 obj\n<< /Length {streamData.Length} {filter} >>\nstream\n{streamData}\nendstream\nendobj\n%%EOF\n";
        var path = Path.Combine(Path.GetTempPath(), $"cv-extract-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(pdf));
        return path;
    }
}
