namespace UniSphere.Api.Components;

/// <summary>
/// Centralized Lucide-style inline SVG icon registry (stroke=currentColor,
/// ISC-licensed geometry). Rendered through the Icon.razor component so the
/// whole app shares one outline style, one sizing rule and one color model.
/// </summary>
public static class IconSet
{
    private static readonly Dictionary<string, string> Icons = new(StringComparer.Ordinal)
    {
        ["house"] = "<path d=\"m3 9 9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z\"/><path d=\"M9 22V12h6v10\"/>",
        ["graduation-cap"] = "<path d=\"M21.42 10.922a1 1 0 0 0-.019-1.838L12.83 5.18a2 2 0 0 0-1.66 0L2.6 9.08a1 1 0 0 0 0 1.832l8.57 3.908a2 2 0 0 0 1.66 0z\"/><path d=\"M22 10v6\"/><path d=\"M6 12.5V16a6 3 0 0 0 12 0v-3.5\"/>",
        ["briefcase"] = "<path d=\"M16 20V4a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v16\"/><rect width=\"20\" height=\"14\" x=\"2\" y=\"6\" rx=\"2\"/>",
        ["flask-conical"] = "<path d=\"M10 2v7.527a2 2 0 0 1-.211.896L4.72 20.55a1 1 0 0 0 .9 1.45h12.76a1 1 0 0 0 .9-1.45l-5.069-10.127A2 2 0 0 1 14 9.527V2\"/><path d=\"M8.5 2h7\"/><path d=\"M7 16h10\"/>",
        ["landmark"] = "<line x1=\"3\" x2=\"21\" y1=\"22\" y2=\"22\"/><line x1=\"6\" x2=\"6\" y1=\"18\" y2=\"11\"/><line x1=\"10\" x2=\"10\" y1=\"18\" y2=\"11\"/><line x1=\"14\" x2=\"14\" y1=\"18\" y2=\"11\"/><line x1=\"18\" x2=\"18\" y1=\"18\" y2=\"11\"/><polygon points=\"12 2 20 7 4 7\"/>",
        ["life-buoy"] = "<circle cx=\"12\" cy=\"12\" r=\"10\"/><path d=\"m4.93 4.93 4.24 4.24\"/><path d=\"m14.83 9.17 4.24-4.24\"/><path d=\"m14.83 14.83 4.24 4.24\"/><path d=\"m9.17 14.83-4.24 4.24\"/><circle cx=\"12\" cy=\"12\" r=\"4\"/>",
        ["trending-up"] = "<polyline points=\"22 7 13.5 15.5 8.5 10.5 2 17\"/><polyline points=\"16 7 22 7 22 13\"/>",
        ["chart-column"] = "<path d=\"M3 3v16a2 2 0 0 0 2 2h16\"/><path d=\"M18 17V9\"/><path d=\"M13 17V5\"/><path d=\"M8 17v-3\"/>",
        ["shield"] = "<path d=\"M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z\"/>",
        ["bot"] = "<path d=\"M12 8V4H8\"/><rect width=\"16\" height=\"12\" x=\"4\" y=\"8\" rx=\"2\"/><path d=\"M2 14h2\"/><path d=\"M20 14h2\"/><path d=\"M15 13v2\"/><path d=\"M9 13v2\"/>",
        ["sparkles"] = "<path d=\"M9.937 15.5A2 2 0 0 0 8.5 14.063l-6.135-1.582a.5.5 0 0 1 0-.962L8.5 9.936A2 2 0 0 0 9.937 8.5l1.582-6.135a.5.5 0 0 1 .963 0L14.063 8.5A2 2 0 0 0 15.5 9.937l6.135 1.581a.5.5 0 0 1 0 .964L15.5 14.063a2 2 0 0 0-1.437 1.437l-1.582 6.135a.5.5 0 0 1-.963 0z\"/><path d=\"M20 3v4\"/><path d=\"M22 5h-4\"/><path d=\"M4 17v2\"/><path d=\"M5 18H3\"/>",
        ["user-round"] = "<circle cx=\"12\" cy=\"8\" r=\"5\"/><path d=\"M20 21a8 8 0 0 0-16 0\"/>",
        ["bell"] = "<path d=\"M10.268 21a2 2 0 0 0 3.464 0\"/><path d=\"M3.262 15.326A1 1 0 0 0 4 17h16a1 1 0 0 0 .74-1.673C19.41 13.956 18 12.499 18 8A6 6 0 0 0 6 8c0 4.499-1.411 5.956-2.738 7.326\"/>",
        ["bell-off"] = "<path d=\"M13.73 21a2 2 0 0 1-3.46 0\"/><path d=\"M18.63 13A17.89 17.89 0 0 1 18 8\"/><path d=\"M6.26 6.26A5.5 5.5 0 0 0 6 8c0 4.499-1.411 5.956-2.739 7.326A1 1 0 0 0 4 17h16.356\"/><path d=\"m2 2 20 20\"/><path d=\"M8.032 2.848A6 6 0 0 1 18 8\"/>",
        ["calendar-clock"] = "<path d=\"M21 7.5V6a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h3.5\"/><path d=\"M16 2 8 6\"/><circle cx=\"16.5\" cy=\"17.5\" r=\"4.5\"/><path d=\"M16.5 15.5v2.5l2 1\"/>",
        ["filter"] = "<polygon points=\"22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3\"/>",
        ["inbox"] = "<polyline points=\"22 12 16 12 14 15 10 15 8 12 2 12\"/><path d=\"M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z\"/>",
        ["sun-moon"] = "<path d=\"M12 8a2.83 2.83 0 0 0 4 4 4 4 0 1 0-4-4\"/><path d=\"M12 2v2\"/><path d=\"M12 20v2\"/><path d=\"m4.9 4.9 1.4 1.4\"/><path d=\"m17.7 17.7 1.4 1.4\"/><path d=\"M2 12h2\"/><path d=\"M20 12h2\"/><path d=\"m6.3 17.7-1.4 1.4\"/><path d=\"m19.1 4.9-1.4 1.4\"/><path d=\"M12 12 8 8\"/>",
        ["settings"] = "<path d=\"M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z\"/><circle cx=\"12\" cy=\"12\" r=\"3\"/>",
        ["circle-help"] = "<circle cx=\"12\" cy=\"12\" r=\"10\"/><path d=\"M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3\"/><path d=\"M12 17h.01\"/>",
        ["log-out"] = "<path d=\"M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4\"/><polyline points=\"16 17 21 12 16 7\"/><line x1=\"21\" x2=\"9\" y1=\"12\" y2=\"12\"/>",
        ["search"] = "<circle cx=\"11\" cy=\"11\" r=\"8\"/><path d=\"m21 21-4.3-4.3\"/>",
        ["clock"] = "<circle cx=\"12\" cy=\"12\" r=\"10\"/><polyline points=\"12 6 12 12 16 14\"/>",
        ["menu"] = "<line x1=\"4\" x2=\"20\" y1=\"12\" y2=\"12\"/><line x1=\"4\" x2=\"20\" y1=\"6\" y2=\"6\"/><line x1=\"4\" x2=\"20\" y1=\"18\" y2=\"18\"/>",
        ["x"] = "<path d=\"M18 6 6 18\"/><path d=\"m6 6 12 12\"/>",
        ["sun"] = "<circle cx=\"12\" cy=\"12\" r=\"4\"/><path d=\"M12 2v2\"/><path d=\"M12 20v2\"/><path d=\"m4.93 4.93 1.41 1.41\"/><path d=\"m17.66 17.66 1.41 1.41\"/><path d=\"M2 12h2\"/><path d=\"M20 12h2\"/><path d=\"m6.34 17.66-1.41 1.41\"/><path d=\"m19.07 4.93-1.41 1.41\"/>",
        ["moon"] = "<path d=\"M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9Z\"/>",
        ["plus"] = "<path d=\"M5 12h14\"/><path d=\"M12 5v14\"/>",
        ["check"] = "<path d=\"M20 6 9 17l-5-5\"/>",
        ["circle"] = "<circle cx=\"12\" cy=\"12\" r=\"10\"/>",
        ["pencil"] = "<path d=\"M17 3a2.85 2.83 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5Z\"/><path d=\"m15 5 4 4\"/>",
        ["trash-2"] = "<path d=\"M3 6h18\"/><path d=\"M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6\"/><path d=\"M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2\"/><line x1=\"10\" x2=\"10\" y1=\"11\" y2=\"17\"/><line x1=\"14\" x2=\"14\" y1=\"11\" y2=\"17\"/>",
        ["refresh-cw"] = "<path d=\"M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8\"/><path d=\"M21 3v5h-5\"/><path d=\"M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16\"/><path d=\"M8 16H3v5\"/>",
        ["map-pin"] = "<path d=\"M20 10c0 4.993-5.539 10.193-7.399 11.799a1 1 0 0 1-1.202 0C9.539 20.193 4 14.993 4 10a8 8 0 0 1 16 0\"/><circle cx=\"12\" cy=\"10\" r=\"3\"/>",
        ["calendar"] = "<path d=\"M8 2v4\"/><path d=\"M16 2v4\"/><rect width=\"18\" height=\"18\" x=\"3\" y=\"4\" rx=\"2\"/><path d=\"M3 10h18\"/>",
        ["calendar-days"] = "<path d=\"M8 2v4\"/><path d=\"M16 2v4\"/><rect width=\"18\" height=\"18\" x=\"3\" y=\"4\" rx=\"2\"/><path d=\"M3 10h18\"/><path d=\"M8 14h.01\"/><path d=\"M12 14h.01\"/><path d=\"M16 14h.01\"/><path d=\"M8 18h.01\"/><path d=\"M12 18h.01\"/><path d=\"M16 18h.01\"/>",
        ["calendar-check"] = "<path d=\"M8 2v4\"/><path d=\"M16 2v4\"/><rect width=\"18\" height=\"18\" x=\"3\" y=\"4\" rx=\"2\"/><path d=\"M3 10h18\"/><path d=\"m9 16 2 2 4-4\"/>",
        ["file-text"] = "<path d=\"M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z\"/><path d=\"M14 2v4a2 2 0 0 0 2 2h4\"/><path d=\"M10 9H8\"/><path d=\"M16 13H8\"/><path d=\"M16 17H8\"/>",
        ["book-open"] = "<path d=\"M2 3h6a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3H2z\"/><path d=\"M22 3h-6a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3h7z\"/>",
        ["award"] = "<circle cx=\"12\" cy=\"8\" r=\"6\"/><path d=\"M15.477 12.89 17 22l-5-3-5 3 1.523-9.11\"/>",
        ["star"] = "<path d=\"M11.525 2.295a.53.53 0 0 1 .95 0l2.31 4.679a2.12 2.12 0 0 0 1.595 1.16l5.166.756a.53.53 0 0 1 .294.907l-3.793 3.687a2.12 2.12 0 0 0-.611 1.88l.894 5.195a.53.53 0 0 1-.775.56l-4.648-2.424a2.12 2.12 0 0 0-1.96 0L6.05 21.679a.53.53 0 0 1-.775-.56l.894-5.195a2.12 2.12 0 0 0-.611-1.88L1.765 10.36a.53.53 0 0 1 .294-.907l5.166-.756a2.12 2.12 0 0 0 1.595-1.16z\"/>",
        ["lightbulb"] = "<path d=\"M15 14c.2-1 .7-1.7 1.5-2.5 1-.9 1.5-2.2 1.5-3.5A6 6 0 0 0 6 8c0 1 .2 2.2 1.5 3.5.7.7 1.3 1.5 1.5 2.5\"/><path d=\"M9 18h6\"/><path d=\"M10 22h4\"/>",
        ["triangle-alert"] = "<path d=\"m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3\"/><path d=\"M12 9v4\"/><path d=\"M12 17h.01\"/>",
        ["target"] = "<circle cx=\"12\" cy=\"12\" r=\"10\"/><circle cx=\"12\" cy=\"12\" r=\"6\"/><circle cx=\"12\" cy=\"12\" r=\"2\"/>",
        ["route"] = "<circle cx=\"6\" cy=\"19\" r=\"3\"/><path d=\"M9 19h5a4 4 0 0 0 0-8h-4a4 4 0 0 1 0-8h6\"/><circle cx=\"18\" cy=\"5\" r=\"3\"/>",
        ["clipboard-list"] = "<rect width=\"8\" height=\"4\" x=\"8\" y=\"2\" rx=\"1\" ry=\"1\"/><path d=\"M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2\"/><path d=\"M12 12h4\"/><path d=\"M12 17h4\"/><path d=\"M8 12h.01\"/><path d=\"M8 17h.01\"/>",
        ["clipboard-check"] = "<rect width=\"8\" height=\"4\" x=\"8\" y=\"2\" rx=\"1\" ry=\"1\"/><path d=\"M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2\"/><path d=\"m9 14 2 2 4-4\"/>",
        ["mic"] = "<path d=\"M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z\"/><path d=\"M19 10v2a7 7 0 0 1-14 0v-2\"/><line x1=\"12\" x2=\"12\" y1=\"19\" y2=\"22\"/>",
        ["message-circle"] = "<path d=\"M7.9 20A9 9 0 1 0 4 16.1L2 22Z\"/>",
        ["megaphone"] = "<path d=\"m3 11 18-5v12L3 14v-3z\"/><path d=\"M11.6 16.8a3 3 0 1 1-5.8-1.6\"/>",
        ["send"] = "<path d=\"M14.536 21.685a.5.5 0 0 0 .934-.048l2.007-6.02a.5.5 0 0 1 .282-.303l6.02-2.007a.5.5 0 0 0 .048-.934z\"/><path d=\"m21.73 21.939-5.32-11.566\"/>",
        ["users"] = "<path d=\"M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2\"/><circle cx=\"9\" cy=\"7\" r=\"4\"/><path d=\"M22 21v-2a4 4 0 0 0-3-3.87\"/><path d=\"M16 3.13a4 4 0 0 1 0 7.75\"/>",
    };

