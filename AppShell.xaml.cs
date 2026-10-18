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
		Routing.RegisterRoute("AssetsPage", typeof(AssetsPage));
		Routing.RegisterRoute("AssetReviewPage", typeof(AssetReviewPage));
		Routing.RegisterRoute("PalettePackPage", typeof(PalettePackPage));
		Routing.RegisterRoute("PaletteStudioPage", typeof(PaletteStudioPage));
		Routing.RegisterRoute("AssetSetPage", typeof(AssetSetPage));
		Routing.RegisterRoute("AssetRemovalReviewPage", typeof(AssetRemovalReviewPage));
		Routing.RegisterRoute("SaveVaultPage", typeof(SaveVaultPage));
		Routing.RegisterRoute("PocketHealthPage", typeof(PocketHealthPage));
		Routing.RegisterRoute("QuickStartPage", typeof(QuickStartPage));
	}
}
