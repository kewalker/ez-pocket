using EzPocket.Models;
using System.IO.Compression;

namespace EzPocket.Services;

/// <summary>
/// Reads and safely imports user-selected Pocket assets. New asset kinds should add their own
/// validation and destination mapping here rather than being folded into core-package sync.
/// </summary>
public sealed class AssetService
{
    private const string PaletteRelativeDirectory = "Assets/gb/common/palettes";
    private const string PalettePackUrl = "https://github.com/davewongillies/openfpga-palettes/archive/refs/heads/master.zip";
    private const string CodeWarioGbBoxArtUrl = "https://github.com/codewario/PocketLibraryImages/releases/download/v1.2/GB.zip";
    private const string CodeWarioGbaBoxArtUrl = "https://github.com/codewario/PocketLibraryImages/releases/download/v1.2/GBA.zip";
    private const string CodeWarioGgBoxArtUrl = "https://github.com/codewario/PocketLibraryImages/releases/download/v1.2/GG.zip";
    private const string NeoGeoPocketBoxArtUrl = "https://github.com/g026r/pocket-neogeo-library-images/releases/latest/download/BoxArt_EU.zip";
    private const string LynxBoxArtUrl = "https://github.com/g026r/pocket-lynx-library-images/releases/latest/download/BoxArt.zip";
    private const string PcEngineBoxArtUrl = "https://github.com/g026r/pocket-pce-library-images/releases/latest/download/BoxArt.zip";
    private const string PlatformArtUrl = "https://github.com/Shissa43/Analogue-Pocket-Platform-Art/archive/refs/heads/main.zip";
    private readonly string backupRoot;
    private readonly string stagingRoot;
    private readonly HttpClient client;
    private readonly IAppDiagnostics diagnostics;

    public AssetService(HttpClient? client = null, string? backupRoot = null, string? stagingRoot = null, IAppDiagnostics? diagnostics = null)
    {
        this.client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        this.backupRoot = backupRoot ?? Path.Combine(FileSystem.AppDataDirectory, "EzPocket", "backups", "assets");
        this.stagingRoot = stagingRoot ?? Path.Combine(FileSystem.CacheDirectory, "EzPocket", "palette-staging");
        this.diagnostics = diagnostics ?? NullAppDiagnostics.Instance;
    }

    public IReadOnlyList<ManagedAsset> Scan(PocketDrive pocket)
    {
        string directory = Path.Combine(pocket.RootPath, PaletteRelativeDirectory);
        if (!Directory.Exists(directory)) return [];

        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Where(IsPaletteFile)
                .Select(path => new FileInfo(path))
                .Select(file => new ManagedAsset(
                    "Game Boy palette",
                    Path.GetFileNameWithoutExtension(file.Name),
                    Path.GetRelativePath(pocket.RootPath, file.FullName),
                    FormatForExtension(file.Extension),
                    file.Length,
                    CompatibilityForExtension(file.Extension)))
                .OrderBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (IOException exception)
        {
            diagnostics.Error("AssetScanFailed", exception);
            return [];
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Error("AssetScanFailed", exception);
            return [];
        }
    }

    public IReadOnlyList<AssetSetInventory> ScanAssetSets(PocketDrive pocket)
    {
        var sets = new (string Key, string Name, string Kind, string Directory)[]
        {
            ("palettes", "Game Boy palettes", "Palette collection", PaletteRelativeDirectory),
            ("gb", "Game Boy + Game Boy Color", "Library box art", "System/Library/Images/GB"),
            ("gba", "Game Boy Advance", "Library box art", "System/Library/Images/GBA"),
            ("gg", "Game Gear", "Library box art", "System/Library/Images/GG"),
            ("ngp", "Neo Geo Pocket / Color", "Library box art", "System/Library/Images/ngp"),
            ("lynx", "Atari Lynx", "Library box art", "System/Library/Images/lynx"),
            ("pce", "PC Engine", "Library box art", "System/Library/Images/pce"),
            ("platform", "openFPGA launcher", "Platform art", "Platforms/_images")
        };

        return sets.Select(set => ScanAssetSet(pocket, set.Key, set.Name, set.Kind, set.Directory)).ToArray();
    }

