using Microsoft.Maui.Hosting;
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
            }
        }
    }

    public static void Main(string[] args) => new Program().Run(args);
}
