namespace EzPocket;

internal static class AppNavigation
{
    public static async Task GoToAsync(string route)
    {
#if LINUX
        var navigation = Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation
            ?? throw new InvalidOperationException("The app window is not ready for navigation.");

        if (route == "//MainPage")
        {
            await navigation.PopToRootAsync();
            return;
        }

        if (route is ".." or "../..")
        {
            int levels = route == ".." ? 1 : 2;
            for (int i = 0; i < levels && navigation.NavigationStack.Count > 1; i++)
                await navigation.PopAsync();
            return;
        }

        Page page = route switch
        {
            "CorePage" => new CorePage(),
            "CoreDetailsPage" => new CoreDetailsPage(),
            "CoreReviewPage" => new CoreReviewPage(),
            "FirmwarePage" => new FirmwarePage(),
            "AssetsPage" => new AssetsPage(),
            "AssetReviewPage" => new AssetReviewPage(),
            "PalettePackPage" => new PalettePackPage(),
            "AssetSetPage" => new AssetSetPage(),
            "AssetRemovalReviewPage" => new AssetRemovalReviewPage(),
            "SaveVaultPage" => new SaveVaultPage(),
            "PocketHealthPage" => new PocketHealthPage(),
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Unknown page route.")
        };
        NavigationPage.SetHasNavigationBar(page, false);
        await navigation.PushAsync(page);
#else
        await Shell.Current.GoToAsync(route);
#endif
    }
}
