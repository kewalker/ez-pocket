using EzPocket.Models;

namespace EzPocket.Services;

public sealed class CoreSelectionService
{
    public CoreComparison? SelectedCore { get; private set; }
    private readonly Dictionary<string, CoreComparison> selectedCores = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CoreComparison> SelectedCores => selectedCores.Values.OrderBy(core => core.FriendlyName).ToArray();

    public void Select(CoreComparison core) => SelectedCore = core;

    public void SetSelected(CoreComparison core, bool isSelected)
    {
        core.IsSelected = isSelected;
        if (isSelected) selectedCores[core.Identifier] = core;
        else selectedCores.Remove(core.Identifier);
    }

    public void ClearSelected() => selectedCores.Clear();
}
