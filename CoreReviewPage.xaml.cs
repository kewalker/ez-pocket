using EzPocket.Models;
using EzPocket.Services;

namespace EzPocket;

public partial class CoreReviewPage : ContentPage
{
    private readonly CoreSelectionService coreSelection;
    private readonly PocketSelectionService pocketSelection;
    private readonly PocketScanner scanner;
    private readonly CoreSyncService coreSync;
    private readonly PocketHealthService health;
    private CoreSyncPreview? preview;
    private bool hasPrepared;
    private bool returnsToDashboard;

    public CoreReviewPage()
    {
        InitializeComponent();
        coreSelection = IPlatformApplication.Current?.Services.GetService<CoreSelectionService>() ?? new CoreSelectionService();
        pocketSelection = IPlatformApplication.Current?.Services.GetService<PocketSelectionService>() ?? new PocketSelectionService();
        scanner = IPlatformApplication.Current?.Services.GetService<PocketScanner>() ?? new PocketScanner();
        coreSync = IPlatformApplication.Current?.Services.GetService<CoreSyncService>() ?? new CoreSyncService();
        health = IPlatformApplication.Current?.Services.GetService<PocketHealthService>() ?? new PocketHealthService();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        string? featuredSetMessage = coreSelection.ConsumeFeaturedSetSelectionMessage();
        returnsToDashboard = featuredSetMessage is not null;
        FeaturedSetSummary.IsVisible = featuredSetMessage is not null;
        if (featuredSetMessage is not null) FeaturedSetSummaryText.Text = featuredSetMessage;
        if (!hasPrepared)
        {
            hasPrepared = true;
            await PrepareAsync();
        }
    }

