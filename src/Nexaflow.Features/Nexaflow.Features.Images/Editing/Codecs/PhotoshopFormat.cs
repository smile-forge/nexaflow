using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Media.Photoshop;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Codecs;

/// <summary>
/// Photoshop ⇄ a layered picture — PSD, or PSB for the large document format — RGB or grayscale, at 8, 16 or 32 bits. Layers
/// are read as they are drawn: pixels at the picture's depth; groups, pass-through ones too; blends, opacity, visibility,
/// locks; user masks; a clipping run as an isolated group the base heads. What Nexaflow does not draw is kept and written
/// back — a text layer's text, a smart object, effects, a vector mask while what they describe is unchanged; an adjustment
/// layer as it was — and a file holding any of it opens on the look Photoshop saved.
/// </summary>
/// <remarks>
/// <para>What only Nexaflow uses rides in tagged blocks of its own: <c>Nxly</c> on each layer — its identity, and a picture or
/// markup layer's recipe, annotations and placement with the hash of the pixels written for it — and <c>Nxfi</c> on the
/// file, the pictures those layers show and their regions' coverage, by content. Photoshop sees each picture or markup layer
/// as its pixels; Nexaflow reads it back as it was, unless the pixels no longer hash the same, when they win.</para>
/// <para>A file's samples stay in it until its layers are built, a band of rows at a time, every channel read in step.</para>
/// </remarks>
public sealed partial class PhotoshopFormat(IVirtualFileSystem fileSystem, bool large, string sourcesFolder) : ILayeredPictureFormat
{
    public const string PsdExtension = ".psd", PsbExtension = ".psb";

    internal const string LayerBlockKey = "Nxly", FilesBlockKey = "Nxfi";

    private const int BandRows = TiledPixels.TileSize;

    /// <summary>A layer's tagged blocks Nexaflow reads into the layer, and writes afresh.</summary>
    private static readonly HashSet<string> OwnKeys = ["luni", "lsct", "lsdk", "lspf", "lyid", "iOpa", LayerBlockKey];

    /// <summary>The blocks a layer's pixels are made from — text, smart objects, shapes, fills — valid only while they are.</summary>
    private static readonly HashSet<string> PixelKeys = ["TySh", "tySh", "SoLd", "SoLE", "PlLd", "plLd", "vscg", "vogk"];

    /// <summary>The keys of adjustment and fill layers, which hold no pixels of their own to draw.</summary>
    private static readonly HashSet<string> AdjustmentKeys =
    [
        "levl", "curv", "brit", "blnc", "blwh", "hue2", "hue ", "selc", "mixr", "grdm", "phfl", "expA", "vibA", "thrs", "post", "nvrt",
        "CgEd", "clrL", "SoCo", "GdFl", "PtFl",
    ];

    private static readonly HashSet<string> EffectKeys = ["lfx2", "lrFX", "lmfx", "lfxs"];

    private static readonly HashSet<string> VectorMaskKeys = ["vmsk", "vsms"];

    /// <summary>Image resources Nexaflow writes afresh, so never keeps.</summary>
    private static readonly HashSet<ushort> OwnResources =
        [PhotoshopResource.ResolutionInfo, PhotoshopResource.LayerState, PhotoshopResource.LayerSelectionIds, PhotoshopResource.IccProfile, PhotoshopResource.Thumbnail, VersionInfo];

    private const ushort VersionInfo = 1057;

    private IVirtualFileSystem FileSystem { get; } = fileSystem;

    public string Extension => large ? PsbExtension : PsdExtension;

    public bool Reads(ImageContainer container) => container == ImageContainer.Photoshop;

    /// <summary>Colour or grey at 8, 16 and 32 bits: every colour a layered picture is held in.</summary>
    public bool HoldsColour(PictureColour colour) => true;

    /// <summary>The file itself, which holds its merged image: <see cref="PhotoshopDecoder"/> shows it.</summary>
    public string SavedLookOf(string path) => path;

    // ── Reading ─────────────────────────────────────────────────────────────

