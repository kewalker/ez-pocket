namespace EzPocket.Models;

public sealed record PocketDrive(
    string RootPath,
    string Name,
    DriveType DriveType,
    long TotalBytes,
    long FreeBytes,
    int PocketFolderCount,
    IReadOnlyList<string> FoundFolders,
    int CoreCount)
{
    public bool LooksLikePocket => PocketFolderCount >= 2;
    public string CapacitySummary => $"{FormatBytes(FreeBytes)} free of {FormatBytes(TotalBytes)}";

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }
}
