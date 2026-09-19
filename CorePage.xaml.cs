using EzPocket.Models;
using EzPocket.Services;

namespace EzPocket;

public partial class CorePage : ContentPage
{
    private readonly PocketSelectionService selection;
    private readonly CoreInventoryService inventory;
    private readonly CoreSelectionService coreSelection;
    private readonly FeaturedCoreSetService featuredCoreSets;
    private IReadOnlyList<CoreComparison> allCores = [];
    private IReadOnlyList<CoreComparison> visibleCores = [];
    private CancellationTokenSource? refreshCancellation;
    private bool refreshInProgress;
    private string? sortColumn;
    private bool sortAscending = true;
    private bool updatingVisibleSelection;

    public CorePage()
    {
        InitializeComponent();
        selection = IPlatformApplication.Current?.Services.GetService<PocketSelectionService>() ?? new PocketSelectionService();
        inventory = IPlatformApplication.Current?.Services.GetService<CoreInventoryService>() ?? new CoreInventoryService();
        coreSelection = IPlatformApplication.Current?.Services.GetService<CoreSelectionService>() ?? new CoreSelectionService();
        featuredCoreSets = IPlatformApplication.Current?.Services.GetService<FeaturedCoreSetService>() ?? new FeaturedCoreSetService();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        string? syncMessage = coreSelection.ConsumeSuccessfulSyncMessage();
        if (syncMessage is not null) ShowSuccessToast(syncMessage);
        await RefreshAsync();
    }

    protected override void OnDisappearing()
    {
        refreshCancellation?.Cancel();
        SuccessToast.Hide();
        base.OnDisappearing();
    }

    private void ShowSuccessToast(string message)
    {
        SuccessToast.ShowSuccess(message);
    }

