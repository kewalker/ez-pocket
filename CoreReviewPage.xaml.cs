using EzPocket.Models;
using EzPocket.Services;

namespace EzPocket;

public partial class CoreReviewPage : ContentPage
{
    private readonly CoreSelectionService coreSelection;
    private readonly PocketSelectionService pocketSelection;
    private readonly PocketScanner scanner;
    private readonly CoreSyncService coreSync;
    private CoreSyncPreview? preview;
    private bool hasPrepared;

    public CoreReviewPage()
    {
        InitializeComponent();
        coreSelection = IPlatformApplication.Current?.Services.GetService<CoreSelectionService>() ?? new CoreSelectionService();
        pocketSelection = IPlatformApplication.Current?.Services.GetService<PocketSelectionService>() ?? new PocketSelectionService();
        scanner = IPlatformApplication.Current?.Services.GetService<PocketScanner>() ?? new PocketScanner();
        coreSync = IPlatformApplication.Current?.Services.GetService<CoreSyncService>() ?? new CoreSyncService();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!hasPrepared)
        {
            hasPrepared = true;
            await PrepareAsync();
        }
    }

    protected override void OnDisappearing()
    {
        if (preview is not null) coreSync.Cleanup(preview);
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
        if (preview is not null) coreSync.Cleanup(preview);
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
        SyncStatus.Text = "Preparing selected core packages. Each download times out after 45 seconds and retries once.";
        try
        {
            IReadOnlyList<CoreComparison> coresToRemove = coreSelection.Cores
                .Where(core => core.IsInstalled && !core.IsSelected)
                .ToArray();
            preview = await coreSync.PrepareAsync(pocket, coreSelection.SelectedCores, coresToRemove);
            ChangeList.ItemsSource = preview.Changes;
            RemovalList.ItemsSource = preview.Removals;
            OverrideSummaryText.Text = preview.Overrides.Count == 1
                ? $"1 shared-file override: {preview.Overrides[0].RelativePath}. {preview.Overrides[0].Summary}"
                : $"{preview.Overrides.Count} shared-file overrides will use the first package's version in this plan.";
            OverrideSummary.IsVisible = preview.Overrides.Count > 0;
            AddSummary.Text = $"{preview.Cores.Count} core(s) · {preview.AddOrReplaceFileCount} file(s) to add or replace";
            RemoveSummary.Text = $"{preview.Removals.Count} core(s) · {preview.RemoveFileCount} file(s) to remove";
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
                : "No changes are planned. Select cores to keep, install, update, or remove.";
        }
        catch (OperationCanceledException)
        {
            SyncStatus.Text = "Preparing the sync was cancelled.";
            PrepareButton.IsVisible = true;
        }
        catch (Exception exception)
        {
            SyncStatus.Text = $"Could not prepare sync: {exception.Message}";
            PrepareButton.IsVisible = true;
        }
        finally
        {
            PrepareButton.IsEnabled = true;
            PreparingIndicator.IsRunning = false;
            PreparingIndicator.IsVisible = false;
        }
    }

    private async void OnSyncClicked(object? sender, EventArgs e)
    {
        if (preview is not { CanSync: true }) return;
        string summary = $"Add or replace {preview.AddOrReplaceFileCount} file(s) and remove {preview.Removals.Count} core(s) ({preview.RemoveFileCount} file(s)) on {preview.PocketPath}? All changed core files will be backed up first.";
        bool confirmed = await DisplayAlert("Apply core changes", summary, "Apply", "Cancel");
        if (!confirmed) return;

        SyncButton.IsEnabled = false;
        CoreSyncResult result = await coreSync.ApplyAsync(preview);
        if (result.Succeeded)
        {
            PocketDrive? refreshedPocket = scanner.ScanFolder(preview.PocketPath);
            if (refreshedPocket is not null) pocketSelection.Select(refreshedPocket);
            coreSelection.ReportSuccessfulSync(result.Message);
            await Shell.Current.GoToAsync("..");
            return;
        }
        SyncStatus.Text = result.Message;
        SyncButton.IsVisible = false;
        SyncButton.IsEnabled = true;
        SyncButton.IsVisible = true;
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    private async void OnManageCoresClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    private async void OnHomeClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//MainPage");
    }

    private static string FormatBytes(long bytes) => bytes < 1024 * 1024
        ? $"{Math.Max(1, bytes / 1024d):0.#} KB"
        : $"{bytes / 1024d / 1024d:0.#} MB";
}