    public AssetRemovalPreview PrepareAssetSetRemoval(PocketDrive pocket, AssetSetInventory set)
    {
        if (!IsSafeRelativePath(set.RelativeDirectory)) return new AssetRemovalPreview(pocket, set, []);
        string directory = Path.Combine(pocket.RootPath, set.RelativeDirectory);
        if (!Directory.Exists(directory)) return new AssetRemovalPreview(pocket, set, []);
        try
        {
            IReadOnlyList<ManagedAsset> files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Where(path => set.Key == "palettes" ? IsPaletteFile(path) : string.Equals(Path.GetExtension(path), ".bin", StringComparison.OrdinalIgnoreCase))
                .Select(path => new FileInfo(path))
                .Select(file => new ManagedAsset(set.Kind, Path.GetFileNameWithoutExtension(file.Name), Path.GetRelativePath(pocket.RootPath, file.FullName), set.Key == "palettes" ? FormatForExtension(file.Extension) : "Library image (.bin)", file.Length, set.Name))
                .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return new AssetRemovalPreview(pocket, set, files);
        }
        catch (IOException exception) { diagnostics.Error("AssetRemovalPrepareFailed", exception); return new AssetRemovalPreview(pocket, set, []); }
        catch (UnauthorizedAccessException exception) { diagnostics.Error("AssetRemovalPrepareFailed", exception); return new AssetRemovalPreview(pocket, set, []); }
    }

