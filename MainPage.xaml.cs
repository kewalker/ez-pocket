using EzPocket.Models;
using EzPocket.Services;
using EzPocket.Controls;

namespace EzPocket;

public partial class MainPage : ContentPage
{
    private readonly PocketScanner scanner;
    private readonly PocketSelectionService selection;
    private readonly IFolderPickerService folderPicker;
    private readonly FeaturedCoreSetService featuredCoreSets;
    private readonly CoreInventoryService inventory;
    private readonly CoreSelectionService coreSelection;
    private readonly IAppDiagnostics diagnostics;
    private bool preparingFeaturedSet;
    private bool scanning;
    private int appearance;
    private List<PocketDrive> candidates = [];

    public MainPage()
    {
        InitializeComponent();
#if LINUX
        ConfigureLinuxCompactLayout();
#endif
        scanner = IPlatformApplication.Current?.Services.GetService<PocketScanner>() ?? new PocketScanner();
        selection = IPlatformApplication.Current?.Services.GetService<PocketSelectionService>() ?? new PocketSelectionService();
        folderPicker = IPlatformApplication.Current?.Services.GetService<IFolderPickerService>() ?? new UnsupportedFolderPickerService();
        featuredCoreSets = IPlatformApplication.Current?.Services.GetService<FeaturedCoreSetService>() ?? new FeaturedCoreSetService();
        inventory = IPlatformApplication.Current?.Services.GetService<CoreInventoryService>() ?? new CoreInventoryService();
        coreSelection = IPlatformApplication.Current?.Services.GetService<CoreSelectionService>() ?? new CoreSelectionService();
        diagnostics = IPlatformApplication.Current?.Services.GetService<IAppDiagnostics>() ?? NullAppDiagnostics.Instance;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        string? successMessage = coreSelection.ConsumeDashboardSuccessMessage();
        if (successMessage is not null) SuccessToast.ShowSuccess(successMessage);
        int currentAppearance = ++appearance;
        PocketDrive? pocket = selection.Restore();
        if (pocket is not null) ShowPocket(pocket);
        else ShowNoPocket();
        if (pocket is null) return;
        bool available = await Task.Run(() => Directory.Exists(pocket.RootPath));
        if (currentAppearance != appearance || !ReferenceEquals(selection.SelectedPocket, pocket)) return;
        if (!available)
        {
            selection.Disconnect();
            ShowNoPocket();
            StatusDetail.Text = "The previous target is unavailable. Reconnect it and scan again.";
        }
    }

    protected override void OnDisappearing()
    {
        appearance++;
        SuccessToast.Hide();
        base.OnDisappearing();
    }

    private async void OnScanClicked(object? sender, EventArgs e)
    {
        if (scanning) return;
        scanning = true;
        ScanButton.IsEnabled = ChangeTargetButton.IsEnabled = false;
        ScanButton.Text = "SCANNING…";
        try { await ScanTargetAsync(); }
        finally
        {
            scanning = false;
            ScanButton.IsEnabled = ChangeTargetButton.IsEnabled = true;
            ScanButton.Text = selection.SelectedPocket is null ? "SCAN TARGET" : "RESCAN";
        }
    }

