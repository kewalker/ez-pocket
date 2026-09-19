namespace EzPocket.Models;

public sealed record FeaturedCoreSet(
    string Id,
    string Name,
    string Description,
    string Note,
    IReadOnlyList<string> CoreIdentifiers);

public sealed record FeaturedCoreSetSelection(
    FeaturedCoreSet Set,
    int MatchedCoreCount,
    IReadOnlyList<string> MissingCoreIdentifiers)
{
    public string Summary => MissingCoreIdentifiers.Count == 0
        ? $"{Set.Name} added: {MatchedCoreCount} core(s) are ready to review. Existing selections are kept."
        : $"{Set.Name} added: {MatchedCoreCount} core(s) are ready to review; {MissingCoreIdentifiers.Count} could not be found in the live inventory. Existing selections are kept.";
}
