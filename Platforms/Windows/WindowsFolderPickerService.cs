using Microsoft.Maui.Controls;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using Windows.Storage.Pickers;

namespace EzPocket.Services;

public sealed class WindowsFolderPickerService : IFolderPickerService
{
    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nativeWindow = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (nativeWindow is null) return null;

        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(nativeWindow));

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
