using EzPocket.Services;

namespace EzPocket;

public partial class CoreDetailsPage : ContentPage
{
    private readonly CoreSelectionService coreSelection;

    public CoreDetailsPage()
    {
        InitializeComponent();
        coreSelection = IPlatformApplication.Current?.Services.GetService<CoreSelectionService>() ?? new CoreSelectionService();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var core = coreSelection.SelectedCore;
        if (core is null) return;

        FriendlyName.Text = core.FriendlyName;
        Identifier.Text = core.Identifier;
        Status.Text = core.StatusLabel;
        Category.Text = core.Category;
        InstalledVersion.Text = core.IsInstalled ? core.InstalledVersion : "Not installed";
        AvailableVersion.Text = core.IsAvailable ? core.AvailableVersion : "Not available";
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