    private async Task ScanTargetAsync()
    {
        int currentAppearance = appearance;
        candidates = (await Task.Run(scanner.Scan)).ToList();
        if (currentAppearance != appearance) return;
        var pocket = candidates.Count == 1
            ? candidates[0]
            : candidates.FirstOrDefault(candidate => string.Equals(candidate.RootPath, selection.SelectedPocket?.RootPath, StringComparison.OrdinalIgnoreCase));

        if (pocket is null && candidates.Count > 1)
        {
            string? choice = await DisplayActionSheet("Choose a Pocket", "Cancel", null, candidates.Select(candidate => candidate.DisplayName).ToArray());
            if (currentAppearance != appearance) return;
            pocket = candidates.FirstOrDefault(candidate => candidate.DisplayName == choice);
        }

        if (pocket is null)
        {
            selection.Disconnect();
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

    private async void OnQuickStartClicked(object? sender, EventArgs e) => await AppNavigation.GoToAsync("QuickStartPage");
    private async void OnPaletteStudioClicked(object? sender, EventArgs e) => await AppNavigation.GoToAsync("PaletteStudioPage");
    private async void OnPocketPluginsClicked(object? sender, EventArgs e) => await AppNavigation.GoToAsync("PocketPluginsPage");

    private async void OnNextStepClicked(object? sender, EventArgs e)
    {
        if (selection.SelectedPocket is null) await ChooseFolderAsync();
        else await AppNavigation.GoToAsync("CorePage");
    }

    private async void OnFirmwareClicked(object? sender, EventArgs e)
    {
        if (selection.SelectedPocket is null)
        {
            await DisplayAlert("Select a Pocket", "Choose or scan a Pocket before preparing a firmware update.", "Got it");
            return;
        }
        await AppNavigation.GoToAsync("FirmwarePage");
    }

    private async void OnAssetsClicked(object? sender, EventArgs e)
    {
        if (selection.SelectedPocket is null)
        {
            await ChooseFolderAsync();
            if (selection.SelectedPocket is null) return;
        }
        await AppNavigation.GoToAsync("AssetsPage");
    }

#if LINUX
    private void ConfigureLinuxCompactLayout()
    {
        SetGridShape(TargetHeroGrid, 3, 5);
        foreach (View child in TargetHeroGrid.Children.OfType<View>().Where(child => child is not EzButton))
            Grid.SetColumnSpan(child, 3);

        EzButton[] targetActions = [ScanButton, ChangeTargetButton, HealthButton];
        for (int column = 0; column < targetActions.Length; column++)
        {
            Grid.SetColumn(targetActions[column], column);
            Grid.SetRow(targetActions[column], 4);
            Grid.SetRowSpan(targetActions[column], 1);
            targetActions[column].HorizontalOptions = LayoutOptions.Fill;
        }

        StackCardAction(FirmwareCardGrid, FirmwareButton);
        StackCardAction(CoreCardGrid, NextStepButton);
        StackCardAction(SaveVaultCardGrid, SaveVaultButton);
        StackCardAction(AssetsCardGrid, AssetsButton);
        StackColumns(FeaturedSetupCards);
        StackColumns(TargetOverviewGrid);
        StackCardAction(DiagnosticsGrid, ExportDiagnosticsButton);
    }

    private static void StackCardAction(Grid grid, EzButton action)
    {
        SetGridShape(grid, 1, 2);
        Grid.SetColumn(action, 0);
        Grid.SetRow(action, 1);
        action.HorizontalOptions = LayoutOptions.Start;
        action.Margin = new Thickness(0, 10, 0, 0);
    }

    private static void StackColumns(Grid grid)
    {
        View[] children = grid.Children.OfType<View>().ToArray();
        SetGridShape(grid, 1, children.Length);
        grid.RowSpacing = 12;
        for (int row = 0; row < children.Length; row++)
        {
            Grid.SetColumn(children[row], 0);
            Grid.SetRow(children[row], row);
        }
    }

    private static void SetGridShape(Grid grid, int columns, int rows)
    {
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        for (int column = 0; column < columns; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        for (int row = 0; row < rows; row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    }
#endif

    private async void OnSaveVaultClicked(object? sender, EventArgs e)
    {
        if (selection.SelectedPocket is not null) await AppNavigation.GoToAsync("SaveVaultPage");
    }

    private async void OnHealthClicked(object? sender, EventArgs e)
    {
        if (selection.SelectedPocket is not null) await AppNavigation.GoToAsync("PocketHealthPage");
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
        if (sender is VisualElement { StyleId: string id }) await OpenFeaturedSetAsync(id);
    }

    private async void OnFeaturedSetClicked(object? sender, EventArgs e)
    {
        if (sender is VisualElement { StyleId: string id }) await OpenFeaturedSetAsync(id);
    }

    private async Task OpenFeaturedSetAsync(string id)
    {
        if (preparingFeaturedSet) return;
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
            PocketDrive? refreshed = await Task.Run(() => scanner.ScanFolder(pocket.RootPath));
            if (refreshed is null)
            {
                FeaturedSetupStatus.Text = "Target unavailable. Reconnect it and scan again.";
                return;
            }
            if (!ReferenceEquals(selection.SelectedPocket, pocket)) return;
            pocket = refreshed;
            selection.Select(pocket);
            IReadOnlyList<AvailableCore> available = await inventory.GetAvailableAsync();
            IReadOnlyList<CoreComparison> comparison = await Task.Run(() => CoreInventoryService.Compare(pocket, available));
            FeaturedCoreSetSelection? featuredSelection = null;
            await Dispatcher.DispatchAsync(() =>
            {
                coreSelection.BeginManageSession();
                coreSelection.InitializeForPocket(pocket, comparison);
                featuredSelection = featuredCoreSets.ApplyPendingSelection(coreSelection, comparison);
                if (featuredSelection is not null) coreSelection.ReportFeaturedSetSelection(featuredSelection.Summary);
            });
            if (featuredSelection is null) return;
            await Dispatcher.DispatchAsync(() => AppNavigation.GoToAsync("CoreReviewPage"));
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or OperationCanceledException)
        {
            await Dispatcher.DispatchAsync(async () =>
            {
                FeaturedSetupStatus.Text = "Could not load the live inventory.";
                await DisplayAlert("Could not load setup", "Connect to the internet and try again. No changes were made.", "Got it");
            });
        }
        finally
        {
            await Dispatcher.DispatchAsync(() =>
            {
                preparingFeaturedSet = false;
                FeaturedSetupCards.IsEnabled = true;
            });
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

        int currentAppearance = appearance;
        PocketDrive? pocket = await Task.Run(() => scanner.ScanFolder(path));
        if (currentAppearance != appearance) return;
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
        bool scanned = selection.LastScannedAt is not null;
        StatusTitle.Text = !scanned || pocket.LooksLikePocket ? pocket.Name : "New Pocket target";
        StatusDetail.Text = pocket.LooksLikePocket
            ? $"{pocket.RootPath} \u00B7 {pocket.CapacitySummary}"
            : $"{pocket.RootPath} | Blank folder target | {pocket.CapacitySummary}";
        if (!scanned) StatusDetail.Text = $"{pocket.RootPath} · Rescan to refresh target details.";
        ReadyBadgeText.Text = "SELECTED";
        ScanButton.Text = "RESCAN";
        NextStepEyebrow.Text = "CORE INVENTORY";
        NextStepTitle.Text = "Review your cores";
        NextStepDetail.Text = "Compare this target with the live inventory. Nothing changes until you approve it.";
        NextStepButton.Text = "MANAGE CORES";
        CoreCountValue.Text = scanned ? pocket.CoreCount.ToString() : "—";
        CoreCountDetail.Text = selection.LastScannedAt is { } scannedAt ? $"Last scanned {scannedAt.ToLocalTime():MMM d, h:mm tt}" : "Rescan to inspect cores";
        TargetValue.Text = !scanned || pocket.LooksLikePocket ? pocket.Name : "New target";
        TargetDetail.Text = pocket.CapacitySummary;
        FirmwareButton.IsEnabled = true;
        AssetsButton.IsEnabled = true;
        SaveVaultButton.IsEnabled = true;
        HealthButton.IsEnabled = true;
        PocketHealthReport? healthReport = selection.LastHealthReport;
        HealthStatusMarker.Color = healthReport?.IsReady == true ? Color.FromArgb("#25B46B") : Color.FromArgb("#D2D2CC");
        HealthStatusText.Text = healthReport is null ? "HEALTH · NOT CHECKED" : $"HEALTH · {(healthReport.IsReady ? "READY" : $"{healthReport.AttentionCount} ATTENTION")} · Last checked {selection.LastHealthCheckedAt?.ToLocalTime():MMM d, h:mm tt}";
        SaveVaultActionTitle.Text = "Protect saves and memories";
        SaveVaultActionDetail.Text = "Create local snapshots and restore selected saves through a review.";
        AssetsActionTitle.Text = "Palettes and optional core files";
        AssetsActionDetail.Text = "Inspect and import shared Game Boy palettes now. Library art and other verified asset types stay opt-in.";
        FirmwareActionTitle.Text = "POCKETOS FIRMWARE";
        FirmwareActionDetail.Text = "Open firmware and choose CHECK FIRMWARE to check the official release.";
        FirmwareButton.Text = "VIEW FIRMWARE";
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
        AssetsButton.IsEnabled = false;
        SaveVaultButton.IsEnabled = false;
        HealthButton.IsEnabled = false;
        HealthStatusMarker.Color = Color.FromArgb("#AFAFAA");
        HealthStatusText.Text = "HEALTH · UNAVAILABLE";
        SaveVaultActionTitle.Text = "Protect saves and memories";
        SaveVaultActionDetail.Text = "Select a target to inspect and snapshot its Saves and Memories locally.";
        AssetsActionTitle.Text = "Palettes and optional core files";
        AssetsActionDetail.Text = "Select a target to inspect shared palettes and other verified, opt-in core assets.";
        FirmwareActionTitle.Text = "POCKETOS FIRMWARE";
        FirmwareActionDetail.Text = "Select a target to check the latest official firmware.";
        FirmwareButton.Text = "VIEW FIRMWARE";
    }

    private async void OnManageCoresClicked(object? sender, EventArgs e)
    {
        await AppNavigation.GoToAsync("CorePage");
    }
}
