using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.Services;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Common.Theming;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// Edit mode: the tool panels' state over the photo the viewer is showing. Every change is a new recipe committed to
/// that photo's history — a slider drag, a crop drag or a pen stroke as one step — so undo, redo, the sidecar and the
/// preview all follow from the history and nothing here keeps a copy of the edits.
/// </summary>
/// <remarks>
/// Pointer input arrives from the canvas already mapped: crop handles in the canvas's layout coordinates (where
/// the frame is drawn and its aspect read), markup in source pixels (where annotations live).
/// </remarks>
public partial class ImageEditorViewModel : ObservableObject
{
    /// <summary>Line weight and text size are given relative to the picture, so the same setting reads the same on any photo.</summary>
    private const double WeightUnit = 1.0 / 800, TextUnit = 1.0 / 100;

    /// <summary>A drawn arrow or box shorter than this, in source pixels, was a click and is dropped.</summary>
    private const double MinShape = 4;

    private readonly ImageViewModel _viewer;
    private readonly IShellServices? _shell;
    private readonly ImageEditingServices _services;

    private readonly PhotoRestorer _restorer;

    private CropRect? _straightenBase;
    private bool _straightenGesture;

    /// <summary>The geometry when the crop tool was opened — what Esc goes back to.</summary>
    private RecipeGeometry? _cropSessionStart;

    // An open crop drag.
    private CropHandle _cropHandle;
    private CropRect _cropStart;
    private SKRect _cropStartShown;
    private SKPoint _cropFrom;

    // The resize panel's way of giving a size, while the recipe has no resize to say.
    private bool _preferPixels;

    // An open markup gesture.
    private Guid? _drawing;
    private SKPoint _markupFrom;
    private bool _moving;

    /// <summary>The cut-out tool: a thing in the photo picked out and copied or saved cut out of its background.</summary>
    public PictureCutter Cutter { get; }

    internal ImageEditorViewModel(ImageViewModel viewer, IShellServices? shell, ImageEditingServices services, PhotoRestorer restorer, PictureCutter cutter)
    {
    _viewer   = viewer;
    Cutter    = cutter;
    _shell    = shell;
    _services = services;
    _restorer = restorer;
    // A restoration arriving lets the photo be saved.
    restorer.Restored += () =>
    {
        SaveCommand.NotifyCanExecuteChanged();
        SaveCopyCommand.NotifyCanExecuteChanged();
    };
    }

    private ImageEditSession? Session => _viewer.CurrentSession;
    private ImageRecipe Recipe => Session?.Recipe ?? ImageRecipe.Identity;
    private SKSize? Source => Session?.Proxy?.SourceSize;

    // ── Panels ────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCropTool))]
    [NotifyPropertyChangedFor(nameof(IsAdjustTool))]
    [NotifyPropertyChangedFor(nameof(IsMarkupTool))]
    [NotifyPropertyChangedFor(nameof(IsCutOutTool))]
    [NotifyPropertyChangedFor(nameof(IsResizeTool))]
    [NotifyPropertyChangedFor(nameof(IsExportTool))]
    [NotifyPropertyChangedFor(nameof(Frame))]
    private EditTool _tool = EditTool.Crop;

    public bool IsCropTool   => Tool == EditTool.Crop;
    public bool IsAdjustTool => Tool == EditTool.Adjust;
    public bool IsMarkupTool => Tool == EditTool.Markup;
    public bool IsCutOutTool => Tool == EditTool.CutOut;
    public bool IsResizeTool => Tool == EditTool.Resize;
    public bool IsExportTool => Tool == EditTool.Export;

    /// <summary>What the canvas shows: the uncropped picture while cropping, so the frame can be dragged over all of it.</summary>
    public RenderFrame Frame => IsCropTool ? RenderFrame.CropEditing : RenderFrame.Output;

    partial void OnToolChanged(EditTool value)
    {
        CancelOpenGestures();
        IsLevelTool = false;
        if (value != EditTool.Markup) SelectedId = null;
        _cropSessionStart = value == EditTool.Crop ? Recipe.Geometry : null;
        _viewer.RequestRender();
    }

    /// <summary>Held to compare: the photo as it is on disk.</summary>
    [ObservableProperty] private bool _showOriginal;

    partial void OnShowOriginalChanged(bool value) => _viewer.RequestRender();

    /// <summary>Raised when a new text annotation wants its words typed — the view focuses the text box.</summary>
    public event Action? TextEditRequested;

    // ── Lifecycle, driven by the viewer ───────────────────────────────────

