using EzPocket.Models;
using EzPocket.Services;

namespace EzPocket;

public partial class FirmwarePage : ContentPage
{
    private readonly PocketSelectionService selection;
    private readonly FirmwareUpdateService firmware;
    private FirmwareUpdatePreview? preview;

    public FirmwarePage()
    {
        InitializeComponent();
        selection = IPlatformApplication.Current?.Services.GetService<PocketSelectionService>() ?? new PocketSelectionService();
        firmware = IPlatformApplication.Current?.Services.GetService<FirmwareUpdateService>() ?? new FirmwareUpdateService();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PocketDrive? pocket = selection.SelectedPocket;
        TargetName.Text = pocket is null ? "No Pocket selected" : pocket.Name;
        TargetPath.Text = pocket?.RootPath ?? "Return to the dashboard and select a Pocket first.";
        PrepareButton.IsEnabled = pocket is not null;
    }

    protected override void OnDisappearing()
    {
        if (preview is not null) firmware.Cleanup(preview);
        base.OnDisappearing();
    }

    private async void OnPrepareClicked(object? sender, EventArgs e)
    {
        PocketDrive? pocket = selection.SelectedPocket;
        if (pocket is null) return;
        if (preview is not null) firmware.Cleanup(preview);
        preview = null;
        PrepareButton.IsEnabled = false;
        ApplyButton.IsVisible = false;
        PreviewCard.IsVisible = false;
        PreparingIndicator.IsVisible = true;
        PreparingIndicator.IsRunning = true;
        FirmwareStatus.Text = "Downloading and verifying the latest official Pocket firmware…";
        try
        {
            preview = await firmware.PrepareAsync(pocket);
            ReleaseTitle.Text = $"Pocket firmware {preview.Release.Version}";
            ReleaseDetail.Text = $"{preview.FileName} · {preview.SizeLabel} · will be placed at the SD-card root";
            ChecksumDetail.Text = $"Verified against Analogue's MD5: {preview.Release.Md5}";
            ReplacementDetail.Text = preview.ExistingFirmwareFiles.Count switch
            {
                0 => "No existing Pocket firmware file will be replaced.",
                1 => $"{preview.ExistingFirmwareFiles[0]} will be backed up and replaced.",
                _ => $"{preview.ExistingFirmwareFiles.Count} existing Pocket firmware files will be backed up and replaced."
            };
            PreviewCard.IsVisible = true;
            ApplyButton.IsVisible = true;
            FirmwareStatus.Text = "Review the staged firmware, then choose to place it on the selected SD card.";
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
        {
            FirmwareStatus.Text = $"Could not prepare firmware: {exception.Message}";
        }
        finally
        {
            PreparingIndicator.IsRunning = false;
            PreparingIndicator.IsVisible = false;
            PrepareButton.IsEnabled = true;
        }
    }

    private async void OnApplyClicked(object? sender, EventArgs e)
    {
        if (preview is null) return;
        string replacementSummary = preview.ExistingFirmwareFiles.Count == 0
            ? "No existing firmware file will be replaced."
            : $"{preview.ExistingFirmwareFiles.Count} existing firmware file(s) will be backed up and replaced.";
        bool confirmed = await DisplayAlert("Stage Pocket firmware", $"Place firmware {preview.Release.Version} at the root of {preview.PocketPath}? {replacementSummary} This does not update your cores. Afterward, safely eject the SD card, power the Pocket off, insert the card, and power it on to begin Analogue's update process.", "Stage firmware", "Cancel");
        if (!confirmed) return;

        ApplyButton.IsEnabled = false;
        FirmwareUpdateResult result = await firmware.ApplyAsync(preview);
        FirmwareStatus.Text = result.Message;
        if (result.Succeeded)
        {
            ApplyButton.IsVisible = false;
            PreviewCard.IsVisible = false;
            preview = null;
        }
        else ApplyButton.IsEnabled = true;
    }

    private async void OnBackClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}
