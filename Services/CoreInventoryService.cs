using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EzPocket.Models;

namespace EzPocket.Services;

public sealed class CoreInventoryService
{
    private const string InventoryUrl = "https://openfpga-cores-inventory.github.io/analogue-pocket/api/v2/cores.json";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<IReadOnlyList<AvailableCore>> GetAvailableAsync(CancellationToken cancellationToken = default)
    {
        InventoryResponse? response = await Client.GetFromJsonAsync<InventoryResponse>(InventoryUrl, cancellationToken);
        return response?.Data?
            .Where(core => !string.IsNullOrWhiteSpace(core.Identifier))
            .Select(core => new AvailableCore(
                core.Identifier!,
                core.Version ?? "Unknown",
                core.Platform?.Name ?? core.Identifier!,
                core.Platform?.Category ?? "Other"))
            .OrderBy(core => core.Name)
            .ToArray() ?? [];
    }

    private sealed class InventoryResponse
    {
        [JsonPropertyName("data")]
        public List<InventoryCore>? Data { get; set; }
    }

    private sealed class InventoryCore
    {
        [JsonPropertyName("identifier")]
        public string? Identifier { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("platform")]
        public InventoryPlatform? Platform { get; set; }
    }

    private sealed class InventoryPlatform
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }
    }
}
