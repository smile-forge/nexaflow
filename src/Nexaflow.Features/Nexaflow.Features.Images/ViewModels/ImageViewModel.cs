using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Erasing;
using Nexaflow.Features.Images.Editing.Kept;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.Editing.Wpf;
using Nexaflow.Features.Images.Services;
using Nexaflow.Features.Images.Views;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Drafts;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Text.Pages;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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

    [ObservableProperty] private string _currentFileName = string.Empty;

    [ObservableProperty]
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

    /// <summary>"1 image" / "N images": the context preview's caption.</summary>
    public string ImageCountText => TotalImages == 1
        ? Str.Get("Images.Header.ImageCountOne")
        : Str.Format("Images.Header.ImageCountManyFormat", TotalImages);

    /// <summary>The tools of edit mode, over the same photo.</summary>
    public ImageEditorViewModel Editor { get; }

    /// <summary>Making a video from the picture on screen, where the workspace can.</summary>
    public MakeVideoViewModel MakeVideo { get; }

    /// <summary>The picture on screen read as a page of writing, its text beside it.</summary>
    public ReadPageViewModel ReadPage { get; }

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

    /// <summary>What gives the photo on screen the restoration its recipe asks for, and says what it is doing.</summary>
    public PhotoRestorer Restorer { get; }

    /// <summary>What gives the photo on screen the removed background its recipe asks for, and says what it is doing.</summary>
    public BackgroundRemover Remover { get; }

    /// <summary>The erasing the photo's recipe asks for, made and shown in the photo's place.</summary>
    public PhotoEraser Eraser { get; }

    /// <summary>What picks things out of the photo for the editor's cut-out tool.</summary>
    public PictureCutter Cutter { get; }

    /// <param name="ai">The workspace's AI, through which the user makes a video from the picture on screen; none, none made.</param>
    /// <param name="pictures">The picture tools, where the app has them: what restores a photo whose recipe asks for it.</param>
    /// <param name="selection">Picture selection, where the app has it: what the editor's cut-out tool picks things out with.</param>
    public ImageViewModel(IReadOnlyList<string> paths, IShellServices? shell = null,
                          ImageViewMode initialMode = ImageViewMode.Carousel, ImageEditingServices? editing = null, IAIService? ai = null,
                          IPictureTools? pictures = null, IPictureSelection? selection = null, IPageReading? reading = null, IPictureErasing? erasing = null)
    {
        _paths       = [.. paths];   // mutable copy — images can be deleted from the viewer
        _shell       = shell;
        _editing     = editing ?? new ImageEditingServices();
        _ownsEditing = editing is null;
        TotalImages  = _paths.Count;

        _sessions = new ImageSessionCache(_editing);
        Func<Action, Task> runOnUi = action => _shell is null ? RunHere(action) : _shell.RunOnUiAsync(action);
        _display  = new ImageDisplay(_editing, runOnUi, _cts.Token);
        _display.ImageChanged += image => CurrentImage = image;
        _display.Presented    += canvas => Canvas = canvas;
        _display.Decoded      += OnDecoded;
        _display.DecodeFailed += session => { if (ReferenceEquals(CurrentSession, session)) LoadFailed = true; };

        Restorer = new PhotoRestorer(_editing, pictures, runOnUi);
        Restorer.Made += RequestRender;
        _display.RestoredDecodeFailed += session => Restorer.Lost(session);

        Remover = new BackgroundRemover(_editing, selection, runOnUi);
        Remover.Made += RequestRender;
        _display.CutOutDecodeFailed += session => Remover.Lost(session);

        // The restoration is of the photo as erased: it follows each erasing as it is made.
        Eraser = new PhotoEraser(_editing, erasing, selection, runOnUi);
        Eraser.Made += () =>
        {
            Restorer.Follow(CurrentSession);
            RequestRender();
        };
        _display.ErasedDecodeFailed += session => Eraser.Lost(session);

        Cutter = new PictureCutter(_editing, selection, shell, runOnUi);
        Editor = new ImageEditorViewModel(this, shell, _editing, Restorer, Remover, Cutter, Eraser);
        MakeVideo = new MakeVideoViewModel(ai,
            () => CurrentSession is { } session && session.OutputSize(session.Recipe) is { } size && size.Height > size.Width,
                EncodeCurrentPng);
            ReadPage = new ReadPageViewModel(reading, ai, shell, _editing, runOnUi, KeptByTools, () => Host);

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
        _shell.QueueBackgroundTask(new ThumbnailLoadTask([.. Thumbnails], ThumbSize, _shell, _editing, KeptByTools), ct: _cts.Token);
    }

    /// <summary>What the tools made and kept for <paramref name="photo"/>'s recipe — its restoration made from the photo as
    /// erased, where it erases — for thumbnails and the page reader.</summary>
    private MadeByTools KeptByTools(string photo, ImageRecipe recipe)
    {
        var erased = recipe.Erasures.IsEmpty ? null : Eraser.KeptFor(photo, new EraseSteps(recipe.Erasures));
        var restoredFrom = recipe.Erasures.IsEmpty ? photo : erased;
        return new(recipe.Restore is { } restore && restoredFrom is not null ? Restorer.KeptFor(restoredFrom, restore) : null,
                   recipe.CutOut is { } cutOut ? Remover.KeptFor(photo, cutOut) : null,
                   erased);
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
        Eraser.Follow(session);
        Restorer.Follow(session);
        Remover.Follow(session);
        ReadPage.Follow(session);

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
        Eraser.Follow(null);
        Restorer.Follow(null);
        Remover.Follow(null);
        ReadPage.Follow(null);
        Cutter.Close();
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
        Eraser.Follow(CurrentSession);
        Restorer.Follow(CurrentSession);
        Remover.Follow(CurrentSession);
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
        ReadPage.PictureDecoded(session);
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

    /// <summary>The pictures this tab holds — never their folder.</summary>
    public ContextGrant GetContextGrant() => ContextGrant.OfFiles(_paths);

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
            exemptFromRepeatGuard: true) { Needs = [AiAbility.ImageRecognition] },

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

        // ── Selection: things picked out of the picture on screen, shown in the editor's Cut out ──
        new DelegateClientTool(
            "select_in_picture",
            "Pick out everything 'what' names in the picture on screen — \"the mug\", \"woman with red hair\" — and show it selected "
          + "in the image editor's Cut out tool, where the user can copy it or save it without its background. Says how many it "
          + "found, how surely and where, in the picture's pixels.",
            [new ClientToolParameter("what", "What to pick out, in a few words: a noun phrase like \"dog\" or \"red car\".")],
            ToolSafety.SafeOperation,
            async (args, _) => await SelectNamedAsync(ToolArgs.Str(args, "what"))),

        new DelegateClientTool(
            "cut_out",
          "Cut something out of the picture on screen — everything 'what' names, or the thing inside 'box' — into a new PNG, "
          + "transparent around it and cropped to it, opened for the user in a new tab to save where they choose; the picture "
          + "itself is untouched.",
            [
                new ClientToolParameter("what", "What to cut out, in a few words: a noun phrase like \"dog\" or \"red car\". Not needed with 'box'.", Required: false),
                new ClientToolParameter("box", "A box round the one thing to cut out, in the picture's pixels: left,top,right,bottom.", Required: false),
            ],
            ToolSafety.SafeOperation,
            async (args, ct) => await CutOutToolAsync(ToolArgs.Str(args, "what"), ToolArgs.Str(args, "box"), ct)),

        // ── Restoring: the picture on screen cleaned or upscaled, as one of its edits ──
        new DelegateClientTool(
            "restore_picture",
            "Restore the picture on screen with the picture tools on this machine — softness, noise and JPEG blocks cleaned up, the "
          + "faces in it repaired — kept at its size or made twice or four times as large, as one of the picture's edits in the "
          + "image editor (AI enhance and AI upscale): shown at once, undone with Undo, saved when the user saves.",
            [
                new ClientToolParameter("scale", "1 keeps its size, 2 or 4 makes it that many times as large. 1 when not given.", Required: false, Choices: ["1", "2", "4"]),
                new ClientToolParameter("faces", "Whether the faces in it are repaired. Yes when not given.", Required: false, Choices: ["yes", "no"]),
                new ClientToolParameter("strength", "How much of the restoration shows over the picture as it was, 0 to 100. 100 when not given.", Required: false),
            ],
            ToolSafety.SafeOperation,
            async (args, ct) => await RestoreToolAsync(ToolArgs.Str(args, "scale"), ToolArgs.Str(args, "faces"), ToolArgs.Str(args, "strength"), ct)),

        // ── Erasing: things rubbed out of the picture on screen, as a step of its edits ──
        new DelegateClientTool(
            "erase_in_picture",
            "Erase from the picture on screen everything 'what' names — \"the sign\", \"people in the background\" — or the thing "
          + "inside 'box', and fill where it was from what is round it, as a step of the picture's edits in the image editor: "
          + "shown at once, undone with Undo, saved when the user saves. Says what it erased and where.",
            [
                new ClientToolParameter("what", "What to erase, in a few words: a noun phrase like \"sign\" or \"man in a red coat\". Not needed with 'box'.", Required: false),
                new ClientToolParameter("box", "A box round the one thing to erase, in the picture's pixels: left,top,right,bottom.", Required: false),
                new ClientToolParameter("fill", "How to fill where it was: 'ai' (LaMa, best where an edge or shape runs through it) or "
                                              + "'content-aware' (the picture's own texture). AI where it is downloaded, else content-aware.",
                                        Required: false, Choices: ["ai", "content-aware"]),
            ],
            ToolSafety.SafeOperation,
            async (args, ct) => await EraseNamedAsync(ToolArgs.Str(args, "what"), ToolArgs.Str(args, "box"), ToolArgs.Str(args, "fill"), ct)),

        // ── Page reading: the picture on screen read as a page of writing ──
        new DelegateClientTool(
            "read_page",
            "Read the picture on screen as a page of writing — a scan, a photographed page, a form, a receipt — with page "
          + "reading on this machine, and return its text as Markdown: columns as one text, formulas as LaTeX, tables. A few "
          + "seconds; the picture's own text, never a description of it.",
            [],
            ToolSafety.SafeOperation,
            async (_, ct) => await ReadPageToolAsync(ct)),
    ];

    /// <summary>The tab showing the pictures, set by its view: what opens beside it opens in its window.</summary>
    public IPageView? Host { get; set; }

    /// <summary>The picture on screen read as a page — or found read before — for the assistant.</summary>
    private async Task<ToolResult> ReadPageToolAsync(CancellationToken ct)
    {
        if (ReadPage.Conversion is not { Available: true } conversion) return ToolResult.Error("Page reading cannot run on this machine.");
        if (CurrentSession is null) return ToolResult.Error("No image is loaded.");
        var read = _shell is null ? await ReadPage.ReadAsync(ct) : await _shell.RunOnUiAsync(() => ReadPage.ReadAsync(ct));
        return read is null
            ? ToolResult.Error(conversion.Status ?? "The page could not be read.")
            : ToolResult.Ok($"Read '{CurrentFileName}' as a page.", read.Markdown.Trim().Length > 0 ? read.Markdown : "(no text was read from it)");
    }

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

    /// <summary>The assistant's select_in_picture: the editor opened on the Cut out tool, and everything <paramref name="what"/>
    /// names picked out there.</summary>
    private async Task<ToolResult> SelectNamedAsync(string? what)
    {
        if (string.IsNullOrWhiteSpace(what)) return ToolResult.Error("nothing named", "Say what to pick out as 'what'.");
        if (!Cutter.Available) return ToolResult.Error("picture selection unavailable", "This machine cannot run picture selection.");
        if (!Cutter.CanPick)
            return ToolResult.Error("picture selection not downloaded",
                "Picture selection is not on this machine yet: the user can download it in Settings > Local > Tools, or from the image editor's Cut out.");

        Task<IReadOnlyList<PictureFind>>? finding = null;
        void Start()
        {
            EnterEditMode();
            if (!IsEditing) return;
            Editor.Tool = EditTool.CutOut;
            finding = Cutter.FindNamedAsync(what.Trim());
        }
        if (_shell is not null) await _shell.RunOnUiAsync(Start);
        else Start();
        if (finding is null) return ToolResult.Error("no picture", "No picture is open to pick from.");

        var finds = await finding;
        if (Cutter.Failed) return ToolResult.Error("could not pick it out", Cutter.Notice);
        if (finds.Count == 0) return ToolResult.Ok($"found no {what}", $"Nothing in '{CurrentFileName}' is \"{what}\".");
        var where = string.Join("; ", finds.Select(f => string.Create(CultureInfo.InvariantCulture,
            $"{f.Score:P0} sure at {f.Box.Left:F0},{f.Box.Top:F0},{f.Box.Right:F0},{f.Box.Bottom:F0}")));
        return ToolResult.Ok($"picked out {finds.Count} × {what}",
            $"Picked out {finds.Count} \"{what}\" in '{CurrentFileName}' ({where}); they show selected in the editor's Cut out tool, where the user can copy or save them.");
    }

    /// <summary>restore_picture: the picture on screen restored as the editor's AI enhance and AI upscale restore it — the
    /// recipe's restore section set, the editor opened on it — answered once the restoration is made.</summary>
    private async Task<ToolResult> RestoreToolAsync(string? scale, string? faces, string? strength, CancellationToken ct)
    {
        int times = scale switch { null or "" or "1" => 1, "2" => 2, "4" => 4, _ => 0 };
        if (times == 0) return ToolResult.Error("no such scale", "The scale is 1, 2 or 4.");
        bool repair = !string.Equals(faces, "no", StringComparison.OrdinalIgnoreCase);
        double shown = 1;
        if (strength is { Length: > 0 })
        {
            if (!double.TryParse(strength, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) || percent is < 0 or > 100)
                return ToolResult.Error("no such strength", "The strength is from 0 to 100.");
            shown = percent / 100;
        }
        if (!Restorer.Available) return ToolResult.Error("picture tools unavailable", "This machine cannot run the picture tools.");
        if (!Restorer.RestoresNow)
            return ToolResult.Error("picture tools not downloaded",
                "The picture tools are not on this machine yet: the user can download them in Settings > Local > Tools, or from the image editor's AI enhance.");

        var wanted = new RecipeRestore { Scale = times, Faces = repair, Strength = shown };
        string? refused = null;
        Task<string?>? made = null;
        bool already = false;
        void Start()
        {
            EnterEditMode();
            if (!IsEditing || CurrentSession is not { Proxy: { } proxy } session)
            {
                refused = "No picture is open to restore.";
                return;
            }
            if (!Restorer.Restores((int)Math.Round(proxy.SourceSize.Width), (int)Math.Round(proxy.SourceSize.Height), times))
            {
                refused = $"'{CurrentFileName}' is too large to restore at {times}x; try a smaller scale.";
                return;
            }
            Editor.Tool = times > 1 ? EditTool.Resize : EditTool.Adjust;
            if (session.Recipe.Restore == wanted && session.RestoredPath is not null)
            {
                already = true;
                return;
            }
            made = Restorer.NextMadeAsync(ct);
            session.History.Commit(session.Recipe with { Restore = wanted });
        }
        if (_shell is not null) await _shell.RunOnUiAsync(Start);
        else Start();
        if (refused is not null) return ToolResult.Error("could not restore it", refused);
        if (!already && await made! is { } failure) return ToolResult.Error("could not restore it", failure);
        return ToolResult.Ok($"restored {CurrentFileName}",
            $"Restored '{CurrentFileName}'{(times > 1 ? $" at {times}x its size" : "")}{(repair ? ", faces repaired" : "")}, shown at {shown:P0} strength, "
          + "as one of its edits in the image editor; the user can undo it or save it.");
    }

    /// <summary>
    /// erase_in_picture: what <paramref name="what"/> names — found first, as the editor's Cut out finds it, so nothing is
    /// erased when nothing is — or what <paramref name="box"/> holds, erased as a step of the recipe with the Erase tool on
    /// screen, filled by picture erasing where it is here or asked, else content-aware fill; answered once it is made.
    /// </summary>
    private async Task<ToolResult> EraseNamedAsync(string? what, string? box, string? fill, CancellationToken ct)
    {
        what = what?.Trim();
        if (!TryBox(box, out var boxed)) return ToolResult.Error("no such box", "Give 'box' as left,top,right,bottom in the picture's pixels.");
        if (string.IsNullOrEmpty(what) && boxed is null) return ToolResult.Error("nothing named", "Say what to erase as 'what', or give a 'box' round it.");
        if (!Cutter.Available) return ToolResult.Error("picture selection unavailable", "This machine cannot run picture selection, which finds what to erase.");
        if (!Cutter.CanPick)
            return ToolResult.Error("picture selection not downloaded",
                "Picture selection, which finds what to erase, is not on this machine yet: the user can download it in Settings > Local > Tools, or from the image editor's Erase.");
        var by = fill switch
        {
            null or "" => Eraser.ErasesByAiNow ? EraseFill.Ai : EraseFill.ContentAware,
            _ when fill.Equals("ai", StringComparison.OrdinalIgnoreCase) => EraseFill.Ai,
            _ when fill.Equals("content-aware", StringComparison.OrdinalIgnoreCase) => EraseFill.ContentAware,
            _ => (EraseFill?)null,
        };
        if (by is not { } chosen) return ToolResult.Error("no such fill", "Fill with 'ai' or 'content-aware'.");
        if (chosen == EraseFill.Ai && !Eraser.ErasesByAiNow)
            return ToolResult.Error("picture erasing not downloaded",
                "Picture erasing (LaMa) is not on this machine yet: the user can download it in Settings > Local > Tools, or erase with fill 'content-aware'.");

        Task<IReadOnlyList<PictureFind>>? finding = null;
        bool open = false;
        void Start()
        {
            EnterEditMode();
            if (!IsEditing) return;
            open = true;
            Editor.Tool = EditTool.Erase;
            if (!string.IsNullOrEmpty(what)) finding = Cutter.FindNamedAsync(what);
        }
        if (_shell is not null) await _shell.RunOnUiAsync(Start);
        else Start();
        if (!open) return ToolResult.Error("no picture", "No picture is open to erase from.");

        string said;
        if (finding is not null)
        {
            var finds = await finding;
            if (Cutter.Failed) return ToolResult.Error("could not find it", Cutter.Notice);
            if (finds.Count == 0) return ToolResult.Ok($"found no {what}", $"Nothing in '{CurrentFileName}' is \"{what}\"; nothing was erased.");
            said = $"{finds.Count} \"{what}\" at " + string.Join("; ", finds.Select(f => string.Create(CultureInfo.InvariantCulture,
                $"{f.Box.Left:F0},{f.Box.Top:F0},{f.Box.Right:F0},{f.Box.Bottom:F0}")));
        }
        else said = string.Create(CultureInfo.InvariantCulture, $"the thing in {boxed!.Left:F0},{boxed.Top:F0},{boxed.Right:F0},{boxed.Bottom:F0}");

        var step = new RecipeErase
        {
            Fill = chosen,
            Pick = !string.IsNullOrEmpty(what) ? new RecipeCutOut { Phrase = what } : new RecipeCutOut { Box = new RecipeCutBox(boxed!.Left, boxed.Top, boxed.Right, boxed.Bottom) },
        };
        var made = Eraser.NextMadeAsync(ct);
        void Commit()
        {
            if (CurrentSession is not { } session) return;
            session.History.Commit(session.Recipe with { Erasures = session.Recipe.Erasures.Add(step) });
            Cutter.ClearCommand.Execute(null);
        }
        if (_shell is not null) await _shell.RunOnUiAsync(Commit);
        else Commit();

        if (await made is { } failure) return ToolResult.Error("could not erase it", failure);
        return ToolResult.Ok($"erased {(string.IsNullOrEmpty(what) ? "the boxed thing" : what)}",
            $"Erased {said} from '{CurrentFileName}', filled {(chosen == EraseFill.Ai ? "by AI" : "content-aware")}, as a step of its edits in the image editor; the user can undo it or save it.");
    }

    /// <summary>A tool's 'box' — left,top,right,bottom in the picture's pixels — read; none given is a box of null, and false
    /// is one that does not read.</summary>
    private static bool TryBox(string? text, out PictureBox? box)
    {
        box = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        var v = new double[4];
        for (int i = 0; i < parts.Length && i < 4; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return false;
        if (parts.Length != 4 || v[2] <= v[0] || v[3] <= v[1]) return false;
        box = new PictureBox(v[0], v[1], v[2], v[3]);
        return true;
    }

    /// <summary>
    /// cut_out: what <paramref name="what"/> names, or what <paramref name="box"/> holds, picked as the editor's Cut out picks
    /// it and made a PNG — transparent around it, cropped to it — opened as a draft in a tab of its own, which the user saves
    /// where they choose. The assistant is told only that the tab opened: the new picture is its
    /// to work on once the user gives it that tab.
    /// </summary>
    private async Task<ToolResult> CutOutToolAsync(string? what, string? box, CancellationToken ct)
    {
        what = what?.Trim();
        if (!TryBox(box, out var boxed)) return ToolResult.Error("no such box", "Give 'box' as left,top,right,bottom in the picture's pixels.");
        if (string.IsNullOrEmpty(what) && boxed is null) return ToolResult.Error("nothing named", "Say what to cut out as 'what', or give a 'box' round it.");
        if (!Cutter.Available) return ToolResult.Error("picture selection unavailable", "This machine cannot run picture selection.");
        if (!Cutter.CanPick)
            return ToolResult.Error("picture selection not downloaded",
                "Picture selection is not on this machine yet: the user can download it in Settings > Local > Tools, or from the image editor's Cut out.");

        Task<IReadOnlyList<PictureFind>>? finding = null;
        Task<bool>? picking = null;
        string? photo = null;
        void Start()
        {
            EnterEditMode();
            if (!IsEditing || CurrentSession is not { } session) return;
            photo = session.Path;
            Editor.Tool = EditTool.CutOut;
            if (!string.IsNullOrEmpty(what)) finding = Cutter.FindNamedAsync(what);
            else picking = Cutter.BoxedAsync(boxed!);
        }
        if (_shell is not null) await _shell.RunOnUiAsync(Start);
        else Start();
        if (photo is null) return ToolResult.Error("no picture", "No picture is open to cut out of.");

        string said;
        if (finding is not null)
        {
            var finds = await finding;
            if (Cutter.Failed) return ToolResult.Error("could not cut it out", Cutter.Notice);
            if (finds.Count == 0) return ToolResult.Ok($"found no {what}", $"Nothing in '{CurrentFileName}' is \"{what}\"; nothing was saved.");
            said = $"{finds.Count} \"{what}\"";
        }
        else
        {
            if (!await picking!) return Cutter.Failed ? ToolResult.Error("could not cut it out", Cutter.Notice)
                                                      : ToolResult.Ok("found nothing in the box", "Nothing stands out inside that box; nothing was saved.");
            said = "the thing in the box";
        }

        byte[]? png;
        try
        {
            png = await Cutter.CutOutAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            return ToolResult.Error("could not cut it out", ex.Message);
        }
        if (png is null) return ToolResult.Ok("found nothing to keep", "What was picked has no pixels in this picture; nothing was saved.");

        // A draft: the picture's tab gives the assistant the picture, never its folder, so nothing is written beside it. The user
        // keeps the cut-out where they choose, Save as starting beside the picture.
        var saved = DraftLocation.NewFile(Path.GetFileNameWithoutExtension(photo) + Str.Get("Images.CutOut.FileSuffix") + ".png");
        await File.WriteAllBytesAsync(saved, png, ct);
        _shell?.OpenTab(ImageTabRegistration.StaticPageKind, new Dictionary<string, string>
        {
            ["paths"]                     = saved,
            [DraftFile.PageParameterName] = ImageEditingServices.IsFileBacked(photo) ? Path.GetDirectoryName(photo) ?? "" : "",
        });
        return ToolResult.Ok($"cut out of {CurrentFileName}",
            $"Cut out {said} from '{CurrentFileName}' and opened it for the user in a new tab, not saved yet: they save it where they choose.");
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
        Cutter.Close();
        _thumbRefresh.Stop();
        _cts.Cancel();
        _sidecarWatch?.Dispose();
        if (CurrentSession is { } current) CloseSession(current.Path);
        _sessions.Dispose();
        _display.Dispose();
        MakeVideo.Dispose();
        if (_ownsEditing) _editing.Dispose();
        _cts.Dispose();
    }

    /// <summary>The Save as of a picture made for the user and not saved yet — the tab's draft — or null for pictures opened where they live.</summary>
    public DraftSaving? Draft { get; init; }
}
