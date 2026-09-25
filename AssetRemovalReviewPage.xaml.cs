using EzPocket.Models;
using EzPocket.Services;

namespace EzPocket;

public partial class AssetRemovalReviewPage : ContentPage
{
    private readonly AssetService assets;
    private readonly AssetImportSelectionService imports;
    private readonly PocketHealthService health;

    public AssetRemovalReviewPage()
    {
        InitializeComponent();
        assets = IPlatformApplication.Current?.Services.GetService<AssetService>() ?? new AssetService();
        imports = IPlatformApplication.Current?.Services.GetService<AssetImportSelectionService>() ?? new AssetImportSelectionService();
        health = IPlatformApplication.Current?.Services.GetService<PocketHealthService>() ?? new PocketHealthService();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        AssetRemovalPreview? preview = imports.PendingRemoval;
        if (preview is null) { _ = Shell.Current.GoToAsync(".."); return; }
        Summary.Text = $"{preview.Set.Name} · {preview.Files.Count} file{(preview.Files.Count == 1 ? string.Empty : "s")} to remove";
        FileList.ItemsSource = preview.Files;
        RemoveButton.IsEnabled = preview.CanApply;
    }

    private async void OnRemoveClicked(object? sender, EventArgs e)
    {
        AssetRemovalPreview? preview = imports.PendingRemoval;
        if (preview is null) return;
        if (!health.Inspect(preview.Pocket).CanWrite)
        {
            Summary.Text = "REMOVAL BLOCKED · The target is no longer available. Scan it again and prepare a new review.";
            return;
        }
        RemoveButton.IsEnabled = false;
        AssetImportResult result = assets.ApplyRemoval(preview);
        if (!result.Succeeded) { Summary.Text = result.Message; RemoveButton.IsEnabled = true; return; }
        imports.ClearRemoval();
        imports.ClearSet();
        imports.ReportSuccess(result.Message);
        await Shell.Current.GoToAsync("../..");
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        imports.ClearRemoval();
        await Shell.Current.GoToAsync("..");
    }
}
