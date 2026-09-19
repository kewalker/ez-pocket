using System.Net.Http.Headers;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EzPocket.Models;

namespace EzPocket.Services;

/// <summary>Stages official Pocket firmware separately from all core operations.</summary>
public sealed class FirmwareUpdateService
{
    private const string FirmwarePageUrl = "https://www.analogue.co/support/pocket/firmware";
    private static readonly Uri FirmwarePage = new(FirmwarePageUrl);
    private static readonly Regex VersionPattern = new(@"Firmware\s+v(?<version>[0-9][0-9A-Za-z.\-]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Md5Pattern = new(@"\bMD5\b\s*(?<md5>[a-f0-9]{32})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex HtmlTagPattern = new(@"<[^>]+>", RegexOptions.CultureInvariant);
    private const int MaximumFirmwareBackups = 5;
    private readonly HttpClient client;
    private readonly string stagingRoot;
    private readonly string backupRoot;

    public FirmwareUpdateService(HttpClient? client = null, string? stagingRoot = null, string? backupRoot = null)
    {
        this.client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        this.stagingRoot = stagingRoot ?? Path.Combine(appData, "EzPocket", "firmware-staging");
        this.backupRoot = backupRoot ?? Path.Combine(appData, "EzPocket", "backups", "firmware");
    }

    public async Task<FirmwareRelease> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        string index = await client.GetStringAsync(FirmwarePage, cancellationToken);
        Match versionMatch = VersionPattern.Match(index);
        if (!versionMatch.Success) throw new InvalidDataException("Analogue's firmware page did not include a supported latest-version value.");

        string version = versionMatch.Groups["version"].Value;
        Uri releasePage = new($"https://www.analogue.co/support/pocket/firmware/{version}");
        string releaseDetails = await client.GetStringAsync(releasePage, cancellationToken);
        string md5 = ExtractPublishedMd5(releaseDetails);

        return new FirmwareRelease(version, md5, new Uri($"https://www.analogue.co/support/pocket/firmware/{version}/download"));
    }

    public async Task<FirmwareUpdatePreview> PrepareAsync(PocketDrive pocket, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(pocket.RootPath)) throw new DirectoryNotFoundException("The selected Pocket is no longer available.");

        FirmwareRelease release = await GetLatestReleaseAsync(cancellationToken);
        string stagingPath = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingPath);
        try
        {
            using HttpResponseMessage response = await client.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            string fileName = GetSafeFileName(response.Content.Headers.ContentDisposition, release.Version);
            string firmwarePath = Path.Combine(stagingPath, fileName);
            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (FileStream destination = File.Create(firmwarePath))
                await source.CopyToAsync(destination, cancellationToken);

            string actualMd5 = await GetMd5Async(firmwarePath, cancellationToken);
            if (!string.Equals(actualMd5, release.Md5, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded firmware did not match Analogue's published MD5 checksum.");

            string[] existingFirmwareFiles = Directory.EnumerateFiles(pocket.RootPath, "pocket_firmware*.bin", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new FirmwareUpdatePreview(pocket.RootPath, release, stagingPath, firmwarePath, fileName, new FileInfo(firmwarePath).Length, existingFirmwareFiles);
        }
        catch
        {
            Cleanup(stagingPath);
            throw;
        }
    }

    public Task<FirmwareUpdateResult> ApplyAsync(FirmwareUpdatePreview preview, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(preview.PocketPath)) throw new DirectoryNotFoundException("The selected Pocket is no longer available.");
            if (!File.Exists(preview.FirmwareFilePath)) throw new FileNotFoundException("The prepared firmware file is no longer available.");

            string destination = Path.Combine(preview.PocketPath, preview.FileName);
            EnsureRootDestination(preview.PocketPath, destination);
            string[] currentFirmwareFiles = Directory.EnumerateFiles(preview.PocketPath, "pocket_firmware*.bin", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (!currentFirmwareFiles.SequenceEqual(preview.ExistingFirmwareFiles, StringComparer.OrdinalIgnoreCase))
                throw new IOException("Pocket firmware files changed after the preview. Prepare the update again before staging it.");
            string[] existingFiles = preview.ExistingFirmwareFiles
                .Select(fileName => Path.Combine(preview.PocketPath, fileName))
                .Where(File.Exists)
                .ToArray();
            foreach (string existingFile in existingFiles) EnsureRootDestination(preview.PocketPath, existingFile);
            string? backupPath = null;
            if (existingFiles.Length > 0)
            {
                backupPath = Path.Combine(backupRoot, $"firmware-{DateTimeOffset.UtcNow.Ticks:D19}-{Guid.NewGuid():N}");
                Directory.CreateDirectory(backupPath);
                foreach (string existingFile in existingFiles)
                    File.Copy(existingFile, Path.Combine(backupPath, Path.GetFileName(existingFile)), true);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string temporaryDestination = destination + ".ez-pocket-" + Guid.NewGuid().ToString("N") + ".tmp";
                File.Copy(preview.FirmwareFilePath, temporaryDestination, true);
                foreach (string existingFile in existingFiles.Where(file => !string.Equals(file, destination, StringComparison.OrdinalIgnoreCase)))
                    File.Delete(existingFile);
                File.Move(temporaryDestination, destination, true);
                Cleanup(preview.StagingPath);
                PruneBackups();
                return Task.FromResult(new FirmwareUpdateResult(true, $"Firmware {preview.Release.Version} is ready on your Pocket SD card.", backupPath));
            }
            catch
            {
                if (backupPath is not null)
                {
                    foreach (string backup in Directory.EnumerateFiles(backupPath))
                        File.Copy(backup, Path.Combine(preview.PocketPath, Path.GetFileName(backup)), true);
                }
                throw;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return Task.FromResult(new FirmwareUpdateResult(false, $"Firmware was not staged: {exception.Message}"));
        }
    }

    public void Cleanup(FirmwareUpdatePreview preview) => Cleanup(preview.StagingPath);

    private static string GetSafeFileName(ContentDispositionHeaderValue? contentDisposition, string version)
    {
        string? suggestedName = contentDisposition?.FileNameStar ?? contentDisposition?.FileName;
        string fileName = Path.GetFileName(suggestedName?.Trim('"') ?? $"pocket_firmware_{version}.bin");
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The official firmware download did not provide a supported firmware filename.");
        return fileName;
    }

    private static string ExtractPublishedMd5(string releaseDetails)
    {
        string plainText = WebUtility.HtmlDecode(HtmlTagPattern.Replace(releaseDetails, " "));
        Match match = Md5Pattern.Match(plainText);
        if (!match.Success) throw new InvalidDataException("Analogue's firmware release notes did not include an MD5 checksum. Firmware was not downloaded.");
        return match.Groups["md5"].Value.ToLowerInvariant();
    }

    private static async Task<string> GetMd5Async(string filePath, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(filePath);
        byte[] hash = await MD5.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void EnsureRootDestination(string rootPath, string destination)
    {
        string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(destination)) + Path.DirectorySeparatorChar, root, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Firmware can only be staged at the root of the selected Pocket.");
    }

    private void PruneBackups()
    {
        if (!Directory.Exists(backupRoot)) return;
        foreach (DirectoryInfo backup in new DirectoryInfo(backupRoot)
            .GetDirectories("firmware-*")
            .OrderByDescending(directory => directory.Name, StringComparer.Ordinal)
            .Skip(MaximumFirmwareBackups))
        {
            backup.Delete(true);
        }
    }

    private static void Cleanup(string stagingPath)
    {
        if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, true);
    }
}
