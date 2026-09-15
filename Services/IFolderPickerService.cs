namespace EzPocket.Services;

public interface IFolderPickerService
{
    Task<string?> PickFolderAsync(CancellationToken cancellationToken = default);
}

public sealed class UnsupportedFolderPickerService : IFolderPickerService
{
    public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
}
