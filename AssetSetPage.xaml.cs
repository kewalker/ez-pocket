using EzPocket.Models;
using EzPocket.Services;

namespace EzPocket;

public partial class AssetSetPage : ContentPage
{
    private readonly PocketSelectionService selection;
    private readonly AssetService assets;
    private readonly AssetImportSelectionService imports;

    public AssetSetPage()
    {
        InitializeComponent();
        selection = IPlatformApplication.Current?.Services.GetService<PocketSelectionService>() ?? new PocketSelectionService();
        assets = IPlatformApplication.Current?.Services.GetService<AssetService>() ?? new AssetService();
        imports = IPlatformApplication.Current?.Services.GetService<AssetImportSelectionService>() ?? new AssetImportSelectionService();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        AssetSetInventory? set = imports.PendingSet;
        if (set is null) { _ = Shell.Current.GoToAsync(".."); return; }
        SetName.Text = set.Name;
        SetKind.Text = set.Kind.ToUpperInvariant();
        SetSummary.Text = set.IsInstalled ? $"{set.Status} installed on the selected target." : "Not installed on the selected target.";
        RemoveButton.IsEnabled = set.IsInstalled;
    }

    private void OnReplaceClicked(object? sender, EventArgs e)
    {
        AssetSetInventory? set = imports.PendingSet;
        PocketDrive? pocket = selection.SelectedPocket;
        if (set is null || pocket is null) return;
        ReplaceButton.IsEnabled = false;
        ReplaceButton.IsBusy = true;
        ReplaceButton.Text = "PREPARING…";
        SetDownloadBusy(true, "PREPARING DOWNLOAD FOR REVIEW");
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), () => _ = ReplaceFromSourceAsync(set, pocket));
    }

    private async Task ReplaceFromSourceAsync(AssetSetInventory set, PocketDrive pocket)
    {
        try
        {
            if (set.Key == "palettes")
            {
                var progress = new Progress<AssetPreparationProgress>(update =>
                {
                    Status.Text = update.TotalBytes is > 0
                        ? $"{update.Stage} · {update.CompletedBytes * 100 / update.TotalBytes.Value}%"
                        : update.Stage;
                });
                AssetImportPreview downloaded = await Task.Run(() => assets.PreparePalettePackAsync(pocket, progress));
                imports.SetPalettePack(new PalettePackCatalog(pocket, downloaded.Changes, downloaded.StagingPath!));
                await Shell.Current.GoToAsync("PalettePackPage");
                return;
            }
            else if (set.Key == "platform")
            {
                string? choice = await DisplayActionSheet("Choose platform art", "Cancel", null, "Home · US", "Home · Japan", "Arcade");
                string? style = choice == "Home · US" ? "home-us" : choice == "Home · Japan" ? "home-jp" : choice == "Arcade" ? "arcade" : null;
                if (style is null) return;
                imports.SetPreview(await assets.PreparePlatformArtAsync(pocket, style));
            }
            else
            {
                string system = set.Key switch { "gb" => "GB", "gba" => "GBA", "gg" => "GG", "ngp" => "ngp", "lynx" => "lynx", "pce" => "pce", _ => throw new InvalidOperationException() };
                imports.SetPreview(await assets.PrepareLibraryBoxArtAsync(pocket, system));
            }
            Status.Text = string.Empty;
            await Shell.Current.GoToAsync("AssetReviewPage");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
        {
            Status.Text = "Could not download this asset set. Check your connection and try again.";
        }
        finally
        {
            ReplaceButton.IsEnabled = true;
            ReplaceButton.IsBusy = false;
            ReplaceButton.Text = "UPDATE FROM SOURCE";
            SetDownloadBusy(false);
        }
    }

    private async void OnRemoveClicked(object? sender, EventArgs e)
    {
        AssetSetInventory? set = imports.PendingSet;
        PocketDrive? pocket = selection.SelectedPocket;
        if (set is null || pocket is null) return;
        imports.SetRemoval(assets.PrepareAssetSetRemoval(pocket, set));
        await Shell.Current.GoToAsync("AssetRemovalReviewPage");
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        imports.ClearSet();
        await Shell.Current.GoToAsync("..");
    }

    private void SetDownloadBusy(bool isBusy, string? message = null)
    {
        DownloadActivity.IsVisible = isBusy;
        DownloadActivity.IsRunning = isBusy;
        if (message is not null) Status.Text = message;
    }
}
