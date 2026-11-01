using System.ComponentModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.Editing.Wpf;
using Nexaflow.Features.Images.Services.Printing;
using Nexaflow.Visuals.Common.Localization;
using SkiaSharp;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// Print: the picture shown — a photo as edited, or a layered picture's composite — laid on paper as set here (fit, fill,
/// actual size or a width; which way up; margins; centred) and rendered at full size for the printer's resolution. Offered while
/// there is something to print.
/// </summary>
public sealed partial class PicturePrinting : ObservableObject
{
    private readonly ImageViewModel _viewer;
    private readonly ImageEditingServices _editing;
    private readonly IShellServices? _shell;

    internal PicturePrinting(ImageViewModel viewer, ImageEditingServices editing, IShellServices? shell)
    {
        _viewer  = viewer;
        _editing = editing;
        _shell   = shell;
        viewer.PropertyChanged += OnViewerChanged;
    }

    private void OnViewerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ImageViewModel.CurrentSession) or nameof(ImageViewModel.CurrentDocument) or nameof(ImageViewModel.IsStarting)
                           or nameof(ImageViewModel.Canvas) or nameof(ImageViewModel.CurrentImage))
        {
            OnPropertyChanged(nameof(CanPrint));
            OnPropertyChanged(nameof(ActualNote));
            OpenCommand.NotifyCanExecuteChanged();
            if (!CanPrint) IsOpen = false;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SetsWidth), nameof(IsActual))]
    private PrintSize _size = PrintSize.Fit;

    public bool SetsWidth => Size == PrintSize.Width;

    public bool IsActual => Size == PrintSize.Actual;

    /// <summary>The printed width, with <see cref="PrintSize.Width"/>.</summary>
    [ObservableProperty] private double _widthMillimetres = 150;

    [ObservableProperty] private PrintOrientation _orientation = PrintOrientation.Auto;

    [ObservableProperty] private double _marginMillimetres = 10;

    [ObservableProperty] private bool _centre = true;

    /// <summary>The print options are open.</summary>
    [ObservableProperty] private bool _isOpen;

    /// <summary>Something is shown to print: a photo decoded, or a layered picture. Never on the start screen.</summary>
    public bool CanPrint => !_viewer.IsStarting && (_viewer.CurrentDocument is not null || _viewer.CurrentSession?.Proxy is not null);

    /// <summary>Opens the print options, or closes them.</summary>
    [RelayCommand(CanExecute = nameof(CanPrint))]
    private void Open() => IsOpen = !IsOpen;

    [RelayCommand]
    private void Close() => IsOpen = false;

    internal PrintOptions Options => new(Size, WidthMillimetres, Orientation, MarginMillimetres, Centre);

    /// <summary>The picture's resolution: a layered picture's own; a photo's, at the resolution pictures are taken to have.</summary>
    internal double Dpi => _viewer.CurrentDocument?.Picture.Dpi ?? LayeredPicture.DefaultDpi;

    /// <summary>What actual size means for this picture.</summary>
    public string ActualNote => Str.Format("Images.Print.ActualNote", Dpi);

    /// <summary>The picture's size in pixels, as it prints; null while there is none.</summary>
    internal SKSizeI? PictureSize => _viewer.CurrentDocument is { } document
        ? new SKSizeI(document.Picture.Width, document.Picture.Height)
        : _viewer.CurrentSession is { } session ? session.OutputSize(session.Recipe) : null;

    /// <summary>Whether the page is turned landscape for this picture.</summary>
    public bool PrintsLandscape => PictureSize is { } size && PicturePrintLayout.Landscape(size, Options);

    /// <summary>What the print job is called: the picture's file name.</summary>
    public string Title => Path.GetFileName(_viewer.CurrentDocument?.Path ?? _viewer.CurrentSession?.Path ?? string.Empty);

    /// <summary>
    /// The pages to print on a printable area of <paramref name="printable"/> device-independent pixels at
    /// <paramref name="printerDpi"/>: the picture rendered at full size — no more pixels than the printer lays down — and laid out as
    /// set. Null where it cannot be yet: a picture tool it waits on, the user told so.
    /// </summary>
    public async Task<PicturePaginator?> PaginatorAsync(Size printable, double printerDpi)
    {
        if (PictureSize is not { } size) return null;
        var tiles = PicturePrintLayout.Tiles(size, Dpi, Options, new SKSize((float)printable.Width, (float)printable.Height));
        if (tiles.Count == 0) return null;
        using var rendered = await RenderAsync();
        if (rendered is null)
        {
            _shell?.ShowNotification(Str.Get("Images.Print.WaitForTools"));
            return null;
        }

        // No more pixels than the printer lays down at the printed size.
        double ratio = Math.Min(1, PicturePrintLayout.Scale(tiles) / 96 * Math.Max(72, printerDpi));
        if (ratio < 1)
        {
            int w = Math.Max(1, (int)Math.Round(rendered.Width * ratio)), h = Math.Max(1, (int)Math.Round(rendered.Height * ratio));
            using var reduced = await Task.Run(() => Reduced(rendered, w, h));
            double sx = (double)w / rendered.Width, sy = (double)h / rendered.Height;
            tiles = [.. tiles.Select(t => t with
            {
                Source = new SKRectI((int)Math.Floor(t.Source.Left * sx), (int)Math.Floor(t.Source.Top * sy),
                                     (int)Math.Ceiling(t.Source.Right * sx), (int)Math.Ceiling(t.Source.Bottom * sy)),
            })];
            return new PicturePaginator(SkiaWpf.ToFrozenBitmap(reduced), tiles, printable);
        }
        return new PicturePaginator(SkiaWpf.ToFrozenBitmap(rendered), tiles, printable);
    }

    // The picture at full size, through the same path a save takes; null where a picture tool it needs has not made its part yet.
    private async Task<SKImage?> RenderAsync()
    {
        if (_viewer.CurrentDocument is { } document)
        {
            using var renderer = await document.RendererForAsync(document.Picture);
            return renderer?.Composite(1);
        }
        if (_viewer.CurrentSession is not { } session || !session.Made.Serves(session.Recipe)) return null;
        return await _editing.Exporter.RenderAsync(session.Path, session.Recipe, session.Made);
    }

    private static SKImage Reduced(SKImage image, int width, int height)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul))
                            ?? throw new InvalidOperationException("No memory to make the picture the printer's size.");
        using var paint = new SKPaint { IsAntialias = true };
        surface.Canvas.DrawImage(image, SKRect.Create(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        return surface.Snapshot();
    }

    /// <summary>A print that failed, told to the user.</summary>
    internal void Failed(string reason) => _shell?.ShowError(Str.Format("Images.Print.FailedFormat", Title, reason));
}
