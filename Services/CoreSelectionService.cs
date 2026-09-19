using EzPocket.Models;

namespace EzPocket.Services;

public sealed class CoreSelectionService
{
    public CoreComparison? SelectedCore { get; private set; }
    private readonly Dictionary<string, CoreComparison> selectedCores = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<CoreComparison> cores = [];
    private string? initializedPocketPath;
    private string? syncSuccessMessage;
    private string? featuredSetSelectionMessage;

    public IReadOnlyList<CoreComparison> SelectedCores => selectedCores.Values.OrderBy(core => core.FriendlyName).ToArray();
    public IReadOnlyList<CoreComparison> Cores => cores;

    public void Select(CoreComparison core) => SelectedCore = core;

    public void SetSelected(CoreComparison core, bool isSelected)
    {
        core.IsSelected = isSelected;
        if (isSelected) selectedCores[core.Identifier] = core;
        else selectedCores.Remove(core.Identifier);
    }

    public void InitializeForPocket(PocketDrive pocket, IReadOnlyList<CoreComparison> cores)
    {
        bool isNewPocket = !string.Equals(initializedPocketPath, pocket.RootPath, StringComparison.OrdinalIgnoreCase);
        var previouslySelected = isNewPocket
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : selectedCores.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        selectedCores.Clear();
        this.cores = cores;
        foreach (CoreComparison core in cores)
        {
            bool isSelected = isNewPocket ? core.IsInstalled : previouslySelected.Contains(core.Identifier);
            core.IsSelected = isSelected;
            if (isSelected) selectedCores[core.Identifier] = core;
        }
        initializedPocketPath = pocket.RootPath;
    }

    public void ClearSelected()
    {
        foreach (CoreComparison core in selectedCores.Values) core.IsSelected = false;
        selectedCores.Clear();
    }

    public void ReplaceSelection(IEnumerable<CoreComparison> desiredCores)
    {
        var desiredIdentifiers = desiredCores
            .Select(core => core.Identifier)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        selectedCores.Clear();
        foreach (CoreComparison core in cores)
        {
            bool isSelected = desiredIdentifiers.Contains(core.Identifier);
            core.IsSelected = isSelected;
            if (isSelected) selectedCores[core.Identifier] = core;
        }
    }

    public void ReportSuccessfulSync(string message) => syncSuccessMessage = message;

    public void ReportFeaturedSetSelection(string message) => featuredSetSelectionMessage = message;

    public string? ConsumeFeaturedSetSelectionMessage()
    {
        string? message = featuredSetSelectionMessage;
        featuredSetSelectionMessage = null;
        return message;
    }

    public string? ConsumeSuccessfulSyncMessage()
    {
        string? message = syncSuccessMessage;
        syncSuccessMessage = null;
        return message;
    }
}
