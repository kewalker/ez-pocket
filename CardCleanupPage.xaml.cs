using EzPocket.Models;
using EzPocket.Services;

namespace EzPocket;

public partial class CardCleanupPage : ContentPage
{
    private const int PreviewPathLimit = 40;
    private static readonly Color Ink = Color.FromArgb("#151515");
    private static readonly Color Quiet = Color.FromArgb("#60605C");
    private static readonly Color Rule = Color.FromArgb("#C9C9C2");
    private readonly PocketSelectionService selection;
    private readonly CardCleanupService cleanup;
    private readonly PocketHealthService health;
    private readonly IEjectService eject;
    private readonly HashSet<CleanupCategory> selected = [];
    private CardCleanupPreview? preview;
    private CancellationTokenSource? scanCancellation;
    private bool applying;

    public CardCleanupPage()
    {
        InitializeComponent();
        var services = IPlatformApplication.Current?.Services;
        selection = services?.GetService<PocketSelectionService>() ?? new PocketSelectionService();
        cleanup = services?.GetService<CardCleanupService>() ?? new CardCleanupService();
        health = services?.GetService<PocketHealthService>() ?? new PocketHealthService();
        eject = services?.GetService<IEjectService>() ?? new UnsupportedEjectService();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ResultLabel.IsVisible = false;
        await ScanAsync();
    }

    protected override void OnDisappearing()
    {
        scanCancellation?.Cancel();
        base.OnDisappearing();
    }

