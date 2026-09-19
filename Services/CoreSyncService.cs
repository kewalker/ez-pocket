using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EzPocket.Models;

namespace EzPocket.Services;

/// <summary>Stages approved core release archives and applies only their Pocket content folders.</summary>
public sealed class CoreSyncService
{
    private static readonly string[] AllowedRootFolders = ["Assets", "Cores", "Platforms"];
    private const int MaximumBackupsPerPocket = 5;
    private const long MaximumBackupBytesPerPocket = 1024L * 1024 * 1024;
    private const int PackageDownloadAttempts = 2;
    private static readonly TimeSpan PackageDownloadTimeout = TimeSpan.FromSeconds(45);
    private const long MaximumPackageDownloadBytes = 128L * 1024 * 1024;
    private const long MaximumPackageExtractedBytes = 512L * 1024 * 1024;
    private const int MaximumPackageEntryCount = 10_000;
    private const double MaximumCompressionRatio = 100;
    private const long CompressionRatioCheckMinimumBytes = 1024L * 1024;
    private const long MinimumFreeSpaceReserveBytes = 16L * 1024 * 1024;
    private readonly HttpClient client;
    private readonly string workingRoot;
    private readonly string backupRoot;
    private readonly IAppDiagnostics diagnostics;

    public CoreSyncService(HttpClient? client = null, string? workingRoot = null, string? backupRoot = null, IAppDiagnostics? diagnostics = null)
    {
        this.client = client ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        this.workingRoot = workingRoot ?? Path.Combine(appData, "EzPocket", "staging");
        this.backupRoot = backupRoot ?? Path.Combine(appData, "EzPocket", "backups");
        this.diagnostics = diagnostics ?? NullAppDiagnostics.Instance;
    }

    public Task<CoreSyncPreview> PrepareAsync(PocketDrive pocket, IReadOnlyList<CoreComparison> cores, CancellationToken cancellationToken = default) =>
        PrepareAsync(pocket, cores, [], cancellationToken);

