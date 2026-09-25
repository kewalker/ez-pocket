namespace EzPocket.Models;

public sealed record SaveVaultSource(string Name, string RelativePath, int FileCount, long SizeBytes, string? Issue = null)
{
    public string Summary => Issue ?? (FileCount == 0 ? "No files found" : $"{FileCount} file{(FileCount == 1 ? string.Empty : "s")} · {FormatBytes(SizeBytes)}");

    internal static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }
}

public sealed record SaveVaultPreview(
    PocketDrive Pocket,
    IReadOnlyList<SaveVaultSource> Sources,
    int FileCount,
    long SizeBytes)
{
    public bool HasContent => FileCount > 0;
    public bool HasUnavailableSources => Sources.Any(source => source.Issue is not null);
    public bool CanCreate => HasContent && !HasUnavailableSources;
    public string Summary => HasContent
        ? $"{FileCount} file{(FileCount == 1 ? string.Empty : "s")} · {SaveVaultSource.FormatBytes(SizeBytes)}"
        : "No save or memory files found";
}

public sealed record SaveVaultSnapshot(
    string Id,
    DateTimeOffset CreatedAt,
    string ArchivePath,
    int FileCount,
    long SizeBytes)
{
    public string Summary => $"{FileCount} file{(FileCount == 1 ? string.Empty : "s")} · {SaveVaultSource.FormatBytes(SizeBytes)}";
}

public sealed record SaveVaultResult(bool Succeeded, string Message, SaveVaultSnapshot? Snapshot = null);

public enum SaveVaultRestoreChangeKind
{
    SnapshotOnly,
    Changed,
    Unchanged,
    TargetOnly
}

public sealed record SaveVaultRestoreEntry(
    string RelativePath,
    SaveVaultRestoreChangeKind ChangeKind,
    long SnapshotSizeBytes,
    long TargetSizeBytes)
{
    public bool CanRestore => ChangeKind is SaveVaultRestoreChangeKind.SnapshotOnly or SaveVaultRestoreChangeKind.Changed;
    public string Summary => ChangeKind switch
    {
        SaveVaultRestoreChangeKind.SnapshotOnly => "Absent from target · will be added",
        SaveVaultRestoreChangeKind.Changed => "Different on target · will be replaced",
        SaveVaultRestoreChangeKind.Unchanged => "Matches target · no restore needed",
        SaveVaultRestoreChangeKind.TargetOnly => "Not in snapshot · will be kept",
        _ => string.Empty
    };
}

public sealed record SaveVaultRestorePreview(
    PocketDrive Pocket,
    SaveVaultSnapshot Snapshot,
    IReadOnlyList<SaveVaultRestoreEntry> Entries,
    string? Issue = null)
{
    public bool CanRestore => Issue is null && Entries.Any(entry => entry.CanRestore);
    public int ChangeCount => Entries.Count(entry => entry.CanRestore);
    public string Summary => Issue ?? (ChangeCount == 0
        ? "This snapshot already matches the target."
        : $"{ChangeCount} file{(ChangeCount == 1 ? string.Empty : "s")} can be restored; target-only files will be kept.");
}

public sealed record SaveVaultRestoreResult(bool Succeeded, string Message, int RestoredCount = 0, SaveVaultSnapshot? SafetySnapshot = null);
