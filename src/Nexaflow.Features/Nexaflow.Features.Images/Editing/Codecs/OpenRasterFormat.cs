using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.OpenRaster;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Codecs;

/// <summary>
/// OpenRaster ⇄ a layered picture. What it writes is standard OpenRaster any reader draws — every layer as its pixels, groups
/// as stacks, blends as OpenRaster names them, locks, the active layer and clipping as Krita writes them — with what only
/// Nexaflow uses beside it under Nexaflow's own namespace: a picture layer's source and recipe, a markup layer's annotations,
/// a mask as a stack of two each reader draws as the masked layer, the transparency lock, each layer's identity and the hash
/// of the pixels it was written with. What it reads, it reads as each application wrote it, and what it does not understand
/// — attributes, elements, files — it keeps and writes back.
/// </summary>
/// <remarks>
/// Where an application kept Nexaflow's attributes but changed a layer's pixels, the two no longer agree: the pixels' hash
/// says so, and the pixels win — the layer is read as a pixel layer. A layer whose extension files are missing is read the
/// same way.
/// </remarks>
public sealed class OpenRasterFormat(IVirtualFileSystem fileSystem) : ILayeredPictureFormat
{
    private const string Ns = OpenRasterNamespaces.Nexaflow;
    private const string NormalOp = "svg:src-over", ClipOp = "svg:src-atop", MaskOp = "svg:dst-in";
    private const string RoleMasked = "masked", RoleMask = "mask";

    /// <summary>Nexaflow's attributes, which it reads itself and writes afresh, so never keeps.</summary>
    private static readonly HashSet<string> OwnAttributes = ["id", "source", "recipe", "markup", "placement", "pixels", "alpha-locked", "role", "mask-linked", "background"];

    /// <summary>The file system packages are read and written through.</summary>
    private IVirtualFileSystem FileSystem { get; } = fileSystem;

    public string Extension => OpenRasterPackage.Extension;

    public bool Reads(ImageContainer container) => container == ImageContainer.OpenRaster;

    // ── Reading ─────────────────────────────────────────────────────────────

    public async Task<LayeredPictureRead> ReadAsync(string path, CancellationToken ct = default)
    {
        if (!await OpenRasterPackage.IsOpenRasterAsync(FileSystem, path, ct)) throw new InvalidDataException($"{path} is not an OpenRaster picture.");
        var image = await OpenRasterPackage.ReadStackAsync(FileSystem, path, ct);
        LayeredPicture.RequireSize(image.Width, image.Height);
        var reading = new Reading(this, path, image.Width, image.Height, ct);
        var layers = await reading.LayersAsync(image.Root.Children);

        var files = await OpenRasterPackage.FilesAsync(FileSystem, path, ct);
        var keptFiles = ImmutableDictionary.CreateBuilder<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            if (reading.Used.Contains(file) || file is OpenRasterPackage.MimeTypePath or OpenRasterPackage.StackPath
                or OpenRasterPackage.MergedImagePath or OpenRasterPackage.ThumbnailPath) continue;
            keptFiles[file] = await FileSystem.ReadAllBytesAsync(OpenRasterPackage.EntryPath(path, file), ct);
        }

