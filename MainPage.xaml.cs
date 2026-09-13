using EzPocket.Services;

namespace EzPocket;

public partial class MainPage : ContentPage
{
    private readonly PocketScanner scanner;

    public MainPage()
    {
        InitializeComponent();
        scanner = IPlatformApplication.Current?.Services.GetService<PocketScanner>() ?? new PocketScanner();
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
            await DisplayAlert("No Pocket found", "Connect an SD card or Pocket over USB, then scan again.", "Got it");
            return;
        }

        string folders = pocket.FoundFolders.Count == 0 ? "No Pocket folders recognized yet." : string.Join(", ", pocket.FoundFolders);
        await DisplayAlert("Pocket found", $"{pocket.Name} ({pocket.RootPath})\n{pocket.CapacitySummary}\n\nRecognized: {folders}", "Continue");
    }
}
