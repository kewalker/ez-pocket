using System.Text;
using EzPocket.Controls;
using EzPocket.Models;
using EzPocket.Services;

namespace EzPocket;

public partial class PocketPluginsPage : ContentPage
{
    private readonly PocketPluginService service;
    private readonly PocketSelectionService selection;
    private readonly StringBuilder logs = new();
    private readonly object logLock = new();
    private PocketPluginPreview? preview;
    private CancellationTokenSource? cancellation;
    private TaskCompletionSource<string?>? answer;
    private bool busy, applying, active;
    private int offeredLinks;

    public PocketPluginsPage()
    {
        InitializeComponent();
        service = IPlatformApplication.Current?.Services.GetService<PocketPluginService>() ?? new();
        selection = IPlatformApplication.Current?.Services.GetService<PocketSelectionService>() ?? new();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        active = true;
        TargetSummary.Text = selection.SelectedPocket is { } pocket ? $"TARGET · {pocket.DisplayName}" : "TARGET · Scan or choose a Pocket on the dashboard before running a plugin.";
        RefreshPlugins();
    }

    protected override void OnDisappearing()
    {
        active = false;
        if (!applying) cancellation?.Cancel();
        if (!busy) ClearPreview();
        base.OnDisappearing();
    }

    private void RefreshPlugins(string? selectId = null)
    {
        try
        {
            string? id = selectId ?? (PluginPicker.SelectedItem as PocketPlugin)?.Id;
            var plugins = service.ListInstalled();
            PluginPicker.ItemsSource = plugins.ToList();
            PluginPicker.SelectedItem = plugins.FirstOrDefault(plugin => plugin.Id == id) ?? plugins.FirstOrDefault();
            ShowPlugin();
        }
        catch (Exception exception) { Status.Text = $"Could not read installed plugins: {exception.Message}"; }
    }

    private void OnPluginChanged(object? sender, EventArgs e) { ClearPreview(); ShowPlugin(); }

    private void ShowPlugin()
    {
        PocketPlugin? plugin = PluginPicker.SelectedItem as PocketPlugin;
        PluginDescription.Text = plugin?.Description ?? "No plugin selected.";
        PluginSource.Text = plugin is null ? string.Empty : $"{plugin.Repository} · {plugin.Version}";
        PluginNetwork.Text = plugin?.NetworkSummary ?? string.Empty;
        RunButton.IsEnabled = !busy && plugin is not null && selection.SelectedPocket is not null;
        RemoveButton.IsEnabled = !busy && plugin is not null;
    }

    private async void OnInstallClicked(object? sender, EventArgs e)
    {
        if (busy) return;
        ClearPreview();
        SetBusy(true, "Downloading the latest plugin release…");
        try
        {
            PocketPlugin plugin = await service.InstallFromGitHubAsync(RepositoryEntry.Text ?? string.Empty, cancellation!.Token);
            RefreshPlugins(plugin.Id);
            Status.Text = $"Installed {plugin.Name} locally. Review its access before running.";
        }
        catch (OperationCanceledException) { Status.Text = "Installation cancelled."; }
        catch (Exception exception) { Status.Text = $"Installation failed: {exception.Message}"; }
        finally { SetBusy(false); }
    }

    private void OnRemoveClicked(object? sender, EventArgs e)
    {
        if (busy || PluginPicker.SelectedItem is not PocketPlugin plugin) return;
        try
        {
            ClearPreview();
            service.Uninstall(plugin);
            RefreshPlugins();
            Status.Text = $"Removed {plugin.Name} and its private local cache.";
        }
        catch (Exception exception) { Status.Text = $"Could not remove plugin: {exception.Message}"; }
    }

