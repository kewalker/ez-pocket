using EzPocket.Services;

namespace EzPocket;

public partial class CoreReviewPage : ContentPage
{
    private readonly CoreSelectionService coreSelection;

    public CoreReviewPage()
    {
        InitializeComponent();
        coreSelection = IPlatformApplication.Current?.Services.GetService<CoreSelectionService>() ?? new CoreSelectionService();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SelectedCoreList.ItemsSource = coreSelection.SelectedCores;
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