    /// <summary>Renders the full inline SVG for a known icon name.</summary>
    public static string Svg(string name, double size = 18, double strokeWidth = 0)
    {
        var key = Icons.ContainsKey(name ?? "") ? name! : Default;
        if (strokeWidth <= 0)
            strokeWidth = size <= 14 ? 2.25 : 2;
        return
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Fmt(size)}\" height=\"{Fmt(size)}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"{Fmt(strokeWidth)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\" class=\"lucide\" focusable=\"false\">{Icons[key]}</svg>";
    }

    /// <summary>
    /// Maps icon values that arrive from the API (legacy emoji codepoints,
    /// module names or already-modern keys) onto a registry key. Unknown
    /// values fall back to a neutral circle so nothing renders blank.
    /// </summary>
    public static string ResolveName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Default;

        var v = value.Trim();
        if (Icons.ContainsKey(v))
            return v;

        foreach (var (sequence, key) in LegacyMap)
        {
            if (v.Contains(sequence, StringComparison.Ordinal))
                return key;
        }
        return Default;
    }

    private const string Default = "circle";

    // Legacy emoji → modern key (older cached/persisted payloads).
    private static readonly (string Sequence, string Key)[] LegacyMap =
    {
        ("\U0001F3EB", "graduation-cap"), ("\U0001F393", "graduation-cap"),
        ("\U0001F4C4", "file-text"), ("\U0001F4CB", "clipboard-list"),
        ("\U0001F4C5", "calendar"), ("\U0001F4CD", "map-pin"), ("\U0001F514", "bell"),
        ("\U0001F50E", "search"), ("\U0001F50D", "search"), ("\u231A", "clock"), ("\U0001F560", "clock"),
        ("\U0001F3E0", "house"), ("\U0001F4BC", "briefcase"), ("\U0001F3DB", "landmark"),
        ("\U0001F3E2", "briefcase"), ("\U0001F4C8", "trending-up"), ("\U0001F4CA", "chart-column"),
        ("\U0001F464", "user-round"), ("\u2699", "settings"), ("\u2755", "circle-help"),
        ("\u2764", "sparkles"), ("\U0001F525", "sparkles"), ("\u2728", "sparkles"),
        ("\U0001F44D", "check"), ("\U0001F3AF", "target"), ("\U0001F4A1", "lightbulb"),
        ("\U0001F37E", "message-circle"), ("\U0001F5E8", "message-circle"), ("\U0001F916", "bot"),
        ("\u2705", "check"), ("\u26A0", "triangle-alert"), ("\u274C", "x"), ("\u23F0", "clock"),
    };

    private static string Fmt(double value) =>
        value == Math.Floor(value)
            ? ((int)value).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
