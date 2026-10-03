using Microsoft.Maui.Hosting;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
using EzPocket.Models;
using EzPocket.Services;

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

                Gtk.Widget? windowContent = gtkWindow.GetChild();
                int previousWidth = windowContent?.GetAllocatedWidth() ?? 0;
                int previousHeight = windowContent?.GetAllocatedHeight() ?? 0;
                GtkLayoutPanel? previousPanel = null;
                gtkWindow.AddTickCallback((_, _) =>
                {
                    int width = windowContent?.GetAllocatedWidth() ?? 0;
                    int height = windowContent?.GetAllocatedHeight() ?? 0;
                    if (width < 1 || height < 1)
                        return true;

                    if (window is Microsoft.Maui.Controls.Window { Page: NavigationPage navigation }
                        && navigation.CurrentPage is ContentPage page
                        && page.Content is Layout content
                        && content.Handler?.PlatformView is GtkLayoutPanel panel)
                    {
                        if (width == previousWidth && height == previousHeight && ReferenceEquals(panel, previousPanel))
                            return true;

                        previousWidth = width;
                        previousHeight = height;
                        previousPanel = panel;
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
        string? inventoryMarker = Environment.GetEnvironmentVariable("EZPOCKET_INVENTORY_SMOKE_MARKER");
        string? returnMarker = Environment.GetEnvironmentVariable("EZPOCKET_RETURN_SMOKE_MARKER");
        if (inventoryMarker is not null || returnMarker is not null)
        {
            var selection = IPlatformApplication.Current?.Services.GetRequiredService<PocketSelectionService>()
                ?? throw new InvalidOperationException("Pocket selection service is unavailable.");
            selection.Select(new PocketDrive(Path.GetTempPath(), "CI Pocket", DriveType.Unknown,
                0, 0, 2, ["Assets", "Cores"], 1, ["example.core"]));
        }
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
        if (inventoryMarker is not null && Application.Windows.FirstOrDefault() is Microsoft.Maui.Controls.Window inventoryWindow)
        {
            inventoryWindow.Dispatcher.Dispatch(async () =>
            {
                try
                {
                    await AppNavigation.GoToAsync("CorePage");
                    for (int attempt = 0; attempt < 45; attempt++)
                    {
                        if (inventoryWindow.Page?.Navigation.NavigationStack.LastOrDefault() is CorePage { HasPopulatedInventory: true })
                        {
                            File.WriteAllText(inventoryMarker, "CorePage populated");
                            return;
                        }
                        await Task.Delay(TimeSpan.FromSeconds(1));
                    }
                    throw new InvalidOperationException("Core inventory did not populate within 45 seconds.");
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"Linux inventory smoke check failed: {exception}");
                    Environment.Exit(1);
                }
            });
        }
        if (returnMarker is not null && Application.Windows.FirstOrDefault() is Microsoft.Maui.Controls.Window returnWindow)
        {
            returnWindow.Dispatcher.Dispatch(async () =>
            {
                try
                {
                    await AppNavigation.GoToAsync("CorePage");
                    await AppNavigation.GoToAsync("CoreReviewPage");
                    File.WriteAllText(returnMarker + ".review", "CoreReviewPage");
                    for (int attempt = 0; attempt < 30 && !File.Exists(returnMarker + ".back"); attempt++)
                        await Task.Delay(TimeSpan.FromSeconds(1));
                    if (!File.Exists(returnMarker + ".back"))
                        throw new InvalidOperationException("Resize signal was not received.");

                    await AppNavigation.GoToAsync("..");
                    for (int attempt = 0; attempt < 30; attempt++)
                    {
                        if (returnWindow.Page?.Navigation.NavigationStack.LastOrDefault() is CorePage { HasPopulatedInventory: true })
                        {
                            await Task.Delay(TimeSpan.FromSeconds(2));
                            File.WriteAllText(returnMarker, "CorePage restored");
                            return;
                        }
                        await Task.Delay(TimeSpan.FromSeconds(1));
                    }
                    throw new InvalidOperationException("Core inventory did not return after review.");
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"Linux return smoke check failed: {exception}");
                    Environment.Exit(1);
                }
            });
        }
#endif
    }

    public static void Main(string[] args) => new Program().Run(args);
}