    public async Task<LayeredPictureRead> ReadAsync(string path, CancellationToken ct = default)
    {
        await using var stream = await FileSystem.OpenReadAsync(path, ReadIntent.Random, ct);
        var (read, sources) = await Task.Run(() => ReadFrom(stream, sourcesFolder, ct), ct);
        // The pictures Nexaflow's picture layers show, out of the file and into the cache, where a picture layer reads them.
        if (sources.Count > 0) await FileSystem.CreateDirectoryAsync(sourcesFolder, ct);
        foreach (var (file, bytes) in sources)
            if (!await FileSystem.ExistsAsync(file, ct)) await FileSystem.WriteAllBytesAsync(file, bytes, ct);
        return read;
    }

    /// <summary>A Photoshop file read from <paramref name="stream"/> as a layered picture, and the source files its picture
    /// layers need written where they name them, in <paramref name="sourcesFolder"/>.</summary>
    /// <exception cref="InvalidDataException">It is not a Photoshop file, or not one of a colour Nexaflow holds.</exception>
    internal static (LayeredPictureRead Read, IReadOnlyDictionary<string, byte[]> Sources) ReadFrom(Stream stream, string sourcesFolder, CancellationToken ct = default)
    {
        var document = PhotoshopReader.Read(stream);
        var header = document.Header;
        var colour = ColourOf(header) ?? throw new InvalidDataException(
            $"A {header.Depth}-bit {header.Mode} Photoshop picture can't be edited here — only RGB and grayscale at 8, 16 or 32 bits.");
        LayeredPicture.RequireSize(header.Width, header.Height);

        var reading = new Reading(sourcesFolder, stream, document, colour, ct);
        var layers = reading.Layers();
        var picture = new LayeredPicture
        {
            Width = header.Width,
            Height = header.Height,
            Dpi = DpiOf(document) ?? LayeredPicture.DefaultDpi,
            Colour = colour,
            Profile = document.Resource(PhotoshopResource.IccProfile) is { } icc ? [.. icc.Data.Span] : [],
            Background = reading.Background,
            Layers = layers,
            ActiveId = reading.Active ?? (layers.IsEmpty ? null : layers[^1].Id),
            Selection = TiledCoverage.Clear(header.Width, header.Height),
            Kept = KeptFromFile.Of(new PhotoshopPictureKept(
                document.ColourModeData,
                [.. document.Resources.Where(r => !OwnResources.Contains(r.Id))],
                document.GlobalLayerMask,
                [.. document.GlobalBlocks.Where(b => b.Key != FilesBlockKey)])),
        };
        return (new LayeredPictureRead(picture, reading.HoldsUndrawable && HasRealMergedImage(document)), reading.Sources);
    }

    /// <summary>The colour a header's mode and depth are held in; null where it is none Nexaflow holds.</summary>
    internal static PictureColour? ColourOf(PhotoshopFileHeader header)
    {
        ColourModel? model = header.Mode switch { PhotoshopColourMode.Rgb => ColourModel.Rgb, PhotoshopColourMode.Grayscale => ColourModel.Grey, _ => null };
        ChannelDepth? depth = header.Depth switch { 8 => ChannelDepth.U8, 16 => ChannelDepth.U16, 32 => ChannelDepth.F32, _ => null };
        return model is { } m && depth is { } d ? new PictureColour(m, d) : null;
    }

    /// <summary>Whether the merged image is the picture drawn — false where it was saved without Maximize Compatibility,
    /// which leaves a blank one.</summary>
    internal static bool HasRealMergedImage(PhotoshopDocument document)
        => document.Resource(VersionInfo) is not { } version || version.Data.Length < 5 || version.Data.Span[4] != 0;

    /// <summary>The resolution the file names, in dots an inch.</summary>
    private static double? DpiOf(PhotoshopDocument document)
    {
        if (document.Resource(PhotoshopResource.ResolutionInfo) is not { Data.Length: >= 6 } resolution) return null;
        var data = resolution.Data.Span;
        double perUnit = BinaryPrimitives.ReadUInt32BigEndian(data) / 65536.0;
        ushort unit = BinaryPrimitives.ReadUInt16BigEndian(data[4..]);
        double dpi = unit == 2 ? perUnit * 2.54 : perUnit;
        return dpi > 0 ? dpi : null;
    }

