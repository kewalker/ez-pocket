using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace EzPocket.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		UnhandledException += (_, args) => WriteStartupException(args.Exception);
		this.InitializeComponent();
	}

	private static void WriteStartupException(Exception exception)
	{
		// Opt-in diagnostic capture, including inner exceptions, before MAUI services exist.
		string? path = Environment.GetEnvironmentVariable("EZPOCKET_STARTUP_LOG");
		if (string.IsNullOrWhiteSpace(path)) return;
		try { System.IO.File.AppendAllText(path, exception.ToString() + Environment.NewLine); }
		catch (System.IO.IOException) { }
		catch (UnauthorizedAccessException) { }
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

