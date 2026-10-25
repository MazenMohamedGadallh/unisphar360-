namespace UniSphere.Api.Services;

/// <summary>
/// Shared disk-safety primitives for server-stored uploads. Callers keep their
/// own extension/signature policy; this class only generates bare GUID file
/// names, resolves them without allowing path traversal, and deletes files
/// best-effort. Absolute server paths are never persisted or returned.
/// </summary>
internal static class StoredFileStorage
{
    public static string NewFileName(string extension) =>
        $"{Guid.NewGuid():N}{extension}";

    /// <summary>Resolves a stored bare file name to an absolute path inside
    /// <paramref name="root"/>. Returns null for paths, traversal attempts,
    /// disallowed extensions and anything whose full path escapes the root.</summary>
    public static string? ResolvePath(string root, string? fileName, HashSet<string> allowedExtensions)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var safeName = Path.GetFileName(fileName);
        if (!string.Equals(safeName, fileName, StringComparison.Ordinal)) return null;
        if (safeName.Contains('/') || safeName.Contains('\\')) return null;
        if (safeName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;

        var extension = Path.GetExtension(safeName);
        if (string.IsNullOrWhiteSpace(extension) || !allowedExtensions.Contains(extension))
            return null;

        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, safeName));
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        return fullPath;
    }

    public static void TryDelete(string? path, ILogger logger, string description)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete {FileDescription}", description);
        }
    }
}
