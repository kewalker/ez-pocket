using System.ComponentModel;

namespace EzPocket.Models;

public sealed record CoreComparison(
    string Identifier,
    string FriendlyName,
    string Category,
    string InstalledVersion,
    string AvailableVersion,
    bool IsInstalled,
    bool IsAvailable,
    string Status) : INotifyPropertyChanged
{
    private bool isSelected;

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value) return;
            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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
