using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EzPocket.Models;

namespace EzPocket.Services;

public sealed class CoreInventoryService
{
    private const string InventoryUrl = "https://openfpga-cores-inventory.github.io/analogue-pocket/api/v2/cores.json";
    private readonly HttpClient client;

    public CoreInventoryService(HttpClient? client = null)
    {
        this.client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    public async Task<IReadOnlyList<AvailableCore>> GetAvailableAsync(CancellationToken cancellationToken = default)
    {
        InventoryResponse? response = await client.GetFromJsonAsync<InventoryResponse>(InventoryUrl, cancellationToken);
        return response?.Data?
            .Where(core => !string.IsNullOrWhiteSpace(core.Identifier))
            .Select(core => new AvailableCore(core.Identifier!, core.Version ?? "Unknown", core.Platform?.Name ?? core.Identifier!, core.Platform?.Category ?? "Other", core.DownloadUrl, core.RequiresLicense ?? false))
            .OrderBy(core => core.Name)
            .ToArray() ?? [];
    }

    public static IReadOnlyList<CoreComparison> Compare(PocketDrive pocket, IReadOnlyList<AvailableCore> available)
    {
        var availableById = available
            .GroupBy(core => core.Identifier, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(core => ParseVersion(core.Version)).First())
            .ToDictionary(core => core.Identifier, StringComparer.OrdinalIgnoreCase);
        var installedIds = pocket.InstalledCoreNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = availableById.Values.Select(core =>
        {
            string installedVersion = GetInstalledVersion(pocket, core.Identifier) ?? "-";
            string status = !installedIds.Contains(core.Identifier)
                ? "Available"
                : IsOlder(installedVersion, core.Version) ? "Update" : "Installed";
            return new CoreComparison(core.Identifier, core.Name, core.Category, installedVersion, core.Version, installedIds.Contains(core.Identifier), true, status, core.DownloadUrl, core.RequiresLicense);
        });

        var missingFromInventory = pocket.InstalledCoreNames
            .Where(identifier => !availableById.ContainsKey(identifier))
            .Select(identifier => new CoreComparison(identifier, identifier, "Unknown", GetInstalledVersion(pocket, identifier) ?? "Unknown", "-", true, false, "Unknown"));
        return result.Concat(missingFromInventory).OrderBy(core => core.FriendlyName).ToArray();
    }

    private static string? GetInstalledVersion(PocketDrive pocket, string identifier)
    {
        string file = Path.Combine(pocket.RootPath, "Cores", identifier, "core.json");
        if (!File.Exists(file)) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
            return document.RootElement.GetProperty("core").GetProperty("metadata").GetProperty("version").GetString();
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static bool IsOlder(string installed, string available) => ParseVersion(installed).CompareTo(ParseVersion(available)) < 0;

    private static Version ParseVersion(string value)
    {
        string numeric = value.Trim().TrimStart('v').Split('-', 2)[0];
        return Version.TryParse(numeric, out Version? version) ? version : new Version(0, 0);
    }

    private sealed class InventoryResponse
    {
        [JsonPropertyName("data")] public List<InventoryCore>? Data { get; set; }
    }

    private sealed class InventoryCore
    {
        [JsonPropertyName("identifier")] public string? Identifier { get; set; }
        [JsonPropertyName("version")] public string? Version { get; set; }
        [JsonPropertyName("download_url")] public string? DownloadUrl { get; set; }
        [JsonPropertyName("requires_license")] public bool? RequiresLicense { get; set; }
        [JsonPropertyName("platform")] public InventoryPlatform? Platform { get; set; }
    }

    private sealed class InventoryPlatform
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("category")] public string? Category { get; set; }
    }
}
