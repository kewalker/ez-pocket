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
        StatusSummary.Text = core.Status switch
        {
            "Installed" => "This core is installed and up to date.",
            "Update" => "An update is available. You can review it from Manage cores.",
            "Available" => "This core is not installed. You can select it from Manage cores.",
            _ => "Return to Manage cores to review this core."
        };
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    private async void OnManageCoresClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    private async void OnHomeClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//MainPage");
    }
}
