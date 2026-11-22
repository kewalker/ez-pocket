using EzPocket.Models;
using EzPocket.Services;
using Xunit;

namespace EzPocket.Tests;

public sealed class CoreReadinessServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ez-pocket-readiness-" + Guid.NewGuid().ToString("N"));

    public CoreReadinessServiceTests()
    {
        foreach (string folder in new[] { "Assets", "Cores", "Platforms", "System" }) Directory.CreateDirectory(Path.Combine(root, folder));
    }

    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void ReportsMissingRequiredBiosInCommonFolder()
    {
        AddCore("Spiritualized.PSX", "psx", "PlayStation", """
            [{"name":"Disc","id":1,"required":true,"parameters":"0x113","extensions":["json"]},
             {"name":"BIOS","id":2,"required":true,"parameters":0,"filename":"ps-bios.bin","size_exact":524288}]
            """);

        CoreReadiness core = Assert.Single(Inspect().Cores);

        Assert.Equal(CoreReadinessState.Missing, core.State);
        Assert.Equal("PlayStation", core.Name);
        CoreRequiredFile file = Assert.Single(core.Files);
        Assert.Equal(RequiredFileState.Missing, file.State);
        Assert.Equal("Assets/psx/common/ps-bios.bin", file.ExpectedRelativePath);
    }

    [Fact]
    public void CoreSpecificFlagChangesTheExpectedFolderAndDetectsMisplacedFiles()
    {
        AddCore("Author.Arcade", "arcade", "Arcade", """
            [{"name":"ROM","id":0,"required":true,"parameters":2,"filename":"boot.rom"}]
            """);
        WriteAsset("arcade/common/boot.rom", 16);

        CoreRequiredFile file = Assert.Single(Assert.Single(Inspect().Cores).Files);

        Assert.Equal(RequiredFileState.WrongFolder, file.State);
        Assert.Equal("Assets/arcade/Author.Arcade/boot.rom", file.ExpectedRelativePath);
    }

    [Fact]
    public void WrongSizeAndOversizedFilesNeedAttention()
    {
        AddCore("Author.Exact", "gba", "Game Boy Advance", """
            [{"name":"BIOS","id":1,"required":true,"parameters":0,"filename":"gba_bios.bin","size_exact":"0x4000"}]
            """);
        AddCore("Author.Max", "nes", "NES", """
            [{"name":"Boot","id":1,"required":true,"parameters":0,"filename":"boot.bin","size_maximum":8}]
            """);
        WriteAsset("gba/common/gba_bios.bin", 100);
        WriteAsset("nes/common/boot.bin", 9);

        CoreReadinessReport report = Inspect();

        Assert.All(report.Cores, core => Assert.Equal(CoreReadinessState.Attention, core.State));
        Assert.All(report.Cores, core => Assert.Equal(RequiredFileState.WrongSize, Assert.Single(core.Files).State));
        Assert.Equal(2, report.ActionCount);
    }

    [Fact]
    public void PresentFilesGameSlotsSavesAndOptionalFilesAreReady()
    {
        AddCore("Author.GB", "gb", "Game Boy", """
            [{"name":"Cartridge","id":0,"required":true,"parameters":"0x109","extensions":["gb"]},
             {"name":"Save","id":1,"nonvolatile":true,"parameters":"0x84","filename":"save.sav"},
             {"name":"Boot ROM","id":2,"required":true,"parameters":0,"filename":"dmg_boot.bin","size_exact":256},
             {"name":"Palette","id":3,"required":false,"parameters":0,"filename":"default.pal"},
             {"name":"Variant","id":4,"required":true,"secondary":true,"parameters":0,"filename":"variant.bin"}]
            """);
        WriteAsset("gb/common/dmg_boot.bin", 256);

        CoreReadiness core = Assert.Single(Inspect().Cores);

        Assert.Equal(CoreReadinessState.Ready, core.State);
        Assert.Equal(2, core.Files.Count);
        Assert.Contains(core.Files, file => file.FileName == "dmg_boot.bin" && file.State == RequiredFileState.Present);
        Assert.Contains(core.Files, file => file.FileName == "default.pal" && file.State == RequiredFileState.OptionalAbsent);
    }

    [Fact]
    public void PlatformIndexSelectsAnotherPlatformsAssetFolder()
    {
        AddCore("Author.GBC", "gbc", "Game Boy Color", """
            [{"name":"GB Boot","id":2,"required":true,"parameters":"0x01000000","filename":"dmg.bin"}]
            """, platformIds: ["gbc", "gb"]);
        WriteAsset("gb/common/dmg.bin", 1);

        CoreRequiredFile file = Assert.Single(Assert.Single(Inspect().Cores).Files);

        Assert.Equal(RequiredFileState.Present, file.State);
        Assert.Equal("Assets/gb/common/dmg.bin", file.ExpectedRelativePath);
    }

    [Fact]
    public void MissingBitstreamPlatformFileAndBrokenDefinitionsAreReported()
    {
        AddCore("Author.NoBits", "sms", "Master System", "[]", writeBitstream: false, writePlatform: false);
        string broken = Path.Combine(root, "Cores", "Author.Broken");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "core.json"), "{ not json");

        CoreReadinessReport report = Inspect();

        CoreReadiness noBits = report.Cores.Single(core => core.Identifier == "Author.NoBits");
        Assert.Equal(CoreReadinessState.Attention, noBits.State);
        Assert.Contains(noBits.Notes, note => note.Contains("bitstream.rbf_r"));
        Assert.Contains(noBits.Notes, note => note.Contains("Platforms/sms.json"));
        Assert.Equal(CoreReadinessState.Unreadable, report.Cores.Single(core => core.Identifier == "Author.Broken").State);
    }

    [Fact]
    public void TraversalFilenamesAreNotFollowed()
    {
        AddCore("Author.Evil", "gb", "Game Boy", """
            [{"name":"Escape","id":1,"required":true,"parameters":0,"filename":"../../../outside.bin"}]
            """);

        CoreReadiness core = Assert.Single(Inspect().Cores);

        Assert.Empty(core.Files);
        Assert.Single(core.Notes);
    }

    [Fact]
    public void HealthCheckSummarizesCoresThatNeedFiles()
    {
        AddCore("Author.PSX", "psx", "PlayStation", """
            [{"name":"BIOS","id":2,"required":true,"parameters":0,"filename":"ps-bios.bin"}]
            """);
        var pocket = new PocketDrive(root, "Pocket", DriveType.Unknown, 0, 0, 4, ["Assets", "Cores", "Platforms", "System"], 1, ["Author.PSX"]);

        PocketHealthReport report = new PocketHealthService().Inspect(pocket);

        PocketHealthFinding finding = Assert.Single(report.Findings);
        Assert.Equal(PocketHealthAction.CoreReadiness, finding.Action);
        Assert.Contains("PlayStation", finding.Detail);
        Assert.True(report.CanWrite);
    }

    private CoreReadinessReport Inspect() =>
        new CoreReadinessService().Inspect(new PocketDrive(root, "Pocket", DriveType.Unknown, 0, 0, 4, [], 0, []));

    private void AddCore(string identifier, string platform, string platformName, string slots, string[]? platformIds = null, bool writeBitstream = true, bool writePlatform = true)
    {
        string directory = Path.Combine(root, "Cores", identifier);
        Directory.CreateDirectory(directory);
        string ids = string.Join(",", (platformIds ?? [platform]).Select(id => $"\"{id}\""));
        File.WriteAllText(Path.Combine(directory, "core.json"),
            $$$"""{"core":{"magic":"APF_VER_1","metadata":{"shortname":"{{{identifier}}}","version":"1.0.0","platform_ids":[{{{ids}}}]},"cores":[{"name":"default","id":0,"filename":"bitstream.rbf_r"}]}}""");
        File.WriteAllText(Path.Combine(directory, "data.json"), $$$"""{"data":{"magic":"APF_VER_1","data_slots":{{{slots}}}}}""");
        if (writeBitstream) File.WriteAllBytes(Path.Combine(directory, "bitstream.rbf_r"), [0]);
        if (writePlatform)
            File.WriteAllText(Path.Combine(root, "Platforms", platform + ".json"), $$$"""{"platform":{"category":"Handheld","name":"{{{platformName}}}","year":1990,"manufacturer":"Example"}}""");
    }

    private void WriteAsset(string relative, int size)
    {
        string path = Path.Combine(root, "Assets", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
    }
}
