using System.Collections.Immutable;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Kept;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.Visuals.Common.Editing;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Layers;

/// <summary>A picture layer as it is drawn: a rendering of its own, its recipe kept in step with the layer's as the picture changes.</summary>
public sealed class LayerRendering(string path, ImageEditingServices services, Lock gate) : PictureRendering(path, services, gate)
{
    private ImageRecipe _recipe = ImageRecipe.Identity;

    public override ImageRecipe Recipe => _recipe;

    internal void Follow(ImageRecipe recipe) => _recipe = recipe;
}

/// <summary>
/// One layered picture open in a viewer: the picture and its history — budgeted in bytes of tiles — where it is kept, each
/// picture layer's rendering, and the compositor that stacks them. Every layer renders under the session's one lock, so a
/// composite holds them all at once.
/// </summary>
/// <remarks>History and the picture belong to the UI thread; rendering happens off it, one render at a time.</remarks>
public sealed class LayeredSession : IPictureLayerImages, IDisposable
{
    private readonly ImageEditingServices _services;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, LayerRendering> _renderings = [];
    private readonly LayerCompositor _compositor;
    private (double Exposure, ImmutableArray<byte> Profile)? _shownLook;
    private bool _disposed;

    /// <param name="path">Where the picture is kept: its package, or the draft it is kept in until it is saved somewhere.</param>
    /// <param name="historyBudget">What the history's kept steps may hold, in bytes of tiles no longer in the picture.</param>
    public LayeredSession(string path, LayeredPicture picture, ImageEditingServices services, long historyBudget)
    {
        Path       = path;
        _services  = services;
        _compositor = new LayerCompositor(services.Markup);
        History    = new SnapshotHistory<LayeredPicture>(picture, cost: (kept, beside) => kept.BytesNotIn(beside), budget: historyBudget);
        History.Changed += OnHistoryChanged;
        FollowLayers();
    }

    /// <summary>Where the picture is kept — its package, or a draft.</summary>
    public string Path { get; private set; }

    public SnapshotHistory<LayeredPicture> History { get; }

    public LayeredPicture Picture => History.Current;

    /// <summary>Raised on the UI thread whenever the picture changes, by an edit, an undo or an amendment.</summary>
    public event Action? PictureChanged;

    /// <summary>Every picture layer's rendering.</summary>
    public IReadOnlyCollection<LayerRendering> Renderings => _renderings.Values;

    public LayerRendering? RenderingOf(Guid id) => _renderings.GetValueOrDefault(id);

    /// <summary>The active layer, where it is a picture layer, as the editor's tools edit it; null otherwise.</summary>
    public IRecipeEditing? ActivePicture => Picture.Active is PictureLayer layer && RenderingOf(layer.Id) is { } rendering
        ? new LayerRecipeEditing(this, layer.Id, rendering) : null;

    /// <summary>The annotations the Markup tool works on: the active markup layer's own, through its placement; null where the
    /// active layer is no markup layer — <see cref="MakeNotes"/> adds one.</summary>
    public IMarkupEditing? ActiveNotes => Picture.Active is MarkupLayer layer ? new MarkupLayerEditing(this, layer.Id) : null;

    /// <summary>
    /// A markup layer added above the active layer and made active, for the Markup tool — so every annotation lives in a markup
    /// layer. Above a picture layer it is placed as that layer draws, and takes over what the layer's recipe annotates, so nothing
    /// moves; above any other it lies on the canvas as it is. One step, or part of the open gesture's.
    /// </summary>
    /// <param name="activeSource">The active picture layer's source size, where it is known before the layer's own rendering has
    /// read it — a photo just made a layered picture.</param>
    public IMarkupEditing MakeNotes(string name, SKSize? activeSource = null)
    {
        var notes = new MarkupLayer { Name = name };
        bool lifts = false;
        if (Picture.Active is PictureLayer picture && (RenderingOf(picture.Id)?.Proxy?.SourceSize ?? activeSource) is { } source)
        {
            notes = notes with { Placement = LayerPlacement.FromMatrix(SourceToCanvas(picture, source)) };
            lifts = !picture.Locked && !picture.Recipe.Markup.IsDefaultOrEmpty;
            if (lifts) notes = notes with { Elements = picture.Recipe.Markup };
        }
        var made = notes;
        Commit(p => (lifts && p.Active is PictureLayer from
                        ? p.WithLayer(from.Id, l => l is PictureLayer layer ? layer with { Recipe = layer.Recipe with { Markup = [] } } : l)
                        : p)
                    .Inserted(made, p.ActiveId));
        return new MarkupLayerEditing(this, made.Id);
    }