    /// <summary>One file being read: its layer records made layers, the background and active layer found, the sources its
    /// picture layers show, and whether anything was left undrawn.</summary>
    private sealed class Reading(string sourcesFolder, Stream stream, PhotoshopDocument document, PictureColour colour, CancellationToken ct)
    {
        private readonly PhotoshopFileHeader _header = document.Header;
        private readonly TileFormat _tiles = colour.Tiles;
        private readonly Dictionary<string, byte[]> _files = FilesOf(document.GlobalBlocks);
        private readonly Dictionary<uint, Guid> _byLayerId = [];

        public bool HoldsUndrawable { get; private set; }
        public LayerBackground Background { get; private set; } = LayerBackground.Transparent;
        public Guid? Active { get; private set; }
        public Dictionary<string, byte[]> Sources { get; } = new(StringComparer.OrdinalIgnoreCase);

        private sealed record Item(Layer Layer, bool Clipped);

        /// <summary>Every record, bottom first, as layers: groups closed where their divider opens them, clipping runs made groups.</summary>
        public ImmutableArray<Layer> Layers()
        {
            DecodePixels();
            var levels = new Stack<List<Item>>();
            var current = new List<Item>();
            foreach (var record in document.Layers)
            {
                ct.ThrowIfCancellationRequested();
                uint section = SectionOf(record);
                if (section == 3)
                {
                    levels.Push(current);
                    current = [];
                    continue;
                }
                if (section is 1 or 2)
                {
                    var children = Runs(current);
                    current = levels.Count > 0 ? levels.Pop() : [];
                    current.Add(Group(record, children, section));
                    continue;
                }
                if (Content(record) is { } item) current.Add(item);
            }
            // A divider no group closed: its layers belong to what holds it.
            while (levels.Count > 0)
            {
                var parent = levels.Pop();
                parent.AddRange(current);
                current = parent;
            }
            Active = ActiveOf() ?? Active;
            return Runs(current);
        }

        /// <summary>One level's layers, bottom first, each clipping run — a base and the layers clipped to it — made an isolated
        /// group the base heads; a clipped layer with no base beneath is drawn as any other.</summary>
        private ImmutableArray<Layer> Runs(List<Item> items)
        {
            var layers = ImmutableArray.CreateBuilder<Layer>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                int end = i + 1;
                while (end < items.Count && items[end].Clipped) end++;
                if (item.Clipped || end == i + 1)
                {
                    layers.Add(item.Layer);
                    continue;
                }
                var baseLayer = item.Layer;
                var run = new GroupLayer
                {
                    Name = baseLayer.Name, Opacity = RunOpacity(baseLayer), Blend = baseLayer.Blend, Visible = baseLayer.Visible, Isolated = true,
                    Children = [baseLayer with { Opacity = BaseFill(baseLayer), Blend = LayerBlend.Normal, Visible = true },
                                .. items.Skip(i + 1).Take(end - i - 1).Select(c => c.Layer with { ClipsToBelow = true })],
                    Kept = KeptFromFile.Of(PhotoshopClipRun.Mark),
                };
                layers.Add(run);
                i = end - 1;
            }
            return layers.ToImmutable();
        }

        /// <summary>The base's opacity as written, which applies to the whole run; its fill is its own.</summary>
        private static double RunOpacity(Layer baseLayer)
            => baseLayer.Kept.Get<PhotoshopLayerKept>()?.OpacityAndFill is { } written ? written.Opacity / 255.0 : baseLayer.Opacity;

        /// <summary>The base's own fill, which applies to it alone; its opacity is the run's.</summary>
        private static double BaseFill(Layer baseLayer)
            => baseLayer.Kept.Get<PhotoshopLayerKept>()?.OpacityAndFill is { } written && written.Opacity > 0
                ? Math.Clamp(baseLayer.Opacity / (written.Opacity / 255.0), 0, 1)
                : 1;

