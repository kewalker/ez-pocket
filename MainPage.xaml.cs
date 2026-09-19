using EzPocket.Models;
using EzPocket.Services;

namespace EzPocket;

public partial class MainPage : ContentPage
{
    private readonly PocketScanner scanner;
    private readonly PocketSelectionService selection;
    private readonly IFolderPickerService folderPicker;
    private readonly FirmwareUpdateService firmware;
    private List<PocketDrive> candidates = [];

    public MainPage()
    {
        InitializeComponent();
        scanner = IPlatformApplication.Current?.Services.GetService<PocketScanner>() ?? new PocketScanner();
        selection = IPlatformApplication.Current?.Services.GetService<PocketSelectionService>() ?? new PocketSelectionService();
        folderPicker = IPlatformApplication.Current?.Services.GetService<IFolderPickerService>() ?? new UnsupportedFolderPickerService();
        firmware = IPlatformApplication.Current?.Services.GetService<FirmwareUpdateService>() ?? new FirmwareUpdateService();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PocketDrive? pocket = selection.Restore(scanner);
        if (pocket is not null) ShowPocket(pocket);
        else ShowNoPocket();
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
            ShowNoPocket();
            string message = candidates.Count > 1
                ? "More than one Pocket-like drive was found. Choose one from the list."
                : "Connect an SD card or Pocket over USB, or choose its folder manually.";
            await DisplayAlert(candidates.Count > 1 ? "Choose a Pocket" : "No Pocket found", message, "Got it");
            return;
        }

        selection.Select(pocket);
        ShowPocket(pocket);
    }

    private async void OnChooseFolderClicked(object? sender, EventArgs e) => await ChooseFolderAsync();

    private async void OnNextStepClicked(object? sender, EventArgs e)
    {
        if (selection.SelectedPocket is null) await ChooseFolderAsync();
        else await Shell.Current.GoToAsync("CorePage");
    }

    private async void OnFirmwareClicked(object? sender, EventArgs e)
    {
        if (selection.SelectedPocket is null)
        {
            await DisplayAlert("Select a Pocket", "Choose or scan a Pocket before preparing a firmware update.", "Got it");
            return;
        }
        await Shell.Current.GoToAsync("FirmwarePage");
    }

    private async Task ChooseFolderAsync()
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
        NextStepEyebrow.Text = "NEXT STEP";
        NextStepTitle.Text = "Manage your cores";
        NextStepDetail.Text = "Compare installed cores with the live inventory, then prepare a safe sync.";
        NextStepButton.Text = "Manage cores";
        CoreCountValue.Text = pocket.CoreCount.ToString();
        CoreCountDetail.Text = pocket.CoreCount == 1 ? "core installed" : "cores installed";
        TargetValue.Text = pocket.LooksLikePocket ? pocket.Name : "New target";
        TargetDetail.Text = pocket.CapacitySummary;
        FirmwareButton.IsEnabled = true;
        FirmwareActionTitle.Text = "Checking Pocket firmware…";
        FirmwareActionDetail.Text = "Checking the official release and this target's staged firmware.";
        FirmwareButton.Text = "Checking…";
        _ = CheckFirmwareAsync(pocket);
    }

    private void ShowNoPocket()
    {
        StatusTitle.Text = "No Pocket selected";
        StatusDetail.Text = "Connect an SD card or USB Pocket, or choose a folder target to begin.";
        ReadyBadgeText.Text = "Ready";
        ScanButton.Text = "Scan";
        NextStepEyebrow.Text = "GET STARTED";
        NextStepTitle.Text = "Select your Pocket";
        NextStepDetail.Text = "Choose an SD card, connected Pocket, or a folder target. Nothing will be changed.";
        NextStepButton.Text = "Choose folder";
        CoreCountValue.Text = "—";
        CoreCountDetail.Text = "Select a Pocket first";
        TargetValue.Text = "None";
        TargetDetail.Text = "No folder selected";
        FirmwareButton.IsEnabled = false;
        FirmwareActionTitle.Text = "Update Pocket firmware";
        FirmwareActionDetail.Text = "Select a target to check the latest official firmware.";
        FirmwareButton.Text = "Update firmware";
    }

    private async Task CheckFirmwareAsync(PocketDrive pocket)
    {
        try
        {
            FirmwareTargetCheck check = await firmware.CheckTargetAsync(pocket);
            if (!string.Equals(selection.SelectedPocket?.RootPath, pocket.RootPath, StringComparison.OrdinalIgnoreCase)) return;

            if (check.IsLatestFirmwareStaged)
            {
                FirmwareActionTitle.Text = $"Firmware {check.Release.Version} is staged";
                FirmwareActionDetail.Text = "The latest official firmware is verified on this target. Installed PocketOS version cannot be read from storage.";
                FirmwareButton.Text = "View firmware";
            }
            else
            {
                FirmwareActionTitle.Text = $"Firmware {check.Release.Version} is available";
                FirmwareActionDetail.Text = check.ExistingFirmwareFiles.Count == 0
                    ? "No verified latest firmware is staged on this target."
                    : "An older or unverified firmware file is staged and can be safely replaced.";
                FirmwareButton.Text = "Stage firmware";
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
        {
            if (!string.Equals(selection.SelectedPocket?.RootPath, pocket.RootPath, StringComparison.OrdinalIgnoreCase)) return;
            FirmwareActionTitle.Text = "Could not check firmware";
            FirmwareActionDetail.Text = "Check your connection, then retry from the firmware page.";
            FirmwareButton.Text = "Check firmware";
        }
    }

    private async void OnManageCoresClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("CorePage");
    }
}
