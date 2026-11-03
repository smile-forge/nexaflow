using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.SceneDepth;
using Nexaflow.Visuals.Common.Localization;
using SkiaSharp;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// What the Adjust panel draws from the picture's scene depth, on the photo or the active picture layer: a lens's blur — focused
/// where the picture is clicked, or on the thing clicked, as wide open and as deep as asked, in the aperture's shape — haze
/// that thickens with distance, and the depth itself as a layer or a mask for the user's own compositing. Lens blur and haze
/// are recipe sections, so they stay editable and undo as any edit; the depth is estimated once and kept, so a drag only
/// redraws the picture.
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
    }

    private void Commit(Func<ImageRecipe, ImageRecipe> change) => _viewer.Edit(change);

    // ── Lens blur ──────────────────────────────────────────────────────────

    /// <summary>The picture is blurred as a lens would — focused, to start with, on its middle.</summary>
    public bool LensBlur
    {
        get => Recipe.LensBlur is not null;
        set
        {
            if (value == LensBlur) return;
            if (!value) Commit(r => r with { LensBlur = null });
            else if (Photo is { } photo) Commit(r => r with { LensBlur = new RecipeLensBlur { FocusX = photo.Width / 2.0, FocusY = photo.Height / 2.0 } });
        }
    }

    /// <summary>How wide open the lens is, 0–100: how blurred what is far from the focus becomes.</summary>
    public double Aperture
    {
        get => (Recipe.LensBlur?.Aperture ?? new RecipeLensBlur().Aperture) * 100;
        set => Blur(b => b with { Aperture = Math.Clamp(Math.Round(value) / 100, 0, 1) });
    }

    /// <summary>How much depth either side of the focus stays sharp, 0–100 of the most it can.</summary>
    public double FocusRange
    {
        get => (Recipe.LensBlur?.Range ?? new RecipeLensBlur().Range) / RecipeLensBlur.WidestRange * 100;
        set => Blur(b => b with { Range = Math.Clamp(Math.Round(value) / 100 * RecipeLensBlur.WidestRange, 0, RecipeLensBlur.WidestRange) });
    }

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

    /// <summary>A click focuses on the whole thing it lands on — all of it kept sharp — rather than on the point; needs picture
    /// selection, to find the thing.</summary>
    [ObservableProperty] private bool _focusOnSubject;

    /// <summary>Clicks on the picture set the focus: the lens blur is on, in the Adjust panel.</summary>
    public bool ChoosesFocus => LensBlur && _viewer.Editor.IsAdjustTool;

    /// <summary>Picking out the thing a click focuses on is under way.</summary>
    [ObservableProperty] private bool _isFocusing;

    /// <summary>
    /// The lens focused by a click at <paramref name="at"/>, in the photo's pixels: on the point — or, focusing on the subject, on
    /// the thing there, at its middle distance and with all of it kept sharp. One step of the recipe.
    /// </summary>
    public async Task FocusAtAsync(MarkupPoint at)
    {
        if (Recipe.LensBlur is null || Photo is not { } photo || at.X < 0 || at.Y < 0 || at.X >= photo.Width || at.Y >= photo.Height) return;
        _focusing?.Cancel();
        if (!FocusOnSubject || Target?.Rendering.SceneDepth is not { } plane)
        {
            Blur(b => b with { FocusX = at.X, FocusY = at.Y });
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
            else Blur(b => b with { FocusX = at.X, FocusY = at.Y });
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

    /// <summary>Where the lens is focused, in the photo's pixels — what the canvas marks; null without a lens blur.</summary>
    public MarkupPoint? Focus => Recipe.LensBlur is { } blur ? new MarkupPoint(blur.FocusX, blur.FocusY) : null;

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
        set => Hazed(h => h with { Colour = value ? SpecColours.Argb(_viewer.Editor.InkColour) : null });
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
