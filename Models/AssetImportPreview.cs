namespace EzPocket.Models;

public sealed record AssetFileChange(
    string SourcePath,
    string RelativePath,
    string Name,
    string Format,
    long SizeBytes,
    bool ReplacesExisting);

public sealed record AssetImportPreview(
    PocketDrive Pocket,
    IReadOnlyList<AssetFileChange> Changes,
    IReadOnlyList<string> Blockers,
    string? StagingPath = null,
    string Kind = "Game Boy palettes")
{
    public bool CanApply => Changes.Count > 0 && Blockers.Count == 0;
    public int ReplacementCount => Changes.Count(change => change.ReplacesExisting);
}

public sealed record AssetImportResult(bool Succeeded, int AddedCount, int ReplacedCount, string? BackupPath, string Message);

public sealed record AssetImportProgress(int CompletedCount, int TotalCount, string CurrentFile, bool ReplacingExisting);

public sealed record AssetPreparationProgress(string Stage, long CompletedBytes = 0, long? TotalBytes = null);

public sealed record PalettePackCatalog(PocketDrive Pocket, IReadOnlyList<AssetFileChange> Candidates, string StagingPath);
