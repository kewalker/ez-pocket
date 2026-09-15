using EzPocket.Models;

namespace EzPocket.Services;

public sealed class PocketSelectionService
{
    public PocketDrive? SelectedPocket { get; private set; }

    public void Select(PocketDrive pocket) => SelectedPocket = pocket;

    public void Clear() => SelectedPocket = null;
}
