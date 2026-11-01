using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Erasing;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.Features.Images.Services;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Common.Theming;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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

    private readonly ArtistViewModel _viewer;
    private readonly IShellServices? _shell;
    private readonly ImageEditingServices _services;

    private readonly PhotoRestorer _restorer;
    private readonly BackgroundRemover _remover;
    private readonly PhotoEraser _eraser;

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

    /// <summary>The photo's background is removed: only what was selected is kept, the rest transparent.</summary>
    public bool RemovesBackground => Recipe.CutOut is not null;

    /// <summary>The selection drawn by hand — marquee, lasso and refinements — over what the cut-out tool picks: the one
    /// selection Cut out and Erase both take.</summary>
    public RegionMarking RegionMarks { get; }

    /// <summary>What is selected as a recipe's cut-out: what the cut-out tool picked and the region drawn over it; null where
    /// neither is.</summary>
    private RecipeCutOut? MarkedCutOut => MarkedRegion is { } region ? (Cutter.PickedCutOut ?? new RecipeCutOut()) with { Region = region } : Cutter.PickedCutOut;

    /// <summary>
    /// The selection drawn by hand as the recipe of the picture the tools work on keeps it: the photo editor's region; in a
    /// layered picture, the canvas's selection carried onto the active picture layer's pixels as coverage — joining what is
    /// picked there, or alone.
    /// </summary>
    private RecipeRegion? MarkedRegion => _viewer.CurrentDocument is { } document ? CarriedRegion(document) : RegionMarks.Region;

    // The canvas's selection last carried onto a picture layer, kept while neither changes: carrying it is a full-size resample.
    private (TiledCoverage Selection, PictureLayer Layer, bool Picks, RecipeRegion? Region)? _carried;

    private RecipeRegion? CarriedRegion(LayeredSession document)
    {
        var picture = document.Picture;
        if (picture.Selection.IsEmpty || picture.Active is not PictureLayer layer || document.RenderingOf(layer.Id)?.Proxy is not { } proxy) return null;
        bool picks = Cutter.PickedCutOut is not null;
        if (_carried is { } carried && ReferenceEquals(carried.Selection, picture.Selection) && ReferenceEquals(carried.Layer, layer) && carried.Picks == picks)
            return carried.Region;
        var source = proxy.SourceSize;
        RecipeRegion? region = null;
        if (LayeredSession.SourceToCanvas(layer, source).TryInvert(out var toSource))
        {
            var coverage = CoverageMapping.Map(picture.Selection, toSource, (int)Math.Round(source.Width), (int)Math.Round(source.Height));
            region = new RecipeRegion { Steps = [new RegionCoverage { Coverage = coverage, Mode = picks ? RegionMode.Add : RegionMode.Replace }] };
        }
        _carried = (picture.Selection, layer, picks, region);
        return region;
    }

    /// <summary>The active layer of a layered picture, where it is a pixel layer the tools may edit; null otherwise.</summary>
    private PixelLayer? ActivePixels => _viewer.CurrentDocument?.Picture.Active as PixelLayer is { Locked: false } layer ? layer : null;

    /// <summary>How the Cut out tool marks what to keep; another way lets go of an outline half drawn.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DrawsMarquee), nameof(DrawsLasso), nameof(DrawsShapes), nameof(PicksByModel))]
    private CutOutMarkWay _cutOutWay = CutOutMarkWay.Pick;

    partial void OnCutOutWayChanged(CutOutMarkWay value) => RegionMarks.CancelDraft();

    /// <summary>The open tool draws on the photo with the marquee.</summary>
    public bool DrawsMarquee => IsCutOutTool && CutOutWay == CutOutMarkWay.Marquee || IsRetouchTool && EraseMarks.Way == EraseMarkWay.Marquee;

    /// <summary>The open tool draws on the photo with the lasso.</summary>
    public bool DrawsLasso => IsCutOutTool && CutOutWay == CutOutMarkWay.Lasso || IsRetouchTool && EraseMarks.Way == EraseMarkWay.Lasso;

    /// <summary>The open tool picks things out of the photo by picture selection.</summary>
    public bool PicksByModel => IsCutOutTool && CutOutWay == CutOutMarkWay.Pick || IsRetouchTool && EraseMarks.IsPicking;

    /// <summary>The open tool draws on the photo with the marquee or the lasso.</summary>
    public bool DrawsShapes => DrawsMarquee || DrawsLasso;

    /// <summary>The marquee (M): taken up — or, held already, turned from one shape to the other.</summary>
    public void ChooseMarquee()
    {
        if (DrawsMarquee) RegionMarks.Marquee = RegionMarks.Marquee == MarqueeShape.Rectangle ? MarqueeShape.Ellipse : MarqueeShape.Rectangle;
        else MarkBy(CutOutMarkWay.Marquee, EraseMarkWay.Marquee);
    }

    /// <summary>The lasso (L): taken up — or, held already, turned from freehand to polygon and back.</summary>
    public void ChooseLasso()
    {
        if (DrawsLasso) RegionMarks.Lasso = RegionMarks.Lasso == LassoShape.Freehand ? LassoShape.Polygon : LassoShape.Freehand;
        else MarkBy(CutOutMarkWay.Lasso, EraseMarkWay.Lasso);
    }

    private void MarkBy(CutOutMarkWay cutOut, EraseMarkWay erase)
    {
        if (IsCutOutTool) CutOutWay = cutOut;
        else if (IsRetouchTool) EraseMarks.Way = erase;
    }

    /// <summary>Whether the open tool takes a selection — Cut out and Erase do.</summary>
    public bool TakesSelection => IsCutOutTool || IsRetouchTool;

    /// <summary>Keeps only what is selected — what the cut-out tool has picked and the region drawn over it — in the photo or
    /// picture layer itself, an edit like any other, undone or put back by Keep background; the mask is made at full size in the
    /// background and kept. On a pixel layer, what lies outside the selection is deleted.</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveBackground))]
    private void RemoveBackground()
    {
        if (_viewer.CurrentDocument is { } document && ActivePixels is { } pixels)
        {
            document.Commit(p => p.WithLayer(pixels.Id, l => ((PixelLayer)l) with { Pixels = ((PixelLayer)l).Pixels.KeptWhere(p.Selection) }));
            return;
        }
        if (MarkedCutOut is { } cutOut) Commit(r => r with { CutOut = cutOut });
    }

    private bool CanRemoveBackground() => ActivePixels is not null ? _viewer.CurrentDocument!.Picture.Selection.IsEmpty == false
                                          : !Cutter.IsWorking && MarkedCutOut is { } cutOut && cutOut != Recipe.CutOut;

    /// <summary>What is selected copied cut out of the photo: transparent round it, as selected as each pixel is, cropped to it.</summary>
    [RelayCommand(CanExecute = nameof(CanKeepCutOut))]
    private async Task CopyCutOut()
    {
        try
        {
            if (await Cutter.CutOutAsync(MarkedRegion, CancellationToken.None) is { } png)
            {
                PictureClipboard.CopyPng(png);
                _shell?.ShowNotification(Str.Get("Images.CutOut.Copied"));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or InvalidDataException or System.Runtime.InteropServices.ExternalException)
        {
            _shell?.ShowError(Str.Format("Images.CutOut.Failed", ex.Message));
        }
    }

    /// <summary>What is selected saved cut out of the photo, as a PNG where the reader chooses — beside the photo to start with.</summary>
    [RelayCommand(CanExecute = nameof(CanKeepCutOut))]
    private async Task SaveCutOut()
    {
        if (_shell is null || Session?.Path is not { } photo) return;
        var suggested = Path.GetFileNameWithoutExtension(photo) + Str.Get("Images.CutOut.FileSuffix") + ".png";
        var folder = ImageEditingServices.IsFileBacked(photo) ? Path.GetDirectoryName(photo) : null;
        var destination = await _shell.PickSaveFileAsync(suggested, [".png"], folder);
        if (destination is null) return;
        try
        {
            if (await Cutter.CutOutAsync(MarkedRegion, CancellationToken.None) is not { } png) return;
            await _services.FileSystem.WriteAllBytesAsync(Path.ChangeExtension(destination, ".png"), png);
            _shell.ShowNotification(Str.Format("Images.CutOut.SavedFormat", Path.GetFileName(destination)));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _shell.ShowError(Str.Format("Images.CutOut.Failed", ex.Message));
        }
    }

    private bool CanKeepCutOut() => !Cutter.IsWorking && RegionMarks.HasSelection;

    /// <summary>Puts the photo's background back.</summary>
    [RelayCommand(CanExecute = nameof(RemovesBackground))]
    private void KeepBackground() => Commit(r => r with { CutOut = null });

    /// <summary>The Erase tool's marks: brush strokes, the fill, where the healing brush copies from.</summary>
    public EraseMarking EraseMarks { get; }

    /// <summary>Erases what is marked — what the cut-out tool has picked, the region drawn over it, and the strokes — as one
    /// step of the recipe, undone like any other; on a pixel layer, the selection and the strokes filled straight into its pixels.
    /// The marks go once it is.</summary>
    [RelayCommand(CanExecute = nameof(CanErase))]
    private async Task Erase()
    {
        if (_viewer.CurrentDocument is { } document && ActivePixels is { } pixels)
        {
            await ErasePixelsAsync(document, pixels);
            return;
        }
        if (EraseMarks.Step(Cutter.PickedCutOut, MarkedRegion) is not { } step) return;
        Commit(r => r with { Erasures = r.Erasures.Add(step) });
        ClearMarks();
    }

    /// <summary>A pixel layer erased where the selection and the strokes mark it — off the UI thread — as one step of the picture.</summary>
    private async Task ErasePixelsAsync(LayeredSession document, PixelLayer layer)
    {
        var picture = document.Picture;
        if (PixelEraseMask(picture) is not { } mask) return;
        IsErasingPixels = true;
        try
        {
            var erased = await LayerErasing.EraseAsync(layer.Pixels, mask, picture.Width, picture.Height, EraseMarks.Fill, EraseMarks.HealOffset,
                                                       _eraser.Erasing, null, CancellationToken.None);
            if (!ReferenceEquals(document.Picture, picture)) return;   // edited meanwhile: the result is of a picture gone
            document.Commit(p => p.WithLayer(layer.Id, l => ((PixelLayer)l) with { Pixels = erased }));
            ClearMarks();
        }
        catch (InvalidOperationException ex)
        {
            _shell?.ShowError(Str.Format("Images.Erase.Failed", ex.Message));
        }
        finally
        {
            IsErasingPixels = false;
        }
    }

    /// <summary>What erasing a pixel layer erases: the selection, half selected or more, and the strokes over the canvas; null where
    /// nothing is marked.</summary>
    private byte[]? PixelEraseMask(LayeredPicture picture)
    {
        var mask = picture.Selection.IsEmpty ? new byte[picture.Width * picture.Height] : picture.Selection.ToBytes();
        for (int i = 0; i < mask.Length; i++) mask[i] = mask[i] >= 128 ? (byte)255 : (byte)0;
        if (EraseMarks.Strokes.Count > 0) EraseMasks.Paint(mask, picture.Width, picture.Height, EraseMarks.Strokes);
        return Array.IndexOf(mask, (byte)255) >= 0 ? mask : null;
    }

    /// <summary>A pixel layer is being erased.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EraseCommand))]
    private bool _isErasingPixels;

    private bool CanErase() => ActivePixels is not null
        ? !IsErasingPixels && (_viewer.CurrentDocument!.Picture.Selection.IsEmpty == false || EraseMarks.Strokes.Any(s => s.Marks && !s.Points.IsEmpty))
        : !Cutter.IsWorking && EraseMarks.Step(Cutter.PickedCutOut, MarkedRegion) is not null;

    /// <summary>Lets everything marked go: the strokes, where the healing brush copies from, what is picked and what is drawn.</summary>
    [RelayCommand]
    private void ClearMarks()
    {
        EraseMarks.Clear();
        RegionMarks.Clear();
        Cutter.ClearCommand.Execute(null);
    }

    // ── Models the tools wait for ─────────────────────────────────────────

    /// <summary>Picture selection: what Cut out, and picking what to retouch, need.</summary>
    public ModelsGate PickingModels { get; }

    /// <summary>Picture erasing: what AI fill needs.</summary>
    public ModelsGate ErasingModels { get; }

    /// <summary>The picture tools: what AI enhance and AI upscale need.</summary>
    public ModelsGate RestoringModels { get; }

    /// <summary>The models the open panel needs are offered over the picture while they are not here.</summary>
    private void WantModels(bool editing)
    {
        PickingModels.IsWanted   = editing && Tool is EditTool.CutOut or EditTool.Retouch;
        ErasingModels.IsWanted   = editing && Tool == EditTool.Retouch;
        RestoringModels.IsWanted = editing && Tool is EditTool.Adjust or EditTool.Resize;
    }

    /// <summary>Cut out and Retouch mark and fill only with what is here: picking waits for picture selection and AI fill for
    /// picture erasing, so each gives way — to the marquee, the brush and content-aware fill — until they are.</summary>
    private void SettleRetouchChoices()
    {
        if (EraseMarks.IsPicking && !PickingModels.IsReady) EraseMarks.Way = EraseMarkWay.Brush;
        if (CutOutWay == CutOutMarkWay.Pick && !PickingModels.IsReady) CutOutWay = CutOutMarkWay.Marquee;
        if (EraseMarks.Fill == EraseFill.Ai && !ErasingModels.IsReady) EraseMarks.Fill = EraseFill.ContentAware;
    }

    internal ImageEditorViewModel(ArtistViewModel viewer, IShellServices? shell, ImageEditingServices services, PhotoRestorer restorer, BackgroundRemover remover,
                                  PictureCutter cutter, PhotoEraser eraser, ModelsGate picking, ModelsGate erasing, ModelsGate restoring)
    {
        _viewer   = viewer;
        Cutter    = cutter;
        _shell    = shell;
        _services = services;
        _restorer = restorer;
        _remover  = remover;
        _eraser   = eraser;
        PickingModels   = picking;
        ErasingModels   = erasing;
        RestoringModels = restoring;
        EraseMarks = new EraseMarking(erasing.IsReady);
        RegionMarks = new RegionMarking(cutter, () => Source);
        // A restoration, a cut-out mask or an erased photo arriving lets the photo be saved.
        restorer.Made += ToolMade;
        remover.Made  += ToolMade;
        eraser.Made   += ToolMade;
        // What is picked and drawn is what Remove background keeps and what is copied, and part of what Erase erases.
        cutter.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PictureCutter.Shown) or nameof(PictureCutter.IsWorking)) SelectionMoved();
        };
        RegionMarks.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(RegionMarking.Region) or nameof(RegionMarking.Shown)) SelectionMoved();
        };
        EraseMarks.PropertyChanged += (_, e) =>
        {
            EraseCommand.NotifyCanExecuteChanged();
            if (e.PropertyName != nameof(EraseMarking.Way)) return;
            RegionMarks.CancelDraft();
            OnPropertyChanged(nameof(DrawsMarquee));
            OnPropertyChanged(nameof(DrawsLasso));
            OnPropertyChanged(nameof(DrawsShapes));
            OnPropertyChanged(nameof(PicksByModel));
        };
        // Models arriving, or going, change what the controls that need them allow.
        picking.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ModelsGate.IsReady)) return;
            SettleRetouchChoices();
            RegionMarks.PickingChanged();
        };
        erasing.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ModelsGate.IsReady)) SettleRetouchChoices(); };
        restoring.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ModelsGate.IsReady)) return;
            OnPropertyChanged(nameof(CanToggleAiEnhance));
            OnPropertyChanged(nameof(UpscaleTwiceOffered));
            OnPropertyChanged(nameof(UpscaleFourTimesOffered));
        };
        SettleRetouchChoices();
    }

    private void ToolMade()
    {
        SaveCommand.NotifyCanExecuteChanged();
        SaveCopyCommand.NotifyCanExecuteChanged();
    }

    /// <summary>What is selected moved: what takes it may be offered or not.</summary>
    private void SelectionMoved()
    {
        RemoveBackgroundCommand.NotifyCanExecuteChanged();
        EraseCommand.NotifyCanExecuteChanged();
        CopyCutOutCommand.NotifyCanExecuteChanged();
        SaveCutOutCommand.NotifyCanExecuteChanged();
    }

    private ImageEditSession? Session => _viewer.CurrentSession;
    /// <summary>The picture the tools work on: the photo, or the active picture layer of a layered picture.</summary>
    private IRecipeEditing? Target => _viewer.EditTarget;

    /// <summary>The annotations the Markup tool works on: the photo's or a picture layer's, or a markup layer's own.</summary>
    private IMarkupEditing? Notes => _viewer.MarkupTarget;

    private ImmutableArray<MarkupElement> Markup => Notes?.Markup ?? [];
    private ImageRecipe Recipe => Target?.Recipe ?? ImageRecipe.Identity;
    private SKSize? Source => Target?.Rendering.Proxy?.SourceSize;

    // ── Panels ────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCropTool))]
    [NotifyPropertyChangedFor(nameof(IsAdjustTool))]
    [NotifyPropertyChangedFor(nameof(IsRetouchTool))]
    [NotifyPropertyChangedFor(nameof(IsCutOutTool))]
    [NotifyPropertyChangedFor(nameof(IsMarkupTool))]
    [NotifyPropertyChangedFor(nameof(IsResizeTool))]
    [NotifyPropertyChangedFor(nameof(IsPaintTool), nameof(IsMoveTool))]
    [NotifyPropertyChangedFor(nameof(Frame))]
    [NotifyPropertyChangedFor(nameof(DrawsMarquee), nameof(DrawsLasso), nameof(DrawsShapes), nameof(PicksByModel), nameof(TakesSelection))]
    private EditTool _tool = EditTool.Crop;

    public bool IsCropTool    => Tool == EditTool.Crop;
    public bool IsAdjustTool  => Tool == EditTool.Adjust;
    public bool IsRetouchTool => Tool == EditTool.Retouch;
    public bool IsCutOutTool  => Tool == EditTool.CutOut;
    public bool IsMarkupTool  => Tool == EditTool.Markup;
    public bool IsResizeTool  => Tool == EditTool.Resize;
    public bool IsPaintTool   => Tool == EditTool.Paint;
    public bool IsMoveTool    => Tool == EditTool.Move;
    /// <summary>What the canvas shows: the uncropped picture while cropping, so the frame can be dragged over all of it.</summary>
    public RenderFrame Frame => IsCropTool ? RenderFrame.CropEditing : RenderFrame.Output;

    partial void OnToolChanged(EditTool value)
    {
        CancelOpenGestures();
        RegionMarks.CancelDraft();
        IsLevelTool = false;
        if (value != EditTool.Markup) SelectedId = null;
        _cropSessionStart = value == EditTool.Crop ? Recipe.Geometry : null;
        WantModels(_viewer.IsEditing);
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
    WantModels(editing);
        if (editing)
        {
            _cropSessionStart = IsCropTool ? Recipe.Geometry : null;
        Cutter.Follow(Target?.Rendering.Path);
        RegionMarks.Follow(Target?.Rendering.Path);
        return;
        }
        Cutter.Close();   // the photo read for picking is let go with the editor
        RegionMarks.Follow(null);
        EraseMarks.Clear();
        CancelOpenGestures();
        ShowOriginal = false;
        IsLevelTool  = false;
        SelectedId   = null;
        IsSaveOptionsOpen = false;
    }

    internal void OnSessionChanged()
    {
        CancelOpenGestures();
        SelectedId = null;
        EraseMarks.Clear();   // marks are of the photo they were made on
    _cropSessionStart = IsCropTool ? Recipe.Geometry : null;
        if (_viewer.IsEditing)
        {
            Cutter.Follow(Target?.Rendering.Path);
            RegionMarks.Follow(Target?.Rendering.Path);   // marks are of the photo they were made on
        }
        OnRecipeChanged();
    }

    /// <summary>The recipe moved — by an edit, undo, redo or another window: every panel re-reads it.</summary>
    internal void OnRecipeChanged()
    {
        if (SelectedId is { } id && !Markup.Any(e => e.Id == id)) SelectedId = null;
        OnPropertyChanged(string.Empty);
        SaveCommand.NotifyCanExecuteChanged();
        SaveCopyCommand.NotifyCanExecuteChanged();
        RemoveBackgroundCommand.NotifyCanExecuteChanged();
        KeepBackgroundCommand.NotifyCanExecuteChanged();
        EraseCommand.NotifyCanExecuteChanged();
    }

    private void CancelOpenGestures()
    {
        EndTextEdit();
        if (Target?.InGesture == true || Notes?.InGesture == true) _viewer.EndEditGesture();
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
        _viewer.BeginEditGesture();
        _straightenGesture = true;
        _straightenBase = Recipe.Geometry.Crop;
    }

    public void EndGesture()
    {
        _straightenGesture = false;
        _straightenBase = null;
        _viewer.EndEditGesture();
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
        _viewer.BeginEditGesture();
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
        _viewer.EndEditGesture();
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

    /// <summary>AI enhance can be turned on — the picture tools are here — or off, which it always can be.</summary>
    public bool CanToggleAiEnhance => RestoringModels.IsReady || AiEnhance;

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

    /// <summary>Twice the size is offered where the picture tools are here and the photo restores at it; a very large one does not.</summary>
    public bool UpscaleTwiceOffered => Offered(2);

    public bool UpscaleFourTimesOffered => Offered(4);

    private bool Offered(int scale) => RestoringModels.IsReady && Source is { } s && _restorer.Restores((int)Math.Round(s.Width), (int)Math.Round(s.Height), scale);

    private void Restore(Func<RecipeRestore, RecipeRestore> change) => Commit(r => r with { Restore = change(r.Restore ?? new RecipeRestore()) });

    /// <summary>Reads the picture as cropped — its tone and colour untouched — and sets the sliders from it.</summary>
    [RelayCommand]
    private void AutoEnhance()
    {
        if (Target?.Rendering is not { } session || session.OutputSize(Recipe) is not { } output) return;
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

    public MarkupElement? Selected => SelectedId is { } id ? Markup.FirstOrDefault(e => e.Id == id) : null;
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
            uint ink = SpecColours.Argb(value);
            Replace(element switch
            {
                MarkupStroke { Style: StrokeStyle.Highlighter } s => s with { Color = HighlighterInk(ink) },
                MarkupBox { Fill: not 0 } b                       => b with { Color = ink, Fill = Wash(ink) },
                _                                                 => element with { Color = ink },
            });
        }
    }

    /// <summary>The colour chosen for markup, as a fill on a layer lays it down.</summary>
    internal SKColor InkSkColour => new(SpecColours.Argb(InkColour));

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
        if (Notes is not { } session) return;
        EndTextEdit();   // a press elsewhere finishes the text being typed
        _markupFrom = at;

        if (MarkupTool == MarkupTool.Select)
        {
            var hit = Markup.Reverse().FirstOrDefault(e => _services.Markup.HitTest(e, at, tolerance));
            SelectedId = hit?.Id;
            if (hit is null) return;
            PullStyleFrom(hit);
            session.BeginGesture();
            _moving = true;
            return;
        }

        var point = new MarkupPoint(at.X, at.Y);
        uint ink = SpecColours.Argb(InkColour);
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

        session.BeginGesture();
        session.CommitMarkup(m => m.Add(created));
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
        if (_drawing is not { } id || Markup.FirstOrDefault(e => e.Id == id) is not { } drawing) return;

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
        if (Notes is not { } session) return;
        if (_moving)
        {
            _moving = false;
            session.EndGesture();
            return;
        }
        if (_drawing is not { } id) return;
        var drawn = Markup.FirstOrDefault(e => e.Id == id);
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
            session.CancelGesture();
            SelectedId = null;
        }
        else session.EndGesture();
    }

    /// <summary>Typing into a new text annotation finished: kept as one step, or dropped if nothing was typed.</summary>
    public void EndTextEdit()
    {
        if (Notes is not { } session || _drawing is not { } id) return;
        _drawing = null;
        if (Markup.FirstOrDefault(e => e.Id == id) is MarkupText { Text.Length: 0 })
        {
            session.CancelGesture();
            SelectedId = null;
        }
        else session.EndGesture();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteSelected()
    {
        if (SelectedId is not { } id) return;
        Notes?.CommitMarkup(m => m.RemoveAll(e => e.Id == id));
        SelectedId = null;
    }

    [RelayCommand]
    private void ClearMarkup()
    {
        SelectedId = null;
        Notes?.CommitMarkup(_ => []);
    }

    /// <summary>The selected annotation's outline on the canvas (layout coordinates), for the selection to be drawn around.</summary>
    public SKPoint[]? SelectedOutline => Selected is { } element && _viewer.Canvas is { } canvas
        ? canvas.SourceToCanvas.MapPoints(_services.Markup.Outline(element))
        : null;

    private void Replace(MarkupElement element) =>
        Notes?.CommitMarkup(m => [.. m.Select(e => e.Id == element.Id ? element : e)]);

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

    private double? LongEdge => Notes?.Extent is { } s ? Math.Max(s.Width, s.Height) : null;

    private double StrokeFor(bool highlighter) => Math.Max(1, Weight * WeightUnit * (LongEdge ?? 1000) * (highlighter ? 4 : 1));

    /// <summary>Text placed on a turned or straightened picture is turned back, so it reads upright as it was typed.</summary>
    private double UprightTextRotation() => -(Recipe.Geometry.QuarterTurns * 90 + Recipe.Geometry.StraightenDegrees);

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

    /// <summary>The save options — what a save keeps of the picture's metadata, and the quality it is written at — are showing beside Save.</summary>
    [ObservableProperty] private bool _isSaveOptionsOpen;

    [RelayCommand]
    private void OpenSaveOptions() => IsSaveOptionsOpen = true;

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

    /// <summary>The picture can be saved in place (Save), not only copied: a layered picture always — where it is kept — and a
    /// photo whose format can be written and holds the transparency a removed background leaves.</summary>
    public bool CanSaveInPlace => _viewer.IsLayered
                                  || Session is { IsFileBacked: true, Proxy: not null } && Capability is { CanSaveInPlace: true } capability
                                     && (Recipe.CutOut is null || capability.KeepsTransparency);

    /// <summary>Why Save is unavailable, or what it will not keep; null when there is nothing to say.</summary>
    public string? SaveNote => _viewer.IsLayered ? null : Session switch
    {
        null or { Proxy: null }             => null,
        { IsFileBacked: false }             => Str.Get("Images.Edit.Save.NoteArchive"),
        { Proxy.IsAnimated: true }          => Str.Get("Images.Edit.Save.NoteAnimated"),
        _ when !Capability.CanSaveInPlace   => Str.Format("Images.Edit.Save.NoteFormatFormat", SourceContainer.ToString().ToUpperInvariant()),
        _ when Recipe.CutOut is not null && !Capability.KeepsTransparency
                                            => Str.Format("Images.Edit.Save.NoteNoTransparencyFormat", SourceContainer.ToString().ToUpperInvariant()),
        _ when !Capability.KeepsMetadata && !StripMetadata => Str.Format("Images.Edit.Save.NoteNoMetadataFormat", SourceContainer.ToString().ToUpperInvariant()),
        _                                   => null,
    };

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCopyCommand))]
    private bool _isSaving;

    /// <summary>The picture can be written: not saving already, and what its recipe asks of the picture tools made — a layered
    /// picture where it is kept, or somewhere asked for.</summary>
    private bool CanSave() => !IsSaving && (_viewer.CurrentDocument is { } document ? document.IsComplete
                                             : CanSaveInPlace && !Recipe.IsIdentity && !_restorer.IsPending && !_remover.IsPending && !_eraser.IsPending);
    private bool CanSaveCopy() => !IsSaving && (_viewer.CurrentDocument is { } document ? document.IsComplete
                                                : Session?.Proxy is not null && !_restorer.IsPending && !_remover.IsPending && !_eraser.IsPending);

    /// <summary>Bakes the edits into the original after a confirmation; the recipe is then spent.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        if (_viewer.IsLayered)
        {
            // A layered picture is written where it is kept — a draft asks where first — never over the photo it was made from.
            IsSaving = true;
            try { await _viewer.SaveDocumentAsAsync(askWhere: false); }
            finally { IsSaving = false; }
            return;
        }
        if (_shell is null || Session is not { } session) return;
        var name = Path.GetFileName(session.Path);
        if (!await _shell.ConfirmAsync(Str.Get("Images.Edit.Save.ConfirmTitle"), Str.Format("Images.Edit.Save.ConfirmMessageFormat", name)))
            return;

        Queue(new ImageSaveTask(_services, session.Path, session.Recipe, SourceContainer, session.Path, session.Made), success =>
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
        if (_viewer.IsLayered)
        {
            // Export: the layered picture flattened into a file of a common format.
            await _viewer.ExportDocumentAsync();
            return;
        }
        if (_shell is null || Session is not { } session) return;
        var source = SourceContainer;
        // A photo whose background is removed is offered as a PNG unless its own format holds the transparency.
        var defaultTarget = session.Recipe.CutOut is not null && !_services.Encoders.CarriesTransparency(source) ? ImageContainer.Png
                          : _services.Encoders.CanEncode(source) ? source : ImageContainer.Jpeg;
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
        Queue(new ImageSaveTask(_services, session.Path, recipe, target, destination, session.Made), success =>
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