    protected override void OnDisappearing()
    {
        if (preview is { } stagedPreview)
            _ = Task.Run(() =>
            {
                try { coreSync.Cleanup(stagedPreview); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            });
        base.OnDisappearing();
    }

    private async void OnPrepareClicked(object? sender, EventArgs e)
    {
        await PrepareAsync();
    }

    private async Task PrepareAsync()
    {
        var pocket = pocketSelection.SelectedPocket;
        if (pocket is null)
        {
            SyncStatus.Text = "Select a Pocket before preparing a sync.";
            return;
        }
        if (preview is { } oldPreview) await Task.Run(() => coreSync.Cleanup(oldPreview));
        preview = null;
        PrepareButton.IsEnabled = false;
        PrepareButton.IsVisible = false;
        PreparingIndicator.IsVisible = true;
        PreparingIndicator.IsRunning = true;
        SyncButton.IsVisible = false;
        SyncButton.IsEnabled = true;
        ChangeList.IsVisible = false;
        ChangeDetails.IsVisible = false;
        OverrideSummary.IsVisible = false;
        SyncStatus.Text = "Preparing selected core packages and listing unselected inventory cores for removal. Cores not listed in the inventory are preserved. Downloads are size-limited, package contents are validated, and each download times out after 45 seconds with one retry.";
        try
        {
            IReadOnlyList<CoreComparison> coresToRemove = coreSelection.Cores
                .Where(core => core.IsInstalled && core.IsAvailable && !core.IsSelected)
                .ToArray();
            CoreSyncPreview preparedPreview = await Task.Run(() => coreSync.PrepareAsync(pocket, coreSelection.SelectedCores, coresToRemove));
            await Dispatcher.DispatchAsync(() =>
            {
                preview = preparedPreview;
                SelectedCoreList.ItemsSource = preview.Cores;
                SelectedCoreSummary.Text = preview.Cores.Count == 1 ? "1 core" : $"{preview.Cores.Count} cores";
                ChangeList.ItemsSource = preview.Changes;
                RemovalList.ItemsSource = preview.Removals;
                OverrideSummaryText.Text = preview.Overrides.Count == 1
                    ? $"1 shared-file override: {preview.Overrides[0].RelativePath}. {preview.Overrides[0].Summary}"
                    : $"{preview.Overrides.Count} shared-file overrides will use the first package's version in this plan.";
                OverrideSummary.IsVisible = preview.Overrides.Count > 0;
                AddSummary.Text = $"{preview.Cores.Count} core(s) \u00B7 {preview.AddOrReplaceFileCount} file(s) to add or replace";
                RemoveSummary.Text = $"{preview.Removals.Count} core(s) \u00B7 {preview.RemoveFileCount} file(s) to remove";
                ChangeList.IsVisible = preview.Changes.Count > 0;
                RemovalList.IsVisible = preview.Removals.Count > 0;
                ChangeDetails.IsVisible = preview.Changes.Count > 0 || preview.Removals.Count > 0;
                if (preview.Blockers.Count > 0)
                {
                    SyncStatus.Text = string.Join(Environment.NewLine, preview.Blockers);
                    return;
                }

                SyncButton.IsEnabled = preview.CanSync;
                SyncButton.IsVisible = preview.CanSync;
                SyncStatus.Text = preview.CanSync
                    ? "Review the planned additions and removals, then apply them."
                    : "No changes are planned. Select cores to keep, install, or update; unselect an installed core to remove it.";
            });
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.DispatchAsync(() =>
            {
                SyncStatus.Text = "Preparing the sync was cancelled.";
                PrepareButton.IsVisible = true;
            });
        }
        catch (Exception exception)
        {
            await Dispatcher.DispatchAsync(() =>
            {
                SyncStatus.Text = $"Could not prepare sync: {exception.Message}";
                PrepareButton.IsVisible = true;
            });
        }
        finally
        {
            await Dispatcher.DispatchAsync(() =>
            {
                PrepareButton.IsEnabled = true;
                PreparingIndicator.IsRunning = false;
                PreparingIndicator.IsVisible = false;
            });
        }
    }

    private async void OnSyncClicked(object? sender, EventArgs e)
    {
        if (preview is not { CanSync: true }) return;
        PocketDrive? pocket = pocketSelection.SelectedPocket;
        if (pocket is null || !string.Equals(pocket.RootPath, preview.PocketPath, StringComparison.OrdinalIgnoreCase) || !health.Inspect(pocket).CanWrite)
        {
            SyncStatus.Text = "SYNC BLOCKED · The selected target changed or is unavailable. Scan it again, then review a new plan.";
            return;
        }
        int coresToAdd = preview.Cores.Count(core => !core.IsInstalled);
        string summary = $"Apply changes on {preview.PocketPath}? {FormatCoreCount(coresToAdd)} will be added and {FormatCoreCount(preview.Removals.Count)} will be removed. Existing files affected by updates will be backed up first.";
        bool confirmed = await DisplayAlert("Apply core changes", summary, "Apply", "Cancel");
        if (!confirmed) return;

        SyncButton.IsEnabled = false;
        CoreSyncPreview activePreview = preview;
        CoreSyncResult result = await Task.Run(() => coreSync.ApplyAsync(activePreview));
        if (result.Succeeded)
        {
            PocketDrive? refreshedPocket = await Task.Run(() => scanner.ScanFolder(activePreview.PocketPath));
            await Dispatcher.DispatchAsync(() =>
            {
                if (refreshedPocket is not null) pocketSelection.Select(refreshedPocket);
                if (returnsToDashboard) coreSelection.ReportDashboardSuccess(result.Message);
                else coreSelection.ReportSuccessfulSync(result.Message);
            });
            await Dispatcher.DispatchAsync(() => AppNavigation.GoToAsync(".."));
            return;
        }
        await Dispatcher.DispatchAsync(() =>
        {
            SyncStatus.Text = result.Message;
            SyncButton.IsEnabled = true;
        });
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await AppNavigation.GoToAsync("..");
    }

    private async void OnManageCoresClicked(object? sender, EventArgs e)
    {
        await AppNavigation.GoToAsync("CorePage");
    }

    private async void OnHomeClicked(object? sender, EventArgs e)
    {
        await AppNavigation.GoToAsync("//MainPage");
    }

    private static string FormatCoreCount(int count) => count == 1 ? "1 core" : $"{count} cores";

    private static string FormatBytes(long bytes) => bytes < 1024 * 1024
        ? $"{Math.Max(1, bytes / 1024d):0.#} KB"
        : $"{bytes / 1024d / 1024d:0.#} MB";
}
