using Microsoft.UI.Xaml.Controls;

namespace EzPocket.Services;

public sealed class WindowsColorPickerService : IColorPickerService
{
    public async Task<uint?> PickAsync(uint initialColor, Action<uint> preview)
    {
        var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        var root = window?.Content?.XamlRoot ?? throw new InvalidOperationException("The app window is not ready.");
        var picker = new ColorPicker
        {
            Color = global::Windows.UI.Color.FromArgb(255, (byte)(initialColor >> 16), (byte)(initialColor >> 8), (byte)initialColor),
            IsAlphaEnabled = false,
            IsColorSpectrumVisible = true,
            IsColorSliderVisible = true,
            IsColorChannelTextInputVisible = true,
            IsHexInputVisible = true,
            IsMoreButtonVisible = false,
        };
        picker.ColorChanged += (_, args) => preview((uint)(args.NewColor.R << 16 | args.NewColor.G << 8 | args.NewColor.B));
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "Choose color",
            Content = picker,
            PrimaryButtonText = "Use color",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? (uint)(picker.Color.R << 16 | picker.Color.G << 8 | picker.Color.B) : null;
    }
}
