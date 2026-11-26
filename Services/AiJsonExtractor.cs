using System.Text.Json;
using System.Text.RegularExpressions;

namespace UniSphere.Api.Services;

public static class AiJsonExtractor
{
    private static readonly Regex ThinkRegex = new(
        "<" + "think" + ">" + "[\\s\\S]*?" + "<" + "/" + "think" + ">",
        RegexOptions.Compiled);

    private static readonly Regex FenceRegex =
        new("```(?:json|JSON)?\\s*([\\s\\S]*?)```", RegexOptions.Compiled);

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

        return TrySalvageObjects(text, out element);
    }

    public static int ReadAnswerIndex(JsonElement item, IReadOnlyList<string> options)
    {
        foreach (var name in new[] { "correctIndex", "correct_index", "answerIndex", "correctAnswer", "answer" })
        {
            if (!item.TryGetProperty(name, out var v)) continue;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n))
                return n >= 0 && n < options.Count ? n : -1;
            if (v.ValueKind != JsonValueKind.String) continue;
            var t = (v.GetString() ?? "").Trim();
            if (int.TryParse(t, out var parsed)) return parsed >= 0 && parsed < options.Count ? parsed : -1;
            if (t.Length is >= 1 and <= 3 && char.IsLetter(t[0]) && (t.Length == 1 || t[1] is '.' or ')'))
            {
                var letter = char.ToUpperInvariant(t[0]) - 'A';
                if (letter >= 0 && letter < options.Count) return letter;
            }
            var match = options.ToList().FindIndex(o => string.Equals(o.Trim(), t, StringComparison.OrdinalIgnoreCase));
            if (match >= 0) return match;
        }
        return -1;
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
            if (end < 0) { i++; continue; }             // unclosed (truncated) - look inside for complete elements
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
