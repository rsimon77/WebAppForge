using System.IO;

namespace WebAppForge.Desktop.Services;

/// <summary>Converts between project-relative paths (forward slashes) and full local paths.</summary>
public static class ProjectPaths
{
    /// <summary>Full path of a value that may be relative to <paramref name="folder"/>.</summary>
    public static string Resolve(string? folder, string path)
    {
        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(folder ?? string.Empty, path));
    }

    /// <summary>Expresses <paramref name="fullPath"/> relative to <paramref name="folder"/>, or leaves it absolute when that is not possible.</summary>
    public static string ToRelative(string folder, string fullPath)
    {
        var relative = Path.GetRelativePath(folder, fullPath);
        return Path.IsPathRooted(relative) ? fullPath : relative.Replace('\\', '/');
    }
}
