using EzPocket.Models;
using EzPocket.Services;
using EzPocket.Controls;
using System.Security.Cryptography;
using System.Text;

namespace EzPocket;

public partial class PocketHealthPage : ContentPage
{
    private readonly PocketSelectionService selection;
    private readonly PocketHealthService health;
    private readonly PocketInitializationService initialization;
    private readonly PocketScanner scanner;
    public PocketHealthPage()
    {
        InitializeComponent();
        selection = IPlatformApplication.Current?.Services.GetService<PocketSelectionService>() ?? new PocketSelectionService();
        health = IPlatformApplication.Current?.Services.GetService<PocketHealthService>() ?? new PocketHealthService();
        initialization = IPlatformApplication.Current?.Services.GetService<PocketInitializationService>() ?? new PocketInitializationService();
        scanner = IPlatformApplication.Current?.Services.GetService<PocketScanner>() ?? new PocketScanner();
    }
    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadReport();
    }

    private void LoadReport()
    {
        FindingList.Children.Clear();
        PocketDrive? pocket = selection.SelectedPocket;
        if (pocket is null) { TargetName.Text = "No Pocket selected"; Summary.Text = "Return to the dashboard and choose a target first."; InitializeButton.IsVisible = false; return; }
        PocketHealthReport report = health.Inspect(pocket);
        TargetName.Text = pocket.Name;
        InitializeButton.IsVisible = false;
        InitializeButton.IsEnabled = true;
        int ignoredCount = 0;
        int activeAttentionCount = 0;
        foreach (PocketHealthFinding finding in report.Findings)
        {
            bool ignored = !finding.IsBlocking && IsIgnored(pocket, finding);
            ignoredCount += ignored ? 1 : 0;
            activeAttentionCount += finding.RequiresAttention && !ignored ? 1 : 0;
            FindingList.Children.Add(CreateFindingCard(pocket, finding, ignored));
        }
        Summary.Text = activeAttentionCount == 0
            ? ignoredCount == 0 ? "Ready for reviewed maintenance" : $"No active findings · {ignoredCount} ignored locally"
            : $"{activeAttentionCount} active item{(activeAttentionCount == 1 ? string.Empty : "s")} need attention{(ignoredCount == 0 ? string.Empty : $" · {ignoredCount} ignored locally")}";
    }

    private Border CreateFindingCard(PocketDrive pocket, PocketHealthFinding finding, bool ignored)
    {
        var content = new VerticalStackLayout { Spacing = 5 };
        content.Children.Add(new Label { Text = ignored ? "IGNORED" : finding.Severity, FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(ignored ? "#60605C" : "#151515") });
        content.Children.Add(new Label { Text = finding.Title, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#151515") });
        content.Children.Add(new Label { Text = finding.Detail, FontSize = 12, TextColor = Color.FromArgb("#60605C") });

        if (ignored)
        {
            var restore = CreateButton("RESTORE FINDING", false);
            restore.Clicked += (_, _) => { SetIgnored(pocket, finding, false); LoadReport(); };
            content.Children.Add(restore);
        }
        else
        {
            var actions = new HorizontalStackLayout { Spacing = 7, Margin = new Thickness(0, 5, 0, 0) };
            string? actionLabel = ActionLabel(finding.Action);
            if (actionLabel is not null)
            {
                var action = CreateButton(actionLabel, true);
                action.Clicked += async (_, _) => await HandleFindingActionAsync(finding);
                actions.Children.Add(action);
            }
            if (finding.RequiresAttention && !finding.IsBlocking)
            {
                var ignore = CreateButton("IGNORE", false);
                ignore.Clicked += (_, _) => { SetIgnored(pocket, finding, true); LoadReport(); };
                actions.Children.Add(ignore);
            }
            if (actions.Children.Count > 0) content.Children.Add(actions);
        }

        return new Border { BackgroundColor = finding.RequiresAttention && !ignored ? Color.FromArgb("#F4F4F0") : Colors.White, Stroke = Color.FromArgb("#C9C9C2"), StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 2 }, Padding = new Thickness(14, 10), Content = content };
    }

    private static EzButton CreateButton(string text, bool primary) => new()
    {
        Text = text,
        FontSize = 11,
        MinimumHeight = 30,
        ButtonPadding = new Thickness(10, 4),
        CornerRadius = 2,
        ButtonBackgroundColor = primary ? Color.FromArgb("#151515") : Color.FromArgb("#E5E5DF"),
        ButtonTextColor = primary ? Colors.White : Color.FromArgb("#151515")
    };

    private static string? ActionLabel(PocketHealthAction action) => action switch
    {
        PocketHealthAction.InitializeTarget => "INITIALIZE TARGET",
        PocketHealthAction.ManageCores => "MANAGE CORES",
        PocketHealthAction.ManageAssets => "MANAGE ASSETS",
        PocketHealthAction.ReturnToTarget => "RETURN TO TARGET",
        _ => null
    };

    private async Task HandleFindingActionAsync(PocketHealthFinding finding)
    {
        switch (finding.Action)
        {
            case PocketHealthAction.InitializeTarget:
                await InitializeTargetAsync();
                break;
            case PocketHealthAction.ManageCores:
                await Shell.Current.GoToAsync("CorePage");
                break;
            case PocketHealthAction.ManageAssets:
                await Shell.Current.GoToAsync("AssetsPage");
                break;
            case PocketHealthAction.ReturnToTarget:
                await Shell.Current.GoToAsync("..");
                break;
        }
    }

    private static string PreferenceKey(PocketDrive pocket, PocketHealthFinding finding)
    {
        string value = $"{pocket.RootPath}|{finding.Key}|{finding.Title}";
        return $"pocket-health-ignore-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))}";
    }

    private static bool IsIgnored(PocketDrive pocket, PocketHealthFinding finding) => Preferences.Default.Get(PreferenceKey(pocket, finding), false);
    private static void SetIgnored(PocketDrive pocket, PocketHealthFinding finding, bool ignored) => Preferences.Default.Set(PreferenceKey(pocket, finding), ignored);
    private async void OnInitializeClicked(object? sender, EventArgs e) => await InitializeTargetAsync();

    private async Task InitializeTargetAsync()
    {
        PocketDrive? pocket = selection.SelectedPocket;
        if (pocket is null) return;
        PocketInitializationPreview preview = initialization.Preview(pocket);
        if (!preview.IsRequired) { LoadReport(); return; }
        bool confirmed = await DisplayAlert("Initialize target", $"Create these empty Pocket folders on {pocket.Name}: {string.Join(", ", preview.MissingFolders)}? Existing files will not be changed.", "Initialize", "Cancel");
        if (!confirmed) return;
        InitializeButton.IsEnabled = false;
        try
        {
            initialization.Initialize(pocket);
            PocketDrive? refreshed = scanner.ScanFolder(pocket.RootPath);
            if (refreshed is not null) selection.Select(refreshed);
            LoadReport();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Summary.Text = "Could not initialize this target. Check that the storage is available and writable.";
            InitializeButton.IsEnabled = true;
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}
