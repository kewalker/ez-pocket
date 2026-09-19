using EzPocket.Models;
using EzPocket.Services;

namespace EzPocket;

public partial class MainPage : ContentPage
{
    private readonly PocketScanner scanner;
    private readonly PocketSelectionService selection;
    private readonly IFolderPickerService folderPicker;
    private readonly FirmwareUpdateService firmware;
    private readonly FeaturedCoreSetService featuredCoreSets;
    private readonly CoreInventoryService inventory;
    private readonly CoreSelectionService coreSelection;
    private readonly IAppDiagnostics diagnostics;
    private bool preparingFeaturedSet;
    private List<PocketDrive> candidates = [];

    public MainPage()
    {
        InitializeComponent();
        scanner = IPlatformApplication.Current?.Services.GetService<PocketScanner>() ?? new PocketScanner();
        selection = IPlatformApplication.Current?.Services.GetService<PocketSelectionService>() ?? new PocketSelectionService();
        folderPicker = IPlatformApplication.Current?.Services.GetService<IFolderPickerService>() ?? new UnsupportedFolderPickerService();
        firmware = IPlatformApplication.Current?.Services.GetService<FirmwareUpdateService>() ?? new FirmwareUpdateService();
        featuredCoreSets = IPlatformApplication.Current?.Services.GetService<FeaturedCoreSetService>() ?? new FeaturedCoreSetService();
        inventory = IPlatformApplication.Current?.Services.GetService<CoreInventoryService>() ?? new CoreInventoryService();
        coreSelection = IPlatformApplication.Current?.Services.GetService<CoreSelectionService>() ?? new CoreSelectionService();
        diagnostics = IPlatformApplication.Current?.Services.GetService<IAppDiagnostics>() ?? NullAppDiagnostics.Instance;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        string? successMessage = coreSelection.ConsumeDashboardSuccessMessage();
        if (successMessage is not null) SuccessToast.ShowSuccess(successMessage);
        PocketDrive? pocket = selection.Restore(scanner);
        if (pocket is not null) ShowPocket(pocket);
        else ShowNoPocket();
    }

