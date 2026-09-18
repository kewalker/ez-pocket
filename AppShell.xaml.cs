namespace EzPocket;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute("CorePage", typeof(CorePage));
		Routing.RegisterRoute("CoreDetailsPage", typeof(CoreDetailsPage));
		Routing.RegisterRoute("CoreReviewPage", typeof(CoreReviewPage));
		Routing.RegisterRoute("FirmwarePage", typeof(FirmwarePage));
	}
}
