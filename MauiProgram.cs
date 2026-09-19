using EzPocket.Services;
using Microsoft.Extensions.Logging;

namespace EzPocket;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
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
        builder.Services.AddSingleton<FirmwareUpdateService>();
#if WINDOWS
        builder.Services.AddSingleton<IFolderPickerService, WindowsFolderPickerService>();
#else
        builder.Services.AddSingleton<IFolderPickerService, UnsupportedFolderPickerService>();
#endif

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
