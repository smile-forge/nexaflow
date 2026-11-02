using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing.Recipe;
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
}

/// <summary>
/// A way of selecting as its button offers it: picked by picture selection, brushed (Retouch only), or drawn by hand — a
/// rectangle, an ellipse, a lasso drawn freehand, or a polygon clicked point to point. One choice standing for the way and the
/// shape it draws, so a panel offers each once.
/// </summary>
public enum SelectionWay
{
    Pick,
    Brush,
    Rectangle,
    Ellipse,
    Lasso,
    Polygon,
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
}

/// <summary>
/// The selection drawn by hand in the photo editor, before Cut out or Erase takes it: marquee and lasso shapes, and
/// refinements of everything selected so far, in the order they were made — the region a recipe keeps
/// (<see cref="RecipeRegion"/>). It starts from what the cut-out tool has picked, so the two together are the one selection
/// both tools take; a shape that replaces the selection lets the picks go too. Shown over the photo as
/// <see cref="Shown"/>, at the size the cut-out tool shows its picks. Shapes are in the photo's pixels as it stands.
/// </summary>
public sealed partial class RegionMarking : ObservableObject
{
    /// <summary>The least and most a refinement works by, in the photo's pixels.</summary>
    public const double LeastRefine = 1, MostRefine = 250;

    private readonly PictureCutter _cutter;
    private readonly Func<SKSize?> _photoSize;
    private readonly List<RegionStep> _steps = [];
    private readonly List<MarkupPoint> _draft = [];
    private string? _photo;

    private IDocumentSelection? _document;

    /// <param name="cutter">The cut-out tool, whose picks the selection starts from.</param>
    /// <param name="photoSize">The photo's size as it stands, once it is read; null before.</param>
    public RegionMarking(PictureCutter cutter, Func<SKSize?> photoSize)
    {
        _cutter = cutter;
        _photoSize = photoSize;
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
        if (shape.Mode == RegionMode.Replace)
        {
            _steps.Clear();
            _cutter.ClearCommand.Execute(null);
        }
        _steps.Add(shape);
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
