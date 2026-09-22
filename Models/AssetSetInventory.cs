namespace EzPocket.Models;

/// <summary>Summary of an optional asset set found on a Pocket target.</summary>
public sealed record AssetSetInventory(
    string Key,
    string Name,
    string Kind,
    string RelativeDirectory,
    int FileCount,
    long SizeBytes)
{
    public bool IsInstalled => FileCount > 0;
    public string Status => IsInstalled
        ? $"{FileCount:N0} file{(FileCount == 1 ? string.Empty : "s")} · {FormatSize(SizeBytes)}"
        : "Not installed";

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024d:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024):0.#} MB",
        _ => $"{bytes / (1024d * 1024 * 1024):0.#} GB"
    };
}
