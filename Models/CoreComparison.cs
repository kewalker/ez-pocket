namespace EzPocket.Models;

public sealed record CoreComparison(
    string Identifier,
    string FriendlyName,
    string Category,
    string AvailableVersion,
    bool IsInstalled,
    bool IsAvailable,
    string Status)
{
    public string StatusLabel => Status switch
    {
        "Installed" => "Installed",
        "Available" => "Available to install",
        _ => "Not in inventory"
    };
}
