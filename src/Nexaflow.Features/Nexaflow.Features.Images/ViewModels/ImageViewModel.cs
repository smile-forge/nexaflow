using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Images.Services;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Formatting;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>The viewer's sub-views, selected by the toolbar's segmented toggle.</summary>
public enum ImageViewMode
{
    Carousel,   // one image at a time
    Album,      // wrapping thumbnail grid
    Explore,    // thumbnail strip on the left, selected image on the right
    Collage,    // scattered, tilted thumbnails on a pan/zoom canvas
}

/// <summary>Represents a single dot in the image-navigator indicator strip.</summary>
public partial class ImageDotItem : ObservableObject
{
    [ObservableProperty] private int  _index;
    [ObservableProperty] private bool _isCurrent;
}

public partial class ImageViewModel : ObservableObject, IPageViewModel, IDisposable
{
    private const int ThumbSize = 200;

    // Auto-advance intervals per speed slider position (Slow / Medium / Fast).
    private static readonly TimeSpan[] _autoIntervals =
        [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1)];

    // ── Image collection ──────────────────────────────────────────────────

    private readonly List<string> _paths;
    private readonly IShellServices? _shell;
    private readonly DispatcherTimer _autoTimer;
    private readonly CancellationTokenSource _cts = new();
    private bool _thumbsRequested;
    private bool _collageComputed;

    [ObservableProperty] private int _currentIndex;
    [ObservableProperty] private BitmapSource? _currentImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderText))]
    private string _currentFileName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderText))]
    private int _totalImages;

    // ── View mode ─────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCarousel))]
    [NotifyPropertyChangedFor(nameof(IsAlbum))]
    [NotifyPropertyChangedFor(nameof(IsExplore))]
    [NotifyPropertyChangedFor(nameof(IsCollage))]
    [NotifyPropertyChangedFor(nameof(HeaderText))]
    private ImageViewMode _viewMode = ImageViewMode.Carousel;

    public bool IsCarousel => ViewMode == ImageViewMode.Carousel;
    public bool IsAlbum    => ViewMode == ImageViewMode.Album;
    public bool IsExplore  => ViewMode == ImageViewMode.Explore;
    public bool IsCollage  => ViewMode == ImageViewMode.Collage;

    /// <summary>Toolbar heading: the current file when an image is featured, else the image count.</summary>
    public string HeaderText => (IsCarousel || IsExplore)
        ? CurrentFileName
        : (TotalImages == 1 ? "1 image" : $"{TotalImages} images");

    // ── View state ────────────────────────────────────────────────────────

    /// <summary>When true the image is scaled to fit the viewer; when false it is shown at 100%.</summary>
    [ObservableProperty] private bool _fitToWindow = true;

    /// <summary>Rotation angle in degrees (multiples of 90).</summary>
    [ObservableProperty] private double _rotationAngle;

    /// <summary>When true the current image is presented full-screen in a borderless window.</summary>
    [ObservableProperty] private bool _isFullScreen;

    // ── Auto-advance ──────────────────────────────────────────────────────

    /// <summary>When true the slideshow advances on a timer.</summary>
    [ObservableProperty] private bool _isAuto;

    /// <summary>Speed slider position: 0 = Slow (10s), 1 = Medium (4s), 2 = Fast (1s).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoSpeedLabel))]
    private int _autoSpeed = 1;

    public string AutoSpeedLabel => AutoSpeed switch { 0 => "Slow", 2 => "Fast", _ => "Medium" };

    // ── Dot indicator + thumbnails ────────────────────────────────────────

    public ObservableCollection<ImageDotItem>   Dots       { get; } = [];
    public ObservableCollection<ImageThumbItem> Thumbnails { get; } = [];

    /// <summary>The slice of <see cref="Dots"/> currently shown — capped at <see cref="DotWindow"/>.</summary>
    public ObservableCollection<ImageDotItem> VisibleDots { get; } = [];

    private const int DotWindow = 20;
    private int _dotBlock = -1;

    [ObservableProperty] private bool _canPageDotsLeft;
    [ObservableProperty] private bool _canPageDotsRight;

    public bool HasMultiple => _paths.Count > 1;

    partial void OnCurrentIndexChanged(int value) => LoadImage(value);

    partial void OnViewModeChanged(ImageViewMode value)
    {
        if (value is ImageViewMode.Album or ImageViewMode.Explore or ImageViewMode.Collage)
            EnsureThumbnailsRequested();
        if (value == ImageViewMode.Collage)
            EnsureCollageLayout();
    }

    partial void OnIsAutoChanged(bool value)
    {
        if (value)
        {
            _autoTimer.Interval = _autoIntervals[Math.Clamp(AutoSpeed, 0, 2)];
            _autoTimer.Start();
        }
        else
        {
            _autoTimer.Stop();
        }
    }

    partial void OnAutoSpeedChanged(int value)
    {
        if (IsAuto)
        {
            _autoTimer.Stop();
            _autoTimer.Interval = _autoIntervals[Math.Clamp(value, 0, 2)];
            _autoTimer.Start();
        }
    }

    // ── Navigation ────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void Previous() => StepPrevious();
    private bool CanGoPrevious() => CurrentIndex > 0;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => StepNext();
    private bool CanGoNext() => CurrentIndex < _paths.Count - 1;

    /// <summary>Next image, clamped at the last — manual navigation (wheel, arrows, keys) does not wrap.</summary>
    public void StepNext()     { if (CurrentIndex < _paths.Count - 1) CurrentIndex++; }

    /// <summary>Previous image, clamped at the first.</summary>
    public void StepPrevious() { if (CurrentIndex > 0) CurrentIndex--; }

    /// <summary>Next image, wrapping to the first — used only by auto-advance (the slideshow loops).</summary>
    public void Advance()
    {
        if (_paths.Count == 0) return;
        CurrentIndex = (CurrentIndex + 1) % _paths.Count;
    }

    [RelayCommand]
    private void RotateLeft() => RotationAngle = (RotationAngle - 90 + 360) % 360;

    [RelayCommand]
    private void RotateRight() => RotationAngle = (RotationAngle + 90) % 360;

    [RelayCommand]
    private void ToggleFit() => FitToWindow = !FitToWindow;

    [RelayCommand]
    private void EnterFullScreen() => IsFullScreen = true;

    /// <summary>Esc handler: leaves full-screen and stops auto-advance, keeping the current image.</summary>
    public void ExitFullScreen()
    {
        IsAuto       = false;
        IsFullScreen = false;
    }

    [RelayCommand]
    private void GoToIndex(int index)
    {
        if (index >= 0 && index < _paths.Count)
            CurrentIndex = index;
    }

    // ── Position-indicator paging (20 dots per block) ─────────────────────

    [RelayCommand]
    private void PageDotsLeft()
    {
        if (_dotBlock > 0) CurrentIndex = (_dotBlock - 1) * DotWindow;
    }

    [RelayCommand]
    private void PageDotsRight()
    {
        CurrentIndex = Math.Min(_paths.Count - 1, (_dotBlock + 1) * DotWindow);
    }

    /// <summary>Keeps <see cref="VisibleDots"/> showing the (≤20-dot) block containing the current image.</summary>
    private void UpdateDotWindow()
    {
        int count = Dots.Count;
        if (count <= DotWindow)
        {
            if (VisibleDots.Count != count)
            {
                VisibleDots.Clear();
                foreach (var d in Dots) VisibleDots.Add(d);
            }
            _dotBlock = 0;
            CanPageDotsLeft = CanPageDotsRight = false;
            return;
        }

        int block = CurrentIndex / DotWindow;
        if (block != _dotBlock)
        {
            _dotBlock = block;
            VisibleDots.Clear();
            for (int i = block * DotWindow; i < Math.Min((block + 1) * DotWindow, count); i++)
                VisibleDots.Add(Dots[i]);
        }
        CanPageDotsLeft  = block > 0;
        CanPageDotsRight = (block + 1) * DotWindow < count;
    }

    /// <summary>Selects an image without leaving the current view (Explore strip click).</summary>
    public void Select(int index)
    {
        if (index >= 0 && index < _paths.Count)
            CurrentIndex = index;
    }

    /// <summary>Album/collage double-click: jump to <paramref name="index"/> and switch to the carousel.</summary>
    public void OpenInCarousel(int index)
    {
        if (index < 0 || index >= _paths.Count) return;
        CurrentIndex = index;
        ViewMode     = ImageViewMode.Carousel;
    }

    /// <summary>Right-click → Delete: confirms, sends the image to the Recycle Bin, drops it from the view.</summary>
    [RelayCommand]
    private async Task Delete(int index)
    {
        if (_shell is null || index < 0 || index >= _paths.Count) return;

        var name = Path.GetFileName(_paths[index]);
        if (!await _shell.ConfirmAsync("Delete image", $"Send '{name}' to the Recycle Bin?"))
            return;

        if (!RecycleBin.TryRecycle(_paths[index]))
        {
            _shell.ShowError($"Couldn't delete '{name}'.");
            return;
        }
        RemoveAt(index);
    }

    /// <summary>Drops the image at <paramref name="index"/> from every collection and re-points the view.</summary>
    private void RemoveAt(int index)
    {
        _paths.RemoveAt(index);
        Dots.RemoveAt(index);
        Thumbnails.RemoveAt(index);
        for (int i = index; i < _paths.Count; i++)
        {
            Dots[i].Index       = i;
            Thumbnails[i].Index = i;
        }

        TotalImages = _paths.Count;
        OnPropertyChanged(nameof(HasMultiple));
        _collageComputed = false;   // re-scatter the smaller set when the collage is next shown
        _dotBlock        = -1;      // force the dot window to rebuild

        if (_paths.Count == 0)
        {
            CurrentImage    = null;
            CurrentFileName = string.Empty;
            VisibleDots.Clear();
            return;
        }

        int newIndex = index < CurrentIndex ? CurrentIndex - 1 : Math.Min(CurrentIndex, _paths.Count - 1);
        if (newIndex == CurrentIndex) LoadImage(newIndex);   // same slot now shows a different image
        else CurrentIndex = newIndex;                         // change triggers LoadImage
    }

    // ── Activation (driven by the view's Loaded/Unloaded) ─────────────────

    public void OnActivated()
    {
        if (IsAuto) { _autoTimer.Interval = _autoIntervals[Math.Clamp(AutoSpeed, 0, 2)]; _autoTimer.Start(); }
    }

    public void OnDeactivated() => _autoTimer.Stop();

    // ── Construction ──────────────────────────────────────────────────────

    public ImageViewModel(IReadOnlyList<string> paths, IShellServices? shell = null,
                          ImageViewMode initialMode = ImageViewMode.Carousel)
    {
        _paths      = [.. paths];   // mutable copy — images can be deleted from the viewer
        _shell      = shell;
        TotalImages = _paths.Count;

        for (int i = 0; i < paths.Count; i++)
        {
            Dots.Add(new ImageDotItem { Index = i, IsCurrent = i == 0 });
            Thumbnails.Add(new ImageThumbItem
            {
                Index    = i,
                FilePath = paths[i],
                FileName = Path.GetFileName(paths[i]),
                IsSelected = i == 0,
            });
        }

        _autoTimer = new DispatcherTimer(DispatcherPriority.Background);
        _autoTimer.Tick += (_, _) => Advance();

        LoadImage(0);

        // A single image has nothing to grid/scatter — the other modes only make sense with several.
        if (paths.Count > 1) ViewMode = initialMode;   // setter requests thumbnails / layout as needed
    }

    // ── Thumbnails ────────────────────────────────────────────────────────

    private void EnsureThumbnailsRequested()
    {
        if (_thumbsRequested || _shell is null || Thumbnails.Count == 0) return;
        _thumbsRequested = true;
        _shell.QueueBackgroundTask(new ThumbnailLoadTask([.. Thumbnails], ThumbSize, _shell), ct: _cts.Token);
    }

    // ── Collage scatter (deterministic so it doesn't reshuffle on re-entry) ──

    private void EnsureCollageLayout()
    {
        if (_collageComputed || Thumbnails.Count == 0) return;
        _collageComputed = true;

        // Card size lives in the collage template (≈170×150); spacing is smaller so cards overlap.
        const double spacingX = 135, spacingY = 120;
        int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Thumbnails.Count)));
        var rng = new Random(2027);                     // fixed seed → stable scatter

        for (int i = 0; i < Thumbnails.Count; i++)
        {
            int row = i / cols, col = i % cols;
            var t = Thumbnails[i];
            t.CollageX        = col * spacingX + (rng.NextDouble() - 0.5) * 60;
            t.CollageY        = row * spacingY + (rng.NextDouble() - 0.5) * 60;
            t.CollageRotation = (rng.NextDouble() - 0.5) * 26;   // ±13°
        }
    }

    // ── Image loading ─────────────────────────────────────────────────────

    private void LoadImage(int index)
    {
        if (index < 0 || index >= _paths.Count) return;

        var path = _paths[index];
        CurrentFileName = Path.GetFileName(path);

        foreach (var dot in Dots)
            dot.IsCurrent = dot.Index == index;
        foreach (var thumb in Thumbnails)
            thumb.IsSelected = thumb.Index == index;

        UpdateDotWindow();

        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();

        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption   = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            if (File.Exists(path))
            {
                // Real file: let WPF open it directly (the proven path).
                bi.UriSource = new Uri(path, UriKind.Absolute);
                bi.EndInit();
            }
            else
            {
                // In-archive image: decode through the VFS. OnLoad caches the whole frame at EndInit,
                // so the backing stream is safe to dispose immediately after.
                using var src = VirtualFileSystem.Instance.OpenRead(path);
                using var ms = new MemoryStream();
                src.CopyTo(ms);
                ms.Position = 0;
                bi.StreamSource = ms;
                bi.EndInit();
            }
            bi.Freeze();
            CurrentImage = bi;
        }
        catch
        {
            CurrentImage = null;
        }
    }

    // ── IPageViewModel ────────────────────────────────────────────────────

    public string GetContext()
    {
        if (TotalImages == 0) return "Image viewer: no images loaded.";
        var mode = ViewMode switch
        {
            ImageViewMode.Album   => "album grid",
            ImageViewMode.Explore => "explore",
            ImageViewMode.Collage => "collage",
            _                     => "carousel",
        };
        var position = TotalImages == 1 ? string.Empty : $" ({CurrentIndex + 1} of {TotalImages})";
        return $"Image viewer ({mode}): '{CurrentFileName}'{position} — {DescribeCurrentImage()}. "
             + "Call capture_image to see the picture itself, or get_image_info for its metadata.";
    }

    public string? GetSecurityContext() => CurrentPath;

    public string? GetAiSystemPromptGuidance() =>
        "One or more images are open. To see the picture the user is looking at, call capture_image — it "
      + "returns the current image to you as a vision attachment. Use next_image / previous_image to move "
      + "through a multi-image set, and get_image_info for dimensions, format and size.";

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        // ── Vision: hand the current image to the model so it can actually see the pixels ──
        new DelegateClientTool(
            "capture_image",
            "Return the image the user is currently viewing so you can see it — the way to answer any "
          + "question about what the picture shows.",
            [],
            ToolSafety.SafeOperation,
            (_, _) =>
            {
                var png = EncodeCurrentPng();
                if (png is not { Length: > 0 })
                    return Task.FromResult(ToolResult.Error(
                        "Couldn't capture the image.", "No image is loaded, or it failed to decode."));
                return Task.FromResult(ToolResult.Ok(
                        $"Captured '{CurrentFileName}'.",
                        $"The image \"{CurrentFileName}\" is attached for you to look at.")
                    with { Images = [new ContextImage(png, "image/png", CurrentFileName)] });
            },
            exemptFromRepeatGuard: true),

        // ── Metadata (text only) ──
        new DelegateClientTool(
            "get_image_info",
            "Get the current image's filename, format, pixel dimensions, file size and position in the set.",
            [],
            ToolSafety.SafeOperation,
            (_, _) => Task.FromResult(
                TotalImages == 0
                    ? ToolResult.Error("No images are loaded.")
                    : ToolResult.Ok($"{CurrentFileName}: {DescribeCurrentImage()}.", DescribeImageInfo())),
            parallelizable: true),

        // ── Navigation (moves the viewer; clamps at the ends, never wraps) ──
        new DelegateClientTool(
            "next_image",
            "Move to the next image in the set (stops at the last one).",
            [],
            ToolSafety.SafeOperation,
            async (_, _) => await StepAsync(forward: true)),

        new DelegateClientTool(
            "previous_image",
            "Move to the previous image in the set (stops at the first one).",
            [],
            ToolSafety.SafeOperation,
            async (_, _) => await StepAsync(forward: false)),
    ];

    /// <summary>Shared body for next_image / previous_image: steps the viewer and reports the new position.
    /// Stepping mutates UI-bound state (CurrentImage, the dot/thumbnail strips, command CanExecute) via
    /// LoadImage, so the change is marshalled to the UI thread — client tools run off it.</summary>
    private async Task<ToolResult> StepAsync(bool forward)
    {
        if (!HasMultiple) return ToolResult.Ok("only one image", "There is only one image in this view.");
        var before = CurrentIndex;
        if (_shell is not null)
            await _shell.RunOnUiAsync(() => { if (forward) StepNext(); else StepPrevious(); });
        else if (forward) StepNext(); else StepPrevious();
        if (before == CurrentIndex)
            return ToolResult.Ok(
                forward ? "already at the last image" : "already at the first image",
                $"Already at the {(forward ? "last" : "first")} image ({CurrentIndex + 1} of {TotalImages}).");
        return ToolResult.Ok(
            $"showing {CurrentIndex + 1} of {TotalImages}",
            $"Now showing '{CurrentFileName}' ({CurrentIndex + 1} of {TotalImages}).");
    }

    /// <summary>Format + pixel size + byte size of the current image, best-effort (any part may be unknown).</summary>
    private string DescribeCurrentImage()
    {
        var parts = new List<string> { CurrentFormat() };
        if (CurrentImage is { } img) parts.Add($"{img.PixelWidth}×{img.PixelHeight}px");
        var size = CurrentFileSize();
        if (size >= 0) parts.Add(SizeFormatter.FormatBytes(size));
        return string.Join(", ", parts);
    }

    /// <summary>Multi-line metadata block returned by get_image_info.</summary>
    private string DescribeImageInfo()
    {
        var lines = new List<string>
        {
            $"File: {CurrentFileName}",
            $"Format: {CurrentFormat()}",
        };
        if (CurrentImage is { } img) lines.Add($"Dimensions: {img.PixelWidth}×{img.PixelHeight} px");
        var size = CurrentFileSize();
        if (size >= 0) lines.Add($"Size: {SizeFormatter.FormatBytes(size)}");
        lines.Add(TotalImages == 1 ? "Position: single image" : $"Position: {CurrentIndex + 1} of {TotalImages}");
        return string.Join("\n", lines);
    }

    /// <summary>Upper-case format name from the current file's extension (e.g. PNG, JPEG); "image" if none.</summary>
    private string CurrentFormat()
    {
        var ext = Path.GetExtension(CurrentFileName);
        return string.IsNullOrEmpty(ext) ? "image" : ext.TrimStart('.').ToUpperInvariant();
    }

    /// <summary>Absolute path of the current image, or null when nothing is loaded.</summary>
    private string? CurrentPath
        => CurrentIndex >= 0 && CurrentIndex < _paths.Count ? _paths[CurrentIndex] : null;

    /// <summary>On-disk size of the current image in bytes, or -1 when it can't be determined (e.g. in-archive).</summary>
    private long CurrentFileSize()
    {
        var path = CurrentPath;
        if (path is null) return -1;
        try { return File.Exists(path) ? new FileInfo(path).Length : -1; }
        catch { return -1; }
    }

    /// <summary>Encodes the current (frozen) bitmap to PNG bytes for the model to view; null when nothing is loaded.</summary>
    private byte[]? EncodeCurrentPng()
    {
        var src = CurrentImage;
        if (src is null) return null;
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _autoTimer.Stop();
        _cts.Cancel();
        _cts.Dispose();
    }
}
