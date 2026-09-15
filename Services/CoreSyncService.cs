using System.IO.Compression;
using EzPocket.Models;

namespace EzPocket.Services;

/// <summary>Stages approved core release archives and applies only their Pocket content folders.</summary>
public sealed class CoreSyncService
{
    private static readonly string[] AllowedRootFolders = ["Assets", "Cores", "Platforms"];
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

    public async Task<CoreSyncPreview> PrepareAsync(PocketDrive pocket, IReadOnlyList<CoreComparison> cores, CancellationToken cancellationToken = default)
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
        return new CoreSyncPreview(pocket.RootPath, stagingPath, cores, changes, blockers);
    }

    public async Task<CoreSyncResult> ApplyAsync(CoreSyncPreview preview, CancellationToken cancellationToken = default)
    {
        if (!preview.CanSync)
            return new CoreSyncResult(false, 0, null, "Resolve the package issues before syncing.");

        string backupPath = Path.Combine(backupRoot, $"core-sync-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        var addedFiles = new List<string>();
        var replacedFiles = new List<string>();
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

            try { Cleanup(preview); }
            catch (IOException) { }
            return new CoreSyncResult(true, preview.Changes.Count, Directory.Exists(backupPath) ? backupPath : null, $"Synced {preview.Changes.Count} files.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Restore(backupPath, preview.PocketPath, replacedFiles, addedFiles);
            return new CoreSyncResult(false, 0, Directory.Exists(backupPath) ? backupPath : null, $"Sync stopped and restored changed files: {exception.Message}");
        }
    }

    public void Cleanup(CoreSyncPreview preview)
    {
        if (Directory.Exists(preview.StagingPath)) Directory.Delete(preview.StagingPath, true);
    }

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

    private static void Restore(string backupPath, string pocketPath, IReadOnlyList<string> replacedFiles, IReadOnlyList<string> addedFiles)
    {
        foreach (string added in addedFiles.Where(File.Exists)) File.Delete(added);
        foreach (string destination in replacedFiles)
        {
            string relativePath = Path.GetRelativePath(pocketPath, destination);
            string backup = Path.Combine(backupPath, relativePath);
            if (File.Exists(backup)) File.Copy(backup, destination, true);
        }
    }
}
