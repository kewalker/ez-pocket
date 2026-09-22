using EzPocket.Models;
using EzPocket.Services;

namespace EzPocket;

public partial class AssetReviewPage : ContentPage
{
    private readonly AssetService assets;
    private readonly AssetImportSelectionService imports;
    private bool isApplying;

    public AssetReviewPage()
    {
        InitializeComponent();
        assets = IPlatformApplication.Current?.Services.GetService<AssetService>() ?? new AssetService();
        imports = IPlatformApplication.Current?.Services.GetService<AssetImportSelectionService>() ?? new AssetImportSelectionService();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        AssetImportPreview? preview = imports.PendingPreview;
        if (preview is null) { _ = Shell.Current.GoToAsync(".."); return; }
        Summary.Text = $"{preview.Kind} · {preview.Changes.Count} file{(preview.Changes.Count == 1 ? string.Empty : "s")} to import · {preview.ReplacementCount} replacement{(preview.ReplacementCount == 1 ? string.Empty : "s")}";
        ChangeList.ItemsSource = preview.Changes;
        BlockerPanel.IsVisible = preview.Blockers.Count > 0;
        Blockers.Text = string.Join(Environment.NewLine, preview.Blockers);
        ApplyButton.IsEnabled = true;
        ApplyButton.Text = preview.CanApply
            ? "APPLY IMPORT"
            : $"REVIEW {preview.Blockers.Count:N0} ISSUE{(preview.Blockers.Count == 1 ? string.Empty : "S")}";
    }

    private void OnApplyClicked(object? sender, EventArgs e)
    {
        AssetImportPreview? preview = imports.PendingPreview;
        if (preview is null || isApplying) return;
        if (!preview.CanApply)
        {
            BlockerPanel.IsVisible = true;
            ApplyProgressPanel.IsVisible = true;
            ApplyStatus.Text = $"IMPORT BLOCKED · Resolve {preview.Blockers.Count:N0} review issue{(preview.Blockers.Count == 1 ? string.Empty : "s")} above.";
            return;
        }
        isApplying = true;
        ApplyButton.IsEnabled = false;
        ApplyButton.IsBusy = true;
        ApplyButton.Text = "APPLYING…";
        CancelButton.IsEnabled = false;
        ApplyProgressPanel.IsVisible = true;
        ApplyActivity.IsRunning = true;
        ApplyStatus.Text = $"APPLYING IMPORT · 0 OF {preview.Changes.Count:N0} FILES";
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), () => _ = ApplyImportAsync(preview));
    }

    private async Task ApplyImportAsync(AssetImportPreview preview)
    {
        var progress = new Progress<AssetImportProgress>(update =>
        {
            ApplyStatus.Text = $"APPLYING IMPORT · {update.CompletedCount:N0} OF {update.TotalCount:N0} FILES · {update.CurrentFile}";
        });
        AssetImportResult result;
        try
        {
            result = await Task.Run(() => assets.Apply(preview, progress));
        }
        catch
        {
            ApplyActivity.IsRunning = false;
            ApplyStatus.Text = "IMPORT DID NOT COMPLETE · Try the reviewed import again.";
            ApplyButton.IsEnabled = true;
            ApplyButton.IsBusy = false;
            ApplyButton.Text = "APPLY IMPORT";
            CancelButton.IsEnabled = true;
            isApplying = false;
            return;
        }
        ApplyActivity.IsRunning = false;
        if (!result.Succeeded)
        {
            Summary.Text = result.Message;
            ApplyStatus.Text = "IMPORT DID NOT COMPLETE · Changed files were restored.";
            ApplyButton.IsEnabled = true;
            ApplyButton.IsBusy = false;
            ApplyButton.Text = "APPLY IMPORT";
            CancelButton.IsEnabled = true;
            isApplying = false;
            return;
        }
        imports.ClearPreview();
        imports.ReportSuccess(result.Message);
        await Shell.Current.GoToAsync("..");
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        if (isApplying) return;
        if (imports.PendingPreview is AssetImportPreview preview) assets.Cleanup(preview);
        imports.ClearPreview();
        await Shell.Current.GoToAsync("..");
    }
}
