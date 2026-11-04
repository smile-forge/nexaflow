using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.SceneDepth;
using Nexaflow.Features.Images.Editing.Selection;
using SkiaSharp;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>How the Cut out tool marks what to keep.</summary>
public enum CutOutMarkWay
{
    /// <summary>Picked by picture selection: clicks on it and off it, a box round it, or a phrase.</summary>
    Pick,

    /// <summary>A rectangle or an ellipse dragged out.</summary>
    Marquee,

    /// <summary>An outline drawn round it, freehand or click to click.</summary>
    Lasso,

    /// <summary>What is near the colour clicked.</summary>
    Colour,

    /// <summary>What lies within a span of distance, by the photo's scene depth.</summary>
    Distance,
}

/// <summary>
/// A way of selecting as its button offers it: picked by picture selection, brushed (Retouch only), drawn by hand — a
/// rectangle, an ellipse, a lasso drawn freehand, or a polygon clicked point to point — or by the colour clicked. One choice
/// standing for the way and the shape it draws, so a panel offers each once.
/// </summary>
public enum SelectionWay
{
    Pick,
    Brush,
    Rectangle,
    Ellipse,
    Lasso,
    Polygon,
    Colour,
    Distance,
}

/// <summary>What the marquee draws.</summary>
public enum MarqueeShape
{
    Rectangle,
    Ellipse,
}

/// <summary>How the lasso draws.</summary>
public enum LassoShape
{
    /// <summary>Freehand, as the pointer moves while pressed; closed when it is let go.</summary>
    Freehand,

    /// <summary>Straight edges from click to click; closed on the first point, by a double-click or by Enter.</summary>
    Polygon,
}

/// <summary>
/// The selection a layered picture keeps over its canvas, as the hand-drawn selection works on it: each shape and refinement
/// applied to it at once, as a step of the picture's history.
/// </summary>
public interface IDocumentSelection
{
    /// <summary>The canvas's size.</summary>
    SKSizeI Size { get; }

    /// <summary>What is selected, over the canvas.</summary>
    TiledCoverage Selection { get; }

    /// <summary>Makes <paramref name="selection"/> what is selected — one step of the picture's history.</summary>
    void Select(TiledCoverage selection);

    /// <summary>What picture selection picked on the active picture layer — at the size it is shown, its bounds in that layer's
    /// pixels — carried onto the canvas; null where the active layer is no picture layer.</summary>
    TiledCoverage? FromPicture(PictureMask picked);

    /// <summary>
    /// What is near the colour at <paramref name="at"/> on the canvas — within <paramref name="tolerance"/> (0–255) on each
    /// channel, joined to it where <paramref name="joined"/> — read from what shows where <paramref name="everyLayer"/>, else from
    /// the active layer alone, or the mask being painted. The picture is read as it stands when asked, the reach worked out off
    /// the UI thread; null where there is nothing to read yet.
    /// </summary>
    Task<TiledCoverage?> ReachOfColourAsync(SKPointI at, int tolerance, bool joined, bool everyLayer);

    /// <summary>
    /// What lies from <paramref name="near"/> to <paramref name="far"/> — 0 the nearest part, 1 the farthest — of the active picture
    /// layer's scene depth, fading out over <paramref name="softness"/> past either end, where that layer lies on the canvas; null
    /// where the active layer is no picture layer or its depth cannot be had.
    /// </summary>
    Task<TiledCoverage?> ReachOfDistanceAsync(double near, double far, double softness);

    /// <summary>The active picture layer's scene depth — kept, or estimated the first time; null where the active layer is no
    /// picture layer or its depth cannot be had.</summary>
    Task<SceneDepthPlane?> SceneDepthAsync();
}

/// <summary>
/// The selection drawn by hand in the photo editor, before Cut out or Erase takes it: marquee and lasso shapes, colours
/// clicked, and refinements of everything selected so far, in the order they were made — the region a recipe keeps
/// (<see cref="RecipeRegion"/>). It starts from what the cut-out tool has picked, so the two together are the one selection
/// both tools take; a shape or colour that replaces the selection lets the picks go too. Shown over the photo as
/// <see cref="Shown"/>, at the size the cut-out tool shows its picks. Shapes and clicks are in the photo's pixels as it stands.
/// </summary>
public sealed partial class RegionMarking : ObservableObject
{
    /// <summary>The least and most a refinement works by, in the photo's pixels.</summary>
    public const double LeastRefine = 1, MostRefine = 250;

