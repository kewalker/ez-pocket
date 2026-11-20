using EzPocket.Models;
using EzPocket.Services;

namespace EzPocket;

public partial class CoreReadinessPage : ContentPage
{
    private static readonly Color Ink = Color.FromArgb("#151515");
    private static readonly Color Quiet = Color.FromArgb("#60605C");
    private static readonly Color Rule = Color.FromArgb("#C9C9C2");
    private static readonly Color Wash = Color.FromArgb("#F4F4F0");
    private readonly PocketSelectionService selection;
    private readonly CoreReadinessService readiness;
    private CoreReadinessReport? report;
    private bool showReady;
    private int generation;

    public CoreReadinessPage()
    {
        InitializeComponent();
        selection = IPlatformApplication.Current?.Services.GetService<PocketSelectionService>() ?? new PocketSelectionService();
        readiness = IPlatformApplication.Current?.Services.GetService<CoreReadinessService>() ?? new CoreReadinessService();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    protected override void OnDisappearing()
    {
        generation++;
        CheckingIndicator.IsVisible = CheckingIndicator.IsRunning = false;
        CheckButton.IsEnabled = true;
        base.OnDisappearing();
    }

    private async Task LoadAsync()
    {
        int current = ++generation;
        report = null;
        CoreList.Children.Clear();
        ShowReadyButton.IsVisible = false;
        PocketDrive? pocket = selection.SelectedPocket;
        if (pocket is null) { TargetName.Text = "No Pocket selected"; Summary.Text = "Return to the dashboard and choose a target first."; CheckButton.IsEnabled = false; return; }
        TargetName.Text = pocket.Name;
        Summary.Text = "Reading installed core definitions…";
        CheckButton.IsEnabled = false;
        CheckingIndicator.IsVisible = CheckingIndicator.IsRunning = true;
        try
        {
            CoreReadinessReport result = await Task.Run(() => readiness.Inspect(pocket));
            if (current != generation || !ReferenceEquals(selection.SelectedPocket, pocket)) return;
            report = result;
            Render();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (current == generation) Summary.Text = "Could not read the target. Reconnect it and check again.";
        }
        finally
        {
            if (current == generation)
            {
                CheckButton.IsEnabled = true;
                CheckingIndicator.IsVisible = CheckingIndicator.IsRunning = false;
            }
        }
    }

    private void Render()
    {
        CoreList.Children.Clear();
        if (report is null) return;
        Summary.Text = report.Summary + (report.Limited ? " · only the first 500 cores were checked" : "");
        CoreReadiness[] attention = report.Cores.Where(core => core.NeedsAction).ToArray();
        CoreReadiness[] ready = report.Cores.Where(core => !core.NeedsAction).ToArray();
        ListHeading.Text = attention.Length > 0 ? $"NEEDS ATTENTION · {attention.Length}" : "ALL CORES READY";
        foreach (CoreReadiness core in attention) CoreList.Children.Add(CreateRow(core));
        if (attention.Length == 0 && report.Cores.Count > 0)
            CoreList.Children.Add(new Label { Text = "Every installed core has the fixed files it declares.", FontSize = 13, TextColor = Quiet, Margin = new Thickness(0, 0, 0, 8) });
        ShowReadyButton.IsVisible = ready.Length > 0;
        ShowReadyButton.Text = showReady ? "HIDE READY CORES" : $"SHOW READY CORES · {ready.Length}";
        if (!showReady) return;
        CoreList.Children.Add(new Label { Text = $"READY · {ready.Length}", FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = Ink, Margin = new Thickness(0, 16, 0, 6) });
        foreach (CoreReadiness core in ready) CoreList.Children.Add(CreateRow(core));
    }

    private View CreateRow(CoreReadiness core)
    {
        var content = new VerticalStackLayout { Spacing = 4 };
        var header = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], ColumnSpacing = 12 };
        var names = new VerticalStackLayout { Spacing = 1 };
        names.Children.Add(new Label { Text = core.Name, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Ink });
        names.Children.Add(new Label { Text = string.IsNullOrEmpty(core.Version) ? core.Identifier : $"{core.Identifier} · {core.Version}", FontSize = 11, TextColor = Quiet });
        header.Children.Add(names);
        var state = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
        state.Children.Add(new BoxView { WidthRequest = 7, HeightRequest = 7, VerticalOptions = LayoutOptions.Center, Color = core.NeedsAction ? Ink : Color.FromArgb("#25B46B") });
        state.Children.Add(new Label { Text = core.StateLabel, FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = Ink, VerticalOptions = LayoutOptions.Center });
        header.Add(state, 1);
        content.Children.Add(header);
        content.Children.Add(new Label { Text = core.Summary, FontSize = 12, TextColor = Quiet });

        foreach (CoreRequiredFile file in core.Files.Where(file => core.NeedsAction || file.State != RequiredFileState.OptionalAbsent))
        {
            var line = new Grid { ColumnDefinitions = [new ColumnDefinition(new GridLength(104)), new ColumnDefinition(GridLength.Star)], ColumnSpacing = 10, Margin = new Thickness(0, 6, 0, 0) };
            line.Children.Add(new Label { Text = file.StateLabel, FontSize = 10, FontAttributes = FontAttributes.Bold, TextColor = file.NeedsAction ? Ink : Quiet });
            var text = new VerticalStackLayout { Spacing = 1 };
            text.Children.Add(new Label { Text = $"{file.FileName}  ·  {file.SlotName}{(file.Required ? "" : " (optional)")}", FontSize = 12, FontAttributes = file.NeedsAction ? FontAttributes.Bold : FontAttributes.None, TextColor = Ink });
            text.Children.Add(new Label { Text = file.ExpectedRelativePath, FontSize = 11, TextColor = Quiet });
            if (file.State != RequiredFileState.Present) text.Children.Add(new Label { Text = file.Detail, FontSize = 11, TextColor = Quiet });
            line.Add(text, 1);
            content.Children.Add(line);
        }
        foreach (string note in core.Notes)
            content.Children.Add(new Label { Text = note, FontSize = 11, TextColor = Quiet, Margin = new Thickness(0, 4, 0, 0) });

        return new Border
        {
            BackgroundColor = core.NeedsAction ? Wash : Colors.White,
            Stroke = Rule,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 2 },
            Padding = new Thickness(14, 10),
            Margin = new Thickness(0, 0, 0, -1),
            Content = content
        };
    }

    private void OnShowReadyClicked(object? sender, EventArgs e)
    {
        showReady = !showReady;
        Render();
    }

    private async void OnBackClicked(object? sender, EventArgs e) => await AppNavigation.GoToAsync("..");
    private async void OnCheckClicked(object? sender, EventArgs e) => await LoadAsync();
}