    /// <summary>The picture is kept somewhere else from now on — saved as another package.</summary>
    public void MovedTo(string path)
    {
        Path = path;
        IsDraft = false;
        DraftFolder = null;
    }

    /// <summary>Makes what <paramref name="change"/> makes of the picture current — one undo step, or part of the open gesture's.</summary>
    public void Commit(Func<LayeredPicture, LayeredPicture> change) => History.Commit(change(Picture));

    /// <summary>Makes what <paramref name="change"/> makes of the picture current without an undo step: which layer is active.</summary>
    public void Amend(Func<LayeredPicture, LayeredPicture> change) => History.Amend(change(Picture));

    // ── Keeping it ──────────────────────────────────────────────────────────

    /// <summary>Where Save as starts, while the picture is a draft — beside the photo it was made from; null once it is kept somewhere.</summary>
    public string? DraftFolder { get; private set; }

    /// <summary>The picture is a draft, kept in the temp folder until the first Save asks where.</summary>
    public bool IsDraft { get; private set; }

    /// <summary>The picture is kept in a draft for now, its Save as starting in <paramref name="folder"/>.</summary>
    public void KeptAsDraft(string? folder)
    {
        IsDraft = true;
        DraftFolder = folder;
    }

    /// <summary>The picture kept as a draft at <paramref name="draft"/> from now on — the file it was kept in can no longer hold
    /// it — its Save as offered back in the folder that file is in.</summary>
    public void MovedToDraft(string draft)
    {
        if (!IsDraft) DraftFolder = System.IO.Path.GetDirectoryName(Path);
        Path = draft;
        IsDraft = true;
    }

    /// <summary>
    /// What writing the picture as it stands to <paramref name="destination"/> does — its layers drawn at full size, the package
    /// written whole — as work to run off the UI thread; null while a picture layer waits for what the picture tools make for it.
    /// </summary>
    public Func<CancellationToken, Task>? Writing(string destination)
    {
        var picture = Picture;
        if (_services.LayeredFormatFor(destination) is not { } format) return null;
        var made = MadeFor(picture);
        if (made is null) return null;
        return async ct =>
        {
            using var renderer = await LayeredRenderer.PrepareAsync(picture, l => made[l.Id], _services, ct);
            await format.WriteAsync(picture, destination, renderer, ct);
        };
    }

    /// <summary>
    /// What writing the picture flattened to <paramref name="destination"/> as <paramref name="target"/> does — composited at
    /// full size, encoded with the metadata of the bottom picture layer's photo — as work to run off the UI thread; null while a
    /// picture layer waits for what the picture tools make for it.
    /// </summary>
    public Func<CancellationToken, Task>? Exporting(string destination, ImageContainer target, RecipeExport export)
    {
        var picture = Picture;
        var made = MadeFor(picture);
        if (made is null) return null;
        var metadataFrom = picture.All.OfType<PictureLayer>().FirstOrDefault()?.Source.Path;
        return async ct =>
        {
            using var renderer = await LayeredRenderer.PrepareAsync(picture, l => made[l.Id], _services, ct);
            using var composite = renderer.Composite(1);
            // As deep as the picture is held, so a 16-bit PNG or TIFF keeps it; an 8-bit grey picture over a background as grey.
            using var grey = picture.Colour is { Model: ColourModel.Grey, Depth: ChannelDepth.U8 } && !picture.Background.IsTransparent ? AsGrey(composite) : null;
            var bytes = await _services.Exporter.EncodeAsync(grey ?? composite, target, export, metadataFrom, ct);
            await _services.FileSystem.ReplaceAsync(destination, bytes, ct);
        };
    }

