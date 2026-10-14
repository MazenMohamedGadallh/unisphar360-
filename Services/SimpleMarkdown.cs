using System.Text;
using System.Text.RegularExpressions;

namespace UniSphere.Api.Services;

/// <summary>Minimal Markdown subset rendered WITHOUT raw HTML passthrough:
/// every text fragment is HTML-encoded first, so model output can never
/// inject markup or scripts. Supports fenced code blocks, headings, lists,
/// bold, inline code and http(s) links. Block texts render with dir="auto"
/// for Arabic RTL support.</summary>
public abstract record MdBlock;

public record MdHeading(int Level, string Text) : MdBlock;

public record MdCode(string Language, string Code) : MdBlock;

public record MdList(IReadOnlyList<string> Items, bool Ordered) : MdBlock;

public record MdParagraph(string Html) : MdBlock;

public static class SimpleMarkdown
{
    public static IReadOnlyList<MdBlock> Parse(string markdown)
    {
        var blocks = new List<MdBlock>();
        var lines = (markdown ?? "").Replace("\r\n", "\n").Split('\n');
        var paragraph = new List<string>();
        var listItems = new List<string>();
        bool listOrdered = false;
        var code = new StringBuilder();
        string? codeLang = null;
        var inCode = false;

        void FlushParagraph()
        {
            if (paragraph.Count > 0)
            {
                blocks.Add(new MdParagraph(Inline(string.Join("\n", paragraph))));
                paragraph.Clear();
            }
        }

        void FlushList()
        {
            if (listItems.Count > 0)
            {
                blocks.Add(new MdList(listItems.ToList(), listOrdered));
                listItems.Clear();
            }
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine;
            if (line.StartsWith("```"))
            {
                if (inCode)
                {
                    blocks.Add(new MdCode(codeLang ?? "", code.ToString().TrimEnd('\n')));
                    code.Clear();
                    codeLang = null;
                    inCode = false;
                }
                else
                {
                    FlushParagraph();
                    FlushList();
                    codeLang = line.Length > 3 ? line[3..].Trim() : null;
                    inCode = true;
                }
                continue;
            }
            if (inCode)
            {
                code.AppendLine(line);
                continue;
            }

            var heading = Regex.Match(line, @"^(#{1,4})\s+(.*)$");
            if (heading.Success)
            {
                FlushParagraph();
                FlushList();
                blocks.Add(new MdHeading(heading.Groups[1].Value.Length, Inline(heading.Groups[2].Value)));
                continue;
            }

            var unordered = Regex.Match(line, @"^[-*]\s+(.*)$");
            var ordered = Regex.Match(line, @"^(\d+)[.)]\s+(.*)$");
            if (unordered.Success || ordered.Success)
            {
                FlushParagraph();
                var isOrdered = ordered.Success;
                if (listItems.Count > 0 && listOrdered != isOrdered) FlushList();
                listOrdered = isOrdered;
                listItems.Add(Inline(isOrdered ? ordered.Groups[2].Value : unordered.Groups[1].Value));
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                FlushList();
                continue;
            }

            paragraph.Add(line.Trim());
        }

        if (inCode)
            blocks.Add(new MdCode(codeLang ?? "", code.ToString().TrimEnd('\n')));
        FlushParagraph();
        FlushList();
        return blocks;
    }

    /// <summary>Inline formatting on already-safe HTML: bold, code, links.</summary>
    public static string Inline(string text)
    {
        // 1. Pull out `code` spans so inner markup is never interpreted.
        var codes = new List<string>();
        var staged = Regex.Replace(text, "`([^`\n]+)`", m =>
        {
            codes.Add(System.Net.WebUtility.HtmlEncode(m.Groups[1].Value));
            return "\0" + (codes.Count - 1) + "\0";
        });

        // 2. Encode everything else.
        var html = System.Net.WebUtility.HtmlEncode(staged);

        // 3. Links [text](http...) — http(s) only.
        html = Regex.Replace(html, @"\[([^\]]+)\]\((https?://[^)\s]+)\)",
            m => $"<a href=\"{m.Groups[2].Value}\" target=\"_blank\" rel=\"noopener\">{m.Groups[1].Value}</a>");

        // 4. Bold **text**.
        html = Regex.Replace(html, @"\*\*([^*]+)\*\*", "<strong>$1</strong>");

        // 5. Restore code spans.
        html = Regex.Replace(html, "\0(\\d+)\0", m =>
            "<code>" + codes[int.Parse(m.Groups[1].Value)] + "</code>");

        return html.Replace("\n", "<br />");
    }
}
