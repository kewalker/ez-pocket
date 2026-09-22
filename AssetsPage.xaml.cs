using EzPocket.Models;
using EzPocket.Services;

namespace EzPocket;

public partial class AssetsPage : ContentPage
{
    private readonly PocketSelectionService selection;
    private readonly AssetService assets;
    private readonly AssetImportSelectionService imports;

    public AssetsPage()
    {
        InitializeComponent();
        selection = IPlatformApplication.Current?.Services.GetService<PocketSelectionService>() ?? new PocketSelectionService();
        assets = IPlatformApplication.Current?.Services.GetService<AssetService>() ?? new AssetService();
        imports = IPlatformApplication.Current?.Services.GetService<AssetImportSelectionService>() ?? new AssetImportSelectionService();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        string? success = imports.ConsumeSuccess();
        if (success is not null) SuccessToast.ShowSuccess(success);
        Refresh();
    }

    protected override void OnDisappearing()
    {
        SuccessToast.Hide();
        base.OnDisappearing();
    }

    private void Refresh()
    {
        PocketDrive? pocket = selection.SelectedPocket;
        ImportButton.IsEnabled = pocket is not null;
        DownloadPackButton.IsEnabled = pocket is not null;
        LibraryArtButton.IsEnabled = pocket is not null;
        PlatformArtButton.IsEnabled = pocket is not null;
        if (pocket is null)
        {
            TargetSummary.Text = "Select a target to inspect optional core assets.";
            PaletteSummary.Text = "Target unavailable";
            PaletteList.ItemsSource = null;
            AssetSetList.ItemsSource = null;
            return;
        }

        IReadOnlyList<ManagedAsset> palettes = assets.Scan(pocket);
        TargetSummary.Text = $"Target: {pocket.Name}. Assets are additive and reviewed separately from core packages.";
        PaletteSummary.Text = palettes.Count == 1 ? "1 palette installed" : $"{palettes.Count} palettes installed";
        PaletteList.ItemsSource = palettes;
        AssetSetList.ItemsSource = assets.ScanAssetSets(pocket);
    }

    private async void OnImportClicked(object? sender, EventArgs e)
    {
        PocketDrive? pocket = selection.SelectedPocket;
        if (pocket is null) return;
        try
        {
            IEnumerable<FileResult> selected = await FilePicker.Default.PickMultipleAsync(new PickOptions { PickerTitle = "Select Game Boy palette files" });
            string[] paths = selected.Select(file => file.FullPath).Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
            if (paths.Length == 0) return;
            imports.SetPreview(assets.PreparePaletteImport(pocket, paths));
            await Shell.Current.GoToAsync("AssetReviewPage");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Status.Text = "Could not open the selected palette files.";
        }
    }

