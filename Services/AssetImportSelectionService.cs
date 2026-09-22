using EzPocket.Models;

namespace EzPocket.Services;

/// <summary>Keeps a reviewed asset plan separate from the core-sync review.</summary>
public sealed class AssetImportSelectionService
{
    public AssetImportPreview? PendingPreview { get; private set; }
    public AssetRemovalPreview? PendingRemoval { get; private set; }
    public AssetSetInventory? PendingSet { get; private set; }
    public PalettePackCatalog? PendingPalettePack { get; private set; }
    public string? SuccessfulImportMessage { get; private set; }

    public void SetPreview(AssetImportPreview preview) => PendingPreview = preview;
    public void ClearPreview() => PendingPreview = null;
    public void SetRemoval(AssetRemovalPreview preview) => PendingRemoval = preview;
    public void ClearRemoval() => PendingRemoval = null;
    public void SelectSet(AssetSetInventory set) => PendingSet = set;
    public void ClearSet() => PendingSet = null;
    public void SetPalettePack(PalettePackCatalog catalog) => PendingPalettePack = catalog;
    public void ClearPalettePack() => PendingPalettePack = null;
    public void ReportSuccess(string message) => SuccessfulImportMessage = message;
    public string? ConsumeSuccess() { string? message = SuccessfulImportMessage; SuccessfulImportMessage = null; return message; }
}
