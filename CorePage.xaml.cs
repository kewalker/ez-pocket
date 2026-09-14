using EzPocket.Models;
using EzPocket.Services;

namespace EzPocket;

public partial class CorePage : ContentPage
{
    private readonly PocketScanner scanner;
    private readonly CoreInventoryService inventory;
    private IReadOnlyList<CoreComparison> allCores = [];

    public CorePage()
    {
        InitializeComponent();
        scanner = IPlatformApplication.Current?.Services.GetService<PocketScanner>() ?? new PocketScanner();
        inventory = IPlatformApplication.Current?.Services.GetService<CoreInventoryService>() ?? new CoreInventoryService();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
    }

    private async void OnRefreshClicked(object? sender, EventArgs e) => await RefreshAsync();

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    private void OnButtonPointerEntered(object? sender, PointerEventArgs e) => SetButtonFeedback(sender as Button, 1.02, 0.9);

    private void OnButtonPointerExited(object? sender, PointerEventArgs e) => SetButtonFeedback(sender as Button, 1, 1);

    private void OnButtonPressed(object? sender, EventArgs e) => SetButtonFeedback(sender as Button, 0.96, 0.75);

    private void OnButtonReleased(object? sender, EventArgs e) => SetButtonFeedback(sender as Button, 1.02, 0.9);

    private void OnButtonFocused(object? sender, FocusEventArgs e)
    {
        if (sender is Button button) { button.BorderColor = Colors.Gray; button.BorderWidth = 2; }
    }

    private void OnButtonUnfocused(object? sender, FocusEventArgs e)
    {
        if (sender is Button button) { button.BorderColor = Colors.Transparent; button.BorderWidth = 0; }
    }

    private static void SetButtonFeedback(Button? button, double scale, double opacity)
    {
        if (button is not null) { button.Scale = scale; button.Opacity = opacity; }
    }

    private void OnFilterChanged(object? sender, EventArgs e)
    {
        string query = Search.Text?.Trim() ?? string.Empty;
        string filter = StatusFilter.SelectedItem?.ToString() ?? "All cores";
        IEnumerable<CoreComparison> filtered = allCores;

        if (!string.IsNullOrWhiteSpace(query))
            filtered = filtered.Where(core => core.FriendlyName.Contains(query, StringComparison.OrdinalIgnoreCase) || core.Identifier.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (filter == "Updates") filtered = filtered.Where(core => core.Status == "Update");
        if (filter == "Installed") filtered = filtered.Where(core => core.IsInstalled);
        if (filter == "Available") filtered = filtered.Where(core => core.Status == "Available");
        CoreList.ItemsSource = filtered.ToArray();
    }

    private async Task RefreshAsync()
    {
        var pocket = scanner.Scan().FirstOrDefault(drive => drive.LooksLikePocket);
        if (pocket is null)
        {
            Subtitle.Text = "Connect a Pocket SD card or USB Pocket to compare cores.";
            Summary.Text = "No Pocket connected";
            CoreList.ItemsSource = null;
            return;
        }

        try
        {
            var available = await inventory.GetAvailableAsync();
            var comparison = CoreInventoryService.Compare(pocket, available);
            allCores = comparison;
            StatusFilter.SelectedIndex = 0;
            Subtitle.Text = $"{pocket.Name} · friendly names come from the live inventory";
            Summary.Text = $"{pocket.CoreCount} installed · {available.Count} available";
            OnFilterChanged(this, EventArgs.Empty);
        }
        catch (HttpRequestException)
        {
            Subtitle.Text = "The Pocket was found, but the live inventory could not be reached.";
            Summary.Text = $"{pocket.CoreCount} installed";
            allCores = pocket.InstalledCoreNames.Select(identifier => new CoreComparison(identifier, identifier, "Unknown", "Unknown", "-", true, false, "Unknown")).ToArray();
            OnFilterChanged(this, EventArgs.Empty);
        }
    }
}