        private Item Group(PhotoshopLayerRecord record, ImmutableArray<Layer> children, uint section)
        {
            bool passes = record.BlendKey == PhotoshopBlends.PassThrough || SectionBlendOf(record) == PhotoshopBlends.PassThrough;
            var group = Common(new GroupLayer { Children = children, Isolated = !passes }, record, section, passes ? PhotoshopBlends.Normal : record.BlendKey);
            return new Item(group, record.Clipping != 0);
        }

        /// <summary>A record that is not a group's end: an adjustment Nexaflow cannot draw, the background, a picture or markup
        /// layer Nexaflow wrote, or pixels. Null for the background, which is the picture's, not a layer.</summary>
        private Item? Content(PhotoshopLayerRecord record)
        {
            var own = OwnOf(record);
            if (own?["role"]?.GetValue<string>() == "background" && own["colour"]?.GetValue<string>() is { } argb
                && uint.TryParse(argb, System.Globalization.NumberStyles.HexNumber, null, out var colourValue))
            {
                Background = new LayerBackground(colourValue);
                return null;
            }
            if (record.Blocks.FirstOrDefault(b => AdjustmentKeys.Contains(b.Key)) is { } adjustment)
            {
                HoldsUndrawable = true;
                var undrawable = new UndrawableLayer { Kind = $"photoshop:{adjustment.Key.TrimEnd()}" };
                return new Item(Common(undrawable, record, 0, record.BlendKey, rawChannels: true), record.Clipping != 0);
            }
            Layer layer = NexaflowContent(record, own) ?? new PixelLayer { Pixels = DecodedPixelsOf(record) };
            return new Item(Common(layer, record, 0, record.BlendKey), record.Clipping != 0);
        }

        /// <summary>A picture or markup layer as Nexaflow wrote it, where its pixels hash as they were written; else null.</summary>
        private Layer? NexaflowContent(PhotoshopLayerRecord record, JsonObject? own)
        {
            if (own is null || own["pixels"]?.GetValue<string>() is not { } hash || hash != PixelsHashOf(record)) return null;
            var placement = PlacementOf(own["placement"]?.GetValue<string>());
            switch (own["kind"]?.GetValue<string>())
            {
                case "picture" when own["source"]?.GetValue<string>() is { } source && _files.TryGetValue(source, out var bytes)
                                    && ImageRecipeJson.Deserialize(own["recipe"]?.GetValue<string>() ?? "") is { Status: RecipeReadStatus.Read, Recipe: { } recipe }:
                {
                    var file = Path.Combine(sourcesFolder, Path.GetFileName(source));
                    Sources[file] = bytes;
                    return new PictureLayer
                    {
                        Source = new PictureSource(file, null),
                        Recipe = RecipeRegionFiles.WithCoverages(recipe, f => _files.TryGetValue(f, out var png) ? RecipeRegionFiles.CoverageOf(png) : null),
                        Placement = placement ?? LayerPlacement.At(record.Rect.Left, record.Rect.Top),
                    };
                }
                case "markup" when ImageRecipeJson.DeserializeMarkup(own["markup"]?.GetValue<string>() ?? "") is { } elements:
                    return new MarkupLayer { Elements = elements, Placement = placement ?? LayerPlacement.Origin };
                default:
                    return null;
            }
        }

