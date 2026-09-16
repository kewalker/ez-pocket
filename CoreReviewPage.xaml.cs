using EzPocket.Models;
using EzPocket.Services;

namespace EzPocket;

public partial class CoreReviewPage : ContentPage
{
    private readonly CoreSelectionService coreSelection;
    private readonly PocketSelectionService pocketSelection;
    private readonly CoreSyncService coreSync;
    private CoreSyncPreview? preview;

    public CoreReviewPage()
    {
        InitializeComponent();
        coreSelection = IPlatformApplication.Current?.Services.GetService<CoreSelectionService>() ?? new CoreSelectionService();
        pocketSelection = IPlatformApplication.Current?.Services.GetService<PocketSelectionService>() ?? new PocketSelectionService();
        coreSync = IPlatformApplication.Current?.Services.GetService<CoreSyncService>() ?? new CoreSyncService();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SelectedCoreList.ItemsSource = coreSelection.SelectedCores;
    }

    protected override void OnDisappearing()
    {
        if (preview is not null) coreSync.Cleanup(preview);
        base.OnDisappearing();
    }

    private async void OnPrepareClicked(object? sender, EventArgs e)
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
        PreparingIndicator.IsVisible = true;
        PreparingIndicator.IsRunning = true;
        SyncButton.IsVisible = false;
        SyncButton.IsEnabled = true;
        ChangeList.IsVisible = false;
        SyncStatus.Text = "Preparing selected core packages…";
        try
        {
            IReadOnlyList<CoreComparison> coresToRemove = Array.Empty<CoreComparison>();
            preview = await coreSync.PrepareAsync(pocket, coreSelection.SelectedCores, coresToRemove);
            ChangeList.ItemsSource = preview.Changes;
            RemovalList.ItemsSource = preview.Removals;
            AddSummary.Text = $"{preview.Cores.Count} core(s) · {preview.AddOrReplaceFileCount} file(s) to add or replace";
            RemoveSummary.Text = $"{preview.Removals.Count} core(s) · {preview.RemoveFileCount} file(s) to remove";
            ChangeList.IsVisible = preview.Changes.Count > 0;
            RemovalList.IsVisible = preview.Removals.Count > 0;
            if (preview.Blockers.Count > 0)
            {
                SyncStatus.Text = string.Join(Environment.NewLine, preview.Blockers);
                return;
            }

            SyncButton.IsEnabled = preview.CanSync;
            SyncButton.IsVisible = preview.CanSync;
            SyncStatus.Text = preview.CanSync
                ? "Review the planned file changes, then apply them."
                : "No changes are planned. Select a core to install or update.";
        }
        catch (OperationCanceledException)
        {
            SyncStatus.Text = "Preparing the sync was cancelled.";
        }
        catch (Exception exception)
        {
            SyncStatus.Text = $"Could not prepare sync: {exception.Message}";
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
        string summary = $"Add or replace {preview.AddOrReplaceFileCount} file(s) on {preview.PocketPath}?";
        bool confirmed = await DisplayAlert("Apply core changes", summary, "Apply", "Cancel");
        if (!confirmed) return;

        SyncButton.IsEnabled = false;
        CoreSyncResult result = await coreSync.ApplyAsync(preview);
        SyncStatus.Text = result.Succeeded
            ? result.Message
            : result.Message;
        SyncButton.IsVisible = false;
        PrepareButton.Text = "Prepare another change";
        PrepareButton.IsEnabled = true;
        if (!result.Succeeded)
        {
            SyncButton.IsEnabled = true;
            SyncButton.IsVisible = true;
        }
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
