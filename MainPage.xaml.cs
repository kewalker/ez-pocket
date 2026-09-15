using EzPocket.Models;
using EzPocket.Services;

namespace EzPocket;

public partial class MainPage : ContentPage
{
    private readonly PocketScanner scanner;
    private readonly PocketSelectionService selection;
    private readonly IFolderPickerService folderPicker;
    private List<PocketDrive> candidates = [];

    public MainPage()
    {
        InitializeComponent();
        scanner = IPlatformApplication.Current?.Services.GetService<PocketScanner>() ?? new PocketScanner();
        selection = IPlatformApplication.Current?.Services.GetService<PocketSelectionService>() ?? new PocketSelectionService();
        folderPicker = IPlatformApplication.Current?.Services.GetService<IFolderPickerService>() ?? new UnsupportedFolderPickerService();
    }

    private async void OnScanClicked(object? sender, EventArgs e)
    {
        candidates = scanner.Scan().ToList();
        var pocket = candidates.Count == 1
            ? candidates[0]
            : candidates.FirstOrDefault(candidate => string.Equals(candidate.RootPath, selection.SelectedPocket?.RootPath, StringComparison.OrdinalIgnoreCase));

        if (pocket is null && candidates.Count > 1)
        {
            string? choice = await DisplayActionSheet("Choose a Pocket", "Cancel", null, candidates.Select(candidate => candidate.DisplayName).ToArray());
            pocket = candidates.FirstOrDefault(candidate => candidate.DisplayName == choice);
        }

        if (pocket is null)
        {
            StatusTitle.Text = "No Pocket connected";
            StatusDetail.Text = "Connect an SD card or USB Pocket to begin.";
            ReadyBadgeText.Text = "Ready";
            string message = candidates.Count > 1
                ? "More than one Pocket-like drive was found. Choose one from the list."
                : "Connect an SD card or Pocket over USB, or choose its folder manually.";
            await DisplayAlert(candidates.Count > 1 ? "Choose a Pocket" : "No Pocket found", message, "Got it");
            return;
        }

        selection.Select(pocket);
        ShowPocket(pocket);
    }

    private async void OnChooseFolderClicked(object? sender, EventArgs e)
    {
        string? path = await folderPicker.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(path)) return;

        PocketDrive? pocket = scanner.ScanFolder(path);
        if (pocket is null)
        {
            await DisplayAlert("Folder unavailable", "The selected folder could not be opened.", "Got it");
            return;
        }

        if (!pocket.LooksLikePocket)
        {
            bool useAsNewPocket = await DisplayAlert(
                "Use as a new Pocket",
                $"{pocket.RootPath} does not look like an existing Pocket. Use it as a blank Pocket target? It will not be changed until you sync content.",
                "Use folder", "Cancel");
            if (!useAsNewPocket) return;
        }

        selection.Select(pocket);
        candidates = [pocket];
        ShowPocket(pocket);
    }

    private void ShowPocket(PocketDrive pocket)
    {
        StatusTitle.Text = pocket.LooksLikePocket ? pocket.Name : "New Pocket target";
        StatusDetail.Text = pocket.LooksLikePocket
            ? $"{pocket.RootPath} · {pocket.CapacitySummary}"
            : $"{pocket.RootPath} | Blank folder target | {pocket.CapacitySummary}";
        ReadyBadgeText.Text = pocket.LooksLikePocket ? "Connected" : "New target";
        ScanButton.Text = "Rescan";
        // The Core Manager owns the detailed installed/available comparison.
    }

    private async void OnManageCoresClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("CorePage");
    }
}