    private void OnDownloadPackClicked(object? sender, EventArgs e)
    {
        PocketDrive? pocket = selection.SelectedPocket;
        if (pocket is null) return;
        DownloadPackButton.IsEnabled = false;
        DownloadPackButton.IsBusy = true;
        SetDownloadBusy(true, "DOWNLOADING PACK · Preparing files for review…");
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), () => _ = DownloadPalettePackAsync(pocket));
    }

    private async Task DownloadPalettePackAsync(PocketDrive pocket)
    {
        try
        {
            var progress = new Progress<AssetPreparationProgress>(update =>
            {
                Status.Text = update.TotalBytes is > 0
                    ? $"{update.Stage} · {update.CompletedBytes * 100 / update.TotalBytes.Value}%"
                    : update.Stage;
            });
            AssetImportPreview downloaded = await Task.Run(() => assets.PreparePalettePackAsync(pocket, progress));
            imports.SetPalettePack(new PalettePackCatalog(pocket, downloaded.Changes, downloaded.StagingPath!));
            Status.Text = "PACK READY · Review the file list before anything is copied to the target.";
            await Shell.Current.GoToAsync("PalettePackPage");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
        {
            Status.Text = "Could not download the palette pack. Check your connection and try again.";
        }
        finally
        {
            DownloadPackButton.IsEnabled = true;
            DownloadPackButton.IsBusy = false;
            SetDownloadBusy(false);
        }
    }

    private async void OnPaletteEditorClicked(object? sender, EventArgs e) =>
        await OpenResourceAsync("https://www.nortakales.com/page/gbpEditor");

    private void OnLibraryArtClicked(object? sender, EventArgs e)
    {
        LibraryArtChoices.IsVisible = !LibraryArtChoices.IsVisible;
        LibraryArtButton.Text = LibraryArtChoices.IsVisible ? "CLOSE CHOOSER" : "CHOOSE SYSTEM";
    }

    private void OnGameBoyArtClicked(object? sender, EventArgs e) => BeginLibraryArtDownload("GB");
    private void OnGameBoyAdvanceArtClicked(object? sender, EventArgs e) => BeginLibraryArtDownload("GBA");
    private void OnGameGearArtClicked(object? sender, EventArgs e) => BeginLibraryArtDownload("GG");
    private void OnNeoGeoArtClicked(object? sender, EventArgs e) => BeginLibraryArtDownload("ngp");
    private void OnLynxArtClicked(object? sender, EventArgs e) => BeginLibraryArtDownload("lynx");
    private void OnPcEngineArtClicked(object? sender, EventArgs e) => BeginLibraryArtDownload("pce");

    private async void BeginLibraryArtDownload(string system)
    {
        PocketDrive? pocket = selection.SelectedPocket;
        if (pocket is null) return;
        LibraryArtChoices.IsVisible = false;
        LibraryArtButton.IsEnabled = false;
        LibraryArtButton.IsBusy = true;
        Status.Text = "DOWNLOADING BOX ART FOR REVIEW";
        try
        {
            imports.SetPreview(await assets.PrepareLibraryBoxArtAsync(pocket, system));
            await Shell.Current.GoToAsync("AssetReviewPage");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
        {
            Status.Text = "Could not download the library art pack. Check your connection and try again.";
        }
        finally
        {
            LibraryArtButton.IsEnabled = true;
            LibraryArtButton.IsBusy = false;
        }
    }

#if false
    private async void OldLibraryArtModal(object? sender, EventArgs e)
    {
        PocketDrive? pocket = selection.SelectedPocket;
        if (pocket is null) return;
        string? choice = await DisplayActionSheet("Download library box art", "Cancel", null, "Game Boy + Game Boy Color · 275 MB", "Game Boy Advance · 316 MB", "Game Gear · 75 MB", "Neo Geo Pocket / Color", "Atari Lynx", "PC Engine");
        string? system = choice?.StartsWith("Game Boy +", StringComparison.Ordinal) == true ? "GB" : choice?.StartsWith("Game Boy Advance", StringComparison.Ordinal) == true ? "GBA" : null;
        system ??= choice?.StartsWith("Game Gear", StringComparison.Ordinal) == true ? "GG" : choice == "Neo Geo Pocket / Color" ? "ngp" : choice == "Atari Lynx" ? "lynx" : choice == "PC Engine" ? "pce" : null;
        if (system is null) return;
        LibraryArtButton.IsEnabled = false;
        Status.Text = "Downloading library art for review…";
        try
        {
            imports.SetPreview(await assets.PrepareLibraryBoxArtAsync(pocket, system));
            Status.Text = string.Empty;
            await Shell.Current.GoToAsync("AssetReviewPage");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
        {
            Status.Text = "Could not download the library art pack. Check your connection and try again.";
        }
        finally { LibraryArtButton.IsEnabled = true; }
    }

#endif

    private void OnPlatformArtClicked(object? sender, EventArgs e)
    {
        PlatformArtChoices.IsVisible = !PlatformArtChoices.IsVisible;
        PlatformArtButton.Text = PlatformArtChoices.IsVisible ? "CLOSE CHOOSER" : "CHOOSE PRESENTATION";
    }

    private void OnHomeUsArtClicked(object? sender, EventArgs e) => BeginPlatformArtDownload("home-us");
    private void OnHomeJapanArtClicked(object? sender, EventArgs e) => BeginPlatformArtDownload("home-jp");
    private void OnArcadeArtClicked(object? sender, EventArgs e) => BeginPlatformArtDownload("arcade");

    private async void BeginPlatformArtDownload(string style)
    {
        PocketDrive? pocket = selection.SelectedPocket;
        if (pocket is null) return;
        PlatformArtChoices.IsVisible = false;
        PlatformArtButton.IsEnabled = false;
        PlatformArtButton.IsBusy = true;
        Status.Text = "DOWNLOADING PLATFORM ART FOR REVIEW";
        try
        {
            imports.SetPreview(await assets.PreparePlatformArtAsync(pocket, style));
            await Shell.Current.GoToAsync("AssetReviewPage");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
        {
            Status.Text = "Could not download the platform art pack. Check your connection and try again.";
        }
        finally { PlatformArtButton.IsEnabled = true; PlatformArtButton.IsBusy = false; }
    }

#if false
    private async void OldPlatformArtModal(object? sender, EventArgs e)
    {
        PocketDrive? pocket = selection.SelectedPocket;
        if (pocket is null) return;
        string? choice = await DisplayActionSheet("Download platform art", "Cancel", null, "Home · US", "Home · Japan", "Arcade");
        string? style = choice == "Home · US" ? "home-us" : choice == "Home · Japan" ? "home-jp" : choice == "Arcade" ? "arcade" : null;
        if (style is null) return;
        PlatformArtButton.IsEnabled = false;
        Status.Text = "Downloading platform art for review…";
        try
        {
            imports.SetPreview(await assets.PreparePlatformArtAsync(pocket, style));
            Status.Text = string.Empty;
            await Shell.Current.GoToAsync("AssetReviewPage");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
        {
            Status.Text = "Could not download the platform art pack. Check your connection and try again.";
        }
        finally { PlatformArtButton.IsEnabled = true; }
    }

#endif

    private async void OnManageAssetSetClicked(object? sender, EventArgs e)
    {
        if (sender is not BindableObject { BindingContext: AssetSetInventory set }) return;
        imports.SelectSet(set);
        await Shell.Current.GoToAsync("AssetSetPage");
    }

    private async Task OpenResourceAsync(string url)
    {
        try
        {
            await Launcher.Default.OpenAsync(new Uri(url));
        }
        catch (Exception exception) when (exception is InvalidOperationException or UriFormatException)
        {
            Status.Text = "Could not open the palette resource.";
        }
    }

    private void SetDownloadBusy(bool isBusy, string? message = null)
    {
        DownloadActivity.IsVisible = isBusy;
        DownloadActivity.IsRunning = isBusy;
        if (message is not null) Status.Text = message;
    }

    private async void OnBackClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");
    private async void OnHomeClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("//MainPage");
}
