using EzPocket.Services;

namespace EzPocket;

public partial class MainPage : ContentPage
{
    private readonly PocketScanner scanner;
    private readonly CoreInventoryService inventory;

    public MainPage()
    {
        InitializeComponent();
        scanner = IPlatformApplication.Current?.Services.GetService<PocketScanner>() ?? new PocketScanner();
        inventory = IPlatformApplication.Current?.Services.GetService<CoreInventoryService>() ?? new CoreInventoryService();
    }

    private void OnScanPointerEntered(object? sender, PointerEventArgs e)
    {
        ScanButton.Scale = 1.02;
        ScanButton.Opacity = 0.9;
    }

    private void OnScanPointerExited(object? sender, PointerEventArgs e)
    {
        ResetScanButton();
    }

    private void OnScanPressed(object? sender, EventArgs e)
    {
        ScanButton.Scale = 0.96;
        ScanButton.Opacity = 0.75;
    }

    private void OnScanReleased(object? sender, EventArgs e)
    {
        ScanButton.Scale = 1.02;
        ScanButton.Opacity = 0.9;
    }

    private void ResetScanButton()
    {
        ScanButton.Scale = 1;
        ScanButton.Opacity = 1;
    }

    private async void OnScanClicked(object? sender, EventArgs e)
    {
        var drives = scanner.Scan();
        var pocket = drives.FirstOrDefault(drive => drive.LooksLikePocket) ?? drives.FirstOrDefault();

        if (pocket is null)
        {
            StatusTitle.Text = "No Pocket connected";
            StatusDetail.Text = "Connect an SD card or USB Pocket to begin.";
            ReadyBadgeText.Text = "Ready";
            await DisplayAlert("No Pocket found", "Connect an SD card or Pocket over USB, then scan again.", "Got it");
            return;
        }

        StatusTitle.Text = pocket.Name;
        StatusDetail.Text = $"{pocket.RootPath} · {pocket.CapacitySummary}";
        ReadyBadgeText.Text = "Connected";
        ScanButton.Text = "Rescan";
        IReadOnlyList<Models.AvailableCore> available;
        try
        {
            available = await inventory.GetAvailableAsync();
        }
        catch (HttpRequestException)
        {
            await DisplayAlert("Pocket found", $"{pocket.Name} ({pocket.RootPath})\n{pocket.CapacitySummary}\n\nCores installed: {pocket.CoreCount}\n\nThe live core inventory could not be reached.", "Continue");
            return;
        }

        var installedPreview = pocket.InstalledCoreNames.Count == 0 ? "None" : string.Join(", ", pocket.InstalledCoreNames.Take(8));
        if (pocket.InstalledCoreNames.Count > 8) installedPreview += ", …";
        await DisplayAlert("Pocket found", $"{pocket.Name} ({pocket.RootPath})\n{pocket.CapacitySummary}\n\nInstalled: {pocket.CoreCount} of {available.Count} available\n\nInstalled cores: {installedPreview}\n\nRecognized folders: {string.Join(", ", pocket.FoundFolders)}", "Continue");
    }
}