    private async Task ScanAsync()
    {
        scanCancellation?.Cancel();
        var cancellation = scanCancellation = new CancellationTokenSource();
        preview = null;
        GroupList.Children.Clear();
        LeftoverList.Children.Clear();
        LeftoverSection.IsVisible = false;
        RemoveButton.IsEnabled = false;
        PocketDrive? pocket = selection.SelectedPocket;
        EjectButton.IsVisible = pocket is not null && eject.CanEject(pocket);
        if (pocket is null) { TargetName.Text = "No Pocket selected"; Summary.Text = "Return to the dashboard and choose a target first."; ScanButton.IsEnabled = false; return; }
        TargetName.Text = pocket.Name;
        Summary.Text = "Scanning the card for macOS metadata files…";
        ScanButton.IsEnabled = false;
        ScanningIndicator.IsVisible = ScanningIndicator.IsRunning = true;
        try
        {
            CardCleanupPreview result = await Task.Run(() => cleanup.Preview(pocket, cancellation.Token), cancellation.Token);
            if (cancellation.IsCancellationRequested || !ReferenceEquals(selection.SelectedPocket, pocket)) return;
            preview = result;
            selected.Clear();
            foreach (CleanupGroup group in result.Groups.Where(group => group.SelectedByDefault && group.Items.Count > 0)) selected.Add(group.Category);
            Render();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!cancellation.IsCancellationRequested) Summary.Text = "Could not scan the target. Reconnect it and scan again.";
        }
        finally
        {
            if (ReferenceEquals(scanCancellation, cancellation))
            {
                ScanButton.IsEnabled = true;
                ScanningIndicator.IsVisible = ScanningIndicator.IsRunning = false;
            }
        }
    }

    private void Render()
    {
        GroupList.Children.Clear();
        LeftoverList.Children.Clear();
        if (preview is null) return;
        int found = preview.Groups.Sum(group => group.Items.Count);
        long bytes = preview.Groups.Sum(group => group.Bytes);
        Summary.Text = found == 0
            ? "No macOS metadata files found. This card is clean."
            : $"{found} item{(found == 1 ? "" : "s")} found · {ByteFormat.Format(bytes)}";
        if (preview.Limited) Summary.Text += " · scan stopped at 300,000 entries; scan again after cleaning";
        if (preview.SkippedEntries > 0) Summary.Text += $" · {preview.SkippedEntries} unreadable or linked entr{(preview.SkippedEntries == 1 ? "y" : "ies")} skipped";

        foreach (CleanupGroup group in preview.Groups) GroupList.Children.Add(CreateGroup(group));

        LeftoverSection.IsVisible = preview.LeftoverFolders.Count > 0;
        foreach (LeftoverAssetFolder folder in preview.LeftoverFolders)
        {
            var row = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], Padding = new Thickness(0, 6) };
            row.Children.Add(new Label { Text = folder.RelativePath, FontSize = 12, TextColor = Ink, LineBreakMode = LineBreakMode.MiddleTruncation });
            row.Add(new Label { Text = $"{folder.FileCount} file{(folder.FileCount == 1 ? "" : "s")} · {ByteFormat.Format(folder.Bytes)}", FontSize = 11, TextColor = Quiet }, 1);
            LeftoverList.Children.Add(row);
            LeftoverList.Children.Add(new BoxView { HeightRequest = 1, Color = Rule });
        }
        UpdateRemoveButton();
    }

    private View CreateGroup(CleanupGroup group)
    {
        bool empty = group.Items.Count == 0;
        var content = new VerticalStackLayout { Spacing = 4 };
        var header = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], ColumnSpacing = 8 };
        var check = new CheckBox { IsChecked = selected.Contains(group.Category), IsEnabled = !empty && !applying, Color = Ink, VerticalOptions = LayoutOptions.Center };
        SemanticProperties.SetDescription(check, $"Remove {group.Title}");
        check.CheckedChanged += (_, args) =>
        {
            if (args.Value) selected.Add(group.Category); else selected.Remove(group.Category);
            UpdateRemoveButton();
        };
        header.Children.Add(check);
        header.Add(new Label { Text = group.Title, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = empty ? Quiet : Ink, VerticalOptions = LayoutOptions.Center }, 1);
        header.Add(new Label { Text = empty ? "NONE FOUND" : $"{group.Items.Count} · {ByteFormat.Format(group.Bytes)}", FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = empty ? Quiet : Ink, VerticalOptions = LayoutOptions.Center }, 2);
        content.Children.Add(header);
        content.Children.Add(new Label { Text = group.Detail, FontSize = 12, TextColor = Quiet, Margin = new Thickness(40, 0, 0, 0) });
        if (!empty)
        {
            var paths = string.Join(Environment.NewLine, group.Items.Take(PreviewPathLimit).Select(item => item.RelativePath));
            if (group.Items.Count > PreviewPathLimit) paths += $"{Environment.NewLine}…and {group.Items.Count - PreviewPathLimit} more";
            content.Children.Add(new Label { Text = paths, FontSize = 11, TextColor = Quiet, Margin = new Thickness(40, 4, 0, 0), MaxLines = PreviewPathLimit + 1 });
        }
        return new Border { BackgroundColor = Colors.White, Stroke = Rule, StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 2 }, Padding = new Thickness(10, 10, 14, 10), Margin = new Thickness(0, 0, 0, -1), Content = content };
    }

    private void UpdateRemoveButton()
    {
        int count = preview?.Groups.Where(group => selected.Contains(group.Category)).Sum(group => group.Items.Count) ?? 0;
        RemoveButton.IsEnabled = !applying && count > 0;
        RemoveButton.Text = count > 0 ? $"REMOVE SELECTED · {count}" : "REMOVE SELECTED";
    }

    private async void OnRemoveClicked(object? sender, EventArgs e)
    {
        CardCleanupPreview? current = preview;
        if (current is null || applying) return;
        CleanupCategory[] categories = selected.ToArray();
        int count = current.Groups.Where(group => categories.Contains(group.Category)).Sum(group => group.Items.Count);
        if (count == 0) return;
        if (categories.Contains(CleanupCategory.MacTrash)
            && !await DisplayAlertAsync("Empty the card's macOS Trash?", "Files in .Trashes are permanently deleted and cannot be restored.", "Empty Trash and remove", "Cancel"))
            return;

        applying = true;
        RemoveButton.IsEnabled = ScanButton.IsEnabled = false;
        ResultLabel.IsVisible = false;
        try
        {
            if (!await Task.Run(() => health.Inspect(current.Pocket).CanWrite))
            {
                ResultLabel.Text = "BLOCKED · The target is unavailable or incomplete. Scan it again.";
                ResultLabel.IsVisible = true;
                return;
            }
            CardCleanupResult result = await Task.Run(() => cleanup.Apply(current, categories));
            applying = false;
            await ScanAsync();
            ResultLabel.Text = result.Message;
            ResultLabel.IsVisible = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ResultLabel.Text = "Cleanup stopped because the target could not be written. Reconnect it and scan again.";
            ResultLabel.IsVisible = true;
        }
        finally
        {
            applying = false;
            ScanButton.IsEnabled = true;
            UpdateRemoveButton();
        }
    }

    private async void OnEjectClicked(object? sender, EventArgs e)
    {
        PocketDrive? pocket = selection.SelectedPocket;
        if (pocket is null) return;
        scanCancellation?.Cancel();
        EjectButton.IsEnabled = RemoveButton.IsEnabled = ScanButton.IsEnabled = false;
        EjectResult result = await eject.EjectAsync(pocket);
        ResultLabel.Text = result.Message;
        ResultLabel.IsVisible = true;
        if (result.Succeeded)
        {
            selection.Disconnect();
            preview = null;
            GroupList.Children.Clear();
            LeftoverSection.IsVisible = false;
            EjectButton.IsVisible = false;
            TargetName.Text = "Card ejected";
            Summary.Text = "Reconnect the card and scan it from the dashboard to continue.";
            return;
        }
        EjectButton.IsEnabled = ScanButton.IsEnabled = true;
        UpdateRemoveButton();
    }

    private async void OnBackClicked(object? sender, EventArgs e) => await AppNavigation.GoToAsync("..");
    private async void OnScanClicked(object? sender, EventArgs e)
    {
        ResultLabel.IsVisible = false;
        await ScanAsync();
    }
}