    public AssetImportResult ApplyRemoval(AssetRemovalPreview preview)
    {
        if (!preview.CanApply) return new AssetImportResult(false, 0, 0, null, "There are no files to remove.");
        if (!Directory.Exists(preview.Pocket.RootPath)) return new AssetImportResult(false, 0, 0, null, "The selected target is no longer available.");
        string backupPath = Path.Combine(backupRoot, AppDiagnosticsService.TargetId(preview.Pocket.RootPath), $"asset-removal-{DateTimeOffset.UtcNow.Ticks:D19}-{Guid.NewGuid():N}");
        var removed = new List<string>();
        try
        {
            foreach (ManagedAsset file in preview.Files)
            {
                string source = Path.Combine(preview.Pocket.RootPath, file.RelativePath);
                if (!File.Exists(source)) continue;
                string backup = Path.Combine(backupPath, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(source, backup, true);
                File.Delete(source);
                removed.Add(file.RelativePath);
            }
            return new AssetImportResult(true, 0, removed.Count, backupPath, $"Removed {removed.Count} {preview.Set.Name} file{(removed.Count == 1 ? string.Empty : "s")}. Backup retained.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Error("AssetRemovalApplyFailed", exception);
            foreach (string relativePath in removed)
            {
                string backup = Path.Combine(backupPath, relativePath);
                string destination = Path.Combine(preview.Pocket.RootPath, relativePath);
                if (!File.Exists(backup)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(backup, destination, true);
            }
            return new AssetImportResult(false, 0, 0, Directory.Exists(backupPath) ? backupPath : null, "Asset removal stopped and restored changed files.");
        }
    }

    private AssetSetInventory ScanAssetSet(PocketDrive pocket, string key, string name, string kind, string relativeDirectory)
    {
        string directory = Path.Combine(pocket.RootPath, relativeDirectory);
        if (!Directory.Exists(directory)) return new AssetSetInventory(key, name, kind, relativeDirectory, 0, 0);

        try
        {
            FileInfo[] files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Where(path => key == "palettes" ? IsPaletteFile(path) : string.Equals(Path.GetExtension(path), ".bin", StringComparison.OrdinalIgnoreCase))
                .Select(path => new FileInfo(path))
                .ToArray();
            return new AssetSetInventory(key, name, kind, relativeDirectory, files.Length, files.Sum(file => file.Length));
        }
        catch (IOException exception)
        {
            diagnostics.Error("AssetSetScanFailed", exception);
            return new AssetSetInventory(key, name, kind, relativeDirectory, 0, 0);
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Error("AssetSetScanFailed", exception);
            return new AssetSetInventory(key, name, kind, relativeDirectory, 0, 0);
        }
    }

    public AssetImportPreview PreparePaletteImport(PocketDrive pocket, IEnumerable<string> sourcePaths)
    {
        return PreparePaletteSources(pocket, sourcePaths.Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => new PaletteSource(path, Path.Combine(PaletteRelativeDirectory, Path.GetFileName(path)))));
    }

    /// <summary>Downloads the maintained palette collection into temporary staging, then prepares the normal explicit-write review.</summary>
    public async Task<AssetImportPreview> PreparePalettePackAsync(PocketDrive pocket, IProgress<AssetPreparationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        string stagingPath = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingPath);
        try
        {
            progress?.Report(new AssetPreparationProgress("CONNECTING TO PALETTE PACK"));
            using HttpResponseMessage response = await client.GetAsync(PalettePackUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            long? totalBytes = response.Content.Headers.ContentLength;
            await using Stream download = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var archiveBuffer = new MemoryStream(totalBytes is > 0 and <= int.MaxValue ? (int)totalBytes.Value : 0);
            byte[] buffer = new byte[81_920];
            long downloadedBytes = 0;
            int bytesRead;
            while ((bytesRead = await download.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await archiveBuffer.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                downloadedBytes += bytesRead;
                progress?.Report(new AssetPreparationProgress("DOWNLOADING PALETTE PACK", downloadedBytes, totalBytes));
            }

            progress?.Report(new AssetPreparationProgress("PREPARING FILES FOR REVIEW"));
            byte[] archiveBytes = archiveBuffer.ToArray();
            using var archive = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
            var sources = new List<PaletteSource>();
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                string normalized = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                int marker = normalized.IndexOf($"Assets{Path.DirectorySeparatorChar}gb{Path.DirectorySeparatorChar}common{Path.DirectorySeparatorChar}palettes{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
                if (marker < 0 || !IsPaletteFile(normalized)) continue;
                string paletteRelativePath = normalized[(marker + PaletteRelativeDirectory.Length + 1)..];
                if (!IsSafeRelativePath(paletteRelativePath)) continue;
                string sourcePath = Path.Combine(stagingPath, paletteRelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
                entry.ExtractToFile(sourcePath, true);
                sources.Add(new PaletteSource(sourcePath, Path.Combine(PaletteRelativeDirectory, paletteRelativePath)));
            }

            progress?.Report(new AssetPreparationProgress("CHECKING FILES FOR REVIEW"));
            AssetImportPreview preview = PreparePaletteSources(pocket, sources, checkDuplicateNames: false);
            return preview with { StagingPath = stagingPath, Kind = "Palette pack catalog" };
        }
        catch
        {
            TryDeleteDirectory(stagingPath);
            throw;
        }
    }

    public AssetImportPreview PreparePalettePackSelection(PalettePackCatalog catalog, IEnumerable<AssetFileChange> selected)
    {
        AssetImportPreview preview = PreparePaletteSources(catalog.Pocket, selected.Select(change => new PaletteSource(change.SourcePath, change.RelativePath)));
        return preview with { StagingPath = catalog.StagingPath, Kind = "Selected palette pack" };
    }

    public void CleanupPalettePack(PalettePackCatalog catalog) => TryDeleteDirectory(catalog.StagingPath);

    public async Task<AssetImportPreview> PrepareLibraryBoxArtAsync(PocketDrive pocket, string system, CancellationToken cancellationToken = default)
    {
        (string url, string extractPrefix, string targetSystem) = system switch
        {
            "GB" => (CodeWarioGbBoxArtUrl, "GB/BoxArts/", "GB"),
            "GBA" => (CodeWarioGbaBoxArtUrl, "GBA/BoxArts/", "GBA"),
            "GG" => (CodeWarioGgBoxArtUrl, "GG/BoxArts/", "GG"),
            "ngp" => (NeoGeoPocketBoxArtUrl, "ngp/", "ngp"),
            "lynx" => (LynxBoxArtUrl, "lynx/", "lynx"),
            "pce" => (PcEngineBoxArtUrl, "pce/", "pce"),
            _ => throw new ArgumentException("Unsupported library image system.", nameof(system))
        };
        return await PrepareArchiveBinAssetsAsync(pocket, url, extractPrefix, Path.Combine("System", "Library", "Images", targetSystem), $"{FriendlySystemName(targetSystem)} box art", cancellationToken);
    }

    public async Task<AssetImportPreview> PreparePlatformArtAsync(PocketDrive pocket, string style, CancellationToken cancellationToken = default)
    {
        string prefix = style switch
        {
            "home-us" => "Home_USA/Platforms/_images/",
            "home-jp" => "Home_JP/Platforms/_images/",
            "arcade" => "Arcade_Multi/Platforms/_images/",
            _ => throw new ArgumentException("Unsupported platform art style.", nameof(style))
        };
        return await PrepareArchiveBinAssetsAsync(pocket, PlatformArtUrl, prefix, Path.Combine("Platforms", "_images"), $"Platform art · {style}", cancellationToken);
    }

    private async Task<AssetImportPreview> PrepareArchiveBinAssetsAsync(PocketDrive pocket, string url, string extractPrefix, string destinationRoot, string kind, CancellationToken cancellationToken)
    {
        string stagingPath = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingPath);
        try
        {
            string archivePath = Path.Combine(stagingPath, "download.zip");
            await using (Stream download = await client.GetStreamAsync(url, cancellationToken))
            await using (var archiveFile = File.Create(archivePath))
                await download.CopyToAsync(archiveFile, cancellationToken);

            using var archive = ZipFile.OpenRead(archivePath);
            string prefix = extractPrefix.Replace('/', Path.DirectorySeparatorChar);
            var sources = new List<PaletteSource>();
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name) || !string.Equals(Path.GetExtension(entry.Name), ".bin", StringComparison.OrdinalIgnoreCase)) continue;
                string normalized = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                int marker = normalized.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                if (marker < 0) continue;
                string relative = normalized[(marker + prefix.Length)..];
                if (!IsSafeRelativePath(relative)) continue;
                string sourcePath = Path.Combine(stagingPath, "files", relative);
                Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
                entry.ExtractToFile(sourcePath, true);
                sources.Add(new PaletteSource(sourcePath, Path.Combine(destinationRoot, relative)));
            }
            return PrepareLibrarySources(pocket, sources) with { StagingPath = stagingPath, Kind = kind };
        }
        catch
        {
            TryDeleteDirectory(stagingPath);
            throw;
        }
    }

