using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.CuttingOut;
using Nexaflow.Features.Images.Editing.Kept;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.Editing.Wpf;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Nexaflow.Features.Images.Services;

/// <summary>
/// Populates album thumbnails off the UI thread and marshals the results onto their <see cref="ImageThumbItem"/>s in
/// batches, so cells fill in progressively while their placeholders show in the meantime. An edited photo's
/// thumbnail is drawn through its recipe, as the viewer shows it; every other comes from the shell's thumbnail cache.
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

    /// <summary>An edited photo is decoded this many times the thumbnail size, so a tight crop is still sharp.</summary>
    private const int EditedDecodeFactor = 4;

    private readonly IReadOnlyList<ImageThumbItem> _items;
    private readonly int _size;
    private readonly IShellServices _shell;
    private readonly ImageEditingServices _editing;
    private readonly Func<string, ImageRecipe, MadeByTools>? _made;

    /// <param name="made">Finds what the picture tools made and kept for a photo's recipe — its restoration, its cut-out's
    /// mask; without it, or where none is kept, the thumbnail shows the photo without them.</param>
    public ThumbnailLoadTask(IReadOnlyList<ImageThumbItem> items, int size, IShellServices shell, ImageEditingServices editing,
                             Func<string, ImageRecipe, MadeByTools>? made = null)
    {
    _items    = items;
    _size     = size;
    _shell    = shell;
    _editing  = editing;
    _made     = made;
    }

    public string Description => Str.Format("Images.Status.LoadingThumbnailsFormat", _items.Count);

    public async Task RunAsync(CancellationToken ct)
    {
        var batch  = new List<(ImageThumbItem Item, BitmapSource Bmp)>(BatchSize);
        int loaded = 0;

        foreach (var item in _items)
        {
            ct.ThrowIfCancellationRequested();
            if (loaded >= MaxThumbnails) break;

            var bmp = EditedThumbnail(item.FilePath) ?? ShellThumbnail.TryGet(item.FilePath, _size);
            if (bmp is null) continue;

            loaded++;
            batch.Add((item, bmp));
            if (batch.Count >= BatchSize) await FlushAsync(batch);
        }

        if (batch.Count > 0) await FlushAsync(batch);
    }

    /// <summary>The photo drawn with its edits, or null when it has none (or they cannot be applied).</summary>
    private BitmapSource? EditedThumbnail(string path)
    {
        if (!File.Exists(ImageRecipeStore.SidecarPath(path))) return null;
        var load = _editing.Recipes.Load(path);
        if (load is not { Status: RecipeLoadStatus.Loaded, Recipe: { } recipe }) return null;

        try
        {
            using var decoded = _editing.Decode(path, _size * EditedDecodeFactor);
            var output = CropGeometry.OutputSize(recipe, decoded.SourceSize);
            double scale = Math.Min(1, (double)_size / Math.Max(output.Width, output.Height));
            var made = _made?.Invoke(path, recipe) ?? MadeByTools.None;
            using var restored = recipe.Restore is { } restore && made.Restored is { } kept
                ? _editing.DecodeRestored(kept, decoded.SourceSize, _size * EditedDecodeFactor * restore.Scale)
                : null;
            using var mask = recipe.CutOut is not null && made.CutOut is { } cut ? CutOutMasks.Decode(cut) : null;
            using var pipeline = _editing.Pipeline(decoded, cache: false, restored, mask);
            return SkiaWpf.ToFrozenBitmap(pipeline.Render(recipe, new RenderRequest(RenderFrame.Output, scale)));
        }
        catch (Exception ex) when (ex is ImageDecodeException or IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            return null;   // the shell's thumbnail of the unedited photo is shown instead
        }
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
