using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.SceneDepth;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.Visuals.Common.Localization;
using SkiaSharp;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>What a press on the picture focuses a lens blur on.</summary>
public enum LensFocusWay
{
    /// <summary>The distance clicked, the band kept as wide as it is.</summary>
    Point,

    /// <summary>The whole thing clicked, as picture selection finds it.</summary>
    Thing,

    /// <summary>All of a rectangle dragged.</summary>
    Rectangle,

    /// <summary>All of an outline traced.</summary>
    Lasso,
}

/// <summary>
/// What the Adjust panel draws from the picture's scene depth, on the photo or the active picture layer: a lens's blur — sharp
/// over a band of distance, which a click sets on a point or on the thing there, a rectangle or an outline drawn, or the
/// selection, and which can be dragged on the strip of the picture's depths — haze that thickens with distance, and the depth
/// itself as a layer or a mask for the user's own compositing. Lens blur and haze are recipe sections, so they stay editable
/// and undo as any edit; the depth is estimated once and kept, so a drag only redraws the picture.
/// </summary>
public sealed partial class SceneDepthTools : ObservableObject
{
    private readonly ArtistViewModel _viewer;
    private readonly ImageEditingServices _services;
    private readonly PictureCutter _cutter;
    private readonly IShellServices? _shell;
    private CancellationTokenSource? _focusing;

