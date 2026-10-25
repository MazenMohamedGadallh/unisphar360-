using System.Text.Json;
using System.Text.RegularExpressions;

namespace UniSphere.Api.Services;

/// <summary>
/// Tolerant extraction of the JSON payload from an LLM completion. Real models
/// (reasoning models in particular) occasionally wrap the requested JSON in
/// reasoning tag blocks, markdown fences, or prose — and can truncate long
/// outputs mid-array. Blind slicing from the first '[' to the last ']' then
/// parses garbage. This extractor, in order:
/// 1. strips reasoning-tag wrapper blocks (the open/close think-tag pair);
/// 2. tries the whole remaining text as JSON;
/// 3. tries the body of a markdown code fence;
/// 4. scans for the first balanced {'...'} or [...] span, ignoring bracket
///    characters inside JSON string literals;
/// 5. otherwise salvages every complete top-level {'...'} object that fully
///    arrived (handles outputs truncated mid-array).
/// It never invents content: everything must still parse as valid JSON.
/// </summary>
public static class AiJsonExtractor
{
    /// <summary>Strips reasoning-tag blocks, e.g. the 'think'-tag pair some
    /// reasoning models emit inline in the message text.</summary>
    private static readonly Regex ThinkRegex = new(
        "<" + "think" + ">" + "[\\s\\S]*?" + "<" + "/" + "think" + ">",
        RegexOptions.Compiled);

    /// <summary>Matches a markdown code fence, capturing its body.</summary>
    private static readonly Regex FenceRegex =
        new("```(?:json|JSON)?\\s*([\\s\\S]*?)```", RegexOptions.Compiled);

    /// <summary>Attempts to pull a JSON element out of a raw completion.</summary>
    public static bool TryExtract(string raw, out JsonElement element)
    {
        element = default;
        var text = raw ?? "";
        text = ThinkRegex.Replace(text, "").Trim();
        if (text.Length == 0) return false;

        if (TryParse(text, out element)) return true;

        var fence = FenceRegex.Match(text);
        if (fence.Success && TryParse(fence.Groups[1].Value.Trim(), out element)) return true;

        var (start, end, balanced) = FindFirstBalancedSpan(text);
        if (balanced && TryParse(text[start..(end + 1)], out element)) return true;

        // No balanced span (e.g. truncated output): salvage every complete
        // top-level JSON object the text still contains.
        return TrySalvageObjects(text, out element);
    }

    private static bool TryParse(string candidate, out JsonElement element)
    {
        try
        {
            using var doc = JsonDocument.Parse(candidate);
            element = doc.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            element = default;
            return false;
        }
    }

    /// <summary>Scans from the first object/array opener with a closers stack,
    /// so same-type nesting pairs correctly. Returns the balanced span, or
    /// the opener index with balanced=false when it never closes.</summary>
    private static (int Start, int End, bool Balanced) FindFirstBalancedSpan(string text)
    {
        var start = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '{' or '[') { start = i; break; }
        }
        if (start < 0) return (-1, -1, false);

        var closers = new List<char>();
        var inString = false;
        for (var j = start; j < text.Length; j++)
        {
            var ch = text[j];
            if (inString)
            {
                if (ch == '\\') j++;                    // skip the escaped char
                else if (ch == '"') inString = false;
                continue;
            }
            switch (ch)
            {
                case '"': inString = true; break;
                case '{': closers.Add('}'); break;
                case '[': closers.Add(']'); break;
                case '}' or ']':
                    if (closers.Count == 0 || closers[^1] != ch) return (start, -1, false);
                    closers.RemoveAt(closers.Count - 1);
                    if (closers.Count == 0) return (start, j, true);
                    break;
            }
        }
        return (start, -1, false);
    }

    private static bool TrySalvageObjects(string text, out JsonElement element)
    {
        element = default;
        var items = new List<JsonElement>();
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] != '{') { i++; continue; }
            var end = FindMatchingClose(text, i, '{', '}');
            if (end < 0) { i++; continue; }             // unclosed (truncated) — look inside for complete elements
            if (!TryParse(text[i..(end + 1)], out var obj)) { i++; continue; }
            items.Add(obj);
            i = end + 1;
        }
        if (items.Count == 0) return false;

        // Rebuild a syntactically valid JSON array from the salvaged objects.
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var item in items) item.WriteTo(writer);
            writer.WriteEndArray();
        }
        using var doc = JsonDocument.Parse(buffer.ToArray());
        element = doc.RootElement.Clone();
        return true;
    }

    /// <summary>Index of the close character matching the same-type opener at
    /// 'start' (nested same-type openers tracked by depth), or -1 when never
    /// closed. Brackets inside string literals are skipped.</summary>
    private static int FindMatchingClose(string text, int start, char open, char close)
    {
        var depth = 0;
        var inString = false;
        for (var j = start + 1; j < text.Length; j++)
        {
            var ch = text[j];
            if (inString)
            {
                if (ch == '\\') j++;
                else if (ch == '"') inString = false;
                continue;
            }
            if (ch == '"') inString = true;
            else if (ch == open) depth++;
            else if (ch == close)
            {
                if (depth == 0) return j;
                depth--;
            }
        }
        return -1;
    }
}
