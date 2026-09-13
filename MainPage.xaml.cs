namespace EzPocket;

public partial class MainPage : ContentPage
{
    public MainPage() => InitializeComponent();

    private async void OnScanClicked(object? sender, EventArgs e)
    {
        await DisplayAlert("Scan Pocket", "Pocket detection is the next step. Connect an SD card or Pocket over USB, then scan again.", "Got it");
    }
}