    internal void OnEditModeChanged(bool editing)
    {
        if (editing)
        {
            _cropSessionStart = IsCropTool ? Recipe.Geometry : null;
            Cutter.Follow(Session?.Path);
            return;
        }
        Cutter.Close();   // the photo read for picking is let go with the editor
        CancelOpenGestures();
        ShowOriginal = false;
        IsLevelTool  = false;
        SelectedId   = null;
    }

    internal void OnSessionChanged()
    {
        CancelOpenGestures();
        SelectedId = null;
    _cropSessionStart = IsCropTool ? Recipe.Geometry : null;
        if (_viewer.IsEditing) Cutter.Follow(Session?.Path);
        OnRecipeChanged();
    }

    /// <summary>The recipe moved — by an edit, undo, redo or another window: every panel re-reads it.</summary>
    internal void OnRecipeChanged()
    {
        if (SelectedId is { } id && !Recipe.Markup.Any(e => e.Id == id)) SelectedId = null;
        OnPropertyChanged(string.Empty);
        SaveCommand.NotifyCanExecuteChanged();
        SaveCopyCommand.NotifyCanExecuteChanged();
    }

    private void CancelOpenGestures()
    {
        EndTextEdit();
        if (Session?.History.InGesture == true) Session.History.EndGesture();
        _cropHandle = CropHandle.None;
        _drawing = null;
        _moving = false;
        _straightenGesture = false;
        _straightenBase = null;
    }

    // ── Gestures ──────────────────────────────────────────────────────────

    /// <summary>A continuous change begins — a slider drag — and lasts as one undo step until <see cref="EndGesture"/>.</summary>
    public void BeginGesture()
    {
        Session?.History.BeginGesture();
        _straightenGesture = true;
        _straightenBase = Recipe.Geometry.Crop;
    }

    public void EndGesture()
    {
        _straightenGesture = false;
        _straightenBase = null;
        Session?.History.EndGesture();
    }

    private void Commit(Func<ImageRecipe, ImageRecipe> change) => _viewer.Edit(change);

    // ── Crop, rotate and straighten ───────────────────────────────────────


    public AspectPreset Aspect
    {
        get => Recipe.Geometry.Aspect;
        set => ShapeCrop(Recipe.Geometry with { Aspect = value });
    }

    /// <summary>A non-square preset held upright rather than across.</summary>
    public bool AspectPortrait
    {
        get => Recipe.Geometry.AspectPortrait;
        set => ShapeCrop(Recipe.Geometry with { AspectPortrait = value });
    }

    private void ShapeCrop(RecipeGeometry geometry)
    {
        if (Source is not { } source) return;
        var shaped = geometry.Aspect == AspectPreset.Free ? geometry : geometry with { Crop = CropGeometry.ApplyAspect(geometry, source) };
        Commit(r => r with { Geometry = shaped });
    }

    /// <summary>Fine rotation, −45…45°; the crop shrinks to stay on the picture, and grows back as the angle returns.</summary>
    public double Straighten
    {
        get => Recipe.Geometry.StraightenDegrees;
        set => SetStraighten(Math.Round(value, 1));
    }

    private void SetStraighten(double degrees)
    {
        if (Source is not { } source) return;
        degrees = Math.Clamp(degrees, -RecipeGeometry.MaxStraightenDegrees, RecipeGeometry.MaxStraightenDegrees);
        // Within a drag the crop is refitted from where it began, so passing through a steep angle and back does not
        // leave it shrunk.
        var basis = _straightenGesture ? _straightenBase : Recipe.Geometry.Crop;
        var crop = basis is { } c ? CropGeometry.FitInside(source, degrees, c) : (CropRect?)null;
        Commit(r => r with { Geometry = r.Geometry with { StraightenDegrees = degrees, Crop = crop } });
    }

    /// <summary>The level tool is armed: the next drag on the canvas is a line to straighten against.</summary>
    [ObservableProperty] private bool _isLevelTool;

    /// <summary>Straightens so the line <paramref name="from"/>→<paramref name="to"/>, drawn on the canvas, is level or plumb.</summary>
    public void Level(SKPoint from, SKPoint to)
    {
        if (SKPoint.Distance(from, to) < 4) return;
        _straightenGesture = false;
        SetStraighten(CropGeometry.LevelAngle(Recipe.Geometry, from, to));
        IsLevelTool = false;
    }

    /// <summary>Back to the whole, unstraightened picture; turns and flips stay.</summary>
    [RelayCommand]
    private void ResetCrop() => Commit(r => r with { Geometry = r.Geometry with { Crop = null, StraightenDegrees = 0 } });

