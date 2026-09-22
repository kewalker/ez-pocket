namespace EzPocket.Models;

/// <summary>A reviewed, reversible removal confined to one logical asset resource.</summary>
public sealed record AssetRemovalPreview(
    PocketDrive Pocket,
    AssetSetInventory Set,
    IReadOnlyList<ManagedAsset> Files)
{
    public bool CanApply => Files.Count > 0;
}