        /// <summary>
        /// What every layer reads from its record: name, opacity — its fill folded in where that draws the same — visibility,
        /// blend, locks, mask and identity; and what it keeps of the rest. A clip is said by the record and made by
        /// <see cref="Runs"/>.
        /// </summary>
        private Layer Common(Layer layer, PhotoshopLayerRecord record, uint section, string blendKey, bool rawChannels = false)
        {
            var own = OwnOf(record);
            var undrawn = ImmutableArray.CreateBuilder<string>();
            var blend = PhotoshopBlends.BlendOf(blendKey);
            if (own?["blend"]?.GetValue<string>() is { } nexaflowBlend) blend = new LayerBlend(nexaflowBlend);
            if (blend is null)
            {
                undrawn.Add($"blend {blendKey.TrimEnd()}");
                blend = LayerBlend.Normal;
            }

            byte fill = record.Block("iOpa") is { Data.Length: >= 1 } iOpa ? iOpa.Data.Span[0] : (byte)255;
            double opacity = record.Opacity / 255.0;
            if (fill != 255)
            {
                if (PhotoshopBlends.FillFoldsIntoOpacity(blendKey) && !record.Blocks.Any(b => EffectKeys.Contains(b.Key))) opacity *= fill / 255.0;
                else undrawn.Add("fill opacity");
            }
            if (record.Blocks.Any(b => EffectKeys.Contains(b.Key))) undrawn.Add("effects");
            if (record.Blocks.Any(b => VectorMaskKeys.Contains(b.Key))) undrawn.Add("vector mask");
            if (undrawn.Count > 0) HoldsUndrawable = true;

            uint locks = record.Block("lspf") is { Data.Length: >= 4 } lspf ? BinaryPrimitives.ReadUInt32BigEndian(lspf.Data.Span) : 0;
            var pixels = (layer as PixelLayer)?.Pixels;
            var kept = new PhotoshopLayerKept
            {
                Blocks = [.. record.Blocks.Where(b => !OwnKeys.Contains(b.Key) && !PixelKeys.Contains(b.Key))],
                PixelBlocks = [.. record.Blocks.Where(b => PixelKeys.Contains(b.Key))],
                ReadPixels = pixels,
                OpacityAndFill = (record.Opacity, fill, opacity),
                Mask = record.Mask,
                BlendingRanges = record.BlendingRanges,
                SectionType = section,
                BlendKey = PhotoshopBlends.BlendOf(blendKey) is null ? blendKey : null,
                Undrawable = rawChannels ? (record.Rect, [.. record.Channels.Select(c => new PhotoshopChannelToWrite(c.Id, RawChannel(c)))]) : null,
                Undrawn = undrawn.ToImmutable(),
            };

            var id = Guid.TryParse(own?["id"]?.GetValue<string>(), out var ownId) ? ownId : layer.Id;
            if (record.Block("lyid") is { Data.Length: >= 4 } lyid) _byLayerId[BinaryPrimitives.ReadUInt32BigEndian(lyid.Data.Span)] = id;
            return layer with
            {
                Id = id,
                Name = NameOf(record),
                Opacity = Math.Clamp(opacity, 0, 1),
                Visible = (record.Flags & PhotoshopLayerRecord.HiddenFlag) == 0,
                Blend = blend.Value,
                Locked = (locks & 0x80000006) != 0,
                AlphaLocked = (record.Flags & PhotoshopLayerRecord.TransparencyProtectedFlag) != 0 || (locks & 1) != 0,
                Mask = record.Mask is { } mask && record.Channels.Any(c => c.Id == -2) ? MaskOf(record, mask) : null,
                Kept = KeptFromFile.Of(kept),
            };
        }

        /// <summary>The layer the file had selected — by its id, or the old index — as Nexaflow's.</summary>
        private Guid? ActiveOf()
        {
            if (document.Resource(PhotoshopResource.LayerSelectionIds) is { Data.Length: >= 6 } selection)
            {
                var data = selection.Data.Span;
                if (_byLayerId.TryGetValue(BinaryPrimitives.ReadUInt32BigEndian(data[2..]), out var id)) return id;
            }
            return null;
        }

        // ── Pixels ──────────────────────────────────────────────────────────

