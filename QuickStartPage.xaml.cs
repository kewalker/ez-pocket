using EzPocket.Models;
using EzPocket.Services;

namespace EzPocket;

/// <summary>An optional, read-only guide into the existing core management workflow.</summary>
public partial class QuickStartPage : ContentPage
{
    private readonly PocketScanner scanner;
    private readonly PocketSelectionService selection;
    private readonly IFolderPickerService folderPicker;
    private readonly FeaturedCoreSetService featuredCoreSets;
    private PocketDrive? currentTarget;
    private bool busy;
    private bool isActive;

    public QuickStartPage()
    {
        InitializeComponent();
        var services = IPlatformApplication.Current?.Services;
        scanner = services?.GetService<PocketScanner>() ?? new PocketScanner();
        selection = services?.GetService<PocketSelectionService>() ?? new PocketSelectionService();
        folderPicker = services?.GetService<IFolderPickerService>() ?? new UnsupportedFolderPickerService();
        featuredCoreSets = services?.GetService<FeaturedCoreSetService>() ?? new FeaturedCoreSetService();
        DetectedTargets.ItemDisplayBinding = new Binding(nameof(PocketDrive.DisplayName));
        StarterCollection.ItemDisplayBinding = new Binding(nameof(FeaturedCoreSet.Name));
        StarterCollection.ItemsSource = featuredCoreSets.Sets.ToList();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        isActive = true;
        PocketDrive? saved = selection.Restore(scanner);
        ShowTarget(saved is null ? null : scanner.ScanFolder(saved.RootPath));
    }

    protected override void OnDisappearing()
    {
        isActive = false;
        base.OnDisappearing();
    }

    private async void OnBackClicked(object? sender, EventArgs e) => await AppNavigation.GoToAsync("//MainPage");

    private async void OnScanClicked(object? sender, EventArgs e)
    {
        if (busy) return;
        SetBusy(true);
        ScanStatus.Text = "Scanning connected storage…";
        try
        {
            IReadOnlyList<PocketDrive> targets = await Task.Run(scanner.Scan);
            if (!isActive) return;
            DetectedTargets.ItemsSource = null;
            DetectedTargets.IsVisible = targets.Count > 1;
            if (targets.Count == 1)
            {
                SelectTarget(targets[0]);
            }
            else if (targets.Count > 1)
            {
                DetectedTargets.ItemsSource = targets.ToList();
                ShowTarget(null);
                ScanStatus.Text = "More than one target was found. Choose the one you want to manage.";
            }
            else
            {
                // Folder targets may not be removable drives. Keep one only if it still exists.
                ShowTarget(currentTarget is null ? null : scanner.ScanFolder(currentTarget.RootPath));
                ScanStatus.Text = "No Pocket-like removable drive found. Check the connection or choose its root folder manually.";
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnDetectedTargetChanged(object? sender, EventArgs e)
    {
        if (DetectedTargets.SelectedItem is PocketDrive target) SelectTarget(target);
    }

    private async void OnChooseFolderClicked(object? sender, EventArgs e)
    {
        if (busy) return;
        SetBusy(true);
        try
        {
            string? path = await folderPicker.PickFolderAsync();
            if (!isActive || string.IsNullOrWhiteSpace(path)) return;
            PocketDrive? target = await Task.Run(() => scanner.ScanFolder(path));
            if (!isActive) return;
            if (target is null)
            {
                ScanStatus.Text = "That folder is unavailable. Reconnect the storage or choose another folder.";
                return;
            }
            if (!target.LooksLikePocket && !await DisplayAlertAsync("Use as a new Pocket",
                $"{target.RootPath} does not look like an existing Pocket. Use it as a new target? Choosing it will not change its files.",
                "Use folder", "Cancel")) return;
            if (!isActive) return;
            DetectedTargets.IsVisible = false;
            SelectTarget(target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ScanStatus.Text = "Could not open the folder picker or read that folder. Try scanning or choosing a folder again.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SelectTarget(PocketDrive target)
    {
        selection.Select(target);
        ShowTarget(target);
        ScanStatus.Text = "Target selected and scanned. No files were changed.";
    }

    private void ShowTarget(PocketDrive? target)
    {
        currentTarget = target;
        TargetName.Text = target?.Name ?? "No target selected";
        TargetPath.Text = target?.RootPath ?? "Connect your storage and scan, or choose a folder above.";
        TargetSummary.IsVisible = target is not null;
        TargetSummary.Text = target is null ? string.Empty
            : $"{target.CoreCount} core{(target.CoreCount == 1 ? string.Empty : "s")} installed · {target.PocketFolderCount}/4 Pocket folders · {target.CapacitySummary}";
        UpdateActions();
    }

    private void OnCollectionChanged(object? sender, EventArgs e)
    {
        FeaturedCoreSet? collection = StarterCollection.SelectedItem as FeaturedCoreSet;
        CollectionDescription.IsVisible = collection is not null;
        CollectionDescription.Text = collection?.Description;
        UpdateActions();
    }

    private async void OnManageCoresClicked(object? sender, EventArgs e) => await OpenCoresAsync(false);

    private async void OnBrowseCollectionClicked(object? sender, EventArgs e) => await OpenCoresAsync(true);

    private async Task OpenCoresAsync(bool useCollection)
    {
        if (busy || currentTarget is null) return;
        SetBusy(true);
        try
        {
            PocketDrive? refreshed = await Task.Run(() => scanner.ScanFolder(currentTarget.RootPath));
            if (!isActive) return;
            if (refreshed is null)
            {
                ShowTarget(null);
                ScanStatus.Text = "The target is unavailable. Reconnect it and scan again before continuing.";
                return;
            }
            selection.Select(refreshed);
            ShowTarget(refreshed);
            if (useCollection && (StarterCollection.SelectedItem is not FeaturedCoreSet collection
                || !featuredCoreSets.Select(collection.Id))) return;
            await AppNavigation.GoToAsync("CorePage");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool value)
    {
        busy = value;
        GuideActions.IsEnabled = !value;
        ScanTargetButton.IsBusy = value;
        ScanTargetButton.BusyIndicatorColor = Colors.White;
        UpdateActions();
    }

    private void UpdateActions()
    {
        ManageCoresButton.IsEnabled = !busy && currentTarget is not null;
        BrowseCollectionButton.IsEnabled = !busy && currentTarget is not null && StarterCollection.SelectedItem is FeaturedCoreSet;
        ActionHint.Text = currentTarget is null
            ? "Select a target above to enable these actions. The live inventory needs an internet connection."
            : "The live inventory needs an internet connection. Your selection is reviewed before any files change.";
    }
}
