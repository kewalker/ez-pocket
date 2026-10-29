using EzPocket.Models;
using EzPocket.Services;
using Xunit;

namespace EzPocket.Tests;

public sealed class PocketSelectionTests
{
    [Fact]
    public void RestoreUsesSavedInventoryEvenWhenTargetIsOffline()
    {
        var preferences = new MemoryPreferences();
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string[] cores = Enumerable.Range(0, 500).Select(index => $"Publisher.Core{index}").ToArray();
        var pocket = new PocketDrive(path, "Pocket", DriveType.Removable, 1000, 500, 4, ["Assets", "Cores", "Platforms", "System"], cores.Length, cores);
        var firstSession = new PocketSelectionService(preferences: preferences);
        firstSession.Select(pocket);

        var nextSession = new PocketSelectionService(preferences: preferences);
        PocketDrive restored = Assert.IsType<PocketDrive>(nextSession.Restore());

        Assert.False(Directory.Exists(path));
        Assert.Equal(cores, restored.InstalledCoreNames);
        Assert.Equal(firstSession.LastScannedAt, nextSession.LastScannedAt);
        Assert.Null(nextSession.LastHealthReport);
        Assert.All(preferences.Values.Values.OfType<string>(), value => Assert.True(value.Length <= 2000));
        nextSession.Clear();
        Assert.Null(new PocketSelectionService(preferences: preferences).Restore());
        Assert.Empty(preferences.Values);
    }

    [Fact]
    public void RestoringLegacyPathDoesNotClaimAnInventoryScanOrCreateTarget()
    {
        var preferences = new MemoryPreferences();
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        preferences.Set("selected-pocket-path", path);
        var selection = new PocketSelectionService(preferences: preferences);

        Assert.Equal(path, selection.Restore()!.RootPath);
        Assert.Null(selection.LastScannedAt);
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public void HealthResultsAreScopedToSelectedTargetAndInvalidatedByRescan()
    {
        var selection = new PocketSelectionService(preferences: new MemoryPreferences());
        var first = new PocketDrive("C:\\First", "First", DriveType.Unknown, 0, 0, 0, [], 0, []);
        var second = first with { RootPath = "C:\\Second" };
        selection.Select(first);
        var report = new PocketHealthReport(first, []);
        selection.RecordHealth(report);
        Assert.Same(report, selection.LastHealthReport);
        selection.Select(second);
        selection.RecordHealth(report);
        Assert.Null(selection.LastHealthReport);
        Assert.Null(selection.LastHealthCheckedAt);
    }

    private sealed class MemoryPreferences : IPreferences
    {
        public Dictionary<string, object> Values { get; } = [];
        public bool ContainsKey(string key, string? sharedName = null) => Values.ContainsKey(key);
        public void Remove(string key, string? sharedName = null) => Values.Remove(key);
        public void Clear(string? sharedName = null) => Values.Clear();
        public void Set<T>(string key, T value, string? sharedName = null) => Values[key] = value!;
        public T Get<T>(string key, T defaultValue, string? sharedName = null) => Values.TryGetValue(key, out var value) ? (T)value : defaultValue;
    }

    [Fact]
    public void BlankFolderCanBeScannedWithoutBeingRecognized()
    {
        string path = CreateTempFolder();
        try
        {
            PocketDrive? pocket = new PocketScanner().ScanFolder(path);
            Assert.NotNull(pocket);
            Assert.False(pocket.LooksLikePocket);
            Assert.Empty(pocket.FoundFolders);
        }
        finally { Directory.Delete(path, true); }
    }

    [Fact]
    public void InitializationPreviewListsOnlyMissingFolders()
    {
        string path = CreateTempFolder();
        try
        {
            Directory.CreateDirectory(Path.Combine(path, "Cores"));
            PocketDrive pocket = new PocketScanner().ScanFolder(path)!;
            PocketInitializationPreview preview = new PocketInitializationService().Preview(pocket);
            Assert.Equal(["Assets", "Platforms", "System"], preview.MissingFolders);
        }
        finally { Directory.Delete(path, true); }
    }

    private static string CreateTempFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "ez-pocket-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void UnknownCapacityDoesNotRenderAsZeroOfZero()
    {
        var pocket = new PocketDrive("C:\\Pocket", "Pocket", DriveType.Unknown, 0, 0, 0, [], 0, []);

        Assert.Equal("Capacity unavailable", pocket.CapacitySummary);
    }
}
