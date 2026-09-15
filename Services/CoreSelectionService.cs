using EzPocket.Models;

namespace EzPocket.Services;

public sealed class CoreSelectionService
{
    public CoreComparison? SelectedCore { get; private set; }

    public void Select(CoreComparison core) => SelectedCore = core;
}