    private async void OnRunClicked(object? sender, EventArgs e)
    {
        if (busy || PluginPicker.SelectedItem is not PocketPlugin plugin || selection.SelectedPocket is not { } pocket) return;
        ClearPreview();
        lock (logLock) logs.Clear();
        RunLog.Text = string.Empty;
        PluginLinks.Children.Clear();
        offeredLinks = 0;
        BackupSummary.Text = string.Empty;
        SetBusy(true, "Preparing the local working copy…");
        CancelRunButton.IsVisible = true;
        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(300), () => { FlushLog(); return busy && active; });
        try
        {
            preview = await service.PrepareAsync(pocket, plugin, AskAsync, AppendLog, OfferUrl, cancellation!.Token);
            if (!active) { ClearPreview(); return; }
            ReviewPanel.IsVisible = true;
            ChangeList.ItemsSource = preview.Changes;
            ReviewSummary.Text = $"{preview.Changes.Count(change => change.Action == "ADD"):N0} additions · {preview.Changes.Count(change => change.Action == "REPLACE"):N0} replacements · {preview.Changes.Count(change => change.Action == "DELETE"):N0} deletions";
            Blockers.Text = string.Join(Environment.NewLine, preview.Blockers);
            ApplyButton.IsEnabled = preview.CanApply;
            Status.Text = preview.Changes.Count == 0 ? "Plugin finished. No target file changes were generated." : "Plugin finished. Review the proposed changes below.";
        }
        catch (OperationCanceledException) { Status.Text = "Run cancelled. No target files were changed."; }
        catch (Exception exception) { Status.Text = $"Plugin run failed. No target files were changed. {exception.Message}"; }
        finally { answer = null; PromptText.Text = string.Empty; PromptPanel.IsVisible = false; SetBusy(false); FlushLog(); }
    }

    private async Task<string?> AskAsync(PocketPluginPrompt question, CancellationToken token)
    {
        var response = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            answer = response;
            PromptQuery.Text = question.Query;
            PromptChoices.IsVisible = question.Choices is not null;
            PromptChoices.ItemsSource = question.Choices?.ToList();
            PromptChoices.SelectedIndex = -1;
            PromptText.IsVisible = question.Choices is null;
            PromptText.Text = string.Empty;
            PromptPanel.IsVisible = true;
            Status.Text = "The plugin needs an answer to continue.";
        });
        return await response.Task.WaitAsync(token);
    }

    private void OnAnswerClicked(object? sender, EventArgs e)
    {
        if (answer is null) return;
        string? value = PromptChoices.IsVisible ? PromptChoices.SelectedItem as string : PromptText.Text ?? string.Empty;
        if (value is null) return;
        PromptPanel.IsVisible = false;
        PromptText.Text = string.Empty;
        Status.Text = "Running plugin against the local copy…";
        answer.TrySetResult(value);
        answer = null;
    }

    private void AppendLog(string message)
    {
        lock (logLock)
        {
            if (message.Length > 32000) message = message[^32000..];
            logs.Append(message);
            if (logs.Length > 32000) logs.Remove(0, logs.Length - 32000);
        }
    }

    private void FlushLog() { lock (logLock) RunLog.Text = logs.ToString(); }

    private void OfferUrl(Uri uri)
    {
        if (Interlocked.Increment(ref offeredLinks) > 20) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!active) return;
            var link = new EzButton { Text = $"OPEN PLUGIN LINK · {uri.Host}", FontSize = 11, HorizontalOptions = LayoutOptions.Start, ButtonBackgroundColor = Color.FromArgb("#F4F4F0"), ButtonTextColor = Color.FromArgb("#151515") };
            SemanticProperties.SetDescription(link, uri.AbsoluteUri);
            link.Clicked += async (_, _) =>
            {
                try { await Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred); }
                catch (Exception exception) { Status.Text = $"Could not open plugin link: {exception.Message}"; }
            };
            PluginLinks.Children.Add(new Label { Text = uri.AbsoluteUri, FontSize = 12, TextColor = Color.FromArgb("#60605C") });
            PluginLinks.Children.Add(link);
        });
    }

    private async void OnApplyClicked(object? sender, EventArgs e)
    {
        if (busy || preview is null || !preview.CanApply) return;
        if (!TargetPaths.Equal(selection.SelectedPocket?.RootPath, preview.TargetPath))
        {
            Status.Text = "The selected target changed. Discard this review and run again.";
            return;
        }
        applying = true;
        SetBusy(true, "Checking the target, backing up originals, and applying reviewed changes…");
        try
        {
            PocketPluginResult result = await service.ApplyAsync(preview, cancellation!.Token);
            Status.Text = result.Message;
            BackupSummary.Text = result.BackupPath is null ? string.Empty : $"LOCAL BACKUP · {result.BackupPath}";
            // Any apply attempt requires a fresh run; never encourage retrying a stale review.
            ClearPreview();
        }
        catch (Exception exception) { Status.Text = $"Could not apply plugin changes: {exception.Message}"; ClearPreview(); }
        finally { applying = false; SetBusy(false); }
    }

    private void OnCancelRunClicked(object? sender, EventArgs e) { cancellation?.Cancel(); Status.Text = "Cancelling the plugin run…"; }
    private void OnDiscardClicked(object? sender, EventArgs e) { if (!busy) { ClearPreview(); Status.Text = "Discarded staged changes. No target files were changed."; } }

    private void ClearPreview()
    {
        if (preview is not null) service.Discard(preview);
        preview = null;
        ReviewPanel.IsVisible = false;
        ChangeList.ItemsSource = null;
    }

    private void SetBusy(bool value, string? status = null)
    {
        busy = value;
        InstallPanel.IsEnabled = SelectionPanel.IsEnabled = !value;
        BackButton.IsEnabled = DiscardButton.IsEnabled = !value;
        ApplyButton.IsEnabled = !value && preview?.CanApply == true;
        Activity.IsVisible = Activity.IsRunning = value;
        if (status is not null) Status.Text = status;
        if (value) cancellation = new CancellationTokenSource();
        else { CancelRunButton.IsVisible = false; cancellation?.Dispose(); cancellation = null; }
        ShowPlugin();
    }

    protected override bool OnBackButtonPressed()
    {
        if (busy) { if (!applying) cancellation?.Cancel(); return true; }
        ClearPreview();
        return base.OnBackButtonPressed();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        if (busy) return;
        ClearPreview();
        await AppNavigation.GoToAsync("..");
    }
}