    /// <summary>An 8-bit picture of greys as one grey channel, what a grey PNG or JPEG holds.</summary>
    private static SKImage AsGrey(SKImage image)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Gray8, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(info);
        if (!image.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0)) throw new InvalidOperationException("The picture could not be made grey.");
        return SKImage.FromPixelCopy(info, bitmap.GetPixels(), bitmap.RowBytes) ?? throw new InvalidOperationException("The picture could not be made grey.");
    }

    /// <summary>What the picture tools made for each picture layer's recipe; null while any is still waiting.</summary>
    private Dictionary<Guid, MadeByTools>? MadeFor(LayeredPicture picture)
    {
        var made = new Dictionary<Guid, MadeByTools>();
        foreach (var layer in picture.All.OfType<PictureLayer>())
        {
            var byTools = RenderingOf(layer.Id)?.Made ?? MadeByTools.None;
            if (!byTools.Serves(layer.Recipe)) return null;
            made[layer.Id] = byTools;
        }
        return made;
    }

    /// <summary>Whether every picture layer has what its recipe asks of the picture tools — what a save needs.</summary>
    public bool IsComplete => MadeFor(Picture) is not null;

    /// <summary>
    /// A full-size renderer of <paramref name="picture"/> — this picture, or part of it, its picture layers drawn from what the
    /// tools made for this session's layers — for an edit that turns layers into pixels: rasterize, merge down, flatten; null
    /// while a picture layer it holds waits for the tools. The caller owns it.
    /// </summary>
    public async Task<LayeredRenderer?> RendererForAsync(LayeredPicture picture, CancellationToken ct = default)
        => MadeFor(picture) is { } made ? await LayeredRenderer.PrepareAsync(picture, l => made[l.Id], _services, ct) : null;

    /// <summary>A small picture of one layer by itself — opaque, unblended, unmasked — at most <paramref name="side"/> pixels a
    /// side, for the layers panel; the caller owns it, null once the session has closed.</summary>
    public SKImage? ThumbnailOf(Layer layer, int side)
    {
        var picture = Picture;
        double scale = Math.Min(1, (double)side / Math.Max(picture.Width, picture.Height));
        var alone = layer with { Visible = true, Opacity = 1, Blend = LayerBlend.Normal, ClipsToBelow = false, Mask = null };
        return Snapshot(picture with { Layers = [alone] }, scale, new CompositeOptions(LeaveOutBackground: true));
    }

    // ── The saved look ──────────────────────────────────────────────────────

    /// <summary>
    /// The picture as the application that wrote it drew it — its package's merged image — shown in place of what Nexaflow
    /// draws, while it holds a layer no reader can draw and no way on has been chosen; null otherwise.
    /// </summary>
    public LayerRendering? SavedLook { get; private set; }

    /// <summary>Opened on its saved look: the package's merged image at <paramref name="mergedImage"/> shown until a way on is chosen.</summary>
    public void ShowSavedLook(string mergedImage)
    {
        SavedLook?.Dispose();
        SavedLook = new LayerRendering(mergedImage, _services, _gate);
    }

    /// <summary>Edit the layers: what Nexaflow draws shows from now on, the layers it cannot draw left out and kept.</summary>
    public void EditTheLayers()
    {
        if (SavedLook is null) return;
        lock (_gate)
        {
            SavedLook.Dispose();
            SavedLook = null;
        }
        PictureChanged?.Invoke();
    }

    /// <summary>
    /// Use the saved look: the stack replaced by one picture layer showing the merged image — kept as <paramref name="copy"/>,
    /// a file of its own, since the package's merged image is rewritten by every save — the look baked in; one history step.
    /// </summary>
    public void UseSavedLook(string copy, string layerName)
    {
        if (SavedLook is null) return;
        var layer = new PictureLayer { Name = layerName, Source = new PictureSource(copy, null) };
        lock (_gate)
        {
            SavedLook.Dispose();
            SavedLook = null;
        }
        Commit(p => p with { Layers = [layer], ActiveId = layer.Id, EditsMask = false });
    }

    private void OnHistoryChanged()
    {
        // Editing anything, before a way on from the saved look is chosen, chooses to edit the layers.
        if (SavedLook is not null && History.CanUndo)
        {
            lock (_gate)
            {
                SavedLook.Dispose();
                SavedLook = null;
            }
        }
        FollowLayers();
        PictureChanged?.Invoke();
    }

    /// <summary>Each picture layer has its rendering, following its recipe; a layer gone lets its rendering go.</summary>
    private void FollowLayers()
    {
        var seen = new HashSet<Guid>();
        foreach (var layer in Picture.All.OfType<PictureLayer>())
        {
            seen.Add(layer.Id);
            if (_renderings.TryGetValue(layer.Id, out var rendering) && !string.Equals(rendering.Path, layer.Source.Path, StringComparison.OrdinalIgnoreCase))
            {
                rendering.Dispose();
                _renderings.Remove(layer.Id);
                rendering = null;
            }
            if (rendering is null) _renderings[layer.Id] = rendering = new LayerRendering(layer.Source.Path, _services, _gate);
            rendering.Follow(layer.Recipe);
        }
        foreach (var gone in _renderings.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _renderings[gone].Dispose();
            _renderings.Remove(gone);
        }
    }

    // ── Rendering ───────────────────────────────────────────────────────────

    /// <summary>Stops of exposure a 32-bit picture is shown at — a view of its light, never written; 0 shows it as it is.</summary>
    public double ViewExposure { get; set; }

    /// <summary>
    /// Composites <paramref name="picture"/> — the picture as it stood when the frame was asked for — at <paramref name="scale"/>
    /// and hands it to <paramref name="consume"/> while it is still valid. False once the session has closed.
    /// </summary>
    public bool Render(LayeredPicture picture, double scale, CompositeOptions options, Action<SKImage, FrameChange> consume)
    {
        lock (_gate)
        {
            if (_disposed) return false;
            var composite = _compositor.Render(picture, scale, this, options);
            // The exposure and the profile it is shown through reach every pixel: either changed, the whole frame is new,
            // whatever the layers did.
            var look = (ViewExposure, picture.Profile);
            var change = look == _shownLook ? _compositor.Change : _compositor.Change.AllOfIt();
            _shownLook = look;
            PictureDisplay.Show(composite, picture, ViewExposure, shown => consume(shown, change));
            return true;
        }
    }

    /// <summary>What shows at <paramref name="at"/> (canvas pixels) in the picture last rendered, every layer together, in the
    /// picture's values; null where nothing is rendered there.</summary>
    public SKColorF? ShownAt(SKPoint at)
    {
        lock (_gate) return _disposed ? null : _compositor.ShownAt(at);
    }

    /// <summary>A one-off composite at <paramref name="scale"/> — a save, an export, a thumbnail — that leaves the display's kept
    /// one alone. The caller owns it; null once the session has closed.</summary>
    public SKImage? Snapshot(LayeredPicture picture, double scale, CompositeOptions options = default)
    {
        lock (_gate)
        {
            if (_disposed) return null;
            using var once = new LayerCompositor(_services.Markup, cache: false);
            return once.Render(picture, scale, this, options);
        }
    }

    PictureLayerImage? IPictureLayerImages.ImageOf(PictureLayer layer, double scale)
    {
        if (RenderingOf(layer.Id) is not { Proxy: { } proxy } rendering) return null;
        double renderScale = Math.Max(1e-3, scale * layer.Placement.Scale);
        if (rendering.RenderHeld(layer.Recipe, new RenderRequest(RenderFrame.Output, renderScale)) is not { } image) return null;
        var toCanvas = PixelsToCanvas(layer, proxy.SourceSize, renderScale);
        return new PictureLayerImage(image, toCanvas, rendering.Version);
    }

    /// <summary>The pixels of a picture layer's finished picture rendered at <paramref name="renderScale"/> to canvas pixels.</summary>
    public static SKMatrix PixelsToCanvas(PictureLayer layer, SKSize source, double renderScale)
    {
        var rendered = CropGeometry.SourceToCanvas(layer.Recipe, source, RenderFrame.Output, renderScale, out _);
        var natural = CropGeometry.SourceToCanvas(layer.Recipe, source, RenderFrame.Output, 1, out _);
        if (!rendered.TryInvert(out var toSource)) return layer.Placement.ToMatrix();
        return toSource.PostConcat(natural).PostConcat(layer.Placement.ToMatrix());
    }

    /// <summary>A picture layer's source pixels to canvas pixels: where its recipe's coordinates lie on the canvas.</summary>
    public static SKMatrix SourceToCanvas(PictureLayer layer, SKSize source)
        => CropGeometry.SourceToCanvas(layer.Recipe, source, RenderFrame.Output, 1, out _).PostConcat(layer.Placement.ToMatrix());

    public void Dispose()
    {
        History.Changed -= OnHistoryChanged;
        lock (_gate)
        {
            _disposed = true;
            _compositor.Dispose();
            foreach (var rendering in _renderings.Values) rendering.Dispose();
            SavedLook?.Dispose();
            SavedLook = null;
            _renderings.Clear();
        }
    }

    /// <summary>A picture layer as the editor's tools edit it: its recipe changed in the layer, as a step of the picture's history.</summary>
    private sealed class LayerRecipeEditing(LayeredSession session, Guid id, LayerRendering rendering) : IRecipeEditing
    {
        public PictureRendering Rendering => rendering;

        public ImageRecipe Recipe => (session.Picture.Find(id) as PictureLayer)?.Recipe ?? ImageRecipe.Identity;

        public void Commit(ImageRecipe next)
            => session.Commit(p => p.WithLayer(id, l => l is PictureLayer picture && !picture.Locked ? picture with { Recipe = next } : l));

        public void BeginGesture() => session.History.BeginGesture();

        public void EndGesture() => session.History.EndGesture();

        public void CancelGesture() => session.History.CancelGesture();

        public bool InGesture => session.History.InGesture;

        public override bool Equals(object? obj) => obj is LayerRecipeEditing other && ReferenceEquals(other.Rendering, rendering);

        public override int GetHashCode() => rendering.GetHashCode();
    }

    /// <summary>A markup layer's annotations as the Markup tool edits them: each change a step of the picture's history, none on a
    /// locked layer; sized against the canvas.</summary>
    private sealed class MarkupLayerEditing(LayeredSession session, Guid id) : IMarkupEditing
    {
        public ImmutableArray<MarkupElement> Markup => (session.Picture.Find(id) as MarkupLayer)?.Elements ?? [];

        public void CommitMarkup(Func<ImmutableArray<MarkupElement>, ImmutableArray<MarkupElement>> change)
            => session.Commit(p => p.WithLayer(id, l => l is MarkupLayer markup && !markup.Locked ? markup with { Elements = change(markup.Elements) } : l));

        public SKSize? Extent => new SKSize(session.Picture.Width, session.Picture.Height);

        public void BeginGesture() => session.History.BeginGesture();

        public void EndGesture() => session.History.EndGesture();

        public void CancelGesture() => session.History.CancelGesture();

        public bool InGesture => session.History.InGesture;

        public override bool Equals(object? obj) => obj is MarkupLayerEditing other && ReferenceEquals(other.Session, session) && other.Id == id;

        public override int GetHashCode() => HashCode.Combine(session, id);

        private LayeredSession Session => session;

        private Guid Id => id;
    }
}