    private readonly PictureCutter _cutter;
    private readonly Func<SKSize?> _photoSize;
    private readonly Func<string, CancellationToken, Task<UprightPixels>> _readUpright;
    private readonly List<RegionStep> _steps = [];
    private readonly List<MarkupPoint> _draft = [];
    private string? _photo;

    private IDocumentSelection? _document;

    // The photo's pixels, read the first time a colour is clicked on it and kept while it is the photo: a click costs its reach.
    private (string Photo, Task<UprightPixels> Read)? _upright;

    // Each colour clicked counts; a reach worked out after a later click is let go.
    private int _colourClicks;

    private readonly Func<string, CancellationToken, Task<SceneDepthPlane>>? _readDepth;

    // The photo's scene depth, read the first time a span of distance is selected on it and kept while it is the photo.
    private (string Photo, Task<SceneDepthPlane> Read)? _depth;

    // Each span asked for counts; one worked out after a later ask is let go.
    private int _distanceAsks;

    // A layered picture's selection as it was before the last span of distance met it, the selection that made, and how they met:
    // what moving the span's ends meets again, while the selection is still the one it made.
    private (TiledCoverage Before, TiledCoverage Made, RegionMode Mode)? _distanceOver;

    /// <param name="cutter">The cut-out tool, whose picks the selection starts from.</param>
    /// <param name="photoSize">The photo's size as it stands, once it is read; null before.</param>
    /// <param name="readUpright">Reads a photo's pixels at its full size as it stands, for what a colour clicked on it reaches.</param>
    /// <param name="readDepth">Reads a photo's scene depth — kept, or estimated — for what a span of distance covers on it; null
    /// where the app has no scene depth.</param>
    public RegionMarking(PictureCutter cutter, Func<SKSize?> photoSize, Func<string, CancellationToken, Task<UprightPixels>> readUpright,
                         Func<string, CancellationToken, Task<SceneDepthPlane>>? readDepth = null)
    {
        _cutter = cutter;
        _photoSize = photoSize;
        _readUpright = readUpright;
        _readDepth = readDepth;
        cutter.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PictureCutter.Shown)) Reshow();
            else if (e.PropertyName == nameof(PictureCutter.IsWorking)) SelectSubjectCommand.NotifyCanExecuteChanged();
        };
    }

    /// <summary>What the marquee draws.</summary>
    [ObservableProperty] private MarqueeShape _marquee = MarqueeShape.Rectangle;

    /// <summary>How the lasso draws; another way lets go of an outline half drawn.</summary>
    [ObservableProperty] private LassoShape _lasso = LassoShape.Freehand;

    partial void OnLassoChanged(LassoShape value) => CancelDraft();

    /// <summary>How a shape meets what is selected, unless a key held as it is begun says otherwise.</summary>
    [ObservableProperty] private RegionMode _mode = RegionMode.Replace;

    /// <summary>How far Feather, Grow, Shrink and Smooth work, in the photo's pixels.</summary>
    [ObservableProperty] private double _refineBy = 8;

    partial void OnRefineByChanged(double value)
    {
        if (value < LeastRefine || value > MostRefine) RefineBy = Math.Clamp(value, LeastRefine, MostRefine);
    }

    /// <summary>How near a colour clicked reaches: 0, the colour itself, to 255, every colour — on each channel.</summary>
    [ObservableProperty] private double _colourTolerance = 32;

    partial void OnColourToleranceChanged(double value)
    {
        if (value < 0 || value > 255) ColourTolerance = Math.Clamp(value, 0, 255);
    }

    /// <summary>A colour clicked reaches only what is joined to where it was clicked; otherwise that colour anywhere.</summary>
    [ObservableProperty] private bool _colourJoined = true;

    /// <summary>In a layered picture, a colour clicked is read from what shows, every layer together; otherwise from the active
    /// layer alone.</summary>
    [ObservableProperty] private bool _colourEveryLayer = true;

    /// <summary>Where the span of distance selected begins, 0–100 from the photo's nearest part to its farthest.</summary>
    [ObservableProperty] private double _distanceNear;

    /// <summary>Where the span of distance selected ends, 0–100 from the photo's nearest part to its farthest.</summary>
    [ObservableProperty] private double _distanceFar = 50;

    /// <summary>How far past either end of the span the selection fades out, 0–100 of the photo's depth.</summary>
    [ObservableProperty] private double _distanceSoftness = 5;

    partial void OnDistanceNearChanged(double value)
    {
        if (value > DistanceFar) DistanceFar = value;
        _ = MoveDistanceAsync();
    }

    partial void OnDistanceFarChanged(double value)
    {
        if (value < DistanceNear) DistanceNear = value;
        _ = MoveDistanceAsync();
    }

    partial void OnDistanceSoftnessChanged(double value) => _ = MoveDistanceAsync();

    /// <summary>A span of distance is being worked out.</summary>
    [ObservableProperty] private bool _isReadingDistance;

    /// <summary>How much of the picture lies at each step of distance, nearest first — what the strip under the span shows; null
    /// until the picture's depth is read.</summary>
    [ObservableProperty] private IReadOnlyList<double>? _distanceShares;

    private SceneDepthPlane? _sharesOf;

    /// <summary>
    /// The depth selecting by distance works by, read — or estimated, the first time — as soon as selecting by distance is
    /// chosen, so the strip shows the picture's distances before anything is selected.
    /// </summary>
    public async Task ShowDistancesAsync()
    {
        try
        {
            var plane = _document is { } document ? await document.SceneDepthAsync()
                      : _photo is { } photo && _readDepth is not null ? await DepthAsync(photo) : null;
            if (plane is not null) Shared(plane);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
        {
            // no depth to be had: the strip stays bare, and selecting says why
        }
    }

    private void Shared(SceneDepthPlane plane)
    {
        if (ReferenceEquals(plane, _sharesOf)) return;
        _sharesOf = plane;
        DistanceShares = plane.Shares();
    }

    /// <summary>The selection drawn by hand, as a recipe keeps it; null where nothing has been drawn or refined.</summary>
    public RecipeRegion? Region => _steps.Count == 0 ? null : new RecipeRegion { Steps = [.. _steps] };

    /// <summary>What is selected — the picks and the region over them — at the size the cut-out tool shows its picks; null
    /// where nothing is picked or drawn.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(FeatherCommand), nameof(GrowCommand), nameof(ShrinkCommand), nameof(SmoothCommand))]
    private RegionSelection? _shown;

    /// <summary>Something is selected, to refine or to take.</summary>
    public bool HasSelection => Shown is { IsEmpty: false };

    /// <summary><see cref="Shown"/>'s pixels per pixel of the photo.</summary>
    public double ShownScale { get; private set; } = 1;

    /// <summary>The polygon lasso's points so far, in the photo's pixels; none while no outline is being drawn.</summary>
    public IReadOnlyList<MarkupPoint> Draft => _draft;

    /// <summary>How the outline being drawn will meet what is selected.</summary>
    public RegionMode DraftMode { get; private set; }

    /// <summary>The photo the editor is on; another one lets everything drawn on the last go.</summary>
    public void Follow(string? photo)
    {
        if (photo == _photo) return;
        _photo = photo;
        _upright = null;
        _depth = null;
        (_sharesOf, DistanceShares) = (null, null);
        Clear();
    }

    /// <summary>
    /// A layered picture's selection to work on from now on — each shape and refinement applied to it at once, over the
    /// canvas — or, with none, the photo editor's way again: a region kept as the steps it was made by.
    /// </summary>
    public void Attach(IDocumentSelection? document)
    {
        if (ReferenceEquals(document, _document)) return;
        _document = document;
        (_sharesOf, DistanceShares) = (null, null);
        _steps.Clear();
        _draft.Clear();
        Changed();
    }

    /// <summary>The layered picture's selection moved — an edit, an undo: what shows follows.</summary>
    public void DocumentChanged() => Reshow();

    /// <summary>A shape drawn: one that replaces the selection lets what was picked and drawn before go.</summary>
    public void Add(RegionShape shape)
    {
        if (_document is { } document)
        {
            document.Select(RegionRaster.Apply(document.Selection, new RecipeRegion { Steps = [shape] }, 1));
            return;
        }
        AddStep(shape, shape.Mode);
    }

    /// <summary>
    /// What is near the colour at <paramref name="at"/> selected, meeting what is selected as <paramref name="mode"/> says — within
    /// <see cref="ColourTolerance"/>, joined to it where <see cref="ColourJoined"/>. On a photo it is a step of the region, kept as
    /// the click; in a layered picture it is read from what shows, or the active layer alone (<see cref="ColourEveryLayer"/>), and
    /// made part of the canvas's selection at once. A photo that cannot be read out leaves the selection as it was.
    /// </summary>
    public async Task SelectColourAsync(MarkupPoint at, RegionMode mode)
    {
        int click = ++_colourClicks, tolerance = (int)Math.Round(ColourTolerance);
        if (_document is { } document)
        {
            var reach = await document.ReachOfColourAsync(new SKPointI((int)Math.Floor(at.X), (int)Math.Floor(at.Y)), tolerance, ColourJoined, ColourEveryLayer);
            if (click != _colourClicks || !ReferenceEquals(document, _document) || reach is null) return;
            document.Select(mode == RegionMode.Replace ? reach : document.Selection.Combined(reach, mode));
            return;
        }
        if (_photo is not { } photo) return;
        var colour = new RegionColour(at.X, at.Y, tolerance, ColourJoined) { Mode = mode };
        TiledCoverage reached;
        try
        {
            var pixels = await UprightAsync(photo);
            reached = await Task.Run(() => RegionColours.ReachOn(colour, pixels));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
        {
            return;   // the photo could not be read out: the selection stays as it was, and the next click reads it again
        }
        if (click != _colourClicks || photo != _photo || _document is not null) return;
        AddStep(colour with { Reach = reached }, mode);
    }

    /// <summary>
    /// What lies within the span from <see cref="DistanceNear"/> to <see cref="DistanceFar"/> of the photo's scene depth selected,
    /// softened over <see cref="DistanceSoftness"/>, meeting what is selected as <paramref name="mode"/> says. On a photo it is a step of
    /// the region, kept as the span; in a layered picture it is read from the active picture layer's depth and made part of the
    /// canvas's selection at once. A photo whose depth cannot be had leaves the selection as it was.
    /// </summary>
    public Task SelectDistanceAsync(RegionMode mode) => DistanceAsync(mode, again: false);

    /// <summary>The span's ends moved: the span last selected, while it is still what the selection ends with, is selected again over
    /// what it met.</summary>
    private Task MoveDistanceAsync() => HoldsSpan ? DistanceAsync(SpanMode, again: true) : Task.CompletedTask;

    /// <summary>The selection ends with a span of distance, which moving the span's ends selects again; until it does, the first move
    /// selects it (<see cref="ImageEditorViewModel"/>).</summary>
    public bool HoldsSpan => _document is { } document
        ? _distanceOver is { } over && ReferenceEquals(document.Selection, over.Made)
        : _steps.Count > 0 && _steps[^1] is RegionSceneDepth;

    /// <summary>How the span last selected met the selection.</summary>
    private RegionMode SpanMode => _document is not null ? _distanceOver!.Value.Mode : ((RegionSceneDepth)_steps[^1]).Mode;

    private async Task DistanceAsync(RegionMode mode, bool again)
    {
        int ask = ++_distanceAsks;
        double near = DistanceNear / 100, far = DistanceFar / 100, softness = DistanceSoftness / 100;
        IsReadingDistance = true;
        try
        {
            if (_document is { } document)
            {
                var before = again && _distanceOver is { } over ? over.Before : document.Selection;
                var reach = await document.ReachOfDistanceAsync(near, far, softness);
                if (ask != _distanceAsks || !ReferenceEquals(document, _document) || reach is null) return;
                var made = mode == RegionMode.Replace ? reach : before.Combined(reach, mode);
                document.Select(made);
                _distanceOver = (before, made, mode);
                return;
            }
            if (_photo is not { } photo || _readDepth is null || _photoSize() is not { } size) return;
            var span = new RegionSceneDepth(near, far, softness) { Mode = mode };
            TiledCoverage reached;
            try
            {
                var plane = await DepthAsync(photo);
                Shared(plane);
                var photoSize = new SKSizeI((int)Math.Round(size.Width), (int)Math.Round(size.Height));
                reached = await Task.Run(() => RegionSceneDepths.ReachOn(span, plane, photoSize));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
            {
                return;   // the depth could not be had: the selection stays as it was, and the next ask tries again
            }
            if (ask != _distanceAsks || photo != _photo || _document is not null) return;
            if (again && _steps.Count > 0 && _steps[^1] is RegionSceneDepth)
            {
                _steps[^1] = span with { Reach = reached };
                Changed();
            }
            else AddStep(span with { Reach = reached }, mode);
        }
        finally
        {
            if (ask == _distanceAsks) IsReadingDistance = false;
        }
    }

    /// <summary>The photo's scene depth: read once, and again only after a read that failed.</summary>
    private Task<SceneDepthPlane> DepthAsync(string photo)
    {
        if (_depth is { } held && held.Photo == photo && !held.Read.IsFaulted && !held.Read.IsCanceled) return held.Read;
        var read = Task.Run(() => _readDepth!(photo, CancellationToken.None));
        _depth = (photo, read);
        return read;
    }

    /// <summary>The photo's pixels as it stands: read once, and again only after a read that failed.</summary>
    private Task<UprightPixels> UprightAsync(string photo)
    {
        if (_upright is { } held && held.Photo == photo && !held.Read.IsFaulted && !held.Read.IsCanceled) return held.Read;
        var read = Task.Run(() => _readUpright(photo, CancellationToken.None));
        _upright = (photo, read);
        return read;
    }

    /// <summary>A step of the region added: one that replaces the selection lets what was picked and drawn before go.</summary>
    private void AddStep(RegionStep step, RegionMode mode)
    {
        if (mode == RegionMode.Replace)
        {
            _steps.Clear();
            _cutter.ClearCommand.Execute(null);
        }
        _steps.Add(step);
        Changed();
    }

    /// <summary>Lets every shape and refinement go — what the cut-out tool picked stays.</summary>
    public void Clear()
    {
        _steps.Clear();
        _draft.Clear();
        Changed();
    }

    // ── The polygon lasso ───────────────────────────────────────────────────

    /// <summary>A point of the outline being drawn, the first beginning it in <paramref name="mode"/>.</summary>
    public void AddDraftPoint(MarkupPoint at, RegionMode mode)
    {
        if (_draft.Count == 0) DraftMode = mode;
        _draft.Add(at);
        OnPropertyChanged(nameof(Draft));
    }

    /// <summary>The last point of the outline being drawn taken back; false where there was none.</summary>
    public bool RemoveDraftPoint()
    {
        if (_draft.Count == 0) return false;
        _draft.RemoveAt(_draft.Count - 1);
        OnPropertyChanged(nameof(Draft));
        return true;
    }

    /// <summary>The outline being drawn let go; false where there was none.</summary>
    public bool CancelDraft()
    {
        if (_draft.Count == 0) return false;
        _draft.Clear();
        OnPropertyChanged(nameof(Draft));
        return true;
    }

    /// <summary>The outline being drawn closed and added — where it has the three points that enclose anything; false where
    /// none was being drawn.</summary>
    public bool CloseDraft()
    {
        if (_draft.Count == 0) return false;
        var points = _draft.ToArray();
        _draft.Clear();
        OnPropertyChanged(nameof(Draft));
        if (points.Length >= 3) Add(new RegionPolygon { Points = [.. points], Mode = DraftMode });
        return true;
    }

    // ── Refining ────────────────────────────────────────────────────────────

    /// <summary>The whole photo selected, in place of everything selected before.</summary>
    [RelayCommand]
    private void SelectAll()
    {
        if (_document is { } document) document.Select(TiledCoverage.Covered(document.Size.Width, document.Size.Height));
        else if (_photoSize() is { } size) Add(new RegionRectangle(0, 0, size.Width, size.Height) { Mode = RegionMode.Replace });
    }

    /// <summary>Nothing selected: what is picked and drawn let go.</summary>
    [RelayCommand]
    private void Deselect()
    {
        _cutter.ClearCommand.Execute(null);
        Clear();
        if (_document is { } document && !document.Selection.IsEmpty) document.Select(TiledCoverage.Clear(document.Size.Width, document.Size.Height));
    }

    [RelayCommand]
    private void Invert() => Refine(new RegionInvert());

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Feather() => Refine(new RegionFeather(RefineBy));

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Grow() => Refine(new RegionGrow(RefineBy));

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Shrink() => Refine(new RegionGrow(-RefineBy));

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Smooth() => Refine(new RegionSmooth(RefineBy));

    /// <summary>The photo's subject picked by picture selection — the one thing a box round the whole photo means — in
    /// place of everything selected before.</summary>
    [RelayCommand(CanExecute = nameof(CanSelectSubject))]
    private async Task SelectSubject()
    {
        if (_photoSize() is not { } size) return;
        Clear();
        await _cutter.BoxedAsync(new PictureBox(0, 0, size.Width, size.Height));
        // In a layered picture, the subject picked on the active picture layer becomes the canvas's selection.
        TakePicked();
    }

    /// <summary>
    /// In a layered picture, what picture selection picked on the active picture layer made part of the canvas's selection — meeting
    /// what is selected as <paramref name="mode"/> says — and let go as a pick. False where nothing was picked, or the picture is a
    /// photo, whose picks stay picks.
    /// </summary>
    internal bool TakePicked(RegionMode mode = RegionMode.Replace)
    {
        if (_document is not { } document || _cutter.Shown is not { Bounds: not null } picked || document.FromPicture(picked) is not { } coverage) return false;
        _cutter.ClearCommand.Execute(null);
        document.Select(mode == RegionMode.Replace ? coverage : document.Selection.Combined(coverage, mode));
        return true;
    }

    private bool CanSelectSubject() => _cutter.CanPick && !_cutter.IsWorking;

    /// <summary>Picture selection came or went: whether the subject can be picked follows.</summary>
    public void PickingChanged() => SelectSubjectCommand.NotifyCanExecuteChanged();

    private void Refine(RegionStep step)
    {
        if (_document is { } document)
        {
            document.Select(RegionRaster.Apply(document.Selection, new RecipeRegion { Steps = [step] }, 1));
            return;
        }
        _steps.Add(step);
        Changed();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(Region));
        Reshow();
    }

    /// <summary>What is selected, shown again: the picks at the size the cut-out tool shows them, the region over them.</summary>
    private void Reshow()
    {
        if (_document is { } document)
        {
            // A layered picture's selection, as it shows: at most the shown long side, over the canvas.
            var selected = document.Selection;
            if (selected.IsEmpty)
            {
                Shown = null;
                return;
            }
            var (w, h) = PictureCutter.ShownSize(document.Size.Width, document.Size.Height);
            ShownScale = (double)w / document.Size.Width;
            Shown = new RegionSelection(selected.Resampled(w, h));
            return;
        }
        var picked = _cutter.Shown is { Bounds: not null } mask ? mask : null;
        if (_photoSize() is not { } photo || photo.Width < 1 || photo.Height < 1 || (picked is null && _steps.Count == 0))
        {
            Shown = null;
            return;
        }
        var (width, height) = picked is not null ? (picked.Width, picked.Height) : PictureCutter.ShownSize((int)Math.Round(photo.Width), (int)Math.Round(photo.Height));
        var start = picked is not null ? TiledCoverage.FromBytes(picked.Pixels, width, height) : TiledCoverage.Clear(width, height);
        ShownScale = width / photo.Width;
        Shown = RegionSelection.Of(start, Region, ShownScale);
    }
}