    private static string FriendlySystemName(string system) => system switch
    {
        "GB" => "Game Boy + Game Boy Color",
        "GBA" => "Game Boy Advance",
        "GG" => "Game Gear",
        "ngp" => "Neo Geo Pocket / Color",
        "lynx" => "Atari Lynx",
        "pce" => "PC Engine",
        _ => system
    };

    private AssetImportPreview PreparePaletteSources(PocketDrive pocket, IEnumerable<PaletteSource> sourceFiles, bool checkDuplicateNames = true)
    {
        var changes = new List<AssetFileChange>();
        var blockers = new List<string>();
        var targetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (PaletteSource source in sourceFiles)
        {
            try
            {
                string sourcePath = Path.GetFullPath(source.SourcePath);
                string fileName = Path.GetFileName(sourcePath);
                if (!File.Exists(sourcePath)) { blockers.Add($"{fileName} is no longer available."); continue; }
                if (!IsPaletteFile(sourcePath)) { blockers.Add($"{fileName} is not a supported palette file."); continue; }
                if (checkDuplicateNames && !targetNames.Add(fileName)) { blockers.Add($"{fileName} was selected more than once."); continue; }
                if (string.Equals(Path.GetExtension(sourcePath), ".pal", StringComparison.OrdinalIgnoreCase) && !IsValidApgb(sourcePath))
                {
                    blockers.Add($"{fileName} is not a valid 56-byte APGB palette.");
                    continue;
                }

                string relativePath = source.TargetRelativePath;
                string destinationPath = Path.Combine(pocket.RootPath, relativePath);
                bool identical = File.Exists(destinationPath) && FilesMatch(sourcePath, destinationPath);
                if (identical) continue;

                var file = new FileInfo(sourcePath);
                changes.Add(new AssetFileChange(sourcePath, relativePath, Path.GetFileNameWithoutExtension(fileName),
                    FormatForExtension(file.Extension), file.Length, File.Exists(destinationPath)));
            }
            catch (IOException exception)
            {
                diagnostics.Error("AssetImportPrepareFailed", exception);
                blockers.Add($"A selected file could not be read.");
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Error("AssetImportPrepareFailed", exception);
                blockers.Add($"A selected file could not be read.");
            }
            catch (ArgumentException)
            {
                blockers.Add("A selected file path is invalid.");
            }
        }

        diagnostics.Info("AssetImportPrepared", new Dictionary<string, string?>
        {
            ["AssetKind"] = "GameBoyPalette",
            ["ChangeCount"] = changes.Count.ToString(),
            ["BlockerCount"] = blockers.Count.ToString()
        });
        return new AssetImportPreview(pocket, changes, blockers);
    }

    private AssetImportPreview PrepareLibrarySources(PocketDrive pocket, IEnumerable<PaletteSource> sourceFiles)
    {
        var changes = new List<AssetFileChange>();
        var blockers = new List<string>();
        foreach (PaletteSource source in sourceFiles)
        {
            string destination = Path.Combine(pocket.RootPath, source.TargetRelativePath);
            try
            {
                if (File.Exists(destination) && FilesMatch(source.SourcePath, destination)) continue;
                var file = new FileInfo(source.SourcePath);
                changes.Add(new AssetFileChange(source.SourcePath, source.TargetRelativePath, Path.GetFileNameWithoutExtension(file.Name), "Library image (.bin)", file.Length, File.Exists(destination)));
            }
            catch (IOException exception) { diagnostics.Error("LibraryImagePrepareFailed", exception); blockers.Add("A library image could not be read."); }
            catch (UnauthorizedAccessException exception) { diagnostics.Error("LibraryImagePrepareFailed", exception); blockers.Add("A library image could not be read."); }
        }
        if (changes.Count == 0 && blockers.Count == 0) blockers.Add("The downloaded pack did not contain usable library images.");
        return new AssetImportPreview(pocket, changes, blockers);
    }

