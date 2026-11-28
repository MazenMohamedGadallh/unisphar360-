using System.Net.Http.Headers;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace UniSphere.Api.Tests;

public class CvPdfUploadTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task UploadedPdf_IsReadLineByLine()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        string[] lines = ["Education", "BSc Computer Science, Cairo University", "Projects", "Library REST API with SQL and Git"];
        for (var i = 0; i < lines.Length; i++) page.AddText(lines[i], 12, new PdfPoint(50, 750 - i * 24), font);

        var file = new ByteArrayContent(builder.Build());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var client = CareerTestHelpers.NewGraduate(factory);
        var cv = await CareerTestHelpers.Json(client.PostAsync("/api/grad/profile/cv",
            new MultipartFormDataContent { { file, "file", "real.pdf" } }));

        Assert.Equal("Parsed", cv.GetProperty("parseStatus").GetString());
        Assert.True(cv.GetProperty("textChars").GetInt32() >= 80);
        await client.DeleteAsync($"/api/grad/profile/cv/{cv.GetProperty("id").GetInt32()}");
    }
}