    protected override void OnDisappearing()
    {
        SuccessToast.Hide();
        base.OnDisappearing();
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

    private async void OnExportDiagnosticsClicked(object? sender, EventArgs e)
    {
        ExportDiagnosticsButton.IsEnabled = false;
        DiagnosticsStatus.Text = "Preparing a local diagnostics bundle\u2026";
        try
        {
            string bundlePath = await diagnostics.CreateBundleAsync();
            DiagnosticsStatus.Text = $"Diagnostics bundle created: {bundlePath}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            DiagnosticsStatus.Text = "Could not create a diagnostics bundle.";
        }
        finally
        {
            ExportDiagnosticsButton.IsEnabled = true;
        }
    }

    private async void OnFeaturedSetTapped(object? sender, TappedEventArgs e)
    {
        if (preparingFeaturedSet || sender is not VisualElement { StyleId: string id }) return;
        if (selection.SelectedPocket is null)
        {
            await DisplayAlert("Select a Pocket", "Choose or scan a Pocket before reviewing a featured setup.", "Got it");
            return;
        }
        if (!featuredCoreSets.Select(id)) return;

        preparingFeaturedSet = true;
        FeaturedSetupCards.IsEnabled = false;
        FeaturedSetupStatus.Text = "Loading the current core lineup\u2026";
        FeaturedSetupStatus.IsVisible = true;
        try
        {
            PocketDrive pocket = selection.SelectedPocket;
            IReadOnlyList<AvailableCore> available = await inventory.GetAvailableAsync();
            IReadOnlyList<CoreComparison> comparison = CoreInventoryService.Compare(pocket, available);
            coreSelection.InitializeForPocket(pocket, comparison);
            FeaturedCoreSetSelection? featuredSelection = featuredCoreSets.ApplyPendingSelection(coreSelection, comparison);
            if (featuredSelection is null) return;
            coreSelection.ReportFeaturedSetSelection(featuredSelection.Summary);
            await Shell.Current.GoToAsync("CoreReviewPage");
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or OperationCanceledException)
        {
            FeaturedSetupStatus.Text = "Could not load the live inventory.";
            await DisplayAlert("Could not load setup", "Connect to the internet and try again. No changes were made.", "Got it");
        }
        finally
        {
            preparingFeaturedSet = false;
            FeaturedSetupCards.IsEnabled = true;
        }
    }

    private void OnFeaturedSetPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Border card)
        {
            card.BackgroundColor = Color.FromArgb("#F4F4F0");
            card.Stroke = Color.FromArgb("#151515");
        }
    }

    private void OnFeaturedSetPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Border card)
        {
            card.BackgroundColor = Colors.White;
            card.Stroke = Color.FromArgb("#151515");
        }
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
            ? $"{pocket.RootPath} \u00B7 {pocket.CapacitySummary}"
            : $"{pocket.RootPath} | Blank folder target | {pocket.CapacitySummary}";
        ReadyBadgeText.Text = pocket.LooksLikePocket ? "CONNECTED" : "NEW TARGET";
        ScanButton.Text = "RESCAN";
        NextStepEyebrow.Text = "CORE INVENTORY";
        NextStepTitle.Text = "Review your cores";
        NextStepDetail.Text = "Compare this target with the live inventory. Nothing changes until you approve it.";
        NextStepButton.Text = "MANAGE CORES";
        CoreCountValue.Text = pocket.CoreCount.ToString();
        CoreCountDetail.Text = pocket.CoreCount == 1 ? "core installed" : "cores installed";
        TargetValue.Text = pocket.LooksLikePocket ? pocket.Name : "New target";
        TargetDetail.Text = pocket.CapacitySummary;
        FirmwareButton.IsEnabled = true;
        FirmwareActionTitle.Text = "Checking Pocket firmware\u2026";
        FirmwareActionDetail.Text = "Checking the official release and this target's staged firmware.";
        FirmwareButton.Text = "Checking\u2026";
        _ = CheckFirmwareAsync(pocket);
    }

    private void ShowNoPocket()
    {
        StatusTitle.Text = "No Pocket selected";
        StatusDetail.Text = "Connect an SD card or USB Pocket, or choose a folder target to begin.";
        ReadyBadgeText.Text = "READY";
        ScanButton.Text = "SCAN TARGET";
        NextStepEyebrow.Text = "CORE INVENTORY";
        NextStepTitle.Text = "Review your cores";
        NextStepDetail.Text = "Choose a target first. Nothing changes until you approve it.";
        NextStepButton.Text = "CHOOSE TARGET";
        CoreCountValue.Text = "\u2014";
        CoreCountDetail.Text = "Select a Pocket first";
        TargetValue.Text = "None";
        TargetDetail.Text = "No folder selected";
        FirmwareButton.IsEnabled = false;
        FirmwareActionTitle.Text = "POCKETOS FIRMWARE";
        FirmwareActionDetail.Text = "Select a target to check the latest official firmware.";
        FirmwareButton.Text = "VIEW FIRMWARE";
    }

    private async Task CheckFirmwareAsync(PocketDrive pocket)
    {
        try
        {
            FirmwareTargetCheck check = await firmware.CheckTargetAsync(pocket);
            if (!string.Equals(selection.SelectedPocket?.RootPath, pocket.RootPath, StringComparison.OrdinalIgnoreCase)) return;

            if (check.IsLatestFirmwareStaged)
            {
                FirmwareActionTitle.Text = $"FIRMWARE {check.Release.Version} IS STAGED";
                FirmwareActionDetail.Text = "The latest official firmware is verified on this target. Installed PocketOS version cannot be read from storage.";
                FirmwareButton.Text = "VIEW FIRMWARE";
            }
            else
            {
                FirmwareActionTitle.Text = $"FIRMWARE {check.Release.Version} IS AVAILABLE";
                FirmwareActionDetail.Text = check.ExistingFirmwareFiles.Count == 0
                    ? "No verified latest firmware is staged on this target."
                    : "An older or unverified firmware file is staged and can be safely replaced.";
                FirmwareButton.Text = "STAGE FIRMWARE";
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
        {
            if (!string.Equals(selection.SelectedPocket?.RootPath, pocket.RootPath, StringComparison.OrdinalIgnoreCase)) return;
            FirmwareActionTitle.Text = "FIRMWARE CHECK UNAVAILABLE";
            FirmwareActionDetail.Text = "Check your connection, then retry from the firmware page.";
            FirmwareButton.Text = "CHECK FIRMWARE";
        }
    }

    private async void OnManageCoresClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("CorePage");
    }
}
