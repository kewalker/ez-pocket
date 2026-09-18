namespace EzPocket.Models;

public sealed record FirmwareRelease(string Version, string Md5, Uri DownloadUrl);

public sealed record FirmwareUpdatePreview(
    string PocketPath,
    FirmwareRelease Release,
    string StagingPath,
    string FirmwareFilePath,
    string FileName,
    long SizeBytes)
{
    public string SizeLabel => SizeBytes < 1024 * 1024
        ? $"{Math.Max(1, SizeBytes / 1024d):0.#} KB"
        : $"{SizeBytes / 1024d / 1024d:0.#} MB";
}

public sealed record FirmwareUpdateResult(bool Succeeded, string Message, string? BackupPath = null);
