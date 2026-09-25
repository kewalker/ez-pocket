using EzPocket.Models;
using EzPocket.Services;
using EzPocket.Controls;

namespace EzPocket;

public partial class SaveVaultPage : ContentPage
{
    private readonly PocketSelectionService selection;
    private readonly SaveVaultService saveVault;
    private readonly PocketHealthService health;
    private SaveVaultPreview? preview;
    private SaveVaultRestorePreview? restorePreview;
    private readonly HashSet<string> selectedRestorePaths = new(StringComparer.OrdinalIgnoreCase);

    public SaveVaultPage()
    {
        InitializeComponent();
        selection = IPlatformApplication.Current?.Services.GetService<PocketSelectionService>() ?? new PocketSelectionService();
        saveVault = IPlatformApplication.Current?.Services.GetService<SaveVaultService>() ?? new SaveVaultService();
        health = IPlatformApplication.Current?.Services.GetService<PocketHealthService>() ?? new PocketHealthService();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadTarget();
    }

    protected override void OnDisappearing()
    {
        SuccessToast.Hide();
        base.OnDisappearing();
    }

    private void LoadTarget()
    {
        restorePreview = null;
        selectedRestorePaths.Clear();
        RestorePanel.IsVisible = false;
        PocketDrive? pocket = selection.SelectedPocket;
        if (pocket is null)
        {
            TargetName.Text = "No Pocket selected";
            TargetDetail.Text = "Return to the dashboard and choose a target first.";
            SavesSummary.Text = "Unavailable";
            MemoriesSummary.Text = "Unavailable";
            SnapshotSummary.Text = "Select a target to inspect its save data.";
            CreateSnapshotButton.IsEnabled = false;
            SnapshotList.Children.Clear();
            EmptySnapshots.IsVisible = true;
            RestorePanel.IsVisible = false;
            return;
        }

        preview = saveVault.Preview(pocket);
        TargetName.Text = pocket.LooksLikePocket ? pocket.Name : "New Pocket target";
        TargetDetail.Text = pocket.RootPath;
        SavesSummary.Text = preview.Sources.Single(source => source.RelativePath == "Saves").Summary;
        MemoriesSummary.Text = preview.Sources.Single(source => source.RelativePath == "Memories").Summary;
        SnapshotSummary.Text = preview.HasUnavailableSources
            ? "One or more save sources could not be read. Reconnect the target and try again."
            : preview.HasContent
            ? $"Snapshot ready: {preview.Summary}."
            : "No snapshot can be created until Saves or Memories contain files.";
        CreateSnapshotButton.IsEnabled = preview.CanCreate;
        RenderSnapshots(saveVault.List(pocket));
    }

    private async void OnCreateSnapshotClicked(object? sender, EventArgs e)
    {
        if (preview is null || !preview.HasContent) return;
        CreateSnapshotButton.IsEnabled = false;
        CreatingIndicator.IsVisible = CreatingIndicator.IsRunning = true;
        Status.Text = "Creating a local snapshot…";
        try
        {
            SaveVaultResult result = await saveVault.CreateSnapshotAsync(preview);
            Status.Text = result.Message;
            if (result.Succeeded)
            {
                SuccessToast.ShowSuccess("Save Vault snapshot created.");
                RenderSnapshots(saveVault.List(preview.Pocket));
            }
        }
        finally
        {
            CreatingIndicator.IsVisible = CreatingIndicator.IsRunning = false;
            CreateSnapshotButton.IsEnabled = preview.CanCreate;
        }
    }