    /// <summary>
    /// Esc while cropping: the geometry goes back to how it was when the crop tool was opened, as one undoable step.
    /// False when nothing had changed, so Esc can mean leaving instead.
    /// </summary>
    public bool CancelCrop()
    {
        if (_cropSessionStart is not { } start || start == Recipe.Geometry) return false;
        Commit(r => r with { Geometry = start });
        return true;
    }

    /// <summary>The crop frame on the canvas (layout coordinates), or null while there is no picture to crop.</summary>
    public SKRect? ShownCrop => _viewer.Canvas is { Frame: RenderFrame.CropEditing } canvas && Source is { } source
        ? canvas.StraightenedToCanvas.MapRect(ToRect(CropGeometry.EffectiveCrop(Recipe.Geometry, source)))
        : null;

    /// <summary>The handle under a canvas point.</summary>
    public CropHandle CropHandleAt(SKPoint point, float grab) =>
        ShownCrop is { } shown ? CropDrag.HitTest(shown, point, grab) : CropHandle.None;

    public void BeginCropDrag(CropHandle handle, SKPoint point)
    {
        if (handle == CropHandle.None || ShownCrop is not { } shown || Source is not { } source) return;
        Session?.History.BeginGesture();
        _cropHandle     = handle;
        _cropStart      = CropGeometry.EffectiveCrop(Recipe.Geometry, source);
        _cropStartShown = shown;
        _cropFrom       = point;
    }

    /// <summary>
    /// Follows the drag on the frame as displayed — where the aspect is read — then maps it back to the straightened
    /// frame and stops it at the picture's edge.
    /// </summary>
    public void ContinueCropDrag(SKPoint point)
    {
        if (_cropHandle == CropHandle.None || _viewer.Canvas is not { } canvas || Source is not { } source) return;
        if (!canvas.StraightenedToCanvas.TryInvert(out var toStraightened)) return;

        var aspect = CropGeometry.DisplayAspect(Recipe.Geometry, source);
        var shown = CropDrag.Drag(_cropStartShown, _cropHandle, point - _cropFrom, aspect);
        var desired = FromRect(toStraightened.MapRect(shown));
        var crop = CropGeometry.ClampTowards(source, Recipe.Geometry.StraightenDegrees, _cropStart, desired);
        Commit(r => r with { Geometry = r.Geometry with { Crop = crop } });
    }

    public void EndCropDrag()
    {
        if (_cropHandle == CropHandle.None) return;
        _cropHandle = CropHandle.None;
        Session?.History.EndGesture();
    }

    // ── Adjust ────────────────────────────────────────────────────────────

    public double Exposure    { get => Recipe.Adjustments.Exposure;    set => Adjust(a => a with { Exposure    = Math.Round(value, 2) }); }
    public double Contrast    { get => Recipe.Adjustments.Contrast;    set => Adjust(a => a with { Contrast    = Math.Round(value) }); }
    public double Highlights  { get => Recipe.Adjustments.Highlights;  set => Adjust(a => a with { Highlights  = Math.Round(value) }); }
    public double Shadows     { get => Recipe.Adjustments.Shadows;     set => Adjust(a => a with { Shadows     = Math.Round(value) }); }
    public double Saturation  { get => Recipe.Adjustments.Saturation;  set => Adjust(a => a with { Saturation  = Math.Round(value) }); }
    public double Temperature { get => Recipe.Adjustments.Temperature; set => Adjust(a => a with { Temperature = Math.Round(value) }); }
    public double Tint        { get => Recipe.Adjustments.Tint;        set => Adjust(a => a with { Tint        = Math.Round(value) }); }

    private void Adjust(Func<RecipeAdjustments, RecipeAdjustments> change) => Commit(r => r with { Adjustments = change(r.Adjustments) });

    // ── AI enhance and upscale ───────────────────────────────────────────

    /// <summary>This machine can restore pictures: the AI controls show only then.</summary>
    public bool CanRestore => _restorer.Available;

    /// <summary>The photo restored by the picture tools — cleaned of softness, noise and JPEG blocks — before every other edit.</summary>
    public bool AiEnhance
    {
        get => Recipe.Restore is not null;
        set => Commit(r => r with { Restore = value ? r.Restore ?? new RecipeRestore() : null });
    }

    /// <summary>How much of the restoration shows over the photo as it was, 0–100.</summary>
    public double EnhanceStrength
    {
        get => (Recipe.Restore?.Strength ?? 1) * 100;
        set => Restore(x => x with { Strength = Math.Clamp(Math.Round(value) / 100, 0, 1) });
    }

    /// <summary>The faces in the photo remade as it is restored.</summary>
    public bool RepairFaces
    {
        get => Recipe.Restore?.Faces ?? true;
        set => Restore(x => x with { Faces = value });
    }

