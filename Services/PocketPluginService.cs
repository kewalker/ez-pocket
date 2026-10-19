using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using EzPocket.Models;

namespace EzPocket.Services;

/// <summary>Installs plugins locally, stages runs, and applies only an unchanged, reviewed file diff.</summary>
public sealed class PocketPluginService
{
    private const long MaximumWorkingBytes = 64L * 1024 * 1024 * 1024;
    private const int MaximumFiles = 200_000;
    private static readonly HashSet<string> SystemMetadataNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System Volume Information", "$RECYCLE.BIN", ".Spotlight-V100", ".Trashes", ".fseventsd", ".TemporaryItems"
    };
    private readonly string libraryRoot, stagingRoot, backupRoot;
    private readonly HttpClient client;
    private readonly IPocketPluginRuntime runtime;
    private readonly Dictionary<Guid, PocketPluginPreview> previews = [];
    private readonly SemaphoreSlim operation = new(1, 1);

    public PocketPluginService(HttpClient? client = null, IPocketPluginRuntime? runtime = null,
        string? libraryRoot = null, string? stagingRoot = null, string? backupRoot = null)
    {
        this.client = client ?? new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        this.runtime = runtime ?? new PocketPluginRuntime();
        this.libraryRoot = Path.GetFullPath(libraryRoot ?? Path.Combine(FileSystem.AppDataDirectory, "EzPocket", "plugins"));
        this.stagingRoot = Path.GetFullPath(stagingRoot ?? Path.Combine(FileSystem.CacheDirectory, "EzPocket", "plugin-runs"));
        this.backupRoot = Path.GetFullPath(backupRoot ?? Path.Combine(FileSystem.AppDataDirectory, "EzPocket", "backups", "plugins"));
    }

    public IReadOnlyList<PocketPlugin> ListInstalled()
    {
        if (!Directory.Exists(libraryRoot)) return [];
        EnsureNoLinks(libraryRoot);
        var result = new List<PocketPlugin>();
        foreach (string directory in Directory.EnumerateDirectories(libraryRoot))
        {
            EnsureNoLinks(directory);
            string info = SafePath(directory, "install.json");
            if (!File.Exists(info)) continue;
            PocketPlugin plugin = JsonSerializer.Deserialize<PocketPlugin>(File.ReadAllText(info))
                ?? throw new InvalidDataException("An installed plugin has invalid metadata.");
            if (Path.GetFileName(directory) != plugin.Id || !IsId(plugin.Id)) throw new InvalidDataException("Invalid plugin installation.");
            ValidateManifest(new(plugin.Name, plugin.Description, plugin.AllowedHosts.ToArray()));
            if (File.Exists(SafePath(directory, "plugin.wasm"))) result.Add(plugin);
        }
        return result.OrderBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static string ParseRepository(string input)
    {
        input = input.Trim();
        if (Uri.TryCreate(input, UriKind.Absolute, out Uri? uri))
        {
            if (uri.Scheme != "https" || uri.Host != "github.com" || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort || uri.Query.Length > 0 || uri.Fragment.Length > 0)
                throw new InvalidDataException("Enter a GitHub repository as owner/repository or its HTTPS repository URL.");
            input = uri.AbsolutePath.Trim('/');
        }
        if (!Regex.IsMatch(input, @"\A[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9_.-]{1,100}\z") || input.Split('/')[1] is "." or "..")
            throw new InvalidDataException("Enter a GitHub repository as owner/repository.");
        return input;
    }

    public async Task<PocketPlugin> InstallFromGitHubAsync(string repository, CancellationToken cancellationToken = default)
    {
        repository = ParseRepository(repository);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        cancellationToken = deadline.Token;
        await operation.WaitAsync(cancellationToken);
        string? temporary = null;
        try
        {
            using var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/latest");
            request.Headers.UserAgent.ParseAdd("ez-pocket/0.1");
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            using JsonDocument release = JsonDocument.Parse(await ReadLimitedAsync(response.Content, 2 * 1024 * 1024, cancellationToken));
            string version = release.RootElement.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("Missing release version.");
            JsonElement[] assets = release.RootElement.GetProperty("assets").EnumerateArray().ToArray();
            string AssetUrl(string name)
            {
                JsonElement[] matches = assets.Where(asset => asset.GetProperty("name").GetString() == name).ToArray();
                if (matches.Length != 1) throw new InvalidDataException($"The release must include {name} as a release asset.");
                string url = matches[0].GetProperty("browser_download_url").GetString()!;
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != "https" || uri.Host != "github.com" ||
                    !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) ||
                    !uri.AbsolutePath.StartsWith($"/{repository}/releases/download/", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Unsupported plugin release URL.");
                return url;
            }
            byte[] json = await DownloadAsync(AssetUrl("plugin.json"), 64 * 1024, cancellationToken);
            PocketPluginManifest manifest = JsonSerializer.Deserialize<PocketPluginManifest>(json) ?? throw new InvalidDataException("Invalid plugin manifest.");
            ValidateManifest(manifest);
            byte[] wasm = await DownloadAsync(AssetUrl("plugin.wasm"), 64 * 1024 * 1024, cancellationToken);
            if (wasm.Length < 8 || !wasm.AsSpan(0, 8).SequenceEqual(new byte[] { 0, 97, 115, 109, 1, 0, 0, 0 }))
                throw new InvalidDataException("The release does not contain a supported WebAssembly module.");
            string id = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(repository.ToLowerInvariant())))[..24];
            var plugin = new PocketPlugin(id, manifest.Name, manifest.Description, version, repository,
                Array.AsReadOnly(manifest.AllowedHosts ?? []));
            EnsureNoLinks(libraryRoot);
            Directory.CreateDirectory(libraryRoot);
            string destination = SafePath(libraryRoot, id);
            // Install is intentionally separate from updates in this first version.
            if (Directory.Exists(destination)) throw new InvalidDataException("This repository is already installed. Remove it before installing another version.");
            temporary = SafePath(libraryRoot, ".install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            await File.WriteAllBytesAsync(Path.Combine(temporary, "plugin.wasm"), wasm, cancellationToken);
            await File.WriteAllBytesAsync(Path.Combine(temporary, "plugin.json"), json, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(temporary, "install.json"), JsonSerializer.Serialize(plugin), cancellationToken);
            Directory.Move(temporary, destination);
            temporary = null;
            return plugin;
        }
        finally
        {
            try { if (temporary is not null) DeleteOwnedFolder(libraryRoot, temporary); }
            finally { operation.Release(); }
        }
    }

    public void Uninstall(PocketPlugin plugin)
    {
        if (!operation.Wait(0)) throw new InvalidOperationException("Wait for the current plugin operation to finish.");
        try
        {
            ValidateInstalled(plugin);
            DeleteOwnedFolder(libraryRoot, SafePath(libraryRoot, plugin.Id));
        }
        finally { operation.Release(); }
    }

    public async Task<PocketPluginPreview> PrepareAsync(PocketDrive pocket, PocketPlugin plugin,
        Func<PocketPluginPrompt, CancellationToken, Task<string?>> prompt, Action<string> log,
        Action<Uri> offerUrl, CancellationToken cancellationToken = default)
    {
        await operation.WaitAsync(cancellationToken);
        Guid id = Guid.NewGuid();
        string working = Path.Combine(stagingRoot, id.ToString("N"));
        bool retained = false;
        try
        {
            string installed = ValidateInstalled(plugin);
            string target = Path.GetFullPath(pocket.RootPath);
            if (!pocket.LooksLikePocket || !Directory.Exists(target)) throw new IOException("Scan a connected Pocket target before running a plugin.");
            EnsureSeparate(target, libraryRoot);
            EnsureSeparate(target, stagingRoot);
            EnsureSeparate(target, backupRoot);
            DateTime targetCreatedUtc = Directory.GetCreationTimeUtc(target);
            log("Reading target files and checking local space…\n");
            Dictionary<string, FileSnapshot> before = await Task.Run(() => Snapshot(target, cancellationToken), cancellationToken);
            string[] targetDirectories = await Task.Run(() => DirectoryNames(target), cancellationToken);
            EnsureNoLinks(working);
            Directory.CreateDirectory(working);
            long bytes = before.Values.Sum(file => file.Size);
            EnsureSpace(working, bytes + 256L * 1024 * 1024);
            string stagedPocket = Path.Combine(working, "pocket");
            Directory.CreateDirectory(stagedPocket);
            log($"Copying {before.Count:N0} files to the local working folder…\n");
            await Task.Run(() =>
            {
                foreach ((string relative, FileSnapshot file) in before)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string source = SafePath(target, relative);
                    string destination = SafePath(stagedPocket, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    CopyVerified(source, destination, file.Hash, cancellationToken);
                }
                // Preserve empty folders, too: some plugins use them to discover installed cores.
                foreach (string directory in targetDirectories)
                    Directory.CreateDirectory(SafePath(stagedPocket, directory));
            }, cancellationToken);
            string host = SafePath(installed, "host");
            Directory.CreateDirectory(host);
            // Plugins retain their private cache, but it is never another mount into the real card.
            foreach (string _ in EnumerateTree(host)) { }
            log("Running plugin against the local copy…\n");
            await runtime.RunAsync(SafePath(installed, "plugin.wasm"), plugin, stagedPocket, host, prompt, log, offerUrl, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            log("Comparing generated files…\n");
            Dictionary<string, FileSnapshot> after = await Task.Run(() => Snapshot(stagedPocket, cancellationToken), cancellationToken);
            var changes = before.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)
                .Select(relative =>
                {
                    before.TryGetValue(relative, out FileSnapshot? oldFile);
                    after.TryGetValue(relative, out FileSnapshot? newFile);
                    return oldFile?.Hash == newFile?.Hash ? null : new PocketPluginChange(relative,
                        oldFile is null ? "ADD" : newFile is null ? "DELETE" : "REPLACE", newFile?.Size ?? 0, oldFile?.Hash, newFile?.Hash);
                }).OfType<PocketPluginChange>().ToArray();
            var blockers = new List<string>();
            foreach (PocketPluginChange change in changes)
            {
                string top = change.RelativePath.Split('/')[0];
                if (top.Equals("Saves", StringComparison.OrdinalIgnoreCase) || top.Equals("Memories", StringComparison.OrdinalIgnoreCase))
                    blockers.Add($"{change.RelativePath}: use Save Vault to change saves or Memories.");
                if (!change.RelativePath.Contains('/') && change.RelativePath.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                    blockers.Add($"{change.RelativePath}: root firmware files must use the separate firmware workflow.");
                // File-to-directory changes need a more complex ordering and are excluded for now.
                if (change.AfterHash is not null && Directory.Exists(SafePath(target, change.RelativePath)))
                    blockers.Add($"{change.RelativePath}: replacing a folder with a file is not supported.");
                string? parent = Path.GetDirectoryName(change.RelativePath);
                while (!string.IsNullOrEmpty(parent))
                {
                    if (File.Exists(SafePath(target, parent))) blockers.Add($"{change.RelativePath}: replacing a file with a folder is not supported.");
                    parent = Path.GetDirectoryName(parent);
                }
            }
            var preview = new PocketPluginPreview(id, plugin, target, working,
                targetCreatedUtc, Array.AsReadOnly(targetDirectories),
                before.ToDictionary(pair => pair.Key, pair => pair.Value.Hash, StringComparer.OrdinalIgnoreCase), changes, blockers);
            lock (previews) previews.Add(id, preview);
            retained = true;
            return preview;
        }
        finally
        {
            try { if (!retained) DeleteOwnedFolder(stagingRoot, working); }
            finally { operation.Release(); }
        }
    }

    public async Task<PocketPluginResult> ApplyAsync(PocketPluginPreview preview, CancellationToken cancellationToken = default)
    {
        await operation.WaitAsync(cancellationToken);
        try { return await Task.Run(() => Apply(preview, cancellationToken), cancellationToken); }
        finally { operation.Release(); }
    }

    private PocketPluginResult Apply(PocketPluginPreview preview, CancellationToken cancellationToken)
    {
        lock (previews)
            if (!previews.TryGetValue(preview.Id, out var registered) || !ReferenceEquals(preview, registered))
                return new(false, "This review has expired. Run the plugin again.");
        if (!preview.CanApply) return new(false, "Resolve the review issues before applying plugin changes.");
        string? backup = null;
        var written = new List<PocketPluginChange>();
        var createdDirectories = new List<string>();
        try
        {
            ValidateTarget();
            string staged = SafePath(preview.WorkingPath, "pocket");
            foreach (PocketPluginChange change in preview.Changes)
            {
                if (change.AfterHash is not null && Hash(SafePath(staged, change.RelativePath), cancellationToken) != change.AfterHash)
                    throw new IOException("A staged file changed after review. Run the plugin again.");
                SafePath(preview.TargetPath, change.RelativePath);
            }
            backup = SafePath(backupRoot, $"{DateTimeOffset.UtcNow.Ticks:D19}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(backup);
            EnsureSpace(backup, preview.Changes.Where(change => change.BeforeHash is not null)
                .Sum(change => new FileInfo(SafePath(preview.TargetPath, change.RelativePath)).Length) + 16L * 1024 * 1024);
            EnsureSpace(preview.TargetPath, preview.Changes.Sum(change => change.SizeBytes) + 16L * 1024 * 1024);
            File.WriteAllText(Path.Combine(backup, "review.json"), JsonSerializer.Serialize(new { preview.Plugin, preview.Changes }));
            foreach (PocketPluginChange change in preview.Changes.Where(change => change.BeforeHash is not null))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destination = SafePath(Path.Combine(backup, "originals"), change.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                CopyVerified(SafePath(preview.TargetPath, change.RelativePath), destination, change.BeforeHash!, cancellationToken);
            }
            ValidateTarget();
            foreach (PocketPluginChange change in preview.Changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destination = SafePath(preview.TargetPath, change.RelativePath);
                if ((File.Exists(destination) ? Hash(destination, cancellationToken) : null) != change.BeforeHash)
                    throw new IOException("A target file changed during apply.");
                if (change.AfterHash is null)
                {
                    File.Delete(destination);
                    written.Add(change);
                    continue;
                }
                string parent = Path.GetDirectoryName(destination)!;
                var missing = new Stack<string>();
                for (string? directory = parent; directory is not null && !Directory.Exists(directory); directory = Path.GetDirectoryName(directory)) missing.Push(directory);
                while (missing.TryPop(out string? directory)) { Directory.CreateDirectory(directory); createdDirectories.Add(directory); }
                AtomicCopy(SafePath(staged, change.RelativePath), destination, change.AfterHash, cancellationToken);
                written.Add(change);
            }
            Discard(preview);
            return new(true, $"Applied {written.Count:N0} plugin file changes. Original files are backed up locally.", backup);

            void ValidateTarget()
            {
                Dictionary<string, FileSnapshot> current = Snapshot(preview.TargetPath, cancellationToken);
                if (Directory.GetCreationTimeUtc(preview.TargetPath) != preview.TargetCreatedUtc ||
                    !DirectoryNames(preview.TargetPath).SequenceEqual(preview.TargetDirectories, StringComparer.OrdinalIgnoreCase) ||
                    current.Count != preview.Baseline.Count || current.Any(pair => !preview.Baseline.TryGetValue(pair.Key, out string? hash) || hash != pair.Value.Hash))
                    throw new IOException("The target changed since this run. Scan and run the plugin again.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            var failures = new List<string>();
            foreach (PocketPluginChange change in written.AsEnumerable().Reverse())
            {
                try
                {
                    string destination = SafePath(preview.TargetPath, change.RelativePath);
                    if (change.BeforeHash is null) File.Delete(destination);
                    else AtomicCopy(SafePath(Path.Combine(backup!, "originals"), change.RelativePath), destination, change.BeforeHash, CancellationToken.None);
                }
                catch (Exception restoreError) when (restoreError is IOException or UnauthorizedAccessException) { failures.Add(change.RelativePath); }
            }
            foreach (string directory in createdDirectories.AsEnumerable().Reverse())
            {
                try { if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); }
                catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException) { }
            }
            string recovery = failures.Count > 0 ? $" Recovery is incomplete for {failures.Count} files. Keep the backup at {backup}." : written.Count > 0 ? " Changed files were restored." : " No target files were changed.";
            return new(false, exception.Message + recovery, backup);
        }
    }

    public void Discard(PocketPluginPreview preview)
    {
        lock (previews)
        {
            if (!previews.TryGetValue(preview.Id, out var registered) || !ReferenceEquals(preview, registered)) return;
            previews.Remove(preview.Id);
        }
        // Cleanup failure must never roll back an otherwise successful apply.
        try { DeleteOwnedFolder(stagingRoot, preview.WorkingPath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private string ValidateInstalled(PocketPlugin plugin)
    {
        if (!IsId(plugin.Id)) throw new InvalidDataException("Invalid plugin identifier.");
        string path = SafePath(libraryRoot, plugin.Id);
        var installed = JsonSerializer.Deserialize<PocketPlugin>(File.ReadAllText(SafePath(path, "install.json")));
        if (installed is null || JsonSerializer.Serialize(installed) != JsonSerializer.Serialize(plugin)) throw new InvalidDataException("The plugin installation changed. Select it again.");
        ValidateManifest(new(plugin.Name, plugin.Description, plugin.AllowedHosts.ToArray()));
        if (!File.Exists(SafePath(path, "plugin.wasm"))) throw new IOException("The plugin module is missing.");
        return path;
    }

    private static bool IsId(string id) => Regex.IsMatch(id, "\\A[A-F0-9]{24}\\z");

    private static void ValidateManifest(PocketPluginManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 200 || manifest.Description is null || manifest.Description.Length > 8000)
            throw new InvalidDataException("The plugin manifest needs a name and description.");
        if (manifest.AllowedHosts is { Length: > 100 } || (manifest.AllowedHosts ?? []).Any(host => string.IsNullOrWhiteSpace(host) || host.Length > 253 || !Regex.IsMatch(host, "\\A[A-Za-z0-9*.-]+\\z")))
            throw new InvalidDataException("The plugin manifest has unsupported network hosts.");
    }

    private async Task<byte[]> DownloadAsync(string url, int limit, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadLimitedAsync(response.Content, limit, cancellationToken);
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int limit, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > limit) throw new InvalidDataException("The plugin download exceeds its size limit.");
        using Stream source = await content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        byte[] buffer = new byte[64 * 1024];
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (destination.Length + count > limit) throw new InvalidDataException("The plugin download exceeds its size limit.");
            destination.Write(buffer, 0, count);
        }
        return destination.ToArray();
    }

    private sealed record FileSnapshot(string Hash, long Size);

    private static string[] DirectoryNames(string root) => EnumerateTree(root, excludeSystemMetadata: true).Where(Directory.Exists)
        .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).Order(StringComparer.OrdinalIgnoreCase).ToArray();

    private static Dictionary<string, FileSnapshot> Snapshot(string root, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The target or working folder is no longer available.");
        var files = new Dictionary<string, FileSnapshot>(StringComparer.OrdinalIgnoreCase);
        long bytes = 0;
        foreach (string path in EnumerateTree(root, excludeSystemMetadata: true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(path)) continue;
            var info = new FileInfo(path);
            bytes = checked(bytes + info.Length);
            if (bytes > MaximumWorkingBytes || files.Count >= MaximumFiles) throw new IOException("Plugin working copies are limited to 64 GiB and 200,000 files in this version.");
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (!files.TryAdd(relative, new(Hash(path, cancellationToken), info.Length))) throw new IOException("The target contains conflicting file names.");
        }
        return files;
    }

    private static IEnumerable<string> EnumerateTree(string root, bool excludeSystemMetadata = false)
    {
        EnsureNoLinks(root);
        var directories = new Stack<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        directories.Push(root);
        int entries = 0;
        while (directories.TryPop(out string? directory))
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                // Windows and macOS create protected storage metadata at the card root.
                // It is not Pocket content and is never mounted or applied by this workflow.
                if (excludeSystemMetadata && TargetPaths.Equal(root, directory) && SystemMetadataNames.Contains(Path.GetFileName(path))) continue;
                if (++entries > MaximumFiles * 2) throw new IOException("Too many entries in the plugin working folder.");
                string safe = SafePath(root, Path.GetRelativePath(root, path));
                if (!names.Add(Path.GetRelativePath(root, safe))) throw new IOException("The target contains conflicting names.");
                yield return safe;
                if (Directory.Exists(safe)) directories.Push(safe);
            }
        }
    }

    private static string Hash(string path, CancellationToken cancellationToken)
    {
        using Stream stream = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1024 * 1024];
        int count;
        while ((count = stream.Read(buffer)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, count);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string SafePath(string root, string relative)
    {
        string[] parts = relative.Replace('\\', '/').Split('/');
        if (Path.IsPathRooted(relative) || parts.Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny([':', '<', '>', '"', '|', '?', '*']) >= 0 || part.Any(char.IsControl) || Regex.IsMatch(part, @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|\z)", RegexOptions.IgnoreCase)))
            throw new InvalidDataException("A plugin file path is unsafe or is not portable between supported platforms.");
        string path = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (!TargetPaths.Contains(root, path) || TargetPaths.Equal(root, path)) throw new InvalidDataException("A plugin file path escapes its folder.");
        EnsureNoLinks(path);
        return path;
    }

    private static void EnsureNoLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked files and folders are not supported by the plugin workflow.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static void EnsureSeparate(string target, string local)
    {
        if (TargetPaths.Contains(target, local) || TargetPaths.Contains(local, target))
            throw new IOException("Plugin storage and the target must be in separate folders.");
    }

    private static void EnsureSpace(string path, long required)
    {
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!);
        if (drive.IsReady && drive.AvailableFreeSpace < required) throw new IOException("There is not enough free space for the plugin working copy or backup.");
    }

    private static void CopyVerified(string source, string destination, string expectedHash, CancellationToken cancellationToken)
    {
        using var input = File.OpenRead(source);
        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            byte[] buffer = new byte[1024 * 1024];
            int count;
            while ((count = input.Read(buffer)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Write(buffer, 0, count);
            }
        }
        if (Hash(destination, cancellationToken) != expectedHash) throw new IOException("A source file changed or could not be copied accurately. Run the plugin again.");
    }

    private static void AtomicCopy(string source, string destination, string expectedHash, CancellationToken cancellationToken)
    {
        string temporary = destination + ".ez-pocket-" + Guid.NewGuid().ToString("N") + ".tmp";
        try { CopyVerified(source, temporary, expectedHash, cancellationToken); File.Move(temporary, destination, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void DeleteOwnedFolder(string root, string path)
    {
        path = Path.GetFullPath(path);
        if (!TargetPaths.Contains(root, path) || TargetPaths.Equal(root, path)) throw new IOException("Invalid plugin cleanup folder.");
        if (!Directory.Exists(path)) return;
        EnsureNoLinks(Path.GetDirectoryName(path)!);
        // The absolute target is inside our owned storage. Unlink plugin-created links
        // without following them, including when a failed run produced an unsafe tree.
        var directories = new Stack<(string Path, bool Visited)>();
        directories.Push((path, false));
        while (directories.TryPop(out var entry))
        {
            FileAttributes attributes = File.GetAttributes(entry.Path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(entry.Path);
                else File.Delete(entry.Path);
                continue;
            }
            if ((attributes & FileAttributes.Directory) == 0) { File.Delete(entry.Path); continue; }
            if (entry.Visited) { Directory.Delete(entry.Path); continue; }
            directories.Push((entry.Path, true));
            foreach (string child in Directory.EnumerateFileSystemEntries(entry.Path)) directories.Push((child, false));
        }
    }
}
