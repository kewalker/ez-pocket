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
        // The Core Manager owns the detailed installed/available comparison.
    }

    private async void OnManageCoresClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("CorePage");
    }
}
