namespace EzPocket.Models;

public sealed record CoreSyncPreview(
    string PocketPath,
    string StagingPath,
    IReadOnlyList<CoreComparison> Cores,
    IReadOnlyList<CoreSyncFileChange> Changes,
    IReadOnlyList<string> Blockers)
{
    public bool CanSync => Blockers.Count == 0 && Changes.Count > 0;
    public long TotalBytes => Changes.Sum(change => change.SizeBytes);
}

public sealed record CoreSyncFileChange(string RelativePath, string SourcePath, long SizeBytes, bool ReplacesExisting)
{
    public string Action => ReplacesExisting ? "Replace" : "Add";
    public string SizeLabel => SizeBytes < 1024 * 1024
        ? $"{Math.Max(1, SizeBytes / 1024d):0.#} KB"
        : $"{SizeBytes / 1024d / 1024d:0.#} MB";
}

public sealed record CoreSyncResult(bool Succeeded, int FilesWritten, string? BackupPath, string Message, int BackupsPruned = 0);
