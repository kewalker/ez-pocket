using Microsoft.Maui.Hosting;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace EzPocket;

public sealed class Program : GtkMauiApplication
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    protected override void OnStarted()
    {
        foreach (var window in Application.Windows)
        {
            if (window.Handler?.PlatformView is Gtk.Window gtkWindow)
            {
                gtkWindow.SetSizeRequest(-1, -1);
                gtkWindow.SetResizable(true);

                int previousWidth = gtkWindow.GetAllocatedWidth();
                int previousHeight = gtkWindow.GetAllocatedHeight();
                gtkWindow.AddTickCallback((_, _) =>
                {
                    int width = gtkWindow.GetAllocatedWidth();
                    int height = gtkWindow.GetAllocatedHeight();
                    if (width < 1 || height < 1 || (width == previousWidth && height == previousHeight))
                        return true;

                    previousWidth = width;
                    previousHeight = height;
                    if (window is Microsoft.Maui.Controls.Window { Page: NavigationPage navigation }
                        && navigation.CurrentPage is ContentPage page
                        && page.Content is Layout content
                        && content.Handler?.PlatformView is GtkLayoutPanel panel)
                    {
                        content.InvalidateMeasure();
                        panel.CrossPlatformMeasure(width, height);
                        panel.CrossPlatformArrange(new Rect(0, 0, width, height));
                    }
                    return true;
                });
            }
        }

#if DEBUG
        string? navigationMarker = Environment.GetEnvironmentVariable("EZPOCKET_NAVIGATION_SMOKE_MARKER");
        if (navigationMarker is not null && Application.Windows.FirstOrDefault() is Microsoft.Maui.Controls.Window testWindow)
        {
            testWindow.Dispatcher.Dispatch(async () =>
            {
                try
                {
                    await AppNavigation.GoToAsync("CorePage");
                    if (testWindow.Page?.Navigation.NavigationStack.LastOrDefault() is not CorePage)
                        throw new InvalidOperationException("CorePage was not pushed onto the navigation stack.");
                    File.WriteAllText(navigationMarker, "CorePage");
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"Linux navigation smoke check failed: {exception}");
                    Environment.Exit(1);
                }
            });
        }
#endif
    }

    public static void Main(string[] args) => new Program().Run(args);
}
