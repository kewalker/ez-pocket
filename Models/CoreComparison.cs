namespace EzPocket.Models;

public sealed record CoreComparison(
    string Identifier,
    string FriendlyName,
    string Category,
    string InstalledVersion,
    string AvailableVersion,
    bool IsInstalled,
    bool IsAvailable,
    string Status)
{
    public string StatusLabel => Status switch
    {
        "Update" => "Update available",
        "Installed" => "Installed",
        "Available" => "Available to install",
        _ => "Not in inventory"
    };

    public string VersionLabel => IsInstalled && IsAvailable
        ? $"Installed {InstalledVersion} · Latest {AvailableVersion}"
        : AvailableVersion;
}