    public async Task<CoreSyncPreview> PrepareAsync(PocketDrive pocket, IReadOnlyList<CoreComparison> cores, IReadOnlyList<CoreComparison> coresToRemove, CancellationToken cancellationToken = default)
    {
        diagnostics.Info("CoreSyncPrepareStarted", new Dictionary<string, string?>
        {
            ["TargetId"] = AppDiagnosticsService.TargetId(pocket.RootPath),
            ["SelectedCoreCount"] = cores.Count.ToString(),
            ["RemovalCandidateCount"] = coresToRemove.Count.ToString()
        });
        string stagingPath = Path.Combine(workingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingPath);
        var blockers = new List<string>();
        var sources = new Dictionary<string, StagedSource>(StringComparer.OrdinalIgnoreCase);
        var overrides = new List<CoreSyncOverride>();

        foreach (CoreComparison core in cores)
        {
            if (!IsSafeCoreIdentifier(core.Identifier))
            {
                blockers.Add($"{core.FriendlyName} has an unsafe core identifier.");
                continue;
            }
            if (core.RequiresLicense)
            {
                blockers.Add($"{core.FriendlyName} requires a license file and cannot be synced automatically.");
                continue;
            }
            if (!Uri.TryCreate(core.DownloadUrl, UriKind.Absolute, out Uri? url) || url.Scheme != Uri.UriSchemeHttps)
            {
                blockers.Add($"{core.FriendlyName} has no supported HTTPS package download.");
                continue;
            }

            string packageDirectory = Path.Combine(stagingPath, SanitizeDirectoryName(core.Identifier));
            string archivePath = Path.Combine(packageDirectory, "package.zip");
            string extractPath = Path.Combine(packageDirectory, "contents");
            Directory.CreateDirectory(packageDirectory);

            try
            {
                await DownloadPackageAsync(url, archivePath, cancellationToken);
                ExtractArchive(archivePath, extractPath);
                ValidateCorePackage(extractPath, core);
                diagnostics.Info("CorePackageStaged", new Dictionary<string, string?> { ["CoreId"] = core.Identifier });

                foreach (string file in Directory.EnumerateFiles(extractPath, "*", SearchOption.AllDirectories))
                {
                    string relativePath = Path.GetRelativePath(extractPath, file);
                    if (!IsAllowedPackageFile(relativePath, core.Identifier)) continue;
                    if (sources.TryGetValue(relativePath, out StagedSource? existing))
                    {
                        if (!FilesMatch(existing.SourcePath, file))
                            overrides.Add(new CoreSyncOverride(relativePath, existing.CoreName, core.FriendlyName));
                        continue;
                    }
                    sources.Add(relativePath, new StagedSource(file, core.FriendlyName));
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or TimeoutException)
            {
                diagnostics.Error("CorePackageStageFailed", exception, new Dictionary<string, string?> { ["CoreId"] = core.Identifier });
                blockers.Add($"{core.FriendlyName} could not be staged: {exception.Message}");
            }
        }

        var changes = new List<CoreSyncFileChange>();
        foreach ((string relativePath, StagedSource source) in sources.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            string destination = Path.Combine(pocket.RootPath, relativePath);
            bool replacesExisting = File.Exists(destination);
            if (replacesExisting && FilesMatch(source.SourcePath, destination)) continue;

            changes.Add(new CoreSyncFileChange(relativePath, source.SourcePath, new FileInfo(source.SourcePath).Length, replacesExisting));
        }
        IReadOnlyList<CoreSyncRemoval> removals = coresToRemove
            .Where(core => core.IsInstalled)
            .Select(core => CreateRemoval(pocket, core))
            .Where(removal => removal is not null)
            .Cast<CoreSyncRemoval>()
            .ToArray();
        CoreSyncPreview preview = new(pocket.RootPath, stagingPath, CreateTargetSnapshot(pocket, changes.Sum(change => change.SizeBytes)), cores, changes, removals, overrides, blockers);
        diagnostics.Info("CoreSyncPrepared", new Dictionary<string, string?>
        {
            ["ChangeCount"] = changes.Count.ToString(),
            ["RemovalCount"] = removals.Count.ToString(),
            ["BlockerCount"] = blockers.Count.ToString(),
            ["OverrideCount"] = overrides.Count.ToString()
        });
        return preview;
    }

    private async Task DownloadPackageAsync(Uri url, string archivePath, CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;
        for (int attempt = 1; attempt <= PackageDownloadAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCancellation.CancelAfter(PackageDownloadTimeout);
            try
            {
                using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, attemptCancellation.Token);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long contentLength && contentLength > MaximumPackageDownloadBytes)
                    throw new InvalidDataException($"Package exceeds the {MaximumPackageDownloadBytes / 1024 / 1024} MB download limit.");
                await using Stream source = await response.Content.ReadAsStreamAsync(attemptCancellation.Token);
                await using FileStream destination = File.Create(archivePath);
                await CopyWithLimitAsync(source, destination, MaximumPackageDownloadBytes, attemptCancellation.Token);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (attemptCancellation.IsCancellationRequested)
            {
                lastFailure = new TimeoutException($"Timed out after {PackageDownloadTimeout.TotalSeconds:0} seconds.");
            }
            catch (HttpRequestException exception)
            {
                lastFailure = exception;
            }
        }

        throw lastFailure ?? new HttpRequestException("Package download failed.");
    }

