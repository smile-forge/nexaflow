using System.Collections.Immutable;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.IO.Common.OpenRaster;

namespace Nexaflow.Features.Images.Editing.Layers;

/// <summary>
/// A picture built of layers: the canvas's size and resolution, what shows beneath every layer, the layers bottom first, the
/// active one, the selection, and what the file it was read from held that Nexaflow does not understand. Immutable: every
/// edit makes a new one sharing everything it did not touch — down to the tiles of pixels, masks and the selection — so a
/// history of them costs what each step changed.
/// </summary>
public sealed record LayeredPicture
{
    /// <summary>The longest a canvas may be on either side.</summary>
    public const int MaxSide = 16384;

    /// <summary>The resolution OpenRaster takes when a file names none.</summary>
    public const double DefaultDpi = 72;

    public required int Width { get; init; }

    public required int Height { get; init; }

    public double Dpi { get; init; } = DefaultDpi;

    public LayerBackground Background { get; init; } = LayerBackground.Transparent;

    /// <remarks>Never default: a canvas with no layers holds none.</remarks>
    public ImmutableArray<Layer> Layers
    {
        get => _layers.IsDefault ? [] : _layers;
        init => _layers = value;
    }

    private readonly ImmutableArray<Layer> _layers = [];

    /// <summary>The layer the tools work on; null where there is none.</summary>
    public Guid? ActiveId { get; init; }

    /// <summary>The tools work on the active layer's mask rather than the layer.</summary>
    public bool EditsMask { get; init; }

    /// <summary>What is selected, over the canvas; empty — selecting everything — where nothing is.</summary>
    public required TiledCoverage Selection { get; init; }

    /// <summary>What the file's <c>image</c> element held that Nexaflow does not understand.</summary>
    public OpenRasterKept Kept { get; init; } = OpenRasterKept.None;

    /// <summary>The files the package held that Nexaflow does not read, by their path inside it, kept to be written back.</summary>
    public ImmutableDictionary<string, byte[]> KeptEntries { get; init; } = ImmutableDictionary<string, byte[]>.Empty;

    /// <summary>A blank canvas: <paramref name="width"/> × <paramref name="height"/>, one empty pixel layer, nothing selected.</summary>
    public static LayeredPicture Blank(int width, int height, LayerBackground background, string layerName, double dpi = DefaultDpi)
    {
        RequireSize(width, height);
        var layer = new PixelLayer { Name = layerName };
        return new LayeredPicture
        {
            Width = width, Height = height, Dpi = dpi, Background = background,
            Layers = [layer], ActiveId = layer.Id, Selection = TiledCoverage.Clear(width, height),
        };
    }

    /// <summary>A photo made the first layer of a canvas its size: a picture layer holding it and its recipe, at the canvas's top-left.</summary>
    public static LayeredPicture OfPhoto(PictureSource photo, ImageRecipe recipe, int width, int height, string layerName, double dpi = DefaultDpi)
    {
        RequireSize(width, height);
        var layer = new PictureLayer { Name = layerName, Source = photo, Recipe = recipe };
        return new LayeredPicture
        {
            Width = width, Height = height, Dpi = dpi,
            Layers = [layer], ActiveId = layer.Id, Selection = TiledCoverage.Clear(width, height),
        };
    }

    /// <exception cref="ArgumentOutOfRangeException">Not between a pixel and <see cref="MaxSide"/> each way.</exception>
    public static void RequireSize(int width, int height)
    {
        if (width < 1 || height < 1 || width > MaxSide || height > MaxSide)
            throw new ArgumentOutOfRangeException(nameof(width), $"A canvas is 1 to {MaxSide} pixels a side, not {width}×{height}.");
    }

    // ── Finding layers ──────────────────────────────────────────────────────

    /// <summary>Every layer, depth first, each group before what it holds.</summary>
    public IEnumerable<Layer> All => Walk(Layers);

    private static IEnumerable<Layer> Walk(ImmutableArray<Layer> layers)
    {
        foreach (var layer in layers)
        {
            yield return layer;
            if (layer is GroupLayer group)
                foreach (var inner in Walk(group.Children)) yield return inner;
        }
    }

    public Layer? Find(Guid id) => All.FirstOrDefault(l => l.Id == id);

    public Layer? Active => ActiveId is { } id ? Find(id) : null;

    /// <summary>The group holding the layer — null for the canvas itself — and its place among the group's layers, bottom first;
    /// null where no layer has the id.</summary>
    public (GroupLayer? Parent, int Index)? Locate(Guid id) => Locate(Layers, null, id);