    private void RenderSnapshots(IReadOnlyList<SaveVaultSnapshot> snapshots)
    {
        SnapshotList.Children.Clear();
        EmptySnapshots.IsVisible = snapshots.Count == 0;
        foreach (SaveVaultSnapshot snapshot in snapshots.Take(5))
        {
            var title = new Label { Text = snapshot.CreatedAt.ToLocalTime().ToString("MMM d, yyyy · h:mm tt"), FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#151515") };
            var detail = new Label { Text = snapshot.Summary, FontSize = 12, TextColor = Color.FromArgb("#60605C") };
            var previewRestore = new EzButton
            {
                Text = "PREVIEW RESTORE",
                FontSize = 11,
                MinimumHeight = 30,
                ButtonPadding = new Thickness(10, 4),
                CornerRadius = 2,
                ButtonBackgroundColor = Color.FromArgb("#E5E5DF"),
                ButtonTextColor = Color.FromArgb("#151515"),
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalOptions = LayoutOptions.Start
            };
            previewRestore.Clicked += async (_, _) => await ShowRestorePreviewAsync(snapshot);
            SnapshotList.Children.Add(new Border
            {
                BackgroundColor = Colors.White,
                Stroke = Color.FromArgb("#C9C9C2"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 2 },
                Padding = new Thickness(14, 10),
                Content = new VerticalStackLayout { Spacing = 2, Children = { title, detail, previewRestore } }
            });
        }
    }

    private async Task ShowRestorePreviewAsync(SaveVaultSnapshot snapshot)
    {
        PocketDrive? pocket = selection.SelectedPocket;
        if (pocket is null) return;
        Status.Text = "Comparing snapshot with the current target…";
        restorePreview = await saveVault.PreviewRestoreAsync(pocket, snapshot);
        Status.Text = string.Empty;
        selectedRestorePaths.Clear();
        RenderRestorePreview();
    }

    private void RenderRestorePreview()
    {
        if (restorePreview is null) { RestorePanel.IsVisible = false; return; }
        RestorePanel.IsVisible = true;
        RestoreSnapshotName.Text = restorePreview.Snapshot.CreatedAt.ToLocalTime().ToString("MMM d, yyyy · h:mm tt");
        RestoreSummary.Text = restorePreview.CanRestore
            ? $"{restorePreview.Summary} Select the individual files you want to restore."
            : restorePreview.Summary;
        RestoreList.Children.Clear();
        foreach (SaveVaultRestoreEntry entry in restorePreview.Entries)
        {
            var detail = new Label { Text = entry.Summary, FontSize = 12, TextColor = Color.FromArgb("#60605C") };
            var path = new Label { Text = entry.RelativePath, FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#151515"), LineBreakMode = LineBreakMode.TailTruncation };
            var content = new VerticalStackLayout { Spacing = 2, Children = { path, detail } };
            if (entry.CanRestore)
            {
                var select = new CheckBox { IsChecked = selectedRestorePaths.Contains(entry.RelativePath), Color = Color.FromArgb("#151515"), VerticalOptions = LayoutOptions.Start };
                select.CheckedChanged += (_, args) =>
                {
                    if (args.Value) selectedRestorePaths.Add(entry.RelativePath);
                    else selectedRestorePaths.Remove(entry.RelativePath);
                    UpdateRestoreSelectionUi();
                };
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
                    ColumnSpacing = 7,
                    Padding = new Thickness(0, 7)
                };
                var card = new Border { Stroke = Color.FromArgb("#C9C9C2"), StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 2 }, Padding = new Thickness(10, 7), Content = content };
                row.Children.Add(select);
                row.Children.Add(card);
                Grid.SetColumn(card, 1);
                RestoreList.Children.Add(row);
            }
            else
            {
                RestoreList.Children.Add(new Border { Stroke = Color.FromArgb("#C9C9C2"), StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 2 }, Padding = new Thickness(10, 7), Margin = new Thickness(0, 3), Content = content });
            }
        }
        SelectRestoreChangesButton.IsEnabled = restorePreview.CanRestore;
        ClearRestoreSelectionButton.IsEnabled = selectedRestorePaths.Count > 0;
        UpdateRestoreSelectionUi();
    }

    private void UpdateRestoreSelectionUi()
    {
        int count = selectedRestorePaths.Count;
        RestoreSelectedButton.Text = count == 0 ? "RESTORE SELECTED" : $"RESTORE {count} SELECTED";
        RestoreSelectedButton.IsEnabled = restorePreview?.CanRestore == true && count > 0;
    }

    private void OnSelectRestoreChangesClicked(object? sender, EventArgs e)
    {
        if (restorePreview is null) return;
        selectedRestorePaths.Clear();
        foreach (SaveVaultRestoreEntry entry in restorePreview.Entries.Where(entry => entry.CanRestore)) selectedRestorePaths.Add(entry.RelativePath);
        RenderRestorePreview();
    }

    private void OnClearRestoreSelectionClicked(object? sender, EventArgs e)
    {
        if (restorePreview is null) return;
        selectedRestorePaths.Clear();
        RenderRestorePreview();
    }

    private async void OnRestoreSelectedClicked(object? sender, EventArgs e)
    {
        if (restorePreview is null || selectedRestorePaths.Count == 0) return;
        PocketDrive? pocket = selection.SelectedPocket;
        if (pocket is null
            || !string.Equals(pocket.RootPath, restorePreview.Pocket.RootPath, StringComparison.OrdinalIgnoreCase)
            || !health.Inspect(pocket).CanWrite)
        {
            Status.Text = "RESTORE BLOCKED · The selected target changed or is unavailable. Scan it again and review the restore anew.";
            RestoreSelectedButton.IsEnabled = false;
            return;
        }
        bool confirmed = await DisplayAlert(
            "Restore selected saves",
            $"Restore {selectedRestorePaths.Count} selected save file{(selectedRestorePaths.Count == 1 ? string.Empty : "s")} to {restorePreview.Pocket.Name}? Changed files are first captured in a new local safety snapshot. Target-only files will be kept.",
            "Restore", "Cancel");
        if (!confirmed) return;

        RestoreSelectedButton.IsEnabled = false;
        Status.Text = "Creating a safety snapshot and restoring selected files…";
        SaveVaultRestoreResult result = await saveVault.RestoreAsync(restorePreview, selectedRestorePaths);
        Status.Text = result.Message;
        if (result.Succeeded)
        {
            SuccessToast.ShowSuccess("Selected saves restored.");
            restorePreview = null;
            selectedRestorePaths.Clear();
            LoadTarget();
        }
        else RestoreSelectedButton.IsEnabled = true;
    }

    private async void OnBackClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}
