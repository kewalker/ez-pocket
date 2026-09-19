using System.Diagnostics;

namespace EzPocket.Controls;

public partial class EzToast : ContentView
{
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(6);
    private CancellationTokenSource? dismissalCancellation;

    public EzToast()
    {
        InitializeComponent();
        Unloaded += (_, _) => Hide();
    }

    public void ShowSuccess(string message, TimeSpan? duration = null)
    {
        Hide();
        var cancellation = new CancellationTokenSource();
        dismissalCancellation = cancellation;
        MessageLabel.Text = message;
        Timer.Progress = 1;
        IsVisible = true;
        _ = DismissAfterAsync(cancellation, duration ?? DefaultDuration);
    }

    public void Hide()
    {
        dismissalCancellation?.Cancel();
        dismissalCancellation = null;
        IsVisible = false;
    }

    private void OnDismissClicked(object? sender, EventArgs e) => Hide();

    private async Task DismissAfterAsync(CancellationTokenSource cancellation, TimeSpan duration)
    {
        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < duration)
            {
                Dispatcher.Dispatch(() => Timer.Progress = Math.Max(0, 1 - stopwatch.Elapsed.TotalMilliseconds / duration.TotalMilliseconds));
                await Task.Delay(TimeSpan.FromMilliseconds(8), cancellation.Token);
            }
            if (ReferenceEquals(dismissalCancellation, cancellation)) Hide();
        }
        catch (OperationCanceledException)
        {
            // A new toast, dismissal, or page unload replaced this notification.
        }
        finally
        {
            cancellation.Dispose();
        }
    }
}