    /// <summary>AI upscale off: the restoration, if any, keeps the photo's size.</summary>
    public bool UpscaleOff
    {
        get => (Recipe.Restore?.Scale ?? 1) == 1;
        set { if (value && Recipe.Restore is { Scale: > 1 }) Restore(x => x with { Scale = 1 }); }
    }

    /// <summary>AI upscale to twice the size — restoring the photo if it was not.</summary>
    public bool UpscaleTwice
    {
        get => Recipe.Restore?.Scale == 2;
        set { if (value) Restore(x => x with { Scale = 2 }); }
    }

    /// <summary>AI upscale to four times the size.</summary>
    public bool UpscaleFourTimes
    {
        get => Recipe.Restore?.Scale == 4;
        set { if (value) Restore(x => x with { Scale = 4 }); }
    }

    /// <summary>Twice the size is offered where the photo restores at it; a very large one does not.</summary>
    public bool UpscaleTwiceOffered => Offered(2);

    public bool UpscaleFourTimesOffered => Offered(4);

    private bool Offered(int scale) => Source is { } s && _restorer.Restores((int)Math.Round(s.Width), (int)Math.Round(s.Height), scale);

    private void Restore(Func<RecipeRestore, RecipeRestore> change) => Commit(r => r with { Restore = change(r.Restore ?? new RecipeRestore()) });

    /// <summary>Reads the picture as cropped — its tone and colour untouched — and sets the sliders from it.</summary>
    [RelayCommand]
    private void AutoEnhance()
    {
        if (Session is not { } session || session.OutputSize(Recipe) is not { } output) return;
        double scale = Math.Min(1, 512.0 / Math.Max(output.Width, output.Height));
        using var picture = session.Snapshot(Recipe, new RenderRequest(RenderFrame.Output, scale, Adjustments: false, Markup: false));
        if (picture is null) return;
        var adjustments = Editing.Rendering.AutoEnhance.Analyse(picture);
        Commit(r => r with { Adjustments = adjustments });
    }

    [RelayCommand]
    private void ResetAdjustments() => Commit(r => r with { Adjustments = RecipeAdjustments.Identity });

    // ── Markup ────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTextStyle))]
    private MarkupTool _markupTool = MarkupTool.Select;

    /// <summary>The ink for new annotations, and the selected one's.</summary>
    [ObservableProperty] private ColorSpec _inkColour = ColorSpec.FromSwatch("Red");

    /// <summary>Line weight, relative to the picture (1–20).</summary>
    [ObservableProperty] private double _weight = 4;

    /// <summary>Text size, relative to the picture (1–20).</summary>
    [ObservableProperty] private double _textSize = 5;

    [ObservableProperty] private string _fontFamily = "Segoe UI";

    /// <summary>New boxes are filled with a wash of the ink.</summary>
    [ObservableProperty] private bool _fillBoxes;

    /// <summary>New text sits on a plate, for legibility over a busy picture.</summary>
    [ObservableProperty] private bool _textBacking;

