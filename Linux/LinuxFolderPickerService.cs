using EzPocket.Services;

namespace EzPocket;

public sealed class LinuxFolderPickerService : IFolderPickerService
{
    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Page? page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null) return null;

        string? path = await page.DisplayPromptAsync(
            "Choose Pocket target",
            "Enter the mounted SD card folder path (for example /media/you/POCKET).",
            "Select folder", "Cancel", "/media/you/POCKET");
        cancellationToken.ThrowIfCancellationRequested();
        return string.IsNullOrWhiteSpace(path) ? null : path.Trim();
    }
}