    public Task<CoreSyncResult> ApplyAsync(CoreSyncPreview preview, CancellationToken cancellationToken = default)
    {
        diagnostics.Info("CoreSyncApplyStarted", new Dictionary<string, string?>
        {
            ["TargetId"] = AppDiagnosticsService.TargetId(preview.PocketPath),
            ["ChangeCount"] = preview.Changes.Count.ToString(),
            ["RemovalCount"] = preview.Removals.Count.ToString()
        });
        if (!preview.CanSync)
        {
            diagnostics.Warning("CoreSyncApplyBlocked");
            return Task.FromResult(new CoreSyncResult(false, 0, 0, null, "Resolve the package issues before syncing."));
        }

        if (!TryValidateApplyTarget(preview, out string targetProblem))
        {
            diagnostics.Warning("CoreSyncApplyTargetInvalid", new Dictionary<string, string?> { ["Reason"] = targetProblem });
            return Task.FromResult(new CoreSyncResult(false, 0, 0, null, targetProblem));
        }

        string backupPath = Path.Combine(GetPocketBackupRoot(preview.PocketPath), $"core-sync-{DateTimeOffset.UtcNow.Ticks:D19}-{Guid.NewGuid():N}");
        var addedFiles = new List<string>();
        var replacedFiles = new List<string>();
        var removedDirectories = new List<CoreSyncRemoval>();
        try
        {
            foreach (CoreSyncFileChange change in preview.Changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureSafeRelativePath(change.RelativePath);
                if (!File.Exists(change.SourcePath)) throw new FileNotFoundException("A staged package file is missing.", change.SourcePath);

                string destination = Path.Combine(preview.PocketPath, change.RelativePath);
                if (File.Exists(destination))
                {
                    string backupFile = Path.Combine(backupPath, change.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
                    File.Copy(destination, backupFile, true);
                    replacedFiles.Add(destination);
                }
                else
                {
                    addedFiles.Add(destination);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                string temporaryDestination = destination + ".ez-pocket-" + Guid.NewGuid().ToString("N") + ".tmp";
                File.Copy(change.SourcePath, temporaryDestination, true);
                File.Move(temporaryDestination, destination, true);
            }

            foreach (CoreSyncRemoval removal in preview.Removals)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureSafeRelativePath(removal.RelativePath);
                string sourceDirectory = Path.Combine(preview.PocketPath, removal.RelativePath);
                if (!Directory.Exists(sourceDirectory)) continue;

                string backupDirectory = Path.Combine(backupPath, "removed", removal.RelativePath);
                CopyDirectory(sourceDirectory, backupDirectory);
                Directory.Delete(sourceDirectory, true);
                removedDirectories.Add(removal);
            }

            int backupsPruned = Directory.Exists(backupPath) ? PruneBackups(preview.PocketPath) : 0;
            try { Cleanup(preview); }
            catch (IOException) { }
            string message = FormatSuccessfulSyncMessage(preview.Cores, removedDirectories.Count);
            diagnostics.Info("CoreSyncApplied", new Dictionary<string, string?>
            {
                ["FilesWritten"] = preview.Changes.Count.ToString(),
                ["CoresRemoved"] = removedDirectories.Count.ToString(),
                ["BackupsPruned"] = backupsPruned.ToString()
            });
            return Task.FromResult(new CoreSyncResult(true, preview.Changes.Count, removedDirectories.Count, Directory.Exists(backupPath) ? backupPath : null, message, backupsPruned));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Restore(backupPath, preview.PocketPath, replacedFiles, addedFiles, removedDirectories);
            diagnostics.Error("CoreSyncApplyFailed", exception, new Dictionary<string, string?>
            {
                ["ReplacedFileCount"] = replacedFiles.Count.ToString(),
                ["AddedFileCount"] = addedFiles.Count.ToString(),
                ["RemovedCoreCount"] = removedDirectories.Count.ToString()
            });
            return Task.FromResult(new CoreSyncResult(false, 0, 0, Directory.Exists(backupPath) ? backupPath : null, $"Sync stopped and restored changed files: {exception.Message}"));
        }
    }

    public void Cleanup(CoreSyncPreview preview)
    {
        if (Directory.Exists(preview.StagingPath)) Directory.Delete(preview.StagingPath, true);
    }

    private int PruneBackups(string pocketPath)
    {
        string pocketBackupRoot = GetPocketBackupRoot(pocketPath);
        if (!Directory.Exists(pocketBackupRoot)) return 0;

        List<DirectoryInfo> backups = new DirectoryInfo(pocketBackupRoot)
            .GetDirectories("core-sync-*")
            .OrderByDescending(directory => directory.Name, StringComparer.Ordinal)
            .ToList();
        long totalBytes = backups.Sum(GetDirectorySize);
        int removed = 0;

        foreach (DirectoryInfo backup in backups.Skip(MaximumBackupsPerPocket).Concat(backups.Skip(1)).Distinct())
        {
            if (backups.Count - removed <= MaximumBackupsPerPocket && totalBytes <= MaximumBackupBytesPerPocket) break;
            long bytes = GetDirectorySize(backup);
            backup.Delete(true);
            totalBytes -= bytes;
            removed++;
        }
        return removed;
    }

    private string GetPocketBackupRoot(string pocketPath)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(pocketPath).ToUpperInvariant()));
        string key = Convert.ToHexString(hash)[..16];
        return Path.Combine(backupRoot, key);
    }

    private static string FormatSuccessfulSyncMessage(IReadOnlyList<CoreComparison> selectedCores, int removedCoreCount)
    {
        int addedCoreCount = selectedCores.Count(core => !core.IsInstalled);
        int updatedCoreCount = selectedCores.Count(core => core.IsInstalled && core.Status == "Update");
        int syncedCoreCount = selectedCores.Count - addedCoreCount - updatedCoreCount;
        var actions = new List<string>();
        if (addedCoreCount > 0) actions.Add($"added {FormatCoreCount(addedCoreCount)}");
        if (updatedCoreCount > 0) actions.Add($"updated {FormatCoreCount(updatedCoreCount)}");
        if (syncedCoreCount > 0) actions.Add($"synced {FormatCoreCount(syncedCoreCount)}");
        if (removedCoreCount > 0) actions.Add($"removed {FormatCoreCount(removedCoreCount)}");
        return char.ToUpperInvariant(actions[0][0]) + actions[0][1..] + string.Concat(actions.Skip(1).Select(action => $" and {action}")) + ".";
    }

    private static string FormatCoreCount(int count) => count == 1 ? "1 core" : $"{count} cores";

    private static long GetDirectorySize(DirectoryInfo directory) => directory
        .EnumerateFiles("*", SearchOption.AllDirectories)
        .Sum(file => file.Length);

    private static bool FilesMatch(string firstPath, string secondPath)
    {
        var first = new FileInfo(firstPath);
        var second = new FileInfo(secondPath);
        if (first.Length != second.Length) return false;

        using FileStream firstStream = File.OpenRead(firstPath);
        using FileStream secondStream = File.OpenRead(secondPath);
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(firstStream), SHA256.HashData(secondStream));
    }

    private sealed record StagedSource(string SourcePath, string CoreName);

    private static void ExtractArchive(string archivePath, string extractPath)
    {
        string root = Path.GetFullPath(extractPath) + Path.DirectorySeparatorChar;
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaximumPackageEntryCount)
            throw new InvalidDataException($"Package contains more than {MaximumPackageEntryCount:N0} entries.");
        long extractedBytes = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            if (entry.Length > MaximumPackageExtractedBytes ||
                entry.Length >= CompressionRatioCheckMinimumBytes && entry.CompressedLength > 0 && entry.Length / (double)entry.CompressedLength > MaximumCompressionRatio)
                throw new InvalidDataException("Package contains a suspiciously compressed entry.");
            extractedBytes = checked(extractedBytes + entry.Length);
            if (extractedBytes > MaximumPackageExtractedBytes)
                throw new InvalidDataException($"Package expands beyond the {MaximumPackageExtractedBytes / 1024 / 1024} MB extraction limit.");
            string target = Path.GetFullPath(Path.Combine(extractPath, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Package contains an unsafe file path.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
    }

    private static bool IsAllowedPackageFile(string relativePath, string? expectedCoreIdentifier = null)
    {
        string[] segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!AllowedRootFolders.Contains(segments[0], StringComparer.OrdinalIgnoreCase)) return false;
        return !string.Equals(segments[0], "Cores", StringComparison.OrdinalIgnoreCase) ||
            expectedCoreIdentifier is null ||
            segments.Length > 1 && string.Equals(segments[1], expectedCoreIdentifier, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureSafeRelativePath(string relativePath)
    {
        if (!IsAllowedPackageFile(relativePath) || Path.IsPathRooted(relativePath) || relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(segment => segment == ".."))
            throw new IOException("Package contains an unsafe destination path.");
    }

    private static bool IsSafeCoreIdentifier(string identifier) =>
        !string.IsNullOrWhiteSpace(identifier) &&
        !Path.IsPathRooted(identifier) &&
        identifier.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0 &&
        identifier is not "." and not "..";

    private static string SanitizeDirectoryName(string value) => string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    private static async Task CopyWithLimitAsync(Stream source, Stream destination, long maximumBytes, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            copied = checked(copied + read);
            if (copied > maximumBytes) throw new InvalidDataException($"Package exceeds the {maximumBytes / 1024 / 1024} MB download limit.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static void ValidateCorePackage(string extractPath, CoreComparison core)
    {
        string coreDefinitionPath = Path.Combine(extractPath, "Cores", core.Identifier, "core.json");
        if (!File.Exists(coreDefinitionPath))
            throw new InvalidDataException($"Package does not contain Cores/{core.Identifier}/core.json.");
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(coreDefinitionPath));
            JsonElement definition = document.RootElement.GetProperty("core");
            if (!string.Equals(definition.GetProperty("magic").GetString(), "APF_VER_1", StringComparison.Ordinal) ||
                !string.Equals(definition.GetProperty("framework").GetProperty("target_product").GetString(), "Analogue Pocket", StringComparison.Ordinal))
                throw new InvalidDataException("Package core definition is not for Analogue Pocket.");
            string? packageVersion = definition.GetProperty("metadata").GetProperty("version").GetString();
            if (string.IsNullOrWhiteSpace(packageVersion)) throw new InvalidDataException("Package core definition has no version.");
        }
        catch (KeyNotFoundException exception) { throw new InvalidDataException("Package core definition is incomplete.", exception); }
        catch (JsonException exception) { throw new InvalidDataException("Package core definition is not valid JSON.", exception); }
        catch (InvalidOperationException exception) { throw new InvalidDataException("Package core definition is incomplete.", exception); }
    }

    private static CoreSyncTargetSnapshot CreateTargetSnapshot(PocketDrive pocket, long changeBytes)
    {
        string fullPath = Path.GetFullPath(pocket.RootPath);
        DriveInfo? drive = FindContainingDrive(fullPath);
        return new CoreSyncTargetSnapshot(fullPath, drive?.RootDirectory.FullName, drive?.TotalSize ?? pocket.TotalBytes,
            drive?.VolumeLabel, checked(changeBytes + MinimumFreeSpaceReserveBytes));
    }

    private static bool TryValidateApplyTarget(CoreSyncPreview preview, out string problem)
    {
        string currentPath = Path.GetFullPath(preview.PocketPath);
        if (!string.Equals(currentPath, preview.TargetSnapshot.FullPath, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(currentPath))
        {
            problem = "The selected Pocket target is no longer available. Re-scan and prepare the changes again.";
            return false;
        }
        DriveInfo? drive = FindContainingDrive(currentPath);
        if (!string.Equals(drive?.RootDirectory.FullName, preview.TargetSnapshot.VolumeRoot, StringComparison.OrdinalIgnoreCase) ||
            drive?.TotalSize != preview.TargetSnapshot.TotalSize ||
            !string.Equals(drive?.VolumeLabel, preview.TargetSnapshot.VolumeLabel, StringComparison.Ordinal))
        {
            problem = "The selected Pocket target changed after preview. Re-scan and prepare the changes again.";
            return false;
        }
        if (drive is not null && drive.AvailableFreeSpace < preview.TargetSnapshot.RequiredFreeBytes)
        {
            problem = "The selected Pocket does not have enough free space for this sync. Free space, then prepare the changes again.";
            return false;
        }
        problem = string.Empty;
        return true;
    }

    private static DriveInfo? FindContainingDrive(string fullPath) => DriveInfo.GetDrives()
        .Where(candidate => candidate.IsReady && fullPath.StartsWith(candidate.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(candidate => candidate.RootDirectory.FullName.Length)
        .FirstOrDefault();

    private static CoreSyncRemoval? CreateRemoval(PocketDrive pocket, CoreComparison core)
    {
        string relativePath = Path.Combine("Cores", core.Identifier);
        EnsureSafeRelativePath(relativePath);
        string directory = Path.Combine(pocket.RootPath, relativePath);
        if (!Directory.Exists(directory)) return null;
        FileInfo[] files = new DirectoryInfo(directory).GetFiles("*", SearchOption.AllDirectories);
        return new CoreSyncRemoval(core.Identifier, core.FriendlyName, relativePath, files.Length, files.Sum(file => file.Length));
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, file);
            string destination = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }
    }

    private static void Restore(string backupPath, string pocketPath, IReadOnlyList<string> replacedFiles, IReadOnlyList<string> addedFiles, IReadOnlyList<CoreSyncRemoval> removedDirectories)
    {
        foreach (string added in addedFiles.Where(File.Exists)) File.Delete(added);
        foreach (string destination in replacedFiles)
        {
            string relativePath = Path.GetRelativePath(pocketPath, destination);
            string backup = Path.Combine(backupPath, relativePath);
            if (File.Exists(backup)) File.Copy(backup, destination, true);
        }
        foreach (CoreSyncRemoval removal in removedDirectories)
        {
            string backup = Path.Combine(backupPath, "removed", removal.RelativePath);
            string destination = Path.Combine(pocketPath, removal.RelativePath);
            if (Directory.Exists(backup)) CopyDirectory(backup, destination);
        }
    }
}
