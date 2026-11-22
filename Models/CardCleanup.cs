namespace EzPocket.Models;

public enum CleanupCategory
{
    AppleDouble,
    DesktopServicesStore,
    MacTrash
}

/// <summary>One file or folder proposed for removal. Size and kind are re-verified before deleting.</summary>
public sealed record CleanupItem(string FullPath, string RelativePath, CleanupCategory Category, long Bytes, bool IsDirectory);

public sealed record CleanupGroup(CleanupCategory Category, string Title, string Detail, bool SelectedByDefault, IReadOnlyList<CleanupItem> Items)
{
    public long Bytes => Items.Sum(item => item.Bytes);
}

/// <summary>Asset folders for a core that is no longer installed. Reported only; they may hold games or BIOS files.</summary>
public sealed record LeftoverAssetFolder(string RelativePath, long Bytes, int FileCount);

public sealed record CardCleanupPreview(
    PocketDrive Pocket,
    IReadOnlyList<CleanupGroup> Groups,
    IReadOnlyList<LeftoverAssetFolder> LeftoverFolders,
    int SkippedEntries,
    bool Limited)
{
    public bool HasRemovableItems => Groups.Any(group => group.Items.Count > 0);
}

public sealed record CardCleanupResult(int Removed, int Skipped, long BytesFreed, string Message)
{
    public bool Succeeded => Skipped == 0;
}

public sealed record EjectResult(bool Succeeded, string Message);

public static class ByteFormat
{
    public static string Format(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }
}
