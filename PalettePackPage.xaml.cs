using EzPocket.Models;
using EzPocket.Services;

namespace EzPocket;

public partial class PalettePackPage : ContentPage
{
    private readonly AssetService assets;
    private readonly AssetImportSelectionService imports;
    private IReadOnlyList<AssetFileChange> selected = [];
    private IReadOnlyList<AssetFileChange> candidates = [];

    public PalettePackPage()
    {
        InitializeComponent();
        assets = IPlatformApplication.Current?.Services.GetService<AssetService>() ?? new AssetService();
        imports = IPlatformApplication.Current?.Services.GetService<AssetImportSelectionService>() ?? new AssetImportSelectionService();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PalettePackCatalog? catalog = imports.PendingPalettePack;
        if (catalog is null) { _ = Shell.Current.GoToAsync(".."); return; }
        Summary.Text = $"{catalog.Candidates.Count:N0} palettes available · 0 selected";
        candidates = catalog.Candidates;
        PaletteList.ItemsSource = candidates;
        ReviewButton.IsEnabled = false;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        selected = e.CurrentSelection.OfType<AssetFileChange>().ToArray();
        Summary.Text = $"{imports.PendingPalettePack?.Candidates.Count:N0} palettes available · {selected.Count:N0} selected";
        ReviewButton.Text = $"REVIEW {selected.Count:N0} PALETTE{(selected.Count == 1 ? string.Empty : "S")}";
        ReviewButton.IsEnabled = selected.Count > 0;
        UpdateReviewState();
    }

    private void OnFilterChanged(object? sender, TextChangedEventArgs e)
    {
        string query = e.NewTextValue?.Trim() ?? string.Empty;
        PaletteList.ItemsSource = string.IsNullOrEmpty(query) ? candidates : candidates.Where(candidate => candidate.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || candidate.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private void UpdateReviewState()
    {
        int conflicts = selected.GroupBy(change => change.Name, StringComparer.OrdinalIgnoreCase).Count(group => group.Count() > 1);
        if (conflicts == 0) return;
        Summary.Text = $"Resolve {conflicts:N0} duplicate-name conflict{(conflicts == 1 ? string.Empty : "s")} before review.";
        ReviewButton.Text = "RESOLVE CONFLICTS";
        ReviewButton.IsEnabled = false;
    }

    private async void OnReviewClicked(object? sender, EventArgs e)
    {
        PalettePackCatalog? catalog = imports.PendingPalettePack;
        if (catalog is null || selected.Count == 0) return;
        imports.SetPreview(assets.PreparePalettePackSelection(catalog, selected));
        imports.ClearPalettePack();
        await Shell.Current.GoToAsync("AssetReviewPage");
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        if (imports.PendingPalettePack is PalettePackCatalog catalog) assets.CleanupPalettePack(catalog);
        imports.ClearPalettePack();
        await Shell.Current.GoToAsync("..");
    }
}
