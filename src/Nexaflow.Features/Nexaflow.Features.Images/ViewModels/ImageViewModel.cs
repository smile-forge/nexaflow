using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.Editing.Wpf;
using Nexaflow.Features.Images.Services;
using Nexaflow.Features.Images.Views;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Nexaflow.Features.Images.ViewModels;

public partial class ImageViewModel : ObservableObject, IPageViewModel, IContextPreview, IDisposable
{
    private const int ThumbSize = 200;

    /// <summary>The longest side the assistant is shown, in pixels.</summary>
    private const int CaptureEdge = 2048;



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

    // ── Editing ───────────────────────────────────────────────────────────

    private readonly ImageEditingServices _editing;
    private readonly bool _ownsEditing;
    private readonly ImageSessionCache _sessions;
    private readonly ImageDisplay _display;
    private readonly DispatcherTimer _thumbRefresh;
    private IFileWatch? _sidecarWatch;
    private ImageViewMode _modeBeforeEdit = ImageViewMode.Carousel;

    [ObservableProperty] private int _currentIndex;
    [ObservableProperty] private BitmapSource? _currentImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderText))]
    private string _currentFileName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderText))]
    [NotifyPropertyChangedFor(nameof(ImageCountText))]
    private int _totalImages;

    /// <summary>The photo being shown, with its recipe and history; null while nothing is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentRecipe))]
    [NotifyPropertyChangedFor(nameof(HasChangedSourceNotice))]
    [NotifyPropertyChangedFor(nameof(SessionOnlyNotice))]
    [NotifyPropertyChangedFor(nameof(HasSessionOnlyNotice))]
    [NotifyPropertyChangedFor(nameof(IsEdited))]
    [NotifyPropertyChangedFor(nameof(ShowsEditedMark))]
    [NotifyPropertyChangedFor(nameof(ShowsSessionOnlyNotice))]
    [NotifyCanExecuteChangedFor(nameof(RotateLeftCommand))]
    [NotifyCanExecuteChangedFor(nameof(RotateRightCommand))]
    [NotifyCanExecuteChangedFor(nameof(FlipHorizontalCommand))]
    [NotifyCanExecuteChangedFor(nameof(FlipVerticalCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertEditsCommand))]
    [NotifyCanExecuteChangedFor(nameof(UndoEditCommand))]
    [NotifyCanExecuteChangedFor(nameof(RedoEditCommand))]
    private ImageEditSession? _currentSession;

    /// <summary>What the presented frame is a picture of; drawn over by the edit canvas.</summary>
    [ObservableProperty] private EditCanvas? _canvas;

    /// <summary>The current photo could not be decoded.</summary>
    [ObservableProperty] private bool _loadFailed;

    // ── View mode ─────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCarousel))]
    [NotifyPropertyChangedFor(nameof(IsAlbum))]
    [NotifyPropertyChangedFor(nameof(IsExplore))]
    [NotifyPropertyChangedFor(nameof(IsCollage))]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyPropertyChangedFor(nameof(IsBrowsing))]
    [NotifyPropertyChangedFor(nameof(HeaderText))]
    [NotifyPropertyChangedFor(nameof(ShowsSubViewSelector))]
    [NotifyPropertyChangedFor(nameof(ShowsEditedMark))]
    [NotifyPropertyChangedFor(nameof(ShowsSessionOnlyNotice))]
    private ImageViewMode _viewMode = ImageViewMode.Carousel;

    public bool IsCarousel => ViewMode == ImageViewMode.Carousel;
    public bool IsAlbum    => ViewMode == ImageViewMode.Album;
    public bool IsExplore  => ViewMode == ImageViewMode.Explore;
    public bool IsCollage  => ViewMode == ImageViewMode.Collage;
    public bool IsEditing  => ViewMode == ImageViewMode.Edit;

    /// <summary>Any view but the editor — where the browsing controls (sub-views, slideshow, full screen) belong.</summary>
    public bool IsBrowsing => !IsEditing;

    /// <summary>Toolbar heading: the current file when an image is featured, else the image count.</summary>
    public string HeaderText => (IsCarousel || IsExplore || IsEditing)
        ? CurrentFileName
        : ImageCountText;

    /// <summary>"1 image" / "N images": the grid views' heading, and the context preview's caption.</summary>
    public string ImageCountText => TotalImages == 1
        ? Str.Get("Images.Header.ImageCountOne")
        : Str.Format("Images.Header.ImageCountManyFormat", TotalImages);

    /// <summary>The tools of edit mode, over the same photo.</summary>
    public ImageEditorViewModel Editor { get; }

    // ── View state ────────────────────────────────────────────────────────

    /// <summary>When true the image is scaled to fit the viewer; when false it is shown at 100%.</summary>
    [ObservableProperty] private bool _fitToWindow = true;

    /// <summary>When true the current image is presented full-screen in a borderless window.</summary>
    [ObservableProperty] private bool _isFullScreen;

    // ── The recipe, as the viewer shows it ────────────────────────────────

    /// <summary>The current photo's edits (nothing, while no photo is shown).</summary>
    public ImageRecipe CurrentRecipe => CurrentSession?.Recipe ?? ImageRecipe.Identity;

    /// <summary>The current photo carries edits — what the "edited" badge says.</summary>
    public bool IsEdited => !CurrentRecipe.IsIdentity;

    /// <summary>The photo changed after its edits were made; the user decides whether they still apply.</summary>
    public bool HasChangedSourceNotice => CurrentSession?.LoadStatus == RecipeLoadStatus.SourceChanged;

    /// <summary>Why edits to this photo last only as long as the viewer, or null when they are kept beside it.</summary>
    public string? SessionOnlyNotice => CurrentSession switch
    {
        null                                                    => null,
        { CanPersist: true }                                    => null,
        { IsFileBacked: false }                                 => Str.Get("Images.Edit.Notice.Archive"),
        { LoadStatus: RecipeLoadStatus.Newer }                  => Str.Get("Images.Edit.Notice.Newer"),
        { LoadStatus: RecipeLoadStatus.Unreadable }             => Str.Get("Images.Edit.Notice.Unreadable"),
        _                                                       => null,
    };

    public bool HasSessionOnlyNotice => SessionOnlyNotice is not null;

    // ── What the toolbar and notices show, by view ────────────────────────

    /// <summary>The sub-view selector: several images, and not editing.</summary>
    public bool ShowsSubViewSelector => HasMultiple && IsBrowsing;

    /// <summary>The "edited" mark beside the heading, where one photo is featured.</summary>
    public bool ShowsEditedMark => IsEdited && (IsCarousel || IsExplore || IsEditing);

    /// <summary>The session-only notice, once it matters: while editing, or once there are edits to lose.</summary>
    public bool ShowsSessionOnlyNotice => HasSessionOnlyNotice && (IsEditing || IsEdited);

    // ── Auto-advance ──────────────────────────────────────────────────────

    /// <summary>When true the slideshow advances on a timer.</summary>
    [ObservableProperty] private bool _isAuto;

    /// <summary>Speed slider position: 0 = Slow (10s), 1 = Medium (4s), 2 = Fast (1s).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoSpeedLabel))]
    private int _autoSpeed = 1;

    public string AutoSpeedLabel => AutoSpeed switch
    {
        0 => Str.Get("Images.Speed.Slow"),
        2 => Str.Get("Images.Speed.Fast"),
        _ => Str.Get("Images.Speed.Medium"),
    };

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

    partial void OnViewModeChanged(ImageViewMode oldValue, ImageViewMode newValue)
    {
        if (newValue is ImageViewMode.Album or ImageViewMode.Explore or ImageViewMode.Collage)
            EnsureThumbnailsRequested();
        if (newValue == ImageViewMode.Collage)
            EnsureCollageLayout();
        if (newValue == ImageViewMode.Edit)
        {
            _modeBeforeEdit = oldValue;
            IsAuto = false;
        }
        Editor.OnEditModeChanged(newValue == ImageViewMode.Edit);
        RequestRender();
    }

    partial void OnFitToWindowChanged(bool value) => RequestRender();

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

    // ── Edits the viewer offers everywhere (each remembered in the photo's recipe) ──

    private bool HasSession() => CurrentSession is not null;

    [RelayCommand(CanExecute = nameof(HasSession))]
    private void RotateLeft() => Edit(r => r with { Geometry = r.Geometry.Turned(-1) });

    [RelayCommand(CanExecute = nameof(HasSession))]
    private void RotateRight() => Edit(r => r with { Geometry = r.Geometry.Turned(1) });

    [RelayCommand(CanExecute = nameof(HasSession))]
    private void FlipHorizontal() => Edit(r => r with { Geometry = r.Geometry.Flipped(horizontal: true) });

    [RelayCommand(CanExecute = nameof(HasSession))]
    private void FlipVertical() => Edit(r => r with { Geometry = r.Geometry.Flipped(horizontal: false) });

    [RelayCommand(CanExecute = nameof(CanUndoEdit))]
    private void UndoEdit() => CurrentSession?.History.Undo();
    private bool CanUndoEdit() => CurrentSession?.History.CanUndo == true;

    [RelayCommand(CanExecute = nameof(CanRedoEdit))]
    private void RedoEdit() => CurrentSession?.History.Redo();
    private bool CanRedoEdit() => CurrentSession?.History.CanRedo == true;

    /// <summary>The photo changed since its edits were made: keep them, anchored to the photo as it is now.</summary>
    [RelayCommand]
    private void KeepChangedEdits() => CurrentSession?.KeepEditsForChangedSource();

    /// <summary>The photo changed since its edits were made: drop them.</summary>
    [RelayCommand]
    private void DiscardChangedEdits()
    {
        if (CurrentSession is not { } session) return;
        session.Revert();
        session.KeepEditsForChangedSource();
    }

    /// <summary>Back to the photo as it is on disk — one undoable step; the sidecar goes with it.</summary>
    [RelayCommand(CanExecute = nameof(HasSession))]
    private void RevertEdits() => CurrentSession?.Revert();

    [RelayCommand]
    private void EnterEditMode()
    {
        if (CurrentSession is not null) ViewMode = ImageViewMode.Edit;
    }

    /// <summary>Done: back to the view edit mode was entered from, with the edits applied on view.</summary>
    [RelayCommand]
    private void ExitEditMode()
    {
        if (!IsEditing) return;
        ViewMode = HasMultiple || _modeBeforeEdit == ImageViewMode.Carousel ? _modeBeforeEdit : ImageViewMode.Carousel;
    }

    /// <summary>Applies <paramref name="change"/> to the current photo's recipe as one undoable step.</summary>
    internal void Edit(Func<ImageRecipe, ImageRecipe> change)
    {
        if (CurrentSession is { } session) session.History.Commit(change(session.Recipe));
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

    /// <summary>Right-click → Delete: confirms, sends the image (and its edits) to the Recycle Bin, drops it from the view.</summary>
    [RelayCommand]
    private async Task Delete(int index)
    {
        if (_shell is null || index < 0 || index >= _paths.Count) return;

        var path = _paths[index];
        var name = Path.GetFileName(path);
        if (!await _shell.ConfirmAsync(Str.Get("Images.Delete.ConfirmTitle"), Str.Format("Images.Delete.ConfirmMessageFormat", name)))
            return;

        // Closed first: closing writes any pending edit, which must land before the sidecar goes with the photo.
        CloseSession(path);
        if (!RecycleBin.TryRecycle(path))
        {
            _shell.ShowError(Str.Format("Images.Delete.FailedFormat", name));
            if (index == CurrentIndex) LoadImage(index);
            return;
        }
        var sidecar = ImageRecipeStore.SidecarPath(path);
        if (File.Exists(sidecar)) RecycleBin.TryRecycle(sidecar);
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
        OnPropertyChanged(nameof(ShowsSubViewSelector));
        _collageComputed = false;   // re-scatter the smaller set when the collage is next shown
        _dotBlock        = -1;      // force the dot window to rebuild

        if (_paths.Count == 0)
        {
            ShowNothing();
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

    public void OnDeactivated()
    {
        _autoTimer.Stop();
        _editing.Recipes.Flush();
    }

    // ── Construction ──────────────────────────────────────────────────────

    public ImageViewModel(IReadOnlyList<string> paths, IShellServices? shell = null,
                          ImageViewMode initialMode = ImageViewMode.Carousel, ImageEditingServices? editing = null)
    {
        _paths       = [.. paths];   // mutable copy — images can be deleted from the viewer
        _shell       = shell;
        _editing     = editing ?? new ImageEditingServices();
        _ownsEditing = editing is null;
        TotalImages  = _paths.Count;

        _sessions = new ImageSessionCache(_editing);
        _display  = new ImageDisplay(_editing, action => _shell is null ? RunHere(action) : _shell.RunOnUiAsync(action), _cts.Token);
        _display.ImageChanged += image => CurrentImage = image;
        _display.Presented    += canvas => Canvas = canvas;
        _display.Decoded      += OnDecoded;
        _display.DecodeFailed += session => { if (ReferenceEquals(CurrentSession, session)) LoadFailed = true; };

        Editor = new ImageEditorViewModel(this, shell, _editing);

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

        // A thumbnail follows the edits once they pause, rather than at every slider tick.
        _thumbRefresh = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(600) };
        _thumbRefresh.Tick += (_, _) => { _thumbRefresh.Stop(); RefreshCurrentThumbnail(); };

        LoadImage(0);

        // A single image has nothing to grid/scatter — the other modes only make sense with several.
        if (paths.Count > 1 || initialMode == ImageViewMode.Edit) ViewMode = initialMode;   // setter requests thumbnails / layout as needed
    }

    private static Task RunHere(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    // ── Thumbnails ────────────────────────────────────────────────────────

    private void EnsureThumbnailsRequested()
    {
        if (_thumbsRequested || _shell is null || Thumbnails.Count == 0) return;
        _thumbsRequested = true;
        _shell.QueueBackgroundTask(new ThumbnailLoadTask([.. Thumbnails], ThumbSize, _shell, _editing), ct: _cts.Token);
    }

    /// <summary>The current photo's thumbnail, redrawn with its edits — once the grids have thumbnails to show.</summary>
    private void RefreshCurrentThumbnail()
    {
        if (!_thumbsRequested || CurrentSession is not { } session) return;
        var item = Thumbnails.FirstOrDefault(t => string.Equals(t.FilePath, session.Path, StringComparison.OrdinalIgnoreCase));
        if (item is null || session.Proxy is not { } proxy) return;

        var recipe = session.Recipe;
        var output = CropGeometry.OutputSize(recipe, proxy.SourceSize);
        double scale = Math.Min(1, (double)ThumbSize / Math.Max(output.Width, output.Height));
        _ = Task.Run(() =>
        {
            using var picture = session.Snapshot(recipe, new RenderRequest(RenderFrame.Output, scale));
            if (picture is null) return;
            var bitmap = SkiaWpf.ToFrozenBitmap(picture);
            _ = (_shell?.RunOnUiAsync(() => item.Thumbnail = bitmap) ?? RunHere(() => item.Thumbnail = bitmap));
        });
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

    /// <summary>
    /// Shows the photo at <paramref name="index"/>: re-points the strips at once, opens (or reuses) its edit session —
    /// recipe and history — and decodes it off the UI thread at the size the screen needs, rendering as soon as
    /// there are pixels.
    /// </summary>
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

        ShowSession(_sessions.Open(path));
    }

    private void ShowSession(ImageEditSession session)
    {
        if (ReferenceEquals(CurrentSession, session) && session.Proxy is not null) { RequestRender(); return; }

        if (CurrentSession is { } previous) previous.RecipeChanged -= OnRecipeChanged;
        _sidecarWatch?.Dispose();
        _sidecarWatch = null;

        CurrentSession = session;
        _display.Show(session);
        session.RecipeChanged += OnRecipeChanged;
        LoadFailed = false;
        Canvas = null;
        Editor.OnSessionChanged();

        if (session.CanPersist && _shell is not null)
            _sidecarWatch = _shell.WatchFile(ImageRecipeStore.SidecarPath(session.Path), () => CurrentSession?.ReloadFromSidecar());

        RequestRender();
    }

    private void CloseSession(string path)
    {
    if (_sessions.Remove(path) is not { } session) return;
    if (ReferenceEquals(CurrentSession, session))
    {
        session.RecipeChanged -= OnRecipeChanged;
        _sidecarWatch?.Dispose();
        _sidecarWatch = null;
        _display.Show(null);
        CurrentSession = null;
    }
    session.Dispose();
    }

    /// <summary>Closes and reopens the current photo — after a save has replaced the file under it.</summary>
    internal void ReloadCurrent()
    {
        if (CurrentSession is not { } session) return;
        CloseSession(session.Path);
        LoadImage(CurrentIndex);
    }

    private void ShowNothing()
    {
        if (CurrentSession is { } session) CloseSession(session.Path);
        _display.Clear();
        Canvas = null;
    }

    private void OnRecipeChanged()
    {
        OnPropertyChanged(nameof(CurrentRecipe));
        OnPropertyChanged(nameof(IsEdited));
        OnPropertyChanged(nameof(HasChangedSourceNotice));
        OnPropertyChanged(nameof(SessionOnlyNotice));
        OnPropertyChanged(nameof(HasSessionOnlyNotice));
        OnPropertyChanged(nameof(ShowsEditedMark));
        OnPropertyChanged(nameof(ShowsSessionOnlyNotice));
        UndoEditCommand.NotifyCanExecuteChanged();
        RedoEditCommand.NotifyCanExecuteChanged();
        Editor.OnRecipeChanged();
        RequestRender();

        _thumbRefresh.Stop();
        _thumbRefresh.Start();
    }

    // ── Rendering ─────────────────────────────────────────────────────────

    /// <summary>The surface the current image is shown on changed size (device-independent pixels) or density.</summary>
    public void SetViewport(double width, double height, double pixelsPerDip)
    {
    if (_display.SetViewport(width, height, pixelsPerDip)) RequestRender();
    }

    /// <summary>The edit canvas was zoomed: render more (or fewer) pixels for the same layout.</summary>
    public void SetEditZoom(double zoom)
    {
    if (_display.SetEditZoom(zoom) && IsEditing) RequestRender();
    }

    /// <summary>Renders the current photo for the way it is being shown: on the edit canvas or not, before or after, fitted or at 100%.</summary>
    internal void RequestRender() =>
        _display.Render(new ImageDisplayMode(IsEditing, IsEditing && Editor.ShowOriginal, Editor.Frame, FitToWindow));

    /// <summary>The current photo has pixels — its first decode, or a sharper one: the editor and the frame catch up.</summary>
    private void OnDecoded(ImageEditSession session)
    {
        if (!ReferenceEquals(CurrentSession, session)) return;
        Editor.OnSessionChanged();
        RequestRender();
    }

    // ── IContextPreview ───────────────────────────────────────────────────

    /// <summary>
    /// A one-column, read-only strip of every image in the tab for the conversation context panel, with the
    /// current one (the image <c>capture_image</c> hands the AI) highlighted. A single image is a strip of one.
    /// </summary>
    /// <remarks>
    /// A fresh control each time the chip is selected, per the contract: it binds to the page's
    /// <see cref="Thumbnails"/> but owns none of them. It asks for them to be loaded because nothing else may
    /// have: only the grid views request thumbnails, and a single image never leaves the carousel. The request
    /// is the page's own (made once, shared with the album, cancelled with the page), not the preview's.
    /// </remarks>
    public UserControl CreateContextPreview()
    {
        EnsureThumbnailsRequested();
        return new ImageContextPreview { DataContext = this };
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
            ImageViewMode.Edit    => "editing",
            _                     => "carousel",
        };
        var position = TotalImages == 1 ? string.Empty : $" ({CurrentIndex + 1} of {TotalImages})";
        var edited = IsEdited ? " It has edits, shown as they would be saved." : string.Empty;
        return $"Image viewer ({mode}): '{CurrentFileName}'{position} — {DescribeCurrentImage()}.{edited} "
             + "Call capture_image to see the picture itself, or get_image_info for its metadata.";
    }

    public string? GetSecurityContext() => CurrentPath;

    public string? GetAiSystemPromptGuidance() =>
        "One or more images are open. To see the picture the user is looking at, call capture_image — it "
      + "returns the current image to you as a vision attachment, with any edits applied. Use next_image / "
      + "previous_image to move through a multi-image set, and get_image_info for dimensions, format and size.";

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
    /// Stepping mutates UI-bound state (the session, the dot/thumbnail strips, command CanExecute) via
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
        if (CurrentPixelSize() is { } px) parts.Add($"{px.Width}×{px.Height}px");
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
        if (CurrentPixelSize() is { } px) lines.Add($"Dimensions: {px.Width}×{px.Height} px{(IsEdited ? " (as edited)" : string.Empty)}");
        var size = CurrentFileSize();
        if (size >= 0) lines.Add($"Size: {SizeFormatter.FormatBytes(size)}");
        lines.Add(TotalImages == 1 ? "Position: single image" : $"Position: {CurrentIndex + 1} of {TotalImages}");
        return string.Join("\n", lines);
    }

    /// <summary>The picture's size as shown — edits applied — once it is decoded.</summary>
    private SKSizeI? CurrentPixelSize() => CurrentSession?.OutputSize(CurrentRecipe);

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

    /// <summary>
    /// The current picture, edits applied, as PNG bytes for the model to view — rendered by the same pipeline as the
    /// screen, at most <see cref="CaptureEdge"/> on its longer side; null when nothing is decoded. Safe off the UI thread.
    /// </summary>
    private byte[]? EncodeCurrentPng()
    {
        if (CurrentSession is not { } session || session.OutputSize(session.Recipe) is not { } output) return null;
        double scale = Math.Min(1, (double)CaptureEdge / Math.Max(output.Width, output.Height));
        using var picture = session.Snapshot(session.Recipe, new RenderRequest(RenderFrame.Output, scale));
        using var data = picture?.Encode(SKEncodedImageFormat.Png, 100);
        return data?.ToArray();
    }

    public void Dispose()
    {
        _autoTimer.Stop();
        _thumbRefresh.Stop();
        _cts.Cancel();
        _sidecarWatch?.Dispose();
        if (CurrentSession is { } current) CloseSession(current.Path);
        _sessions.Dispose();
        _display.Dispose();
        if (_ownsEditing) _editing.Dispose();
        _cts.Dispose();
    }
}
