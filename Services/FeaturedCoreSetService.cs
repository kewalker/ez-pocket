using EzPocket.Models;

namespace EzPocket.Services;

/// <summary>Provides small, reviewed starter lineups rather than an unbounded set manager.</summary>
public sealed class FeaturedCoreSetService
{
    private static readonly IReadOnlyList<FeaturedCoreSet> sets =
    [
        new(
            "handheld-essentials",
            "Handheld essentials",
            "Game Boy through Game Gear, plus Neo Geo Pocket Color, Lynx, and PC Engine.",
            "Uses standard Pocket core releases; cartridge adapters are not required for openFPGA use.",
            ["Spiritualized.GB", "Spiritualized.GBC", "Spiritualized.GBA", "Spiritualized.GG", "janisc.NGPC", "budude2.Lynx", "agg23.PC Engine"]),
        new(
            "console-classics",
            "Console classics",
            "A balanced set of handheld, 8-bit, and 16-bit console cores.",
            "Includes the handheld essentials alongside NES, SNES, Genesis, Master System, and Atari 2600.",
            ["Spiritualized.GB", "Spiritualized.GBC", "Spiritualized.GBA", "Spiritualized.GG", "janisc.NGPC", "budude2.Lynx", "agg23.PC Engine", "Spiritualized.NES", "agg23.SNES", "Spiritualized.Genesis", "Spiritualized.SMS", "Spiritualized.2600"]),
        new(
            "arcade-starter",
            "Arcade starter",
            "Five approachable arcade platforms to begin an arcade collection.",
            "Core packages do not include game ROMs; add files you are entitled to use after installation.",
            ["AwesomeDolphin.SpaceInvaders", "HarpMudd.Berzerk", "HarpMudd.Popeye", "HarpMudd.Tapper", "MorganVieira.Rally-X"])
    ];

    public IReadOnlyList<FeaturedCoreSet> Sets => sets;
    public FeaturedCoreSet? PendingSet { get; private set; }

    public bool Select(string id)
    {
        PendingSet = sets.FirstOrDefault(set => string.Equals(set.Id, id, StringComparison.Ordinal));
        return PendingSet is not null;
    }

    public FeaturedCoreSetSelection? ApplyPendingSelection(CoreSelectionService selection, IReadOnlyList<CoreComparison> availableCores)
    {
        FeaturedCoreSet? set = PendingSet;
        if (set is null) return null;

        var availableByIdentifier = availableCores.ToDictionary(core => core.Identifier, StringComparer.OrdinalIgnoreCase);
        var matched = new List<CoreComparison>();
        var missing = new List<string>();
        foreach (string identifier in set.CoreIdentifiers)
        {
            if (availableByIdentifier.TryGetValue(identifier, out CoreComparison? core)) matched.Add(core);
            else missing.Add(identifier);
        }

        selection.ReplaceSelection(matched);
        PendingSet = null;
        return new FeaturedCoreSetSelection(set, matched.Count, missing);
    }
}
