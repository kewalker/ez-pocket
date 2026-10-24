using Microsoft.Maui.ApplicationModel;
using UIKit;
using UniformTypeIdentifiers;

namespace EzPocket.Services;

/// <summary>Opens a folder in place; selecting a target never copies or writes its contents.</summary>
public sealed class MacFolderPickerService : IFolderPickerService
{
    public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var presenter = Platform.GetCurrentUIViewController();
            if (presenter is null) return null;

            var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var picker = new UIDocumentPickerViewController([UTTypes.Folder], asCopy: false)
            {
                AllowsMultipleSelection = false,
                ModalInPresentation = true
            };
            picker.DidPickDocumentAtUrls += (_, args) =>
                completion.TrySetResult(args.Urls.FirstOrDefault()?.Path);
            picker.WasCancelled += (_, _) => completion.TrySetResult(null);

            presenter.PresentViewController(picker, true, null);
            try
            {
                return await completion.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                if (picker.PresentingViewController is not null)
                    await picker.DismissViewControllerAsync(true);
            }
        });
}
