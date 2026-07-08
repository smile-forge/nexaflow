using Nexaflow.Features.Common;
using Nexaflow.Features.Images.ViewModels;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Nexaflow.Features.Images.Services;

/// <summary>
/// Populates album thumbnails off the UI thread: pulls each image from the shell's thumbnail cache and
/// marshals the results onto their <see cref="ImageThumbItem"/>s in batches, so cells fill in
/// progressively while their placeholders show in the meantime.
/// </summary>
public sealed class ThumbnailLoadTask : IBackgroundTask
{
    /// <summary>
    /// Upper bound on decoded thumbnails kept alive per load — bounds album memory on huge folders
    /// (a 160 px thumb is ~100 KB, so the cap is ~100 MB worst-case). Items beyond it keep their
    /// name-chip placeholder. The album panels aren't UI-virtualized yet, so without this every
    /// image in the folder would stay decoded; proper virtualization is the tracked follow-up
    /// (docs/arch_review_2026-07.md §G1).
    /// </summary>
    private const int MaxThumbnails = 1024;

    /// <summary>Thumbnails applied per UI-thread hop — one dispatcher call per batch, not per image.</summary>
    private const int BatchSize = 24;

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
        var batch  = new List<(ImageThumbItem Item, BitmapSource Bmp)>(BatchSize);
        int loaded = 0;

        foreach (var item in _items)
        {
            ct.ThrowIfCancellationRequested();
            if (loaded >= MaxThumbnails) break;

            var bmp = ShellThumbnail.TryGet(item.FilePath, _size);
            if (bmp is null) continue;

            loaded++;
            batch.Add((item, bmp));
            if (batch.Count >= BatchSize) await FlushAsync(batch);
        }

        if (batch.Count > 0) await FlushAsync(batch);
    }

    private async Task FlushAsync(List<(ImageThumbItem Item, BitmapSource Bmp)> batch)
    {
        var apply = batch.ToArray();
        batch.Clear();
        await _shell.RunOnUiAsync(() =>
        {
            foreach (var (item, bmp) in apply) item.Thumbnail = bmp;
        });
    }
}