    /// <summary>The selected annotation's id.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Selected))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(IsTextSelected))]
    [NotifyPropertyChangedFor(nameof(SelectedText))]
    [NotifyPropertyChangedFor(nameof(IsTextStyle))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private Guid? _selectedId;

    public MarkupElement? Selected => SelectedId is { } id ? Recipe.Markup.FirstOrDefault(e => e.Id == id) : null;
    public bool HasSelection => Selected is not null;
    public bool IsTextSelected => Selected is MarkupText;

    /// <summary>The font controls apply: text is being placed, or is selected.</summary>
    public bool IsTextStyle => MarkupTool == MarkupTool.Text || IsTextSelected;

    /// <summary>The installed font families, by name.</summary>
    public IReadOnlyList<string> FontFamilies { get; } =
        [.. Fonts.SystemFontFamilies.Select(f => f.Source).Order(StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>The selected text annotation's words; typing into it edits the annotation.</summary>
    public string SelectedText
    {
        get => (Selected as MarkupText)?.Text ?? string.Empty;
        set
        {
            if (Selected is not MarkupText text || text.Text == value) return;
            Replace(text with { Text = value });
        }
    }

    partial void OnInkColourChanged(ColorSpec value)
    {
        if (Selected is { } element)
        {
            uint ink = Argb(value);
            Replace(element switch
            {
                MarkupStroke { Style: StrokeStyle.Highlighter } s => s with { Color = HighlighterInk(ink) },
                MarkupBox { Fill: not 0 } b                       => b with { Color = ink, Fill = Wash(ink) },
                _                                                 => element with { Color = ink },
            });
        }
    }

    partial void OnWeightChanged(double value)
    {
        if (Selected is { } element && element is not MarkupText)
            Replace(element with { StrokeWidth = StrokeFor(element is MarkupStroke { Style: StrokeStyle.Highlighter }) });
    }

    partial void OnTextSizeChanged(double value)
    {
        if (Selected is MarkupText text && LongEdge is { } edge) Replace(text with { FontSize = Math.Max(4, value * TextUnit * edge) });
    }

    partial void OnFontFamilyChanged(string value)
    {
        if (Selected is MarkupText text) Replace(text with { FontFamily = value });
    }

    partial void OnFillBoxesChanged(bool value)
    {
        if (Selected is MarkupBox box) Replace(box with { Fill = value ? Wash(box.Color) : 0 });
    }

    partial void OnTextBackingChanged(bool value)
    {
        if (Selected is MarkupText text) Replace(text with { Backing = value ? BackingFor(text.Color) : 0 });
    }

    /// <summary>
    /// A press on the canvas at <paramref name="at"/> (source pixels): selects, or starts drawing with the current
    /// tool. <paramref name="tolerance"/> is how near a click must be to hit a line, in source pixels.
    /// </summary>
    public void MarkupPress(SKPoint at, float tolerance)
    {
        if (Session is not { } session) return;
        EndTextEdit();   // a press elsewhere finishes the text being typed
        _markupFrom = at;

        if (MarkupTool == MarkupTool.Select)
        {
            var hit = Recipe.Markup.Reverse().FirstOrDefault(e => _services.Markup.HitTest(e, at, tolerance));
            SelectedId = hit?.Id;
            if (hit is null) return;
            PullStyleFrom(hit);
            session.History.BeginGesture();
            _moving = true;
            return;
        }

        var point = new MarkupPoint(at.X, at.Y);
        uint ink = Argb(InkColour);
        MarkupElement created = MarkupTool switch
        {
            MarkupTool.Arrow => new MarkupArrow { From = point, To = point, Color = ink, StrokeWidth = StrokeFor(false) },
            MarkupTool.Box   => new MarkupBox { Corner = point, OppositeCorner = point, Color = ink, StrokeWidth = StrokeFor(false), Fill = FillBoxes ? Wash(ink) : 0 },
            MarkupTool.Pen   => new MarkupStroke { Points = [point], Color = ink, StrokeWidth = StrokeFor(false) },
            MarkupTool.Highlighter => new MarkupStroke { Points = [point], Style = StrokeStyle.Highlighter, Color = HighlighterInk(ink), StrokeWidth = StrokeFor(true) },
            _ => new MarkupText
            {
                Anchor = point, Color = ink, FontFamily = FontFamily, FontSize = Math.Max(4, TextSize * TextUnit * (LongEdge ?? 1000)),
                RotationDegrees = UprightTextRotation(), Backing = TextBacking ? BackingFor(ink) : 0,
            },
        };

        session.History.BeginGesture();
        Commit(r => r with { Markup = r.Markup.Add(created) });
        _drawing = created.Id;
        SelectedId = created.Id;
        if (created is MarkupText) TextEditRequested?.Invoke();   // its gesture ends when the typing does
    }

    /// <summary>The pointer moved with the button down: the new shape grows, the stroke extends, the selection moves.</summary>
    public void MarkupDrag(SKPoint at, float minStep)
    {
        if (_moving && Selected is { } selected)
        {
            var moved = selected.Offset(at.X - _markupFrom.X, at.Y - _markupFrom.Y);
            _markupFrom = at;
            Replace(moved);
            return;
        }
        if (_drawing is not { } id || Recipe.Markup.FirstOrDefault(e => e.Id == id) is not { } drawing) return;

        var point = new MarkupPoint(at.X, at.Y);
        switch (drawing)
        {
            case MarkupArrow arrow: Replace(arrow with { To = point }); break;
            case MarkupBox box:     Replace(box with { OppositeCorner = point }); break;
            case MarkupStroke stroke:
                var last = stroke.Points[^1];
                if (Math.Abs(last.X - point.X) + Math.Abs(last.Y - point.Y) >= minStep)
                    Replace(stroke with { Points = stroke.Points.Add(point) });
                break;
        }
    }

    /// <summary>The button came up: the gesture is one step — or none, for a shape too small to have been meant.</summary>
    public void MarkupRelease()
    {
        if (Session is not { } session) return;
        if (_moving)
        {
            _moving = false;
            session.History.EndGesture();
            return;
        }
        if (_drawing is not { } id) return;
        var drawn = Recipe.Markup.FirstOrDefault(e => e.Id == id);
        if (drawn is MarkupText) return;   // still being typed
        _drawing = null;

        bool tooSmall = drawn switch
        {
            MarkupArrow a => Distance(a.From, a.To) < MinShape,
            MarkupBox b   => Math.Abs(b.OppositeCorner.X - b.Corner.X) < MinShape || Math.Abs(b.OppositeCorner.Y - b.Corner.Y) < MinShape,
            _             => false,
        };
        if (tooSmall)
        {
            session.History.CancelGesture();
            SelectedId = null;
        }
        else session.History.EndGesture();
    }

    /// <summary>Typing into a new text annotation finished: kept as one step, or dropped if nothing was typed.</summary>
    public void EndTextEdit()
    {
        if (Session is not { } session || _drawing is not { } id) return;
        _drawing = null;
        if (Recipe.Markup.FirstOrDefault(e => e.Id == id) is MarkupText { Text.Length: 0 })
        {
            session.History.CancelGesture();
            SelectedId = null;
        }
        else session.History.EndGesture();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteSelected()
    {
        if (SelectedId is not { } id) return;
        Commit(r => r with { Markup = r.Markup.RemoveAll(e => e.Id == id) });
        SelectedId = null;
    }

    [RelayCommand]
    private void ClearMarkup()
    {
        SelectedId = null;
        Commit(r => r with { Markup = [] });
    }

    /// <summary>The selected annotation's outline on the canvas (layout coordinates), for the selection to be drawn around.</summary>
    public SKPoint[]? SelectedOutline => Selected is { } element && _viewer.Canvas is { } canvas
        ? canvas.SourceToCanvas.MapPoints(_services.Markup.Outline(element))
        : null;

    private void Replace(MarkupElement element) =>
        Commit(r => r with { Markup = [.. r.Markup.Select(e => e.Id == element.Id ? element : e)] });

    /// <summary>Selecting an annotation shows its style, so the next change starts from what it has.</summary>
    private void PullStyleFrom(MarkupElement element)
    {
        var held = SelectedId;
        SelectedId = null;   // read the style without writing it back to the element
        if (LongEdge is { } edge)
        {
            if (element is MarkupText text)
            {
                FontFamily  = text.FontFamily;
                TextSize    = Math.Round(text.FontSize / (TextUnit * edge), 1);
                TextBacking = text.Backing != 0;
            }
            else
            {
                double unit = WeightUnit * edge * (element is MarkupStroke { Style: StrokeStyle.Highlighter } ? 4 : 1);
                Weight = Math.Round(element.StrokeWidth / unit, 1);
            }
        }
        if (element is MarkupBox box) FillBoxes = box.Fill != 0;
        SelectedId = held;
    }

    private double? LongEdge => Source is { } s ? Math.Max(s.Width, s.Height) : null;

    private double StrokeFor(bool highlighter) => Math.Max(1, Weight * WeightUnit * (LongEdge ?? 1000) * (highlighter ? 4 : 1));

    /// <summary>Text placed on a turned or straightened picture is turned back, so it reads upright as it was typed.</summary>
    private double UprightTextRotation() => -(Recipe.Geometry.QuarterTurns * 90 + Recipe.Geometry.StraightenDegrees);

    /// <summary>The ink as a fixed colour: a swatch is resolved now, so a theme change never recolours an annotation.</summary>
    private static uint Argb(ColorSpec spec)
    {
        var colour = spec.Kind switch
        {
            ColorSpecKind.Swatch => (SwatchPalette.Resolve(spec.SwatchKey) as SolidColorBrush)?.Color ?? Colors.Red,
            ColorSpecKind.Custom => spec.Color,
            _                    => Colors.Red,
        };
        return (uint)colour.A << 24 | (uint)colour.R << 16 | (uint)colour.G << 8 | colour.B;
    }

    private static uint HighlighterInk(uint ink) => 0x66000000u | (ink & 0x00FFFFFF);
    private static uint Wash(uint ink) => 0x40000000u | (ink & 0x00FFFFFF);

    /// <summary>A plate that the ink reads against: dark under light ink, light under dark.</summary>
    private static uint BackingFor(uint ink)
    {
        double luma = (0.2126 * ((ink >> 16) & 0xFF) + 0.7152 * ((ink >> 8) & 0xFF) + 0.0722 * (ink & 0xFF)) / 255;
        return luma > 0.5 ? 0xB0000000u : 0xD0FFFFFFu;
    }

    private static double Distance(MarkupPoint a, MarkupPoint b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // ── Resize ────────────────────────────────────────────────────────────

    public bool IsResized => Recipe.Resize is not null;

    /// <summary>
    /// Sizes are given as a percentage rather than in pixels. A resize in the recipe says which; until there is one,
    /// it is the panel's own choice, since choosing pixels without changing the size is no edit.
    /// </summary>
    public bool ResizeByPercent
    {
        get => Recipe.Resize?.Mode switch
        {
            ResizeMode.Percent => true,
            ResizeMode.Pixels  => false,
            _                  => !_preferPixels,
        };
        set
        {
            if (value == ResizeByPercent) return;
            _preferPixels = !value;
            // A resize already made is carried over to the other way of giving it, at the same output size.
            if (Recipe.Resize is not null && OutputSize is { } output && CroppedSize is { } cropped)
                SetResize(value
                    ? new RecipeResize { Mode = ResizeMode.Percent, Percent = Math.Round(100.0 * output.Width / cropped.Width) }
                    : new RecipeResize { Mode = ResizeMode.Pixels, Width = output.Width, Height = output.Height, LockAspect = true });
            OnPropertyChanged(nameof(ResizeByPercent));
            OnPropertyChanged(nameof(ResizeByPixels));
        }
    }

    /// <summary>The other side of <see cref="ResizeByPercent"/>, for the pair of choices that select between them.</summary>
    public bool ResizeByPixels
    {
        get => !ResizeByPercent;
        set => ResizeByPercent = !value;
    }

    public double ResizePercent
    {
        get => Recipe.Resize is { Mode: ResizeMode.Percent } r ? r.Percent : 100;
        set => SetResize(new RecipeResize { Mode = ResizeMode.Percent, Percent = Math.Clamp(Math.Round(value), 1, 400) });
    }

    public int ResizeWidth
    {
        get => Recipe.Resize is { Mode: ResizeMode.Pixels } r ? r.Width : OutputSize?.Width ?? 0;
        set => SetPixels(Math.Max(1, value), null);
    }

    public int ResizeHeight
    {
        get => OutputSize?.Height ?? 0;
        set => SetPixels(null, Math.Max(1, value));
    }

    public bool LockAspect
    {
        get => Recipe.Resize?.LockAspect ?? true;
        set => SetResize((Recipe.Resize ?? PixelsAsCropped()) with { LockAspect = value });
    }

    /// <summary>The finished size, as it will be saved.</summary>
    public string OutputSizeText => OutputSize is { } s ? Str.Format("Images.Edit.Resize.OutputFormat", s.Width, s.Height) : string.Empty;

    private SKSizeI? OutputSize => Session?.OutputSize(Recipe);

    /// <summary>The cropped and turned size, before any resize.</summary>
    private SKSizeI? CroppedSize => Session?.OutputSize(Recipe with { Resize = null });

    private RecipeResize PixelsAsCropped() =>
        new() { Mode = ResizeMode.Pixels, Width = CroppedSize?.Width ?? 1, Height = CroppedSize?.Height ?? 1, LockAspect = true };

    private void SetPixels(int? width, int? height)
    {
        if (CroppedSize is not { } cropped) return;
        var current = Recipe.Resize is { Mode: ResizeMode.Pixels } r ? r : PixelsAsCropped();
        double ratio = (double)cropped.Width / cropped.Height;
        var next = current.LockAspect
            ? current with { Width = width ?? (int)Math.Round(height!.Value * ratio) }
            : current with { Width = width ?? current.Width, Height = height ?? current.Height };
        SetResize(next with { Height = next.LockAspect ? (int)Math.Round(next.Width / ratio) : next.Height });
    }

    /// <summary>A resize that changes nothing is no resize.</summary>
    private void SetResize(RecipeResize resize)
    {
        bool unchanged = resize.Mode == ResizeMode.Percent && resize.Percent == 100
                      || resize.Mode == ResizeMode.Pixels && CroppedSize is { } c && resize.Width == c.Width && resize.Height == c.Height && resize.LockAspect;
        Commit(r => r with { Resize = unchanged ? null : resize });
    }

    [RelayCommand]
    private void ResetResize() => Commit(r => r with { Resize = null });

    // ── Export ────────────────────────────────────────────────────────────

    public bool StripMetadata
    {
        get => Recipe.Export.StripMetadata;
        set => Commit(r => r with { Export = r.Export with { StripMetadata = value } });
    }

    /// <summary>JPEG and WebP quality, 50–100.</summary>
    public int Quality
    {
        get => Recipe.Export.JpegQuality;
        set => Commit(r => r with { Export = r.Export with { JpegQuality = Math.Clamp(value, 50, 100) } });
    }

    private ImageContainer SourceContainer =>
        Session?.Proxy?.Container ?? (Session is { } s ? ImageContainers.FromExtension(s.Path) : ImageContainer.Unknown);

    private SaveCapability Capability =>
        _services.Exporter.Capability(SourceContainer, Session?.Proxy?.IsAnimated == true, SourceContainer);

    /// <summary>The photo can be overwritten in place (Save), not only copied.</summary>
    public bool CanSaveInPlace => Session is { IsFileBacked: true, Proxy: not null } && Capability.CanSaveInPlace;

    /// <summary>Why Save is unavailable, or what it will not keep; null when there is nothing to say.</summary>
    public string? SaveNote => Session switch
    {
        null or { Proxy: null }             => null,
        { IsFileBacked: false }             => Str.Get("Images.Edit.Save.NoteArchive"),
        { Proxy.IsAnimated: true }          => Str.Get("Images.Edit.Save.NoteAnimated"),
        _ when !Capability.CanSaveInPlace   => Str.Format("Images.Edit.Save.NoteFormatFormat", SourceContainer.ToString().ToUpperInvariant()),
        _ when !Capability.KeepsMetadata && !StripMetadata => Str.Format("Images.Edit.Save.NoteNoMetadataFormat", SourceContainer.ToString().ToUpperInvariant()),
        _                                   => null,
    };

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCopyCommand))]
    private bool _isSaving;

    /// <summary>The photo can be written: not saving already, and its restoration — where its recipe asks for one — made.</summary>
    private bool CanSave() => !IsSaving && CanSaveInPlace && !_restorer.IsPending;
    private bool CanSaveCopy() => !IsSaving && Session?.Proxy is not null && !_restorer.IsPending;

    /// <summary>Bakes the edits into the original after a confirmation; the recipe is then spent.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        if (_shell is null || Session is not { } session) return;
        var name = Path.GetFileName(session.Path);
        if (!await _shell.ConfirmAsync(Str.Get("Images.Edit.Save.ConfirmTitle"), Str.Format("Images.Edit.Save.ConfirmMessageFormat", name)))
            return;

        Queue(new ImageSaveTask(_services, session.Path, session.Recipe, SourceContainer, session.Path, replace: true, session.RestoredPath), success =>
        {
            if (!success) return;
            session.Baked();
            _viewer.ReloadCurrent();
            _shell.ShowNotification(Str.Format("Images.Edit.Save.DoneFormat", name));
        });
    }

    /// <summary>Writes the edited photo to a new file, in the format its name asks for; the original stays as it is.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveCopy))]
    private async Task SaveCopy()
    {
        if (_shell is null || Session is not { } session) return;
        var source = SourceContainer;
        var defaultTarget = _services.Encoders.CanEncode(source) ? source : ImageContainer.Jpeg;
        var suggested = Path.GetFileNameWithoutExtension(session.Path) + Str.Get("Images.Edit.Save.CopySuffix") + defaultTarget.Extension();
        var extensions = ImageExporter.CopyFormats.Select(c => c.Extension()).Prepend(defaultTarget.Extension()).Distinct().ToList();
        var folder = session.IsFileBacked ? Path.GetDirectoryName(session.Path) : null;

        var destination = await _shell.PickSaveFileAsync(suggested, extensions, folder);
        if (destination is null) return;

        var target = ImageContainers.FromExtension(destination);
        if (!_services.Encoders.CanEncode(target))
        {
            target = defaultTarget;
            destination = Path.ChangeExtension(destination, target.Extension());
        }

        var recipe = session.Recipe;
        Queue(new ImageSaveTask(_services, session.Path, recipe, target, destination, replace: File.Exists(destination), session.RestoredPath), success =>
        {
            if (success) _shell.ShowNotification(Str.Format("Images.Edit.Save.CopyDoneFormat", Path.GetFileName(destination)));
        });
    }

    private void Queue(ImageSaveTask task, Action<bool> finished)
    {
        if (_shell is null) return;
        IsSaving = true;
        _shell.QueueBackgroundTask(task, onComplete: success => _ = _shell.RunOnUiAsync(() =>
        {
            IsSaving = false;
            if (!success || task.Failure is not null)
            {
                _shell.ShowError(Str.Format("Images.Edit.Save.FailedFormat", Path.GetFileName(task.Destination), task.Failure?.Message ?? string.Empty));
                finished(false);
                return;
            }
            finished(true);
        }));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static SKRect ToRect(CropRect r) => SKRect.Create((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height);
    private static CropRect FromRect(SKRect r) => CropRect.FromEdges(r.Left, r.Top, r.Right, r.Bottom);
}