    /// <param name="models">Scene depth's models: what every control here waits for.</param>
    /// <param name="cutter">The cut-out tool, whose picture selection finds the thing a click focuses on.</param>
    public SceneDepthTools(ArtistViewModel viewer, ImageEditingServices services, ModelsGate models, PictureCutter cutter, IShellServices? shell)
    {
        _viewer = viewer;
        _services = services;
        _cutter = cutter;
        _shell = shell;
        Models = models;
        models.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ModelsGate.IsReady)) return;
            OnPropertyChanged(nameof(CanEdit));
            DepthAsLayerCommand.NotifyCanExecuteChanged();
            DepthAsMaskCommand.NotifyCanExecuteChanged();
        };
        viewer.Editor.RegionMarks.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RegionMarking.HasSelection)) FocusOnSelectionCommand.NotifyCanExecuteChanged();
        };
    }

    /// <summary>Scene depth: what lens blur, haze, and the depth as a layer or a mask need.</summary>
    public ModelsGate Models { get; }

    private IRecipeEditing? Target => _viewer.EditTarget;

    private ImageRecipe Recipe => Target?.Recipe ?? ImageRecipe.Identity;

    private SKSizeI? Photo => Target?.Rendering.Proxy?.SourceSize is { } s ? new SKSizeI((int)Math.Round(s.Width), (int)Math.Round(s.Height)) : null;

    /// <summary>The controls work: scene depth is here, and there is a photo or picture layer to work on — or the recipe already
    /// draws from its depth, so it can be changed or taken off while the models are away.</summary>
    public bool CanEdit => Target is not null && (Models.IsReady || Recipe.AsksSceneDepth);

    /// <summary>A layered picture whose active layer is no picture layer: the controls wait for one, and say so.</summary>
    public bool NeedsPictureLayer => _viewer.IsLayered && Target is null;

    /// <summary>The recipe moved: every control re-reads it.</summary>
    internal void OnRecipeChanged()
    {
        OnPropertyChanged(string.Empty);
        DepthAsLayerCommand.NotifyCanExecuteChanged();
        DepthAsMaskCommand.NotifyCanExecuteChanged();
        FocusOnSelectionCommand.NotifyCanExecuteChanged();
    }

    private void Commit(Func<ImageRecipe, ImageRecipe> change) => _viewer.Edit(change);

    // ── Lens blur ──────────────────────────────────────────────────────────

    /// <summary>The picture is blurred as a lens would — sharp, to start with, over the nearest tenth of its distance.</summary>
    public bool LensBlur
    {
        get => Recipe.LensBlur is not null;
        set
        {
            if (value == LensBlur || Target is null) return;
            Commit(r => r with { LensBlur = value ? new RecipeLensBlur() : null });
        }
    }

    /// <summary>How wide open the lens is, 0–100: how blurred what is far from the focus becomes.</summary>
    public double Aperture
    {
        get => (Recipe.LensBlur?.Aperture ?? new RecipeLensBlur().Aperture) * 100;
        set => Blur(b => b with { Aperture = Math.Clamp(Math.Round(value) / 100, 0, 1) });
    }

    /// <summary>Where the band kept sharp begins, 0–100 of the picture's distance from its nearest part.</summary>
    public double FocusNear
    {
        get => (Recipe.LensBlur?.Near ?? new RecipeLensBlur().Near) * 100;
        set => Blur(b => b.Banded(Math.Round(value) / 100, b.Far));
    }

    /// <summary>Where the band kept sharp ends, 0–100 of the picture's distance from its nearest part.</summary>
    public double FocusFar
    {
        get => (Recipe.LensBlur?.Far ?? new RecipeLensBlur().Far) * 100;
        set => Blur(b => b.Banded(b.Near, Math.Round(value) / 100));
    }

    private (SceneDepthPlane Plane, double[] Shares)? _shares;

    /// <summary>
    /// How much of the picture lies at each step of distance (<see cref="SceneDepthPlane.Shares"/>), nearest first, the fullest 1 —
    /// what the strip under the band shows, so the band can be laid over what is there; null until the depth is here.
    /// </summary>
    public IReadOnlyList<double>? DistanceShares
    {
        get
        {
            if (Plane is not { } plane) return null;
            if (_shares is { } kept && ReferenceEquals(kept.Plane, plane)) return kept.Shares;
            var shares = plane.Shares();
            _shares = (plane, shares);
            return shares;
        }
    }

    /// <summary>The depth the picture is drawn from now; null until it is estimated or read.</summary>
    internal SceneDepthPlane? Plane => Target?.Rendering.SceneDepth;

    /// <summary>What lies in the band kept sharp is shown over the picture: while the band is pointed at or dragged, or the picture
    /// pressed to focus.</summary>
    [ObservableProperty] private bool _showsInFocus;

    /// <summary>Out-of-focus highlights spread into discs.</summary>
    public bool RoundBokeh
    {
        get => (Recipe.LensBlur?.Shape ?? BokehShape.Circle) == BokehShape.Circle;
        set { if (value) Blur(b => b with { Shape = BokehShape.Circle }); }
    }

    /// <summary>Out-of-focus highlights spread into hexagons, as through a six-bladed aperture.</summary>
    public bool HexagonBokeh
    {
        get => Recipe.LensBlur?.Shape == BokehShape.Hexagon;
        set { if (value) Blur(b => b with { Shape = BokehShape.Hexagon }); }
    }

    private void Blur(Func<RecipeLensBlur, RecipeLensBlur> change)
    {
        if (Recipe.LensBlur is not null) Commit(r => r with { LensBlur = change(r.LensBlur!) });
    }

    /// <summary>What a press on the picture focuses the lens on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DrawsFocus))]
    private LensFocusWay _focusWay;

    /// <summary>Presses on the picture set the focus: the lens blur is on, in the Adjust panel.</summary>
    public bool ChoosesFocus => LensBlur && _viewer.Editor.IsAdjustTool;

    /// <summary>The focus is drawn on the picture — a rectangle dragged or an outline traced — rather than clicked.</summary>
    public bool DrawsFocus => ChoosesFocus && FocusWay is LensFocusWay.Rectangle or LensFocusWay.Lasso;

    /// <summary>Picking out the thing a click focuses on is under way.</summary>
    [ObservableProperty] private bool _isFocusing;

    /// <summary>
    /// The lens focused by a click at <paramref name="at"/>, in the photo's pixels: the band kept sharp moved, as wide as it is,
    /// onto the point's distance — or, focusing on things, laid over all of the thing there. One step of the recipe.
    /// </summary>
    public async Task FocusAtAsync(MarkupPoint at)
    {
        if (Recipe.LensBlur is null || Photo is not { } photo || Plane is not { } plane) return;
        if (at.X < 0 || at.Y < 0 || at.X >= photo.Width || at.Y >= photo.Height) return;
        _focusing?.Cancel();
        double distance = plane.Distance(plane.FocusAt(at.X, at.Y, photo));
        if (FocusWay != LensFocusWay.Thing)
        {
            Blur(b => CentredOn(b, distance));
            return;
        }
        var focusing = new CancellationTokenSource();
        _focusing = focusing;
        IsFocusing = true;
        try
        {
    var thing = await _cutter.ThingAtAsync(at.X, at.Y, focusing.Token);
    
            if (focusing.IsCancellationRequested) return;
            if (thing is not null && SceneDepthFocus.Of(thing, plane, photo) is { } subject) Blur(b => SceneDepthFocus.FocusedOn(b, subject));
            else Blur(b => CentredOn(b, distance));
        }
        catch (OperationCanceledException) when (focusing.IsCancellationRequested)
        {
            // a later click took its place
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            _shell?.ShowError(Str.Format("Images.SceneDepth.FocusFailed", ex.Message));
        }
        finally
        {
            if (ReferenceEquals(_focusing, focusing)) IsFocusing = false;
        }
    }

    /// <summary><paramref name="blur"/>'s band, as wide as it is, centred on <paramref name="distance"/> — slid in from either end.</summary>
    private static RecipeLensBlur CentredOn(RecipeLensBlur blur, double distance)
    {
        double width = blur.Far - blur.Near, from = Math.Clamp(distance - width / 2, 0, 1 - width);
        return blur.Banded(from, from + width);
    }

    /// <summary>The longest side a drawn shape is laid out at to find its distances: plenty to sample, little to lay out.</summary>
    private const int LongestDrawnSide = 1024;

    /// <summary>The lens focused on all of <paramref name="shape"/> — a rectangle or an outline drawn, in the photo's pixels. One
    /// step of the recipe.</summary>
    public Task FocusOnAsync(RegionShape shape) => FocusOnPartAsync(photo =>
    {
        double scale = Math.Min(1, (double)LongestDrawnSide / Math.Max(photo.Width, photo.Height));
        var coverage = RegionRaster.Shape(shape, Math.Max(1, (int)Math.Ceiling(photo.Width * scale)), Math.Max(1, (int)Math.Ceiling(photo.Height * scale)), scale);
        return (coverage, SKMatrix.CreateScale((float)(1 / scale), (float)(1 / scale)));
    });

    /// <summary>The lens focused on all of what is selected — however it was selected — where it lies on the photo.</summary>
    [RelayCommand(CanExecute = nameof(CanFocusOnSelection))]
    private Task FocusOnSelection()
    {
        var marks = _viewer.Editor.RegionMarks;
        if (marks.Shown is not { IsEmpty: false } shown || _viewer.Canvas is not { } canvas || !canvas.SourceToCanvas.TryInvert(out var canvasToPhoto))
            return Task.CompletedTask;
        var toPhoto = SKMatrix.CreateScale((float)(1 / marks.ShownScale), (float)(1 / marks.ShownScale))
            .PostConcat(canvas.DocumentToCanvas).PostConcat(canvasToPhoto);
        return FocusOnPartAsync(_ => (shown.Coverage, toPhoto));
    }

    private bool CanFocusOnSelection() => LensBlur && _viewer.Editor.RegionMarks.HasSelection;

    /// <summary>
    /// The lens focused on a part of the photo: <paramref name="part"/> — given the photo's size, what covers the part and what
    /// carries it onto the photo — worked out off the UI thread, and the band laid over its distances. A later focus takes its place.
    /// </summary>
    private async Task FocusOnPartAsync(Func<SKSizeI, (TiledCoverage Coverage, SKMatrix ToPhoto)> part)
    {
        if (Recipe.LensBlur is null || Photo is not { } photo || Plane is not { } plane) return;
        _focusing?.Cancel();
        var focusing = new CancellationTokenSource();
        _focusing = focusing;
        IsFocusing = true;
        try
        {
            var subject = await Task.Run(() =>
            {
                var (coverage, toPhoto) = part(photo);
                return SceneDepthFocus.Of(coverage, toPhoto, plane, photo);
            });

            if (!focusing.IsCancellationRequested && subject is not null) Blur(b => SceneDepthFocus.FocusedOn(b, subject));
        }
        catch (InvalidOperationException ex)
        {
            _shell?.ShowError(Str.Format("Images.SceneDepth.FocusFailed", ex.Message));
        }
        finally
        {
            if (ReferenceEquals(_focusing, focusing)) IsFocusing = false;
        }
    }

    // ── Haze ───────────────────────────────────────────────────────────────

    /// <summary>Haze thickens over the picture with distance.</summary>
    public bool Haze
    {
        get => Recipe.DepthHaze is not null;
        set
        {
            if (value == Haze) return;
            Commit(r => r with { DepthHaze = value ? new RecipeDepthHaze() : null });
        }
    }

    /// <summary>How thick the haze is, 0–100.</summary>
    public double HazeDensity
    {
        get => (Recipe.DepthHaze?.Density ?? new RecipeDepthHaze().Density) * 100;
        set => Hazed(h => h with { Density = Math.Clamp(Math.Round(value) / 100, 0, 1) });
    }

    /// <summary>How far off the haze starts, 0–100 from the nearest part to the farthest.</summary>
    public double HazeStart
    {
        get => (Recipe.DepthHaze?.Start ?? new RecipeDepthHaze().Start) * 100;
        set => Hazed(h => h with { Start = Math.Clamp(Math.Round(value) / 100, 0, 1) });
    }

    /// <summary>The haze takes the ink chosen in Markup, rather than the colour of the picture's own distance.</summary>
    public bool HazeTakesInk
    {
        get => Recipe.DepthHaze?.Colour is not null;
        set
        {
            if (value != HazeTakesInk) Hazed(h => h with { Colour = value ? SpecColours.Argb(_viewer.Editor.InkColour) : null });
        }
    }

    /// <summary>The haze takes the colour of the picture's own distance, read from its farthest parts.</summary>
    public bool HazeTakesScene
    {
        get => !HazeTakesInk;
        set { if (value) HazeTakesInk = false; }
    }

    private void Hazed(Func<RecipeDepthHaze, RecipeDepthHaze> change)
    {
        if (Recipe.DepthHaze is not null) Commit(r => r with { DepthHaze = change(r.DepthHaze!) });
    }

    // ── The depth itself ───────────────────────────────────────────────────

    /// <summary>
    /// The picture's depth as a layer of greys above the photo — white the nearest, black the farthest — for the user's own
    /// compositing: a photo made a layered picture first. The depth is estimated here where it is not kept yet.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanTakeDepth))]
    private async Task DepthAsLayer()
    {
        if (await DepthOfTargetAsync() is not var (plane, photo) || _viewer.MakeLayered() is not { } document) return;
        if (document.Picture.Active is not PictureLayer layer) return;
        var picture = document.Picture;
        var pixels = await Task.Run(() => SceneDepthLayers.Pixels(plane, photo, LayeredSession.SourceToCanvas(layer, new SKSize(photo.Width, photo.Height)),
                                                                  picture.Width, picture.Height, picture.Colour));
        var depthLayer = new PixelLayer { Name = Str.Format("Images.SceneDepth.LayerNameFormat", layer.Name), Pixels = pixels };
        document.Commit(p => p.Inserted(depthLayer, layer.Id) with { ActiveId = depthLayer.Id, EditsMask = false });
    }

    /// <summary>The picture's depth as the active picture layer's mask: shown where it is near, hidden where it is far.</summary>
    [RelayCommand(CanExecute = nameof(CanMaskByDepth))]
    private async Task DepthAsMask()
    {
        if (_viewer.CurrentDocument is not { } document || document.Picture.Active is not PictureLayer { Mask: null } layer) return;
        if (await DepthOfTargetAsync() is not var (plane, photo)) return;
        var picture = document.Picture;
        var mask = await Task.Run(() => SceneDepthLayers.Mask(plane, photo, LayeredSession.SourceToCanvas(layer, new SKSize(photo.Width, photo.Height)),
                                                              picture.Width, picture.Height));
        document.Commit(p => p.WithLayer(layer.Id, l => l with { Mask = new LayerMask(mask) }) with { EditsMask = true });
    }

    private bool CanTakeDepth() => Models.IsReady && Target is not null && !IsTakingDepth;

    private bool CanMaskByDepth() => CanTakeDepth() && _viewer.CurrentDocument?.Picture.Active is PictureLayer { Mask: null, Locked: false };

    /// <summary>The depth is being estimated or read for a layer or a mask.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DepthAsLayerCommand), nameof(DepthAsMaskCommand))]
    private bool _isTakingDepth;

    /// <summary>The depth of the photo the tools work on, at its size: the one it is drawn from, else the kept one, else estimated
    /// now. Null — said why — where it cannot be had.</summary>
    internal async Task<(SceneDepthPlane Plane, SKSizeI Photo)?> DepthOfTargetAsync()
    {
        if (Target?.Rendering is not { } rendering || Photo is not { } photo) return null;
        if (rendering.SceneDepth is { } drawn) return (drawn, photo);
        if (_services.SceneDepth is not { State: ToolModelsState.Ready } depth) return null;
        IsTakingDepth = true;
        try
        {
            var from = rendering.Recipe.Erasures.IsEmpty ? rendering.Path : rendering.ErasedPath ?? rendering.Path;
            return (await _services.SceneDepths.PlaneAsync(_services, from, depth, CancellationToken.None), photo);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or InvalidDataException)
        {
            _shell?.ShowError(Str.Format("Images.SceneDepth.Failed", ex.Message));
            return null;
        }
        finally
        {
            IsTakingDepth = false;
        }
    }
}
