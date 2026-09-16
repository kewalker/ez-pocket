using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using EzPocket.Models;

namespace EzPocket.Services;

/// <summary>Stages approved core release archives and applies only their Pocket content folders.</summary>
public sealed class CoreSyncService
{
    private static readonly string[] AllowedRootFolders = ["Assets", "Cores", "Platforms"];
    private const int MaximumBackupsPerPocket = 5;
    private const long MaximumBackupBytesPerPocket = 1024L * 1024 * 1024;
    private readonly HttpClient client;
    private readonly string workingRoot;
    private readonly string backupRoot;

    public CoreSyncService(HttpClient? client = null, string? workingRoot = null, string? backupRoot = null)
    {
        this.client = client ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        this.workingRoot = workingRoot ?? Path.Combine(appData, "EzPocket", "staging");
        this.backupRoot = backupRoot ?? Path.Combine(appData, "EzPocket", "backups");
    }

    public Task<CoreSyncPreview> PrepareAsync(PocketDrive pocket, IReadOnlyList<CoreComparison> cores, CancellationToken cancellationToken = default) =>
        PrepareAsync(pocket, cores, [], cancellationToken);

    public async Task<CoreSyncPreview> PrepareAsync(PocketDrive pocket, IReadOnlyList<CoreComparison> cores, IReadOnlyList<CoreComparison> coresToRemove, CancellationToken cancellationToken = default)
    {
        string stagingPath = Path.Combine(workingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingPath);
        var blockers = new List<string>();
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (CoreComparison core in cores)
        {
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
                using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using (FileStream destination = File.Create(archivePath))
                {
                    await source.CopyToAsync(destination, cancellationToken);
                }
                ExtractArchive(archivePath, extractPath);

                foreach (string file in Directory.EnumerateFiles(extractPath, "*", SearchOption.AllDirectories))
                {
                    string relativePath = Path.GetRelativePath(extractPath, file);
                    if (!IsAllowedPackageFile(relativePath)) continue;
                    if (!sources.TryAdd(relativePath, file))
                        blockers.Add($"More than one selected package contains {relativePath}.");
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException)
            {
                blockers.Add($"{core.FriendlyName} could not be staged: {exception.Message}");
            }
        }

        IReadOnlyList<CoreSyncFileChange> changes = sources
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new CoreSyncFileChange(pair.Key, pair.Value, new FileInfo(pair.Value).Length, File.Exists(Path.Combine(pocket.RootPath, pair.Key))))
            .ToArray();
        IReadOnlyList<CoreSyncRemoval> removals = coresToRemove
            .Where(core => core.IsInstalled)
            .Select(core => CreateRemoval(pocket, core))
            .Where(removal => removal is not null)
            .Cast<CoreSyncRemoval>()
            .ToArray();
        return new CoreSyncPreview(pocket.RootPath, stagingPath, cores, changes, removals, blockers);
    }

    public Task<CoreSyncResult> ApplyAsync(CoreSyncPreview preview, CancellationToken cancellationToken = default)
    {
        if (!preview.CanSync)
            return Task.FromResult(new CoreSyncResult(false, 0, 0, null, "Resolve the package issues before syncing."));

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
            string message = $"Synced {preview.Changes.Count} files" + (removedDirectories.Count > 0 ? $" and removed {removedDirectories.Count} core(s)." : ".");
            return Task.FromResult(new CoreSyncResult(true, preview.Changes.Count, removedDirectories.Count, Directory.Exists(backupPath) ? backupPath : null, message, backupsPruned));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Restore(backupPath, preview.PocketPath, replacedFiles, addedFiles, removedDirectories);
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

    private static long GetDirectorySize(DirectoryInfo directory) => directory
        .EnumerateFiles("*", SearchOption.AllDirectories)
        .Sum(file => file.Length);

    private static void ExtractArchive(string archivePath, string extractPath)
    {
        string root = Path.GetFullPath(extractPath) + Path.DirectorySeparatorChar;
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            string target = Path.GetFullPath(Path.Combine(extractPath, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Package contains an unsafe file path.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
    }

    private static bool IsAllowedPackageFile(string relativePath)
    {
        string firstSegment = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return AllowedRootFolders.Contains(firstSegment, StringComparer.OrdinalIgnoreCase);
    }

    private static void EnsureSafeRelativePath(string relativePath)
    {
        if (!IsAllowedPackageFile(relativePath) || Path.IsPathRooted(relativePath) || relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(segment => segment == ".."))
            throw new IOException("Package contains an unsafe destination path.");
    }

    private static string SanitizeDirectoryName(string value) => string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

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