    private async void OnRefreshClicked(object? sender, EventArgs e) => await RefreshAsync();

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    private void OnCoreRowTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject { BindingContext: CoreComparison core })
        {
            coreSelection.SetSelected(core, !core.IsSelected);
            UpdateSelectionBar();
        }
    }

    private async void OnDetailsClicked(object? sender, EventArgs e)
    {
        if (sender is BindableObject { BindingContext: CoreComparison core })
        {
            coreSelection.Select(core);
            await Shell.Current.GoToAsync("CoreDetailsPage");
        }
    }

    private void OnCoreSelectionChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (sender is CheckBox { BindingContext: CoreComparison core })
        {
            coreSelection.SetSelected(core, e.Value);
            UpdateSelectionBar();
        }
    }

    private async void OnReviewClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("CoreReviewPage");
    }

    private void OnSelectVisibleChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (updatingVisibleSelection) return;
        foreach (CoreComparison core in visibleCores)
            coreSelection.SetSelected(core, e.Value);
        UpdateSelectionBar();
    }

    private void UpdateSelectionBar()
    {
        int count = coreSelection.SelectedCores.Count;
        SelectionBar.IsVisible = true;
        int removals = coreSelection.Cores.Count(core => core.IsInstalled && !core.IsSelected);
        string selectedSummary = count switch
        {
            0 => "No cores selected",
            1 => "1 core selected",
            _ => $"{count} cores selected"
        };
        SelectionSummary.Text = removals switch
        {
            0 => $"{selectedSummary}. Installed cores will be kept.",
            1 => $"{selectedSummary}. 1 unselected installed core will be removed after review.",
            _ => $"{selectedSummary}. {removals} unselected installed cores will be removed after review."
        };
        if (SelectVisibleCheckBox is null) return;
        bool hasVisibleCores = visibleCores.Count > 0;
        bool allVisibleSelected = hasVisibleCores && visibleCores.All(core => core.IsSelected);
        updatingVisibleSelection = true;
        SelectVisibleCheckBox.IsEnabled = hasVisibleCores;
        SelectVisibleCheckBox.IsChecked = allVisibleSelected;
        updatingVisibleSelection = false;
        SemanticProperties.SetDescription(SelectVisibleCheckBox, allVisibleSelected ? "Clear all visible core selections" : "Select all visible cores");
    }

    private void OnBreadcrumbPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Label label) label.TextColor = Color.FromArgb("#60605C");
    }

    private void OnBreadcrumbPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Label label) label.TextColor = Color.FromArgb("#151515");
    }

    private void OnFilterChanged(object? sender, EventArgs e)
    {
        string query = Search.Text?.Trim() ?? string.Empty;
        string filter = StatusFilter.SelectedItem?.ToString() ?? "All cores";
        string category = CategoryFilter.SelectedItem?.ToString() ?? "All categories";
        IEnumerable<CoreComparison> filtered = allCores;

        if (!string.IsNullOrWhiteSpace(query))
            filtered = filtered.Where(core => core.FriendlyName.Contains(query, StringComparison.OrdinalIgnoreCase) || core.Identifier.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (filter == "Updates") filtered = filtered.Where(core => core.Status == "Update");
        if (filter == "Installed") filtered = filtered.Where(core => core.IsInstalled);
        if (filter == "Available") filtered = filtered.Where(core => core.Status == "Available");
        if (category != "All categories") filtered = filtered.Where(core => string.Equals(core.Category, category, StringComparison.OrdinalIgnoreCase));
        visibleCores = Sort(filtered).ToArray();
        CoreList.ItemsSource = visibleCores;
        UpdateSelectionBar();
    }

    private void OnCoreSortClicked(object? sender, EventArgs e) => SetSort("Core");
    private void OnCategorySortClicked(object? sender, EventArgs e) => SetSort("Category");
    private void OnInstalledSortClicked(object? sender, EventArgs e) => SetSort("Installed");
    private void OnLatestSortClicked(object? sender, EventArgs e) => SetSort("Latest");
    private void OnStatusSortClicked(object? sender, EventArgs e) => SetSort("Status");

    private void SetSort(string column)
    {
        sortAscending = sortColumn == column ? !sortAscending : true;
        sortColumn = column;
        UpdateSortHeaders();
        OnFilterChanged(this, EventArgs.Empty);
    }

    private IEnumerable<CoreComparison> Sort(IEnumerable<CoreComparison> cores) => sortColumn switch
    {
        "Core" => sortAscending ? cores.OrderBy(core => core.FriendlyName, StringComparer.OrdinalIgnoreCase).ThenBy(core => core.Identifier, StringComparer.OrdinalIgnoreCase) : cores.OrderByDescending(core => core.FriendlyName, StringComparer.OrdinalIgnoreCase).ThenByDescending(core => core.Identifier, StringComparer.OrdinalIgnoreCase),
        "Category" => sortAscending ? cores.OrderBy(core => core.Category, StringComparer.OrdinalIgnoreCase).ThenBy(core => core.FriendlyName, StringComparer.OrdinalIgnoreCase) : cores.OrderByDescending(core => core.Category, StringComparer.OrdinalIgnoreCase).ThenByDescending(core => core.FriendlyName, StringComparer.OrdinalIgnoreCase),
        "Installed" => sortAscending ? cores.OrderBy(core => core.InstalledVersion, VersionComparer).ThenBy(core => core.FriendlyName, StringComparer.OrdinalIgnoreCase) : cores.OrderByDescending(core => core.InstalledVersion, VersionComparer).ThenByDescending(core => core.FriendlyName, StringComparer.OrdinalIgnoreCase),
        "Latest" => sortAscending ? cores.OrderBy(core => core.AvailableVersion, VersionComparer).ThenBy(core => core.FriendlyName, StringComparer.OrdinalIgnoreCase) : cores.OrderByDescending(core => core.AvailableVersion, VersionComparer).ThenByDescending(core => core.FriendlyName, StringComparer.OrdinalIgnoreCase),
        "Status" => sortAscending ? cores.OrderBy(core => core.StatusLabel, StringComparer.OrdinalIgnoreCase).ThenBy(core => core.FriendlyName, StringComparer.OrdinalIgnoreCase) : cores.OrderByDescending(core => core.StatusLabel, StringComparer.OrdinalIgnoreCase).ThenByDescending(core => core.FriendlyName, StringComparer.OrdinalIgnoreCase),
        _ => cores
    };

    private static readonly IComparer<string> VersionComparer = Comparer<string>.Create((left, right) =>
    {
        bool leftIsVersion = Version.TryParse(left?.Trim().TrimStart('v', 'V'), out Version? leftVersion);
        bool rightIsVersion = Version.TryParse(right?.Trim().TrimStart('v', 'V'), out Version? rightVersion);
        if (leftIsVersion && rightIsVersion) return leftVersion!.CompareTo(rightVersion);
        if (leftIsVersion) return -1;
        if (rightIsVersion) return 1;
        return StringComparer.OrdinalIgnoreCase.Compare(left, right);
    });

    private void UpdateSortHeaders()
    {
        SetSortHeader(CoreSortButton, "Core", "CORE");
        SetSortHeader(CategorySortButton, "Category", "CATEGORY");
        SetSortHeader(InstalledSortButton, "Installed", "INSTALLED");
        SetSortHeader(LatestSortButton, "Latest", "LATEST");
        SetSortHeader(StatusSortButton, "Status", "STATUS");
    }

    private void SetSortHeader(Controls.EzButton button, string column, string label)
    {
        bool isActive = sortColumn == column;
        button.Text = isActive ? $"{label} {(sortAscending ? "▲" : "▼")}" : label;
        button.ButtonTextColor = isActive ? Color.FromArgb("#151515") : Color.FromArgb("#60605C");
        SemanticProperties.SetDescription(button, $"Sort {label.ToLowerInvariant()} {(isActive && sortAscending ? "descending" : "ascending")}");
    }

    private void PopulateCategoryFilter()
    {
        string previous = CategoryFilter.SelectedItem?.ToString() ?? "All categories";
        CategoryFilter.Items.Clear();
        CategoryFilter.Items.Add("All categories");
        foreach (string category in allCores.Select(core => core.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(category => category))
            CategoryFilter.Items.Add(category);
        CategoryFilter.SelectedItem = CategoryFilter.Items.Contains(previous) ? previous : "All categories";
    }

    private async Task RefreshAsync()
    {
        if (refreshInProgress) return;

        var pocket = selection.SelectedPocket;
        if (pocket is null)
        {
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
            coreSelection.InitializeForPocket(pocket, comparison);
            FeaturedCoreSetSelection? featuredSelection = featuredCoreSets.ApplyPendingSelection(coreSelection, comparison);
            FeaturedSetNotice.IsVisible = featuredSelection is not null;
            if (featuredSelection is not null) FeaturedSetNoticeText.Text = featuredSelection.Summary;
            UpdateSelectionBar();
            PopulateCategoryFilter();
            StatusFilter.SelectedIndex = 0;
            StatusFilter.SelectedItem = StatusFilter.Items[0];
            Dispatcher.Dispatch(() =>
            {
                StatusFilter.SelectedIndex = 0;
                StatusFilter.SelectedItem = StatusFilter.Items[0];
            });
            InventoryState.Text = "Live inventory updated";
            Summary.Text = $"{pocket.CoreCount} installed · {available.Count} available";
            OnFilterChanged(this, EventArgs.Empty);
        }
        catch (HttpRequestException)
        {
            Summary.Text = $"{pocket.CoreCount} installed";
            InventoryState.Text = "Offline";
            OfflineState.IsVisible = true;
            allCores = pocket.InstalledCoreNames.Select(identifier => new CoreComparison(identifier, identifier, "Unknown", "Unknown", "-", true, false, "Unknown")).ToArray();
            coreSelection.InitializeForPocket(pocket, allCores);
            UpdateSelectionBar();
            PopulateCategoryFilter();
            OnFilterChanged(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return;
        }
        catch (OperationCanceledException)
        {
            Summary.Text = $"{pocket.CoreCount} installed";
            InventoryState.Text = "Offline";
            OfflineState.IsVisible = true;
            allCores = pocket.InstalledCoreNames.Select(identifier => new CoreComparison(identifier, identifier, "Unknown", "Unknown", "-", true, false, "Unknown")).ToArray();
            coreSelection.InitializeForPocket(pocket, allCores);
            UpdateSelectionBar();
            PopulateCategoryFilter();
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
