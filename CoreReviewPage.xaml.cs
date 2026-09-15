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
        if (pocket is null || coreSelection.SelectedCores.Count == 0) return;

        if (preview is not null) coreSync.Cleanup(preview);
        preview = null;
        PrepareButton.IsEnabled = false;
        PreparingIndicator.IsVisible = true;
        PreparingIndicator.IsRunning = true;
        SyncButton.IsVisible = false;
        ChangeList.IsVisible = false;
        SyncStatus.Text = "Downloading and checking selected core packages…";
        try
        {
            preview = await coreSync.PrepareAsync(pocket, coreSelection.SelectedCores);
            ChangeList.ItemsSource = preview.Changes;
            ChangeList.IsVisible = preview.Changes.Count > 0;
            if (preview.Blockers.Count > 0)
            {
                SyncStatus.Text = string.Join(Environment.NewLine, preview.Blockers);
                return;
            }

            SyncButton.IsVisible = preview.CanSync;
            SyncStatus.Text = $"Ready to sync {preview.Changes.Count} files ({FormatBytes(preview.TotalBytes)}). Existing files marked Replace will be backed up.";
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
        bool confirmed = await DisplayAlert("Sync selected cores", $"Copy {preview.Changes.Count} prepared files to {preview.PocketPath}? Existing package files will be backed up first.", "Sync", "Cancel");
        if (!confirmed) return;

        SyncButton.IsEnabled = false;
        CoreSyncResult result = await coreSync.ApplyAsync(preview);
        SyncStatus.Text = result.Succeeded
            ? $"{result.Message} Backup: {result.BackupPath ?? "not needed"}"
            : result.Message;
        SyncButton.IsVisible = false;
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
