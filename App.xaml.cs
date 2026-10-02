namespace EzPocket;

public partial class App : Application
{
	public App()
	{
#if LINUX
		UserAppTheme = AppTheme.Light;
#endif
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
#if LINUX
		var home = new MainPage();
		NavigationPage.SetHasNavigationBar(home, false);
		return new Window(new NavigationPage(home)) { Title = "EzPocket" };
#else
		return new Window(new AppShell());
#endif
	}
}