        /// <summary>
        /// A layer's pixels in the picture's tile format, read a band of rows at a time, every channel in step: colour from
        /// channels 0 and up, transparency from -1 — wholly opaque where the record has none.
        /// </summary>
        private TiledPixels PixelsOf(Stream stream, PhotoshopLayerRecord record)
        {
            var rect = record.Rect;
            if (rect.IsEmpty) return TiledPixels.EmptyIn(_tiles);
            int colourChannels = colour.Model == ColourModel.Rgb ? 3 : 1, sampleBytes = _header.SampleBytes;
            var sources = new IEnumerator<byte[]>?[colourChannels + 1];
            for (int c = 0; c < sources.Length; c++)
            {
                short id = c < colourChannels ? (short)c : (short)-1;
                if (record.Channels.FirstOrDefault(ch => ch.Id == id) is { Length: > 0 } channel)
                    sources[c] = PhotoshopChannels.Rows(stream, channel.Offset, channel.Length, rect.Width, rect.Height, sampleBytes, _header.IsLarge).GetEnumerator();
            }
            try
            {
                var pixels = TiledPixels.EmptyIn(_tiles);
                int width = rect.Width, pixelBytes = _tiles.PixelBytes;
                var band = new byte[width * BandRows * pixelBytes];
                int bandTop = rect.Top;
                for (int y = rect.Top; y < rect.Bottom; y++)
                {
                    ct.ThrowIfCancellationRequested();
                    var line = band.AsSpan((y - bandTop) * width * pixelBytes, width * pixelBytes);
                    for (int c = 0; c < sources.Length; c++)
                    {
                        byte[]? row = sources[c] is { } source && source.MoveNext() ? source.Current : null;
                        Interleave(line, row, c, c == colourChannels, width, sampleBytes);
                    }
                    if (_tiles.Premultiplied) Premultiply(line);
                    bool bandEnds = y + 1 == rect.Bottom || ((y + 1) & (BandRows - 1)) == 0;
                    if (!bandEnds) continue;
                    var written = new SKRectI(rect.Left, bandTop, rect.Right, y + 1);
                    pixels = pixels.Written(written, band.AsSpan(0, written.Height * width * pixelBytes));
                    bandTop = y + 1;
                }
                return pixels;
            }
            finally
            {
                foreach (var source in sources) source?.Dispose();
            }
        }

        /// <summary>One channel's row laid into its place in each pixel of <paramref name="line"/> — a missing transparency as
        /// wholly opaque, a missing colour as 0.</summary>
        private void Interleave(Span<byte> line, byte[]? row, int channel, bool alpha, int width, int sampleBytes)
        {
            int pixelBytes = _tiles.PixelBytes, at = channel * sampleBytes;
            if (sampleBytes == 1)
            {
                // 8 bits, the commonest: a byte a sample, laid without slicing.
                byte missing = alpha ? (byte)0xFF : (byte)0;
                for (int x = 0, p = at; x < width; x++, p += pixelBytes) line[p] = row is null ? missing : row[x];
                return;
            }
            for (int x = 0; x < width; x++)
            {
                var into = line.Slice(x * pixelBytes + at, sampleBytes);
                if (row is not null) row.AsSpan(x * sampleBytes, sampleBytes).CopyTo(into);
                else if (!alpha) into.Clear();
                else if (sampleBytes == 4) BinaryPrimitives.WriteSingleLittleEndian(into, 1f);
                else into.Fill(0xFF);
            }
        }

        /// <summary>8-bit colour multiplied by its alpha, as an 8-bit tile holds it.</summary>
        private static void Premultiply(Span<byte> line)
        {
            for (int p = 0; p < line.Length; p += 4)
            {
                int a = line[p + 3];
                if (a == 255) continue;
                for (int c = 0; c < 3; c++) line[p + c] = (byte)((line[p + c] * a + 127) / 255);
            }
        }

        /// <summary>
        /// A layer's user mask over the canvas, as deep as the picture: its pixels where its rectangle is, its default colour
        /// everywhere else; what lies past the canvas left out.
        /// </summary>
        private LayerMask MaskOf(PhotoshopLayerRecord record, PhotoshopLayerMask mask)
        {
            var depth = colour.Depth;
            var coverage = mask.DefaultColour >= 128 ? TiledCoverage.Covered(_header.Width, _header.Height, depth)
                                                     : TiledCoverage.Clear(_header.Width, _header.Height, depth);
            var rect = mask.Rect;
            var channel = record.Channels.First(c => c.Id == -2);
            var canvas = new SKRectI(0, 0, _header.Width, _header.Height);
            if (!rect.IsEmpty && channel.Length > 2)
            {
                int width = rect.Width, sampleBytes = _header.SampleBytes;
                var values = new float[width];
                int y = rect.Top;
                foreach (var row in PhotoshopChannels.Rows(stream, channel.Offset, channel.Length, width, rect.Height, sampleBytes, _header.IsLarge))
                {
                    for (int x = 0; x < width; x++)
                        values[x] = sampleBytes switch
                        {
                            1 => row[x] / 255f,
                            2 => BinaryPrimitives.ReadUInt16LittleEndian(row.AsSpan(x * 2)) / 65535f,
                            _ => BinaryPrimitives.ReadSingleLittleEndian(row.AsSpan(x * 4)),
                        };
                    var line = SKRectI.Intersect(new SKRectI(rect.Left, y, rect.Right, y + 1), canvas);
                    if (!line.IsEmpty) coverage = coverage.WrittenValues(line, values.AsSpan(line.Left - rect.Left, line.Width));
                    y++;
                }
            }
            return new LayerMask(coverage, Enabled: !mask.Disabled);
        }

