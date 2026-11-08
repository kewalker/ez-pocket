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
    private CancellationTokenSource? cancellation;
    private TaskCompletionSource<string?>? answer;
    private string? selectedAnswer;
    private bool busy, active;
    private int offeredLinks;

    public PocketPluginsPage()
    {
        InitializeComponent();
        PluginPicker.ItemDisplayBinding = new Binding(nameof(PocketPlugin.Name));
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
        cancellation?.Cancel();
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

    private void OnPluginChanged(object? sender, EventArgs e) => ShowPlugin();

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
            service.Uninstall(plugin);
            RefreshPlugins();
            Status.Text = $"Removed {plugin.Name} and its private local cache.";
        }
        catch (Exception exception) { Status.Text = $"Could not remove plugin: {exception.Message}"; }
    }

    private async void OnRunClicked(object? sender, EventArgs e)
    {
        if (busy || PluginPicker.SelectedItem is not PocketPlugin plugin || selection.SelectedPocket is not { } pocket) return;
        lock (logLock) logs.Clear();
        RunLog.Text = string.Empty;
        PluginLinks.Children.Clear();
        offeredLinks = 0;
        SetBusy(true, "Starting plugin directly on the selected target…");
        CancelRunButton.IsVisible = true;
        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(300), () => { FlushLog(); return busy && active; });
        try
        {
            await service.RunDirectAsync(pocket, plugin, AskAsync, AppendLog, OfferUrl, cancellation!.Token);
            Status.Text = "Plugin finished. Check its log for the completed operations.";
        }
        catch (OperationCanceledException) { Status.Text = "Plugin stopped. Changes already made to the target were not undone."; }
        catch (Exception exception) { Status.Text = $"Plugin failed. Target files may have changed; check the log before running again. {exception.Message}"; }
        finally { answer = null; selectedAnswer = null; PromptChoices.Children.Clear(); ContinueButton.IsEnabled = false; PromptText.Text = string.Empty; PromptPanel.IsVisible = false; SetBusy(false); FlushLog(); }
    }

    private async Task<string?> AskAsync(PocketPluginPrompt question, CancellationToken token)
    {
        var response = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            answer = response;
            PromptQuery.Text = question.Query;
            selectedAnswer = null;
            PromptChoices.IsVisible = question.Choices is not null;
            PromptChoices.Children.Clear();
            ContinueButton.IsEnabled = question.Choices is null;
            string group = Guid.NewGuid().ToString("N");
            foreach (string choice in question.Choices ?? [])
            {
                var option = new RadioButton
                {
                    Content = choice,
                    GroupName = group,
                    FontSize = 14,
                    TextColor = Color.FromArgb("#151515"),
                    MinimumHeightRequest = 44,
                    HorizontalOptions = LayoutOptions.Fill
                };
                SemanticProperties.SetDescription(option, choice);
                option.CheckedChanged += (_, args) =>
                {
                    if (!args.Value) return;
                    selectedAnswer = choice;
                    ContinueButton.IsEnabled = true;
                };
                PromptChoices.Children.Add(option);
            }
            PromptText.IsVisible = question.Choices is null;
            PromptText.Text = string.Empty;
            PromptPanel.IsVisible = true;
            Status.Text = "The plugin needs an answer to continue.";
            // Let the newly visible prompt take its space before bringing it into view.
            await Task.Yield();
            if (active && !token.IsCancellationRequested)
                await PageScroll.ScrollToAsync(PromptPanel, ScrollToPosition.MakeVisible, true);
        });
        return await response.Task.WaitAsync(token);
    }

    private void OnAnswerClicked(object? sender, EventArgs e)
    {
        if (answer is null) return;
        string? value = PromptChoices.IsVisible ? selectedAnswer : PromptText.Text ?? string.Empty;
        if (value is null) return;
        PromptPanel.IsVisible = false;
        ContinueButton.IsEnabled = false;
        PromptText.Text = string.Empty;
        Status.Text = "Running plugin directly on the selected target…";
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

    private void OnCancelRunClicked(object? sender, EventArgs e) { cancellation?.Cancel(); Status.Text = "Cancelling the plugin run…"; }

    private void SetBusy(bool value, string? status = null)
    {
        busy = value;
        InstallPanel.IsEnabled = SelectionPanel.IsEnabled = !value;
        BackButton.IsEnabled = !value;
        Activity.IsVisible = Activity.IsRunning = value;
        if (status is not null) Status.Text = status;
        if (value) cancellation = new CancellationTokenSource();
        else { CancelRunButton.IsVisible = false; cancellation?.Dispose(); cancellation = null; }
        ShowPlugin();
    }

    protected override bool OnBackButtonPressed()
    {
        if (busy) { cancellation?.Cancel(); return true; }
        return base.OnBackButtonPressed();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        if (busy) return;
        await AppNavigation.GoToAsync("..");
    }
}