    private static (GroupLayer? Parent, int Index)? Locate(ImmutableArray<Layer> layers, GroupLayer? parent, Guid id)
    {
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i].Id == id) return (parent, i);
            if (layers[i] is GroupLayer group && Locate(group.Children, group, id) is { } found) return found;
        }
        return null;
    }

    /// <summary>The layers that share the layer's group, bottom first.</summary>
    public ImmutableArray<Layer> SiblingsOf(Guid id) => Locate(id) is { Parent: { } parent } ? parent.Children : Layers;

    // ── Changing layers ─────────────────────────────────────────────────────

    /// <summary>This picture with the layer of <paramref name="id"/> made what <paramref name="change"/> makes of it.</summary>
    public LayeredPicture WithLayer(Guid id, Func<Layer, Layer> change) => this with { Layers = Changed(Layers, id, change) };

    private static ImmutableArray<Layer> Changed(ImmutableArray<Layer> layers, Guid id, Func<Layer, Layer> change)
    {
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i].Id == id) return layers.SetItem(i, change(layers[i]));
            if (layers[i] is GroupLayer group)
            {
                var children = Changed(group.Children, id, change);
                if (children != group.Children) return layers.SetItem(i, group with { Children = children });
            }
        }
        return layers;
    }

    /// <summary>This picture with the group the layer of <paramref name="id"/> lies in — or the canvas itself — given the layers
    /// <paramref name="change"/> makes of its layers.</summary>
    public LayeredPicture WithSiblings(Guid id, Func<ImmutableArray<Layer>, ImmutableArray<Layer>> change)
        => Locate(id) is { Parent: { } parent } ? WithLayer(parent.Id, g => ((GroupLayer)g) with { Children = change(((GroupLayer)g).Children) })
           : this with { Layers = change(Layers) };

    /// <summary>This picture with <paramref name="layer"/> put just above the layer of <paramref name="above"/>, in its group —
    /// or at the top of the canvas where none is named or found — and made the active layer.</summary>
    public LayeredPicture Inserted(Layer layer, Guid? above)
    {
        var placed = above is { } id && Locate(id) is { } at
            ? WithSiblings(id, siblings => siblings.Insert(at.Index + 1, layer))
            : this with { Layers = Layers.Add(layer) };
        return placed with { ActiveId = layer.Id, EditsMask = false };
    }

    /// <summary>This picture without the layer of <paramref name="id"/>; the layer beneath it — else above — becomes active.</summary>
    public LayeredPicture Without(Guid id)
    {
        if (Locate(id) is not { } at) return this;
        var siblings = SiblingsOf(id);
        Guid? next = at.Index > 0 ? siblings[at.Index - 1].Id : siblings.Length > 1 ? siblings[1].Id : at.Parent?.Id;
        var removed = WithSiblings(id, s => s.RemoveAt(at.Index));
        return removed with { ActiveId = ActiveId == id ? next : ActiveId, EditsMask = ActiveId != id && EditsMask };
    }

    /// <summary>This picture with the layer of <paramref name="id"/> moved into <paramref name="parent"/> (null: the canvas) at
    /// <paramref name="index"/> among its layers, bottom first — counted as they stand once the layer is taken out. A group is
    /// never moved into itself.</summary>
    public LayeredPicture Moved(Guid id, Guid? parent, int index)
    {
        if (Find(id) is not { } layer) return this;
        if (parent is { } p && (p == id || layer is GroupLayer group && Walk(group.Children).Any(l => l.Id == p))) return this;
        var taken = WithSiblings(id, s => s.RemoveAt(Locate(id)!.Value.Index));
        if (parent is { } into)
            return taken.WithLayer(into, g => ((GroupLayer)g) with { Children = ((GroupLayer)g).Children.Insert(Math.Clamp(index, 0, ((GroupLayer)g).Children.Length), layer) });
        return taken with { Layers = taken.Layers.Insert(Math.Clamp(index, 0, taken.Layers.Length), layer) };
    }

    // ── What history keeps ──────────────────────────────────────────────────

    /// <summary>
    /// The bytes of tiles — pixels, masks, the selection, regions picture layers keep — this picture holds that
    /// <paramref name="other"/> does not share: what keeping this one in a history beside it costs.
    /// </summary>
    public long BytesNotIn(LayeredPicture other)
    {
        var theirs = new Dictionary<Guid, Layer>();
        foreach (var layer in other.All) theirs[layer.Id] = layer;
        long bytes = Selection.BytesNotIn(other.Selection);
        foreach (var layer in All)
        {
            theirs.TryGetValue(layer.Id, out var match);
            if (ReferenceEquals(layer, match)) continue;
            if (layer is PixelLayer pixels) bytes += pixels.Pixels.BytesNotIn((match as PixelLayer)?.Pixels);
            if (layer.Mask is { } mask) bytes += mask.Coverage.BytesNotIn(match?.Mask?.Coverage);
            if (layer is PictureLayer picture) bytes += RegionBytes(picture.Recipe, (match as PictureLayer)?.Recipe);
        }
        return bytes;
    }

    private static long RegionBytes(ImageRecipe recipe, ImageRecipe? other)
    {
        if (ReferenceEquals(recipe, other)) return 0;
        var theirs = other is null ? [] : Coverages(other).ToHashSet(ReferenceEqualityComparer.Instance);
        return Coverages(recipe).Where(c => !theirs.Contains(c)).Sum(c => c.BytesNotIn(null));
    }

    private static IEnumerable<TiledCoverage> Coverages(ImageRecipe recipe)
    {
        var regions = new[] { recipe.CutOut?.Region }.Concat(recipe.Erasures.Select(e => e.Region));
        foreach (var region in regions)
            if (region is not null)
                foreach (var step in region.Steps.OfType<RegionCoverage>()) yield return step.Coverage;
    }
}
