using System.Net.Http.Headers;
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
    private static readonly Regex Md5Pattern = new(@"MD5\D{0,80}(?<md5>[a-f0-9]{32})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
        Match md5Match = Md5Pattern.Match(releaseDetails);
        if (!md5Match.Success) throw new InvalidDataException("Analogue's firmware release notes did not include an MD5 checksum.");

        return new FirmwareRelease(version, md5Match.Groups["md5"].Value.ToLowerInvariant(), new Uri($"https://www.analogue.co/support/pocket/firmware/{version}/download"));
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

            bool hasOtherFirmwareFile = Directory.EnumerateFiles(pocket.RootPath, "pocket_firmware*.bin", SearchOption.TopDirectoryOnly)
                .Any(existingFile => !string.Equals(Path.GetFileName(existingFile), fileName, StringComparison.OrdinalIgnoreCase));
            if (hasOtherFirmwareFile)
                throw new IOException("Remove the other Pocket firmware file from the SD-card root before staging this update.");

            return new FirmwareUpdatePreview(pocket.RootPath, release, stagingPath, firmwarePath, fileName, new FileInfo(firmwarePath).Length);
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
            string? backupPath = null;
            if (File.Exists(destination))
            {
                backupPath = Path.Combine(backupRoot, $"firmware-{DateTimeOffset.UtcNow.Ticks:D19}-{preview.FileName}");
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.Copy(destination, backupPath, true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            string temporaryDestination = destination + ".ez-pocket-" + Guid.NewGuid().ToString("N") + ".tmp";
            File.Copy(preview.FirmwareFilePath, temporaryDestination, true);
            File.Move(temporaryDestination, destination, true);
            Cleanup(preview.StagingPath);
            return Task.FromResult(new FirmwareUpdateResult(true, $"Firmware {preview.Release.Version} is ready on your Pocket SD card.", backupPath));
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

    private static void Cleanup(string stagingPath)
    {
        if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, true);
    }
}