    public AssetImportResult Apply(AssetImportPreview preview, IProgress<AssetImportProgress>? progress = null)
    {
        if (!preview.CanApply) return new AssetImportResult(false, 0, 0, null, "Resolve the asset review before applying it.");
        if (!Directory.Exists(preview.Pocket.RootPath)) return new AssetImportResult(false, 0, 0, null, "The selected target is no longer available.");

        string backupPath = Path.Combine(backupRoot, AppDiagnosticsService.TargetId(preview.Pocket.RootPath), $"asset-import-{DateTimeOffset.UtcNow.Ticks:D19}-{Guid.NewGuid():N}");
        var added = new List<string>();
        var replaced = new List<string>();
        try
        {
            for (int index = 0; index < preview.Changes.Count; index++)
            {
                AssetFileChange change = preview.Changes[index];
                progress?.Report(new AssetImportProgress(index, preview.Changes.Count, change.Name, change.ReplacesExisting));
                string destination = Path.Combine(preview.Pocket.RootPath, change.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (File.Exists(destination))
                {
                    string backup = Path.Combine(backupPath, change.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(destination, backup, true);
                    replaced.Add(change.RelativePath);
                }
                else added.Add(change.RelativePath);

                File.Copy(change.SourcePath, destination, true);
                progress?.Report(new AssetImportProgress(index + 1, preview.Changes.Count, change.Name, change.ReplacesExisting));
            }

            string message = $"Added {added.Count} palette file{(added.Count == 1 ? string.Empty : "s")}" +
                (replaced.Count > 0 ? $" and replaced {replaced.Count}." : ".");
            diagnostics.Info("AssetImportApplied", new Dictionary<string, string?> { ["ChangeCount"] = preview.Changes.Count.ToString() });
            return new AssetImportResult(true, added.Count, replaced.Count, Directory.Exists(backupPath) ? backupPath : null, message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Error("AssetImportApplyFailed", exception);
            Restore(preview.Pocket.RootPath, backupPath, added, replaced);
            return new AssetImportResult(false, 0, 0, Directory.Exists(backupPath) ? backupPath : null, "Asset import stopped and restored changed files.");
        }
        finally
        {
            Cleanup(preview);
        }
    }

    public void Cleanup(AssetImportPreview preview)
    {
        if (!string.IsNullOrWhiteSpace(preview.StagingPath)) TryDeleteDirectory(preview.StagingPath);
    }

    private static bool IsPaletteFile(string path) =>
        string.Equals(Path.GetExtension(path), ".pal", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetExtension(path), ".gbp", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidApgb(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return bytes.Length == 56 && bytes[^5] == 0x81 && bytes[^4] == (byte)'A' && bytes[^3] == (byte)'P' && bytes[^2] == (byte)'G' && bytes[^1] == (byte)'B';
    }

    private static bool FilesMatch(string first, string second)
    {
        var firstInfo = new FileInfo(first);
        var secondInfo = new FileInfo(second);
        return firstInfo.Length == secondInfo.Length && File.ReadAllBytes(first).AsSpan().SequenceEqual(File.ReadAllBytes(second));
    }

    private static string FormatForExtension(string extension) => string.Equals(extension, ".pal", StringComparison.OrdinalIgnoreCase) ? "APGB (.pal)" : "GBP (.gbp)";
    private static string CompatibilityForExtension(string extension) => string.Equals(extension, ".pal", StringComparison.OrdinalIgnoreCase)
        ? "Pocket GB palette"
        : "GB core palette";

    private static bool IsSafeRelativePath(string path) =>
        !Path.IsPathRooted(path) && !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(segment => segment is "." or ".." || string.IsNullOrWhiteSpace(segment));

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void Restore(string pocketPath, string backupPath, IReadOnlyList<string> added, IReadOnlyList<string> replaced)
    {
        foreach (string relativePath in added)
        {
            string destination = Path.Combine(pocketPath, relativePath);
            if (File.Exists(destination)) File.Delete(destination);
        }
        foreach (string relativePath in replaced)
        {
            string backup = Path.Combine(backupPath, relativePath);
            if (File.Exists(backup))
            {
                string destination = Path.Combine(pocketPath, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(backup, destination, true);
            }
        }
    }

    private sealed record PaletteSource(string SourcePath, string TargetRelativePath);
}
