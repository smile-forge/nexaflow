using Nexaflow.Features.Common;
using Nexaflow.Features.Images.ViewModels;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Images.Services;

/// <summary>
/// Populates album thumbnails off the UI thread: pulls each image from the shell's thumbnail cache and
/// marshals the result onto its <see cref="ImageThumbItem"/>, so cells fill in progressively while their
/// placeholders show in the meantime.
/// </summary>
public sealed class ThumbnailLoadTask : IBackgroundTask
{
    private readonly IReadOnlyList<ImageThumbItem> _items;
    private readonly int _size;
    private readonly IShellServices _shell;

    public ThumbnailLoadTask(IReadOnlyList<ImageThumbItem> items, int size, IShellServices shell)
    {
        _items = items;
        _size  = size;
        _shell = shell;
    }

    public string Description => $"Loading {_items.Count} thumbnails";

    public async Task RunAsync(CancellationToken ct)
    {
        foreach (var item in _items)
        {
            ct.ThrowIfCancellationRequested();

            var bmp = ShellThumbnail.TryGet(item.FilePath, _size);
            if (bmp is null) continue;

            await _shell.RunOnUiAsync(() => item.Thumbnail = bmp);
        }
    }
}
