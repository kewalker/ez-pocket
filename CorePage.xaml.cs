using EzPocket.Models;
using EzPocket.Services;

namespace EzPocket;

public partial class CorePage : ContentPage
{
    private readonly PocketSelectionService selection;
    private readonly CoreInventoryService inventory;
    private readonly CoreSelectionService coreSelection;
    private IReadOnlyList<CoreComparison> allCores = [];
    private CancellationTokenSource? refreshCancellation;
    private bool refreshInProgress;

    public CorePage()
    {
        InitializeComponent();
        selection = IPlatformApplication.Current?.Services.GetService<PocketSelectionService>() ?? new PocketSelectionService();
        inventory = IPlatformApplication.Current?.Services.GetService<CoreInventoryService>() ?? new CoreInventoryService();
        coreSelection = IPlatformApplication.Current?.Services.GetService<CoreSelectionService>() ?? new CoreSelectionService();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
    }

    protected override void OnDisappearing()
    {
        refreshCancellation?.Cancel();
        base.OnDisappearing();
    }

    private async void OnRefreshClicked(object? sender, EventArgs e) => await RefreshAsync();

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    private async void OnCoreTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject { BindingContext: CoreComparison core })
        {
            coreSelection.Select(core);
            await Shell.Current.GoToAsync("CoreDetailsPage");
        }
    }

    private void OnBreadcrumbPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Label label) label.TextColor = Color.FromArgb("#1D4ED8");
    }

    private void OnBreadcrumbPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Label label) label.TextColor = Color.FromArgb("#2563EB");
    }

    private void OnFilterChanged(object? sender, EventArgs e)
    {
        StatusValue.Text = StatusFilter.SelectedItem?.ToString() ?? "All cores";
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
        if (refreshInProgress) return;

        var pocket = selection.SelectedPocket;
        if (pocket is null)
        {
            Subtitle.Text = "Select a Pocket on the dashboard before comparing cores.";
            Summary.Text = "No Pocket selected";
            CoreList.ItemsSource = null;
            InventoryState.Text = string.Empty;
            OfflineState.IsVisible = false;
            return;
        }

        refreshInProgress = true;
        refreshCancellation?.Cancel();
        refreshCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        refreshCancellation = cancellation;
        RefreshButton.IsEnabled = false;
        LoadingState.IsVisible = true;
        OfflineState.IsVisible = false;
        InventoryState.Text = "Checking live inventory…";

        try
        {
            var available = await inventory.GetAvailableAsync(cancellation.Token);
            var comparison = CoreInventoryService.Compare(pocket, available);
            allCores = comparison;
            StatusFilter.SelectedIndex = 0;
            StatusFilter.SelectedItem = StatusFilter.Items[0];
            Dispatcher.Dispatch(() =>
            {
                StatusFilter.SelectedIndex = 0;
                StatusFilter.SelectedItem = StatusFilter.Items[0];
            });
            Subtitle.Text = pocket.Name;
            InventoryState.Text = "Live inventory updated";
            Summary.Text = $"{pocket.CoreCount} installed · {available.Count} available";
            OnFilterChanged(this, EventArgs.Empty);
        }
        catch (HttpRequestException)
        {
            Subtitle.Text = "The Pocket was found, but the live inventory could not be reached.";
            Summary.Text = $"{pocket.CoreCount} installed";
            InventoryState.Text = "Offline";
            OfflineState.IsVisible = true;
            allCores = pocket.InstalledCoreNames.Select(identifier => new CoreComparison(identifier, identifier, "Unknown", "Unknown", "-", true, false, "Unknown")).ToArray();
            OnFilterChanged(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return;
        }
        catch (OperationCanceledException)
        {
            Subtitle.Text = "The Pocket was found, but the live inventory could not be reached.";
            Summary.Text = $"{pocket.CoreCount} installed";
            InventoryState.Text = "Offline";
            OfflineState.IsVisible = true;
            allCores = pocket.InstalledCoreNames.Select(identifier => new CoreComparison(identifier, identifier, "Unknown", "Unknown", "-", true, false, "Unknown")).ToArray();
            OnFilterChanged(this, EventArgs.Empty);
        }
        finally
        {
            if (ReferenceEquals(refreshCancellation, cancellation))
            {
                LoadingState.IsVisible = false;
                RefreshButton.IsEnabled = true;
                refreshInProgress = false;
                refreshCancellation = null;
            }
        }
    }
}
