namespace EzPocket.Models;

public sealed record CoreSyncPreview(
    string PocketPath,
    string StagingPath,
    CoreSyncTargetSnapshot TargetSnapshot,
    IReadOnlyList<CoreComparison> Cores,
    IReadOnlyList<CoreSyncFileChange> Changes,
    IReadOnlyList<CoreSyncRemoval> Removals,
    IReadOnlyList<CoreSyncOverride> Overrides,
    IReadOnlyList<string> Blockers)
{
    public bool CanSync => Blockers.Count == 0 && (Changes.Count > 0 || Removals.Count > 0);
    public long TotalBytes => Changes.Sum(change => change.SizeBytes);
    public int AddOrReplaceFileCount => Changes.Count;
    public int RemoveFileCount => Removals.Sum(removal => removal.FileCount);
}

public sealed record CoreSyncFileChange(string RelativePath, string SourcePath, long SizeBytes, bool ReplacesExisting)
{
    public string Action => ReplacesExisting ? "Replace" : "Add";
    public string SizeLabel => SizeBytes < 1024 * 1024
        ? $"{Math.Max(1, SizeBytes / 1024d):0.#} KB"
        : $"{SizeBytes / 1024d / 1024d:0.#} MB";
}

public sealed record CoreSyncTargetSnapshot(
    string FullPath,
    string? VolumeRoot,
    long TotalSize,
    string? VolumeLabel,
    long RequiredFreeBytes);

public sealed record CoreSyncRemoval(string Identifier, string FriendlyName, string RelativePath, int FileCount, long SizeBytes)
{
    public string Summary => FileCount == 1 ? "1 file" : $"{FileCount} files";
}

public sealed record CoreSyncOverride(string RelativePath, string KeptFromCore, string IgnoredFromCore)
{
    public string Summary => $"Using {KeptFromCore}; {IgnoredFromCore}'s version is skipped.";
}

public sealed record CoreSyncResult(bool Succeeded, int FilesWritten, int CoresRemoved, string? BackupPath, string Message, int BackupsPruned = 0);