        /// <summary>A channel's bytes as the file holds them — compression first — to be written back unread.</summary>
        private byte[] RawChannel(PhotoshopChannelData channel)
        {
            var bytes = new byte[channel.Length];
            stream.Position = channel.Offset;
            stream.ReadExactly(bytes);
            return bytes;
        }

        /// <summary>The hash of a layer's colour and transparency channels as the file holds them — what Nexaflow wrote beside
        /// a picture or markup layer, so pixels changed since show.</summary>
        private string PixelsHashOf(PhotoshopLayerRecord record)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            if (_raw.TryGetValue(record, out var raw))
                foreach (var channel in raw.Record.Channels.Where(c => c.Id >= -1).OrderBy(c => c.Id)) hash.AppendData(raw.Bytes, (int)channel.Offset, (int)channel.Length);
            else
                foreach (var channel in record.Channels.Where(c => c.Id >= -1).OrderBy(c => c.Id)) hash.AppendData(RawChannel(channel));
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }

        /// <summary>Each layer's colour and transparency channels as the file holds them — read in one pass, the file front to
        /// back — and the record placing them in those bytes.</summary>
        private readonly Dictionary<PhotoshopLayerRecord, (byte[] Bytes, PhotoshopLayerRecord Record)> _raw = new(ReferenceEqualityComparer.Instance);

        /// <summary>Each pixel layer's pixels, decoded before the layers are built.</summary>
        private readonly Dictionary<PhotoshopLayerRecord, TiledPixels> _decoded = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// Every pixel layer's pixels: its channels' compressed bytes read from the file in one pass, then the layers decoded side by
        /// side across the cores, each from its own bytes. A picture or markup layer Nexaflow wrote, its pixels as written, is not
        /// decoded: it is drawn from what it describes.
        /// </summary>
        private void DecodePixels()
        {
            var pixelLayers = new List<PhotoshopLayerRecord>();
            foreach (var record in document.Layers)
            {
                if (SectionOf(record) != 0 || record.Blocks.Any(b => AdjustmentKeys.Contains(b.Key)) || OwnOf(record)?["role"] is not null) continue;
                var channels = record.Channels.Where(c => c.Id >= -1 && c.Length > 0).ToArray();
                var bytes = new byte[channels.Sum(c => c.Length)];
                var placed = new List<PhotoshopChannelData>(channels.Length);
                long at = 0;
                foreach (var channel in channels.OrderBy(c => c.Offset))
                {
                    stream.Position = channel.Offset;
                    stream.ReadExactly(bytes, (int)at, (int)channel.Length);
                    placed.Add(channel with { Offset = at });
                    at += channel.Length;
                }
                _raw[record] = (bytes, record with { Channels = [.. placed] });
                if (OwnOf(record) is { } own && own["kind"] is not null && own["pixels"]?.GetValue<string>() == PixelsHashOf(record)) continue;
                pixelLayers.Add(record);
            }
            var decoded = new TiledPixels[pixelLayers.Count];
            Parallel.For(0, pixelLayers.Count, new ParallelOptions { CancellationToken = ct }, i =>
            {
                var (bytes, placed) = _raw[pixelLayers[i]];
                using var own = new MemoryStream(bytes, writable: false);
                decoded[i] = PixelsOf(own, placed);
            });
            for (int i = 0; i < pixelLayers.Count; i++) _decoded[pixelLayers[i]] = decoded[i];
        }

        /// <summary>A layer's pixels as decoded side by side — or, one left undecoded as Nexaflow's own whose description cannot be
        /// read after all, decoded now from its bytes.</summary>
        private TiledPixels DecodedPixelsOf(PhotoshopLayerRecord record)
        {
            if (_decoded.TryGetValue(record, out var pixels)) return pixels;
            var (bytes, placed) = _raw[record];
            using var own = new MemoryStream(bytes, writable: false);
            return _decoded[record] = PixelsOf(own, placed);
        }
    }

    // ── Record fields ───────────────────────────────────────────────────────

    /// <summary>A record's section: 1 an open group, 2 a closed one, 3 the divider beneath a group's layers, 0 a layer.</summary>
    private static uint SectionOf(PhotoshopLayerRecord record)
        => (record.Block("lsct") ?? record.Block("lsdk")) is { Data.Length: >= 4 } section ? BinaryPrimitives.ReadUInt32BigEndian(section.Data.Span) : 0;

    /// <summary>The blend key a section block gives its group, where it gives one.</summary>
    private static string? SectionBlendOf(PhotoshopLayerRecord record)
        => (record.Block("lsct") ?? record.Block("lsdk")) is { Data.Length: >= 12 } section ? Encoding.Latin1.GetString(section.Data.Span[8..12]) : null;

    /// <summary>A record's name: its Unicode name where it has one, else its Pascal name.</summary>
    private static string NameOf(PhotoshopLayerRecord record)
    {
        if (record.Block("luni") is not { Data.Length: >= 4 } unicode) return record.PascalName;
        var data = unicode.Data.Span;
        int count = (int)BinaryPrimitives.ReadUInt32BigEndian(data);
        int bytes = Math.Min(count * 2, data.Length - 4);
        return Encoding.BigEndianUnicode.GetString(data.Slice(4, bytes)).TrimEnd('\0');
    }

    /// <summary>Nexaflow's own block on a record, as JSON; null where it has none.</summary>
    private static JsonObject? OwnOf(PhotoshopLayerRecord record)
    {
        if (record.Block(LayerBlockKey) is not { } block) return null;
        try
        {
            return JsonNode.Parse(Encoding.UTF8.GetString(block.Data.Span).TrimEnd('\0')) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The files Nexaflow's own block on the file holds, by name: a count, then each name and its bytes.</summary>
    private static Dictionary<string, byte[]> FilesOf(ImmutableArray<PhotoshopTaggedBlock> blocks)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (blocks.FirstOrDefault(b => b.Key == FilesBlockKey) is not { } block) return files;
        var data = block.Data.Span;
        if (data.Length < 4) return files;
        int count = (int)BinaryPrimitives.ReadUInt32BigEndian(data), at = 4;
        for (int i = 0; i < count && at + 6 <= data.Length; i++)
        {
            int nameLength = BinaryPrimitives.ReadUInt16BigEndian(data[at..]);
            at += 2;
            if (at + nameLength + 4 > data.Length) break;
            var name = Encoding.UTF8.GetString(data.Slice(at, nameLength));
            at += nameLength;
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(data[at..]);
            at += 4;
            if (at + length > data.Length) break;
            files[name] = data.Slice(at, length).ToArray();
            at += length;
        }
        return files;
    }

    /// <summary>A placement as <c>Nxly</c> keeps it — its six terms — or null where it is not one.</summary>
    private static LayerPlacement? PlacementOf(string? text)
    {
        var parts = text?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts is not { Length: 6 }) return null;
        var values = parts.Select(p => double.TryParse(p, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : double.NaN).ToArray();
        return values.Any(v => !double.IsFinite(v)) ? null : new LayerPlacement(values[0], values[1], values[2], values[3], values[4], values[5]);
    }

    private static string PlacementText(LayerPlacement p)
        => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{p.ScaleX:R} {p.SkewX:R} {p.OffsetX:R} {p.SkewY:R} {p.ScaleY:R} {p.OffsetY:R}");
}
