using EzPocket.Models;

namespace EzPocket.Services;

/// <summary>
/// Finds macOS metadata files that clutter Pocket file browsers, previews them, and removes only
/// items the user approved after re-verifying each one. Leftover core asset folders are reported, never removed.
/// </summary>
public sealed class CardCleanupService
{
    private const int MaxEntries = 300_000;
    private const long MaxAppleDoubleBytes = 16L * 1024 * 1024;
    private const long MaxDsStoreBytes = 16L * 1024 * 1024;
    private static readonly byte[] AppleDoubleMagic = [0x00, 0x05, 0x16, 0x07];
    private static readonly byte[] DsStoreMagic = [0x00, 0x00, 0x00, 0x01, 0x42, 0x75, 0x64, 0x31];
    private readonly IAppDiagnostics diagnostics;

    public CardCleanupService(IAppDiagnostics? diagnostics = null) => this.diagnostics = diagnostics ?? NullAppDiagnostics.Instance;

    public CardCleanupPreview Preview(PocketDrive pocket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pocket);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(pocket.RootPath));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The target is unavailable.");
        var appleDouble = new List<CleanupItem>();
        var dsStore = new List<CleanupItem>();
        var trash = new List<CleanupItem>();
        int skipped = 0, entries = 0;
        bool limited = false;

        // macOS keeps deleted files in a hidden .Trashes folder on removable volumes; they still use card space.
        string trashes = Path.Combine(root, ".Trashes");
        if (IsPlainDirectory(trashes))
        {
            long bytes = DirectorySize(trashes, cancellationToken, ref skipped);
            trash.Add(new(trashes, ".Trashes", CleanupCategory.MacTrash, bytes, true));
        }

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            IEnumerable<FileSystemInfo> children;
            try { children = new DirectoryInfo(directory).EnumerateFileSystemInfos().ToArray(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; continue; }
            foreach (FileSystemInfo child in children)
            {
                if (++entries > MaxEntries) { limited = true; pending.Clear(); break; }
                if ((child.Attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                if (child is DirectoryInfo folder)
                {
                    if (IsSkippedDirectory(root, folder)) continue;
                    pending.Push(folder.FullName);
                    continue;
                }
                if (child is not FileInfo file) continue;
                string relative = Relative(root, file.FullName);
                try
                {
                    if (file.Name.StartsWith("._", StringComparison.Ordinal) && file.Length <= MaxAppleDoubleBytes && HasMagic(file.FullName, AppleDoubleMagic))
                        appleDouble.Add(new(file.FullName, relative, CleanupCategory.AppleDouble, file.Length, false));
                    else if (file.Name == ".DS_Store" && file.Length <= MaxDsStoreBytes && HasMagic(file.FullName, DsStoreMagic))
                        dsStore.Add(new(file.FullName, relative, CleanupCategory.DesktopServicesStore, file.Length, false));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
            }
        }

        var groups = new List<CleanupGroup>
        {
            new(CleanupCategory.AppleDouble, "macOS resource files (._*)",
                "Hidden companion files macOS writes next to copied files. The Pocket lists them as broken games, cores, or palettes.", true, Sort(appleDouble)),
            new(CleanupCategory.DesktopServicesStore, "Finder view settings (.DS_Store)",
                "Finder folder-layout files. The Pocket does not use them.", true, Sort(dsStore)),
            new(CleanupCategory.MacTrash, "macOS Trash on this card (.Trashes)",
                "Files deleted in Finder but never emptied. They still take space on the card. Off by default because the files cannot be recovered after removal.", false, trash)
        };
        var leftovers = FindLeftoverAssetFolders(root, pocket, cancellationToken, ref skipped);

        diagnostics.Info("CardCleanupPreviewed", new Dictionary<string, string?>
        {
            ["TargetId"] = AppDiagnosticsService.TargetId(pocket.RootPath),
            ["AppleDouble"] = appleDouble.Count.ToString(),
            ["DsStore"] = dsStore.Count.ToString(),
            ["Leftovers"] = leftovers.Count.ToString()
        });
        return new(pocket, groups, leftovers, skipped, limited);
    }

    public CardCleanupResult Apply(CardCleanupPreview preview, IReadOnlyCollection<CleanupCategory> categories, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(preview.Pocket.RootPath));
        if (!Directory.Exists(root)) return new(0, 0, 0, "The target is unavailable. Reconnect it and prepare a new cleanup review.");
        int removed = 0, skipped = 0;
        long freed = 0;
        foreach (CleanupItem item in preview.Groups.Where(group => categories.Contains(group.Category)).SelectMany(group => group.Items))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!IsInside(root, item.FullPath) || !StillMatches(item)) { skipped++; continue; }
                if (item.IsDirectory) Directory.Delete(item.FullPath, recursive: true);
                else File.Delete(item.FullPath);
                removed++;
                freed += item.Bytes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
        }

        diagnostics.Info("CardCleanupApplied", new Dictionary<string, string?>
        {
            ["TargetId"] = AppDiagnosticsService.TargetId(preview.Pocket.RootPath),
            ["Removed"] = removed.ToString(),
            ["Skipped"] = skipped.ToString()
        });
        string message = $"Removed {removed} item{(removed == 1 ? "" : "s")} · {ByteFormat.Format(freed)} freed";
        if (skipped > 0) message += $" · {skipped} skipped because they changed since the review or could not be removed";
        return new(removed, skipped, freed, message);
    }

    private static bool StillMatches(CleanupItem item)
    {
        if (item.IsDirectory) return IsPlainDirectory(item.FullPath);
        var file = new FileInfo(item.FullPath);
        if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length != item.Bytes) return false;
        return item.Category switch
        {
            CleanupCategory.AppleDouble => file.Name.StartsWith("._", StringComparison.Ordinal) && HasMagic(file.FullName, AppleDoubleMagic),
            CleanupCategory.DesktopServicesStore => file.Name == ".DS_Store" && HasMagic(file.FullName, DsStoreMagic),
            _ => false
        };
    }

    private static List<LeftoverAssetFolder> FindLeftoverAssetFolders(string root, PocketDrive pocket, CancellationToken cancellationToken, ref int skipped)
    {
        var result = new List<LeftoverAssetFolder>();
        string assets = Path.Combine(root, "Assets");
        string cores = Path.Combine(root, "Cores");
        if (!IsPlainDirectory(assets) || !IsPlainDirectory(cores)) return result;
        HashSet<string> installed;
        try
        {
            installed = Directory.EnumerateDirectories(cores).Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; return result; }

        try
        {
            foreach (string platform in Directory.EnumerateDirectories(assets))
            {
                foreach (string folder in Directory.EnumerateDirectories(platform))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string name = Path.GetFileName(folder);
                    // Core-specific asset folders are named Author.Core, matching the installed core folder.
                    if (name.Equals("common", StringComparison.OrdinalIgnoreCase) || !name.Contains('.') || name.StartsWith('.') || installed.Contains(name)) continue;
                    if (!IsPlainDirectory(folder)) continue;
                    int files = 0;
                    long bytes = DirectorySize(folder, cancellationToken, ref skipped, count => files = count);
                    result.Add(new(Relative(root, folder), bytes, files));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
        return result.OrderByDescending(folder => folder.Bytes).ToList();
    }

    private static long DirectorySize(string directory, CancellationToken cancellationToken, ref int skipped, Action<int>? fileCount = null)
    {
        long total = 0;
        int files = 0;
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count > 0 && files < MaxEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string current = pending.Pop();
            try
            {
                foreach (FileSystemInfo child in new DirectoryInfo(current).EnumerateFileSystemInfos())
                {
                    if ((child.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (child is DirectoryInfo folder) pending.Push(folder.FullName);
                    else if (child is FileInfo file) { total += file.Length; files++; }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
        }
        fileCount?.Invoke(files);
        return total;
    }

    private static bool IsSkippedDirectory(string root, DirectoryInfo folder)
    {
        if (!string.Equals(folder.Parent?.FullName is { } parent ? Path.TrimEndingDirectorySeparator(parent) : null, root, PathComparison)) return false;
        // Trash is handled as one opt-in item; OS-owned volume folders are never touched or scanned.
        return folder.Name is ".Trashes" or ".Spotlight-V100" or ".fseventsd" or "System Volume Information" or "$RECYCLE.BIN";
    }

    private static bool HasMagic(string path, byte[] magic)
    {
        Span<byte> buffer = stackalloc byte[8];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        int read = stream.ReadAtLeast(buffer[..magic.Length], magic.Length, throwOnEndOfStream: false);
        return read == magic.Length && buffer[..magic.Length].SequenceEqual(magic);
    }

    private static bool IsPlainDirectory(string path) => Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;

    private static bool IsInside(string root, string path)
    {
        string full = Path.GetFullPath(path);
        string prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return full.Length > prefix.Length && full.StartsWith(prefix, PathComparison);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static IReadOnlyList<CleanupItem> Sort(List<CleanupItem> items) => items.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
}