        var background = Own(image.Kept, "background") is { } colour && uint.TryParse(colour.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb)
            ? new LayerBackground(argb) : LayerBackground.Transparent;
        var picture = new LayeredPicture
        {
            Width = image.Width,
            Height = image.Height,
            Dpi = image.XResolution ?? LayeredPicture.DefaultDpi,
            Background = background,
            Layers = layers,
            ActiveId = reading.Active ?? TopmostOf(layers),
            Selection = TiledCoverage.Clear(image.Width, image.Height),
            Kept = Foreign(image.Kept),
            KeptEntries = keptFiles.ToImmutable(),
        };
        return new LayeredPictureRead(picture, reading.HoldsUndrawable);
    }

    private static Guid? TopmostOf(ImmutableArray<Layer> layers) => layers.IsEmpty ? null : layers[^1].Id;

    /// <summary>One package being read: the files it uses, the active layer, and whether any layer cannot be drawn.</summary>
    private sealed class Reading(OpenRasterFormat format, string package, int width, int height, CancellationToken ct)
    {
        public HashSet<string> Used { get; } = new(StringComparer.Ordinal);
        public Guid? Active { get; private set; }
        public bool HoldsUndrawable { get; private set; }

        /// <summary>The stack's entries, uppermost first, as layers bottom first.</summary>
        public async Task<ImmutableArray<Layer>> LayersAsync(ImmutableArray<OpenRasterEntry> entries)
        {
            var layers = ImmutableArray.CreateBuilder<Layer>(entries.Length);
            foreach (var entry in entries.Reverse()) layers.Add(await LayerAsync(entry));
            return layers.MoveToImmutable();
        }

        private async Task<Layer> LayerAsync(OpenRasterEntry entry)
        {
            ct.ThrowIfCancellationRequested();
            Layer layer = entry switch
            {
                OpenRasterStackEntry { Children: [OpenRasterLayerEntry mask, var masked] } stack
                    when Own(stack.Kept, "role") == RoleMasked && Own(mask.Kept, "role") == RoleMask
                    => (await LayerAsync(masked)) with { Mask = await MaskOfAsync(mask) },
                OpenRasterStackEntry stack => new GroupLayer { Children = await LayersAsync(stack.Children), Isolated = stack.Isolated },
                OpenRasterLayerEntry layerEntry => await ContentAsync(layerEntry),
                OpenRasterOtherEntry other => Undrawable(other),
                _ => throw new InvalidDataException($"No layer for a {entry.GetType().Name}."),
            };
            var op = entry.CompositeOp ?? NormalOp;
            bool clips = op == ClipOp || entry is OpenRasterLayerEntry { AlphaPreserve: true };
            var read = layer with
            {
                Id = Guid.TryParse(Own(entry.Kept, "id"), out var id) ? id : layer.Id,
                Name = entry.Name,
                Opacity = entry.Opacity,
                Visible = entry.Visible,
                Blend = new LayerBlend(op == ClipOp ? NormalOp : op),
                ClipsToBelow = clips,
                Locked = entry.EditLocked,
                AlphaLocked = Own(entry.Kept, "alpha-locked") == "true",
                Kept = Foreign(entry.Kept),
            };
            if (entry.Selected && Active is null) Active = read.Id;
            return read;
        }

        private UndrawableLayer Undrawable(OpenRasterOtherEntry other)
        {
            HoldsUndrawable = true;
            return new UndrawableLayer { Kind = other.Type ?? other.ElementName, Element = other.Element };
        }

        /// <summary>A layer's content: a picture or markup layer where Nexaflow wrote it and its pixels are still those it wrote,
        /// else the pixels as they are.</summary>
        private async Task<Layer> ContentAsync(OpenRasterLayerEntry entry)
        {
            var png = await BytesAsync(entry.Source);
            bool current = Own(entry.Kept, "pixels") is { } hash && png is not null && hash == HashOf(png);
            if (current && Own(entry.Kept, "source") is { } source && Own(entry.Kept, "recipe") is { } recipeFile
                && await ExistsAsync(source) && await BytesAsync(recipeFile) is { } recipeBytes
                && ImageRecipeJson.Deserialize(Encoding.UTF8.GetString(recipeBytes)) is { Status: RecipeReadStatus.Read, Recipe: { } recipe })
            {
                Used.Add(source);
                var coverages = new Dictionary<string, TiledCoverage?>(StringComparer.Ordinal);
                foreach (var file in CoverageFiles(recipe)) coverages[file] = await CoverageOfAsync(file);
                return new PictureLayer
                {
                    Source = new PictureSource(OpenRasterPackage.EntryPath(package, source), null),
                    Recipe = WithCoverages(recipe, file => coverages.GetValueOrDefault(file)),
                    Placement = PlacementOf(Own(entry.Kept, "placement")) ?? new LayerPlacement(entry.X, entry.Y),
                };
            }
            if (current && Own(entry.Kept, "markup") is { } markupFile && await BytesAsync(markupFile) is { } markupBytes
                && ImageRecipeJson.DeserializeMarkup(Encoding.UTF8.GetString(markupBytes)) is { } elements)
                return new MarkupLayer { Elements = elements };
            return new PixelLayer { Pixels = png is null ? TiledPixels.Empty : PixelsOf(png, entry.X, entry.Y) };
        }

        /// <summary>A mask from its layer — white, the mask as its alpha — over the canvas: what it does not reach hides.</summary>
        private async Task<LayerMask> MaskOfAsync(OpenRasterLayerEntry entry)
        {
            var pixels = await BytesAsync(entry.Source) is { } png ? PixelsOf(png, entry.X, entry.Y) : TiledPixels.Empty;
            return new LayerMask(pixels.Alpha(width, height), Enabled: entry.Visible, Linked: Own(entry.Kept, "mask-linked") != "false");
        }

        private async Task<TiledCoverage?> CoverageOfAsync(string file)
        {
            if (await BytesAsync(file) is not { } png) return null;
            using var decoded = SKBitmap.Decode(png);
            if (decoded is null) return null;
            using var grey = decoded.ColorType == SKColorType.Gray8 ? null : decoded.Copy(SKColorType.Gray8);
            var source = grey ?? decoded;
            var bytes = new byte[source.Width * source.Height];
            var origin = source.GetPixels();
            for (int y = 0; y < source.Height; y++) Marshal.Copy(origin + y * source.RowBytes, bytes, y * source.Width, source.Width);
            return TiledCoverage.FromBytes(bytes, source.Width, source.Height);
        }

        private ValueTask<bool> ExistsAsync(string file) => format.FileSystem.ExistsAsync(OpenRasterPackage.EntryPath(package, file), ct);

        private async Task<byte[]?> BytesAsync(string file)
        {
            if (string.IsNullOrEmpty(file) || !await ExistsAsync(file)) return null;
            Used.Add(file);
            return await format.FileSystem.ReadAllBytesAsync(OpenRasterPackage.EntryPath(package, file), ct);
        }
    }

    /// <summary>The files the coverages of a recipe's regions are kept in.</summary>
    private static IEnumerable<string> CoverageFiles(ImageRecipe recipe)
    {
        var files = new List<string>();
        MapRegions(recipe, step =>
        {
            if (step.File.Length > 0) files.Add(step.File);
            return step;
        });
        return files;
    }

    private static TiledPixels PixelsOf(byte[] png, int x, int y)
    {
        using var bitmap = SKBitmap.Decode(png) ?? throw new InvalidDataException("A layer's pixels are not a picture.");
        return TiledPixels.FromBitmap(bitmap, x, y);
    }

    private static LayerPlacement? PlacementOf(string? text)
    {
        var parts = text?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts is not { Length: 4 }) return null;
        var values = parts.Select(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN).ToArray();
        return values.Any(double.IsNaN) ? null : new LayerPlacement(values[0], values[1], values[2], values[3]);
    }

    /// <summary>Nexaflow's own attribute of the element; null where it has none.</summary>
    private static string? Own(OpenRasterKept kept, string name)
    {
        foreach (var attribute in kept.Attributes)
            if (attribute.Namespace == Ns && attribute.LocalName == name) return attribute.Value;
        return null;
    }

    /// <summary>What an element kept besides Nexaflow's own attributes — another application's, written back as read.</summary>
    private static OpenRasterKept Foreign(OpenRasterKept kept)
        => kept with { Attributes = [.. kept.Attributes.Where(a => !(a.Namespace == Ns && OwnAttributes.Contains(a.LocalName)))] };

    private static string HashOf(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    // ── Writing ─────────────────────────────────────────────────────────────

    public async Task WriteAsync(LayeredPicture picture, string path, ILayeredPictureRenderer renderer, CancellationToken ct = default)
    {
        // Each picture layer's source, the original file as it was, read before anything is written: the package being replaced may hold it.
        var sources = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in picture.All.OfType<PictureLayer>())
            if (!sources.ContainsKey(layer.Source.Path)) sources[layer.Source.Path] = await FileSystem.ReadAllBytesAsync(layer.Source.Path, ct);
        var (image, files) = await Task.Run(() => Package(picture, renderer, sources, ct), ct);
        await OpenRasterPackage.WriteAsync(FileSystem, path, image, files, ct);
    }

    /// <summary>The package's stack and files for <paramref name="picture"/>, its layers drawn by <paramref name="renderer"/>.</summary>
    private static (OpenRasterImage Image, IReadOnlyList<OpenRasterFile> Files) Package(LayeredPicture picture, ILayeredPictureRenderer renderer,
                                                                                        IReadOnlyDictionary<string, byte[]> sources, CancellationToken ct)
    {
        var writing = new Writing(picture, renderer, sources, ct);
        var root = new OpenRasterStackEntry { Children = writing.Entries(picture.Layers) };

        using (var merged = renderer.Composite(1)) writing.Add(OpenRasterPackage.MergedImagePath, Png(merged), stored: true);
        double thumbScale = Math.Min(1, (double)OpenRasterPackage.ThumbnailMaxSide / Math.Max(picture.Width, picture.Height));
        using (var thumbnail = renderer.Composite(thumbScale)) writing.Add(OpenRasterPackage.ThumbnailPath, Png(thumbnail), stored: true);
        foreach (var (file, bytes) in picture.KeptEntries)
            if (!writing.Names(file)) writing.Add(file, bytes, stored: false);

        var kept = picture.Kept;
        if (!picture.Background.IsTransparent)
            kept = kept with { Attributes = kept.Attributes.Add(new OpenRasterAttribute(Ns, "background", $"#{picture.Background.Argb:X8}")) };
        var image = new OpenRasterImage
        {
            Width = picture.Width, Height = picture.Height,
            XResolution = picture.Dpi, YResolution = picture.Dpi,
            Version = OpenRasterPackage.WrittenVersion,
            Root = root,
            Kept = kept,
        };
        ct.ThrowIfCancellationRequested();
        return (image, writing.Files);
    }

    /// <summary>One package being written: the files it holds so far, in order.</summary>
    private sealed class Writing(LayeredPicture picture, ILayeredPictureRenderer renderer, IReadOnlyDictionary<string, byte[]> sources, CancellationToken ct)
    {
        private readonly List<OpenRasterFile> _files = [];
        private readonly HashSet<string> _names = new(StringComparer.Ordinal);
        private int _layers, _recipes, _markups;

        public IReadOnlyList<OpenRasterFile> Files => _files;

        public bool Names(string file) => _names.Contains(file);

        public void Add(string file, byte[] bytes, bool stored)
        {
            if (!_names.Add(file)) return;
            _files.Add(new OpenRasterFile(file, () => new MemoryStream(bytes, writable: false), stored));
        }

        /// <summary>Layers bottom first as the stack's entries, uppermost first.</summary>
        public ImmutableArray<OpenRasterEntry> Entries(ImmutableArray<Layer> layers) => [.. layers.Reverse().Select(Entry)];

        private OpenRasterEntry Entry(Layer layer)
        {
            ct.ThrowIfCancellationRequested();
            if (layer.Mask is { } mask)
            {
                // Masked: an isolated stack carrying the layer's name, opacity, visibility, blend and clip, holding the mask on top
                // (white, the mask as its alpha, kept by dst-in) and the layer itself beneath at full opacity, Normal.
                var inner = Entry(layer with { Mask = null, Opacity = 1, Visible = true, Blend = LayerBlend.Normal, ClipsToBelow = false }) with { Selected = false };
                var maskEntry = MaskEntry(mask);
                return Shared(new OpenRasterStackEntry { Children = [maskEntry, inner], Isolation = "isolate" }, layer, role: RoleMasked);
            }
            return layer switch
            {
                GroupLayer group => Shared(new OpenRasterStackEntry { Children = Entries(group.Children), Isolation = group.Isolated ? "isolate" : "auto" }, layer),
                PixelLayer pixels => Shared(PixelEntry(pixels.Pixels), layer),
                PictureLayer picture => Shared(PictureEntry(picture), layer),
                MarkupLayer markup => Shared(MarkupEntry(markup), layer),
                UndrawableLayer undrawable => new OpenRasterOtherEntry { Element = undrawable.Element, Name = undrawable.Name },
                _ => throw new NotSupportedException($"No entry for a {layer.GetType().Name}."),
            };
        }

        /// <summary>What every entry of a layer says: its name, opacity, visibility, blend and clip as Krita writes them, its lock,
        /// whether it is active, and Nexaflow's identity and transparency lock beside what it kept.</summary>
        private OpenRasterEntry Shared(OpenRasterEntry entry, Layer layer, string? role = null)
        {
            var blend = layer.Blend.Op ?? NormalOp;
            bool normal = blend == NormalOp;
            var own = new List<OpenRasterAttribute> { new(Ns, "id", layer.Id.ToString("D")) };
            if (layer.AlphaLocked) own.Add(new OpenRasterAttribute(Ns, "alpha-locked", "true"));
            if (role is not null) own.Add(new OpenRasterAttribute(Ns, "role", role));
            var shared = entry with
            {
                Name = layer.Name,
                Opacity = layer.Opacity,
                Visible = layer.Visible,
                CompositeOp = layer.ClipsToBelow && normal ? ClipOp : blend,
                Selected = picture.ActiveId == layer.Id,
                EditLocked = layer.Locked,
                Kept = layer.Kept with { Attributes = [.. own, .. entry.Kept.Attributes, .. layer.Kept.Attributes] },
            };
            return shared is OpenRasterLayerEntry layerEntry && layer.ClipsToBelow && !normal ? layerEntry with { AlphaPreserve = true } : shared;
        }

        private OpenRasterLayerEntry MaskEntry(LayerMask mask)
        {
            var coverage = mask.Coverage;
            var area = coverage.Outside != 0 ? new SKRectI(0, 0, coverage.Width, coverage.Height) : coverage.Bounds;
            byte[] png;
            if (area.IsEmpty)
            {
                area = new SKRectI(0, 0, 1, 1);
                png = Png(new byte[4], 1, 1);
            }
            else
            {
                var alpha = coverage.Read(area);
                var rgba = new byte[alpha.Length * 4];
                for (int i = 0; i < alpha.Length; i++) rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = rgba[i * 4 + 3] = alpha[i];
                png = Png(rgba, area.Width, area.Height);
            }
            var file = NextLayerFile();
            Add(file, png, stored: true);
            var kept = new List<OpenRasterAttribute> { new(Ns, "role", RoleMask) };
            if (!mask.Linked) kept.Add(new OpenRasterAttribute(Ns, "mask-linked", "false"));
            return new OpenRasterLayerEntry
            {
                Source = file, X = area.Left, Y = area.Top, Name = "mask", Visible = mask.Enabled, CompositeOp = MaskOp,
                Kept = new OpenRasterKept { Attributes = [.. kept] },
            };
        }

        private OpenRasterLayerEntry PixelEntry(TiledPixels pixels)
        {
            var area = pixels.Bounds;
            byte[] png;
            if (area.IsEmpty)
            {
                area = new SKRectI(0, 0, 1, 1);
                png = Png(new byte[4], 1, 1);
            }
            else
            {
                using var bitmap = pixels.ToBitmap(area);
                png = Png(bitmap);
            }
            var file = NextLayerFile();
            Add(file, png, stored: true);
            return new OpenRasterLayerEntry { Source = file, X = area.Left, Y = area.Top };
        }

        private OpenRasterLayerEntry PictureEntry(PictureLayer layer)
        {
            var (png, at) = Rendered(layer);
            var file = NextLayerFile();
            Add(file, png, stored: true);

            var source = sources[layer.Source.Path];
            var extension = System.IO.Path.GetExtension(layer.Source.Path);
            var sourceFile = $"data/nexaflow/source-{HashOf(source)[..16]}{(extension.Length > 0 ? extension : ".bin")}";
            Add(sourceFile, source, stored: true);

            var recipe = WithCoverageFiles(layer.Recipe);
            var recipeFile = $"data/nexaflow/recipe{++_recipes}.nfedit";
            Add(recipeFile, Encoding.UTF8.GetBytes(ImageRecipeJson.Serialize(recipe)), stored: false);

            var p = layer.Placement;
            var placement = string.Create(CultureInfo.InvariantCulture, $"{p.OffsetX:R} {p.OffsetY:R} {p.Scale:R} {p.RotationDegrees:R}");
            return new OpenRasterLayerEntry
            {
                Source = file, X = at.X, Y = at.Y,
                Kept = new OpenRasterKept
                {
                    Attributes =
                    [
                        new(Ns, "source", sourceFile), new(Ns, "recipe", recipeFile),
                        new(Ns, "placement", placement), new(Ns, "pixels", HashOf(png)),
                    ],
                },
            };
        }

        private OpenRasterLayerEntry MarkupEntry(MarkupLayer layer)
        {
            var (png, at) = Rendered(layer);
            var file = NextLayerFile();
            Add(file, png, stored: true);
            var markupFile = $"data/nexaflow/markup{++_markups}.json";
            Add(markupFile, Encoding.UTF8.GetBytes(ImageRecipeJson.SerializeMarkup(layer.Elements)), stored: false);
            return new OpenRasterLayerEntry
            {
                Source = file, X = at.X, Y = at.Y,
                Kept = new OpenRasterKept { Attributes = [new(Ns, "markup", markupFile), new(Ns, "pixels", HashOf(png))] },
            };
        }

        /// <summary>A picture or markup layer drawn by itself as PNG, and where it lies; a transparent pixel where it shows nothing.</summary>
        private (byte[] Png, SKPointI At) Rendered(Layer layer)
        {
            if (renderer.RenderLayer(layer) is not { } rendered) return (Png(new byte[4], 1, 1), new SKPointI(0, 0));
            using (rendered.Pixels) return (Png(rendered.Pixels), rendered.At);
        }

        /// <summary>The recipe with each coverage its regions hold written to the package, the step naming its file.</summary>
        private ImageRecipe WithCoverageFiles(ImageRecipe recipe) => MapRegions(recipe, step =>
        {
            if (step.Coverage is not { } coverage) return step;
            var grey = coverage.ToBytes();
            var file = $"data/nexaflow/region-{HashOf(grey)[..16]}.png";
            if (!Names(file)) Add(file, GreyPng(grey, coverage.Width, coverage.Height), stored: true);
            return step with { File = file };
        });

        private string NextLayerFile() => $"data/layer{++_layers}.png";
    }

    /// <summary>The recipe with every <see cref="RegionCoverage"/> its regions hold given its coverage from the file it names.</summary>
    private static ImageRecipe WithCoverages(ImageRecipe recipe, Func<string, TiledCoverage?> coverageOf)
        => MapRegions(recipe, step => step.Coverage is null && step.File.Length > 0 ? step with { Coverage = coverageOf(step.File) } : step);

    private static ImageRecipe MapRegions(ImageRecipe recipe, Func<RegionCoverage, RegionCoverage> map)
    {
        RecipeRegion? Mapped(RecipeRegion? region) => region is null ? null
            : region with { Steps = [.. region.Steps.Select(s => s is RegionCoverage coverage ? map(coverage) : s)] };
        return recipe with
        {
            CutOut = recipe.CutOut is { } cutOut ? cutOut with { Region = Mapped(cutOut.Region), } : null,
            Erasures = [.. recipe.Erasures.Select(e => e with { Region = Mapped(e.Region), Pick = e.Pick is { } pick ? pick with { Region = Mapped(pick.Region) } : null })],
        };
    }

    // ── PNG ─────────────────────────────────────────────────────────────────

    private static byte[] Png(SKImage image)
    {
        using var data = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidOperationException("A picture could not be written as PNG.");
        return data.ToArray();
    }

    private static byte[] Png(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        return Png(image);
    }

    /// <summary>Premultiplied RGBA as a PNG.</summary>
    private static byte[] Png(byte[] rgba, int width, int height)
    {
        using var image = SKImage.FromPixelCopy(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul), rgba)
                          ?? throw new InvalidOperationException("A picture could not be made.");
        return Png(image);
    }

    private static byte[] GreyPng(byte[] grey, int width, int height)
    {
        using var image = SKImage.FromPixelCopy(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque), grey)
                          ?? throw new InvalidOperationException("A coverage could not be made a picture.");
        return Png(image);
    }
}
