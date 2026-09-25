using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using EzPocket.Models;

namespace EzPocket.Services;

/// <summary>Creates local, read-only snapshots of Pocket saves and Memories. Restore is intentionally a separate future workflow.</summary>
public sealed class SaveVaultService
{
    private static readonly (string Name, string RelativePath)[] SourceFolders = [("Saves", "Saves"), ("Memories", "Memories")];
    private const long MinimumVaultFreeBytes = 32L * 1024 * 1024;
    private readonly string vaultRoot;
    private readonly IAppDiagnostics diagnostics;

    public SaveVaultService(string? vaultRoot = null, IAppDiagnostics? diagnostics = null)
    {
        this.vaultRoot = vaultRoot ?? Path.Combine(FileSystem.AppDataDirectory, "EzPocket", "save-vault");
        this.diagnostics = diagnostics ?? NullAppDiagnostics.Instance;
    }

    public SaveVaultPreview Preview(PocketDrive pocket)
    {
        ArgumentNullException.ThrowIfNull(pocket);
        var sources = new List<SaveVaultSource>();
        foreach ((string name, string relativePath) in SourceFolders)
        {
            string sourcePath = Path.Combine(pocket.RootPath, relativePath);
            try
            {
                IEnumerable<FileInfo> files = Directory.Exists(sourcePath)
                    ? new DirectoryInfo(sourcePath).EnumerateFiles("*", SearchOption.AllDirectories)
                    : [];
                FileInfo[] materialized = files.ToArray();
                sources.Add(new SaveVaultSource(name, relativePath, materialized.Length, materialized.Sum(file => file.Length)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Error("SaveVaultPreviewFailed", exception, new Dictionary<string, string?> { ["Source"] = relativePath });
                sources.Add(new SaveVaultSource(name, relativePath, 0, 0, "Unavailable"));
            }
        }
        return new SaveVaultPreview(pocket, sources, sources.Sum(source => source.FileCount), sources.Sum(source => source.SizeBytes));
    }

    public IReadOnlyList<SaveVaultSnapshot> List(PocketDrive pocket)
    {
        string targetVault = GetTargetVault(pocket.RootPath);
        if (!Directory.Exists(targetVault)) return [];
        try
        {
            return new DirectoryInfo(targetVault)
                .EnumerateFiles("*.zip", SearchOption.TopDirectoryOnly)
                .Select(file =>
                {
                    SaveVaultManifest? manifest = ReadManifest(file);
                    return new SaveVaultSnapshot(
                        Path.GetFileNameWithoutExtension(file.Name),
                        manifest?.CreatedAt ?? file.CreationTimeUtc,
                        file.FullName,
                        manifest?.FileCount ?? 0,
                        manifest?.SizeBytes ?? file.Length);
                })
                .OrderByDescending(snapshot => snapshot.CreatedAt)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Error("SaveVaultListFailed", exception);
            return [];
        }
    }

    public SaveVaultRestorePreview PreviewRestore(PocketDrive pocket, SaveVaultSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(pocket);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Directory.Exists(pocket.RootPath))
            return new SaveVaultRestorePreview(pocket, snapshot, [], "The target is unavailable. Reconnect it and scan again before restoring.");
        if (!File.Exists(snapshot.ArchivePath))
            return new SaveVaultRestorePreview(pocket, snapshot, [], "This local snapshot is unavailable.");

        try
        {
            using ZipArchive archive = ZipFile.OpenRead(snapshot.ArchivePath);
            Dictionary<string, ZipArchiveEntry> archived = archive.Entries
                .Where(entry => IsSaveEntry(entry.FullName))
                .ToDictionary(entry => NormalizeRelativePath(entry.FullName), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, FileInfo> current = EnumerateTargetSaveFiles(pocket.RootPath)
                .ToDictionary(file => NormalizeRelativePath(Path.GetRelativePath(pocket.RootPath, file.FullName)), StringComparer.OrdinalIgnoreCase);

            var entries = new List<SaveVaultRestoreEntry>();
            foreach ((string path, ZipArchiveEntry entry) in archived)
            {
                if (!current.TryGetValue(path, out FileInfo? target))
                    entries.Add(new SaveVaultRestoreEntry(path, SaveVaultRestoreChangeKind.SnapshotOnly, entry.Length, 0));
                else
                    entries.Add(new SaveVaultRestoreEntry(path, EntriesMatch(entry, target) ? SaveVaultRestoreChangeKind.Unchanged : SaveVaultRestoreChangeKind.Changed, entry.Length, target.Length));
            }
            foreach ((string path, FileInfo target) in current.Where(item => !archived.ContainsKey(item.Key)))
                entries.Add(new SaveVaultRestoreEntry(path, SaveVaultRestoreChangeKind.TargetOnly, 0, target.Length));

            return new SaveVaultRestorePreview(pocket, snapshot, entries.OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray());
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Error("SaveVaultRestorePreviewFailed", exception);
            return new SaveVaultRestorePreview(pocket, snapshot, [], "This snapshot could not be read safely.");
        }
    }

    public Task<SaveVaultRestorePreview> PreviewRestoreAsync(PocketDrive pocket, SaveVaultSnapshot snapshot) =>
        Task.Run(() => PreviewRestore(pocket, snapshot));

    public async Task<SaveVaultRestoreResult> RestoreAsync(
        SaveVaultRestorePreview preview,
        IEnumerable<string> selectedPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(selectedPaths);
        if (preview.Issue is not null) return new SaveVaultRestoreResult(false, preview.Issue);
        if (!Directory.Exists(preview.Pocket.RootPath))
            return new SaveVaultRestoreResult(false, "The target is unavailable. Reconnect it and review the restore again.");

        HashSet<string> selected = selectedPaths
            .Select(NormalizeRelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        SaveVaultRestoreEntry[] restoreEntries = preview.Entries
            .Where(entry => entry.CanRestore && selected.Contains(entry.RelativePath))
            .ToArray();
        if (restoreEntries.Length == 0)
            return new SaveVaultRestoreResult(false, "Choose at least one added or changed save file to restore.");

        SaveVaultSnapshot? safetySnapshot = null;
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(preview.Snapshot.ArchivePath);
            Dictionary<string, ZipArchiveEntry> archived = archive.Entries
                .Where(entry => IsSaveEntry(entry.FullName))
                .ToDictionary(entry => NormalizeRelativePath(entry.FullName), StringComparer.OrdinalIgnoreCase);
            foreach (SaveVaultRestoreEntry restoreEntry in restoreEntries)
            {
                if (!archived.TryGetValue(restoreEntry.RelativePath, out ZipArchiveEntry? source))
                    return new SaveVaultRestoreResult(false, "The selected snapshot contents changed. Review the restore preview again.");
                if (!MatchesReviewedTarget(preview.Pocket.RootPath, restoreEntry, source))
                    return new SaveVaultRestoreResult(false, "The target save files changed after this review. Review the restore again before writing.");
            }

            if (restoreEntries.Any(entry => entry.ChangeKind == SaveVaultRestoreChangeKind.Changed))
            {
                SaveVaultResult safetyResult = await CreateSnapshotAsync(Preview(preview.Pocket), cancellationToken);
                if (!safetyResult.Succeeded)
                    return new SaveVaultRestoreResult(false, "Could not create a local safety snapshot before replacing current saves.");
                safetySnapshot = safetyResult.Snapshot;
            }
            foreach (SaveVaultRestoreEntry restoreEntry in restoreEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!archived.TryGetValue(restoreEntry.RelativePath, out ZipArchiveEntry? source))
                    return new SaveVaultRestoreResult(false, "The selected snapshot contents changed. Review the restore preview again.", 0, safetySnapshot);
                RestoreEntry(preview.Pocket.RootPath, restoreEntry.RelativePath, source, cancellationToken);
            }
            diagnostics.Info("SaveVaultRestoreCompleted", new Dictionary<string, string?>
            {
                ["TargetId"] = AppDiagnosticsService.TargetId(preview.Pocket.RootPath),
                ["FileCount"] = restoreEntries.Length.ToString()
            });
            return new SaveVaultRestoreResult(true, $"Restored {restoreEntries.Length} save file{(restoreEntries.Length == 1 ? string.Empty : "s")}. Target-only files were kept.", restoreEntries.Length, safetySnapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new SaveVaultRestoreResult(false, "Restore cancelled. Any completed files can be reviewed against the snapshot.", 0, safetySnapshot);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Error("SaveVaultRestoreFailed", exception);
            return new SaveVaultRestoreResult(false, "Could not finish restoring the selected save files. Review the local safety snapshot before trying again.", 0, safetySnapshot);
        }
    }

    public async Task<SaveVaultResult> CreateSnapshotAsync(SaveVaultPreview preview, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (!preview.CanCreate)
        {
            return new SaveVaultResult(false, preview.HasUnavailableSources
                ? "Could not read every save source. Reconnect the target and try again."
                : "No save or memory files are available to back up.");
        }

        string targetVault = GetTargetVault(preview.Pocket.RootPath);
        string id = $"save-vault-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        string archivePath = Path.Combine(targetVault, $"{id}.zip");
        string temporaryPath = archivePath + ".partial";
        diagnostics.Info("SaveVaultSnapshotStarted", new Dictionary<string, string?>
        {
            ["TargetId"] = AppDiagnosticsService.TargetId(preview.Pocket.RootPath),
            ["FileCount"] = preview.FileCount.ToString()
        });

        try
        {
            Directory.CreateDirectory(targetVault);
            if (!HasCapacityForSnapshot(targetVault, preview.SizeBytes))
                return new SaveVaultResult(false, "This computer does not have enough free space for a safe local snapshot.");
            SaveVaultManifest manifest = await Task.Run(() => WriteArchive(preview, temporaryPath, cancellationToken), cancellationToken);
            File.Move(temporaryPath, archivePath);
            var snapshot = new SaveVaultSnapshot(id, manifest.CreatedAt, archivePath, manifest.FileCount, manifest.SizeBytes);
            diagnostics.Info("SaveVaultSnapshotCompleted", new Dictionary<string, string?> { ["FileCount"] = snapshot.FileCount.ToString() });
            return new SaveVaultResult(true, "Save Vault snapshot created locally. Your Pocket was not changed.", snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryDelete(temporaryPath);
            diagnostics.Info("SaveVaultSnapshotCancelled");
            return new SaveVaultResult(false, "Save Vault snapshot cancelled. Your Pocket was not changed.");
        }
        catch (Exception exception)
        {
            TryDelete(temporaryPath);
            diagnostics.Error("SaveVaultSnapshotFailed", exception);
            return new SaveVaultResult(false, "Could not create a local Save Vault snapshot. Your Pocket was not changed.");
        }
    }

    private static SaveVaultManifest WriteArchive(SaveVaultPreview preview, string archivePath, CancellationToken cancellationToken)
    {
        using ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        int fileCount = 0;
        long sizeBytes = 0;
        foreach (SaveVaultSource source in preview.Sources)
        {
            string sourceRoot = Path.Combine(preview.Pocket.RootPath, source.RelativePath);
            if (!Directory.Exists(sourceRoot)) continue;
            foreach (string filePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relativePath = Path.GetRelativePath(preview.Pocket.RootPath, filePath).Replace(Path.DirectorySeparatorChar, '/');
                archive.CreateEntryFromFile(filePath, relativePath, CompressionLevel.Optimal);
                fileCount++;
                sizeBytes += new FileInfo(filePath).Length;
            }
        }

        SaveVaultManifest manifest = new(DateTimeOffset.UtcNow, fileCount, sizeBytes);
        ZipArchiveEntry entry = archive.CreateEntry("ez-pocket-save-vault.json", CompressionLevel.Optimal);
        using StreamWriter writer = new(entry.Open());
        writer.Write(JsonSerializer.Serialize(manifest));
        return manifest;
    }

    private string GetTargetVault(string pocketPath) => Path.Combine(vaultRoot, AppDiagnosticsService.TargetId(pocketPath));

    private static IEnumerable<FileInfo> EnumerateTargetSaveFiles(string pocketRoot)
    {
        foreach ((_, string relativePath) in SourceFolders)
        {
            string source = Path.Combine(pocketRoot, relativePath);
            if (!Directory.Exists(source)) continue;
            foreach (FileInfo file in new DirectoryInfo(source).EnumerateFiles("*", SearchOption.AllDirectories))
                yield return file;
        }
    }

    private static bool IsSaveEntry(string path)
    {
        string normalized = NormalizeRelativePath(path);
        return !normalized.Contains("..", StringComparison.Ordinal)
            && (normalized.StartsWith("Saves/", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("Memories/", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static bool EntriesMatch(ZipArchiveEntry source, FileInfo target)
    {
        if (source.Length != target.Length) return false;
        using Stream sourceStream = source.Open();
        using Stream targetStream = target.OpenRead();
        using SHA256 hash = SHA256.Create();
        byte[] sourceHash = hash.ComputeHash(sourceStream);
        byte[] targetHash = hash.ComputeHash(targetStream);
        return CryptographicOperations.FixedTimeEquals(sourceHash, targetHash);
    }

    private static bool MatchesReviewedTarget(string pocketRoot, SaveVaultRestoreEntry expected, ZipArchiveEntry source)
    {
        string targetPath = Path.Combine(pocketRoot, expected.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(targetPath)) return expected.ChangeKind == SaveVaultRestoreChangeKind.SnapshotOnly;
        return expected.ChangeKind == (EntriesMatch(source, new FileInfo(targetPath))
            ? SaveVaultRestoreChangeKind.Unchanged
            : SaveVaultRestoreChangeKind.Changed);
    }

    private static void RestoreEntry(string pocketRoot, string relativePath, ZipArchiveEntry source, CancellationToken cancellationToken)
    {
        string targetPath = Path.GetFullPath(Path.Combine(pocketRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string rootPath = Path.GetFullPath(pocketRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!targetPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Snapshot entry escapes the Pocket target.");
        string? directory = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrWhiteSpace(directory)) throw new InvalidDataException("Snapshot entry does not have a target directory.");
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.ez-pocket-restore");
        try
        {
            using (Stream input = source.Open())
            using (FileStream output = File.Create(temporaryPath))
                input.CopyTo(output);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, targetPath, true);
        }
        finally { TryDelete(temporaryPath); }
    }

    private static SaveVaultManifest? ReadManifest(FileInfo archive)
    {
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(archive.FullName);
            ZipArchiveEntry? entry = zip.GetEntry("ez-pocket-save-vault.json");
            if (entry is null) return null;
            using StreamReader reader = new(entry.Open());
            return JsonSerializer.Deserialize<SaveVaultManifest>(reader.ReadToEnd());
        }
        catch (InvalidDataException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    private static bool HasCapacityForSnapshot(string targetVault, long sourceBytes)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(targetVault));
            if (string.IsNullOrWhiteSpace(root)) return true;
            return new DriveInfo(root).AvailableFreeSpace >= sourceBytes + MinimumVaultFreeBytes;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record SaveVaultManifest(DateTimeOffset CreatedAt, int FileCount, long SizeBytes);
}
