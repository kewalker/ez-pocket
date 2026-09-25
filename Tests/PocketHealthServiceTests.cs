using EzPocket.Models;
using EzPocket.Services;
using Xunit;

namespace EzPocket.Tests;

public sealed class PocketHealthServiceTests
{
    [Fact]
    public void InspectReportsMissingFoldersAndUnreadableCoreMetadata()
    {
        string root = CreateTempFolder();
        try
        {
            string pocketPath = Path.Combine(root, "Pocket");
            Directory.CreateDirectory(Path.Combine(pocketPath, "Cores", "broken.core"));
            File.WriteAllText(Path.Combine(pocketPath, "Cores", "broken.core", "core.json"), "not json");
            var pocket = new PocketDrive(pocketPath, "Pocket", DriveType.Unknown, 0, 0, 1, ["Cores"], 1, ["broken.core"]);

            PocketHealthReport report = new PocketHealthService().Inspect(pocket);

            Assert.False(report.IsReady);
            Assert.False(report.CanWrite);
            Assert.Contains(report.Findings, finding => finding.Title == "Pocket folders are incomplete");
            Assert.Contains(report.Findings, finding => finding.Title == "broken.core metadata cannot be read");
            Assert.Contains(report.Findings, finding => finding.Action == PocketHealthAction.InitializeTarget);
            Assert.Contains(report.Findings, finding => finding.Action == PocketHealthAction.ManageCores);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void InspectReportsAHealthyTarget()
    {
        string root = CreateTempFolder();
        try
        {
            string pocketPath = Path.Combine(root, "Pocket");
            foreach (string folder in new[] { "Assets", "Cores", "Platforms", "System" }) Directory.CreateDirectory(Path.Combine(pocketPath, folder));
            string corePath = Path.Combine(pocketPath, "Cores", "example.core");
            Directory.CreateDirectory(corePath);
            File.WriteAllText(Path.Combine(corePath, "core.json"), """{"core":{"metadata":{"version":"1.0"}}}""");
            var pocket = new PocketDrive(pocketPath, "Pocket", DriveType.Unknown, 0, 0, 4, ["Assets", "Cores", "Platforms", "System"], 1, ["example.core"]);

            PocketHealthReport report = new PocketHealthService().Inspect(pocket);

            Assert.True(report.IsReady);
            Assert.Equal("READY", Assert.Single(report.Findings).Severity);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void UnavailableTargetBlocksWrites()
    {
        var pocket = new PocketDrive(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), "Missing", DriveType.Unknown, 0, 0, 0, [], 0, []);

        PocketHealthReport report = new PocketHealthService().Inspect(pocket);

        Assert.False(report.CanWrite);
        Assert.Equal("BLOCKED", Assert.Single(report.Findings).Severity);
    }

    [Fact]
    public void InspectReportsMissingMetadataFieldsWithoutThrowing()
    {
        string root = CreateTempFolder();
        try
        {
            string pocketPath = Path.Combine(root, "Pocket");
            foreach (string folder in new[] { "Assets", "Cores", "Platforms", "System" }) Directory.CreateDirectory(Path.Combine(pocketPath, folder));
            string corePath = Path.Combine(pocketPath, "Cores", "incomplete.core");
            Directory.CreateDirectory(corePath);
            File.WriteAllText(Path.Combine(corePath, "core.json"), "{}");
            var pocket = new PocketDrive(pocketPath, "Pocket", DriveType.Unknown, 0, 0, 4, ["Assets", "Cores", "Platforms", "System"], 1, ["incomplete.core"]);

            PocketHealthReport report = new PocketHealthService().Inspect(pocket);

            Assert.Contains(report.Findings, finding => finding.Title == "incomplete.core metadata cannot be read");
        }
        finally { Directory.Delete(root, true); }
    }

    private static string CreateTempFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "ez-pocket-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
