using EzPocket.Services;
using Microsoft.Extensions.Logging;
#if LINUX
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
#endif

namespace EzPocket;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
#if LINUX
        builder.UseMauiAppLinuxGtk4<App>()
            .AddLinuxGtk4Essentials();
        LinuxTextStyles.Register();
#else
        builder.UseMauiApp<App>();
#endif
        builder
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<IAppDiagnostics, AppDiagnosticsService>();
        builder.Services.AddSingleton<PocketScanner>();
        builder.Services.AddSingleton<PocketSelectionService>();
        builder.Services.AddSingleton<PocketInitializationService>();
        builder.Services.AddSingleton<CoreInventoryService>();
        builder.Services.AddSingleton<CoreSelectionService>();
        builder.Services.AddSingleton<FeaturedCoreSetService>();
        builder.Services.AddSingleton<CoreSyncService>();
        builder.Services.AddSingleton<AssetService>();
        builder.Services.AddSingleton<AssetImportSelectionService>();
        builder.Services.AddSingleton<FirmwareUpdateService>();
        builder.Services.AddSingleton<SaveVaultService>();
        builder.Services.AddSingleton<PocketHealthService>();
#if WINDOWS
        builder.Services.AddSingleton<IFolderPickerService, WindowsFolderPickerService>();
#elif LINUX
        builder.Services.AddSingleton<IFolderPickerService, LinuxFolderPickerService>();
#else
        builder.Services.AddSingleton<IFolderPickerService, UnsupportedFolderPickerService>();
#endif

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
