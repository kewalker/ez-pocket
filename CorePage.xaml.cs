using EzPocket.Models;
using EzPocket.Services;

namespace EzPocket;

public partial class CorePage : ContentPage
{
    private readonly PocketScanner scanner;
    private readonly CoreInventoryService inventory;

    public CorePage()
    {
        InitializeComponent();
        scanner = IPlatformApplication.Current?.Services.GetService<PocketScanner>() ?? new PocketScanner();
        inventory = IPlatformApplication.Current?.Services.GetService<CoreInventoryService>() ?? new CoreInventoryService();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
    }

    private async void OnRefreshClicked(object? sender, EventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        var pocket = scanner.Scan().FirstOrDefault(drive => drive.LooksLikePocket);
        if (pocket is null)
        {
            Subtitle.Text = "Connect a Pocket SD card or USB Pocket to compare cores.";
            Summary.Text = "No Pocket connected";
            CoreList.ItemsSource = null;
            return;
        }

        try
        {
            var available = await inventory.GetAvailableAsync();
            var comparison = CoreInventoryService.Compare(pocket, available);
            Subtitle.Text = $"{pocket.Name} · friendly names come from the live inventory";
            Summary.Text = $"{pocket.CoreCount} installed · {available.Count} available";
            CoreList.ItemsSource = comparison;
        }
        catch (HttpRequestException)
        {
            Subtitle.Text = "The Pocket was found, but the live inventory could not be reached.";
            Summary.Text = $"{pocket.CoreCount} installed";
            CoreList.ItemsSource = pocket.InstalledCoreNames.Select(identifier => new CoreComparison(identifier, identifier, "Unknown", "Unknown", "—", true, false, "Unknown")).ToArray();
        }
    }
}
