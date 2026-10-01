namespace EzPocket.Services;

internal static class TargetPaths
{
    public static StringComparison Comparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static bool Equal(string? left, string? right) => string.Equals(left, right, Comparison);

    public static bool Contains(string root, string path)
    {
        string normalizedRoot = Path.GetFullPath(root);
        string normalizedPath = Path.GetFullPath(path);
        string prefix = Path.EndsInDirectorySeparator(normalizedRoot)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        return Equal(normalizedRoot, normalizedPath) || normalizedPath.StartsWith(prefix, Comparison);
    }
}
