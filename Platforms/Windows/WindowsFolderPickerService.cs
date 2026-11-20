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

        Windows.Storage.StorageFolder? folder;
        // The system picker can fail (E_FAIL) for some locations or dialog states; treat that as no selection rather than crashing.
        try { folder = await picker.PickSingleFolderAsync(); }
        catch (System.Runtime.InteropServices.COMException) { return null; }
        return folder?.Path;
    }
}
