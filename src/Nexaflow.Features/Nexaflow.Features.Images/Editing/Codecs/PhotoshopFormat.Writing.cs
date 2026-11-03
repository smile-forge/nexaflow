using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Media.Photoshop;
using SkiaSharp;
using System.Buffers;

namespace Nexaflow.Features.Images.Editing.Codecs;

public sealed partial class PhotoshopFormat
{
    /// <summary>Each pixel layer's channels as last written, by its pixels: a save after an edit compresses only the layers
    /// it changed, every other layer's tiles being the very ones written before.</summary>
    private readonly ConditionalWeakTable<TiledPixels, EncodedPixels> _encoded = new();

    /// <summary>A layer's pixels as written: where they lie, whether as a PSB, each channel compressed, and their hash.</summary>
    private sealed record EncodedPixels(SKRectI Rect, bool Large, ImmutableArray<PhotoshopChannelToWrite> Channels)
    {
        private string? _hash;

        /// <summary>The channels' SHA-256 in channel order — what a picture or markup layer's private block names its pixels by.</summary>
        public string Hash => _hash ??= HashOf(Channels);

        private static string HashOf(ImmutableArray<PhotoshopChannelToWrite> channels)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var channel in channels.OrderBy(c => c.Id)) hash.AppendData(channel.Encoded);
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
    }

    public async Task WriteAsync(LayeredPicture picture, string path, ILayeredPictureRenderer renderer, CancellationToken ct = default)
    {
        // Each picture layer's source, the original file as it was, read before anything is written: the file being replaced may hold it.
        var sources = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in picture.All.OfType<PictureLayer>())
            if (!sources.ContainsKey(layer.Source.Path)) sources[layer.Source.Path] = await FileSystem.ReadAllBytesAsync(layer.Source.Path, ct);
        using var writing = new Writing(this, picture, renderer, sources, ct);
        var document = await Task.Run(writing.Document, ct);
        await using var write = await FileSystem.BeginWriteAsync(path, WriteMode.Replace, null, ct);
        await Task.Run(() => PhotoshopWriter.Write(write.Content, document), ct);
        await write.CommitAsync(ct);
    }

    /// <summary>One file being written: its layers as records, Nexaflow's files, and the merged image, held until it is written.</summary>
    private sealed class Writing(PhotoshopFormat format, LayeredPicture picture, ILayeredPictureRenderer renderer,
                                 IReadOnlyDictionary<string, byte[]> sources, CancellationToken ct) : IDisposable
    {
        private readonly TileFormat _tiles = picture.Colour.Tiles;
        private readonly int _sampleBytes = picture.Colour.Depth switch { ChannelDepth.U8 => 1, ChannelDepth.U16 => 2, _ => 4 };
        private readonly int _colourChannels = picture.Colour.Model == ColourModel.Rgb ? 3 : 1;
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
        private SKBitmap? _merged;
        private uint _nextId;
        private uint? _activeId;

        // The clipping runs written, by the layer heading each: the run's identity, kept on its head's record.
        private readonly Dictionary<Guid, Guid> _runs = [];

        // The pixel layers still as a file was read, by their tiles: their channels as it compressed them, written as they are.
        private readonly Dictionary<TiledPixels, EncodedPixels> _asRead = AsReadOf(picture, format.Extension == PsbExtension);

        private static Dictionary<TiledPixels, EncodedPixels> AsReadOf(LayeredPicture picture, bool large)
        {
            var asRead = new Dictionary<TiledPixels, EncodedPixels>(ReferenceEqualityComparer.Instance);
            foreach (var layer in picture.All.OfType<PixelLayer>())
                if (layer.Kept.Get<PhotoshopLayerKept>() is { ReadPixels: { } read, ReadChannels: { } channels } && ReferenceEquals(read, layer.Pixels) && channels.Large == large)
                    asRead[read] = new EncodedPixels(new SKRectI(channels.Rect.Left, channels.Rect.Top, channels.Rect.Right, channels.Rect.Bottom), large, channels.Channels);
            return asRead;
        }

        private bool Large => format.Extension == PsbExtension;

        public PhotoshopDocumentToWrite Document()
        {
            // Every pixel layer's channels compressed side by side first; the records then take them as they are written.
            Parallel.ForEach(picture.All.OfType<PixelLayer>().Select(l => l.Pixels).Distinct(ReferenceEqualityComparer.Instance).Cast<TiledPixels>(),
                             new ParallelOptions { CancellationToken = ct }, pixels => Encoded(pixels));
            var records = new List<PhotoshopLayerToWrite>();
            if (!picture.Background.IsTransparent) records.Add(BackgroundRecord());
            foreach (var layer in picture.Layers) records.AddRange(Records(layer));

            var kept = picture.Kept.Get<PhotoshopPictureKept>();
            var blocks = (kept?.Blocks ?? []).ToList();
            if (_files.Count > 0) blocks.Add(PhotoshopTaggedBlock.Of(FilesBlockKey, FilesData()));

            _merged = MergedBitmap();
            var merged = _merged;
            int width = picture.Width, colourChannels = _colourChannels, sampleBytes = _sampleBytes, pixelBytes = 4 * _sampleBytes;
            return new PhotoshopDocumentToWrite
            {
                Header = new PhotoshopFileHeader(Large, colourChannels + 1, picture.Width, picture.Height, picture.Colour.Bits,
                                                 picture.Colour.Model == ColourModel.Rgb ? PhotoshopColourMode.Rgb : PhotoshopColourMode.Grayscale),
                ColourModeData = kept?.ColourModeData ?? ReadOnlyMemory<byte>.Empty,
                Resources = [.. kept?.Resources ?? [], .. Resources()],
                Layers = [.. records],
                MergedHasTransparency = true,
                GlobalLayerMask = kept?.GlobalLayerMask ?? ReadOnlyMemory<byte>.Empty,
                GlobalBlocks = [.. blocks],
                MergedChannels = colourChannels + 1,
                // Colour, then transparency: channel c of each pixel's red, green, blue and alpha — a grey picture's grey its red —
                // colour composited over white where it is partly transparent, as Photoshop writes a merged image.
                Merged = (channel, y, into) =>
                {
                    var row = merged.GetPixelSpan().Slice(y * merged.RowBytes, width * pixelBytes);
                    bool isColour = channel < colourChannels;
                    int at = (isColour ? channel : 3) * sampleBytes, alphaAt = 3 * sampleBytes;
                    for (int x = 0; x < width; x++)
                    {
                        var pixel = row.Slice(x * pixelBytes, pixelBytes);
                        var target = into.Slice(x * sampleBytes, sampleBytes);
                        if (!isColour)
                        {
                            pixel.Slice(at, sampleBytes).CopyTo(target);
                            continue;
                        }
                        switch (sampleBytes)
                        {
                            case 1:
                                target[0] = (byte)Math.Round(PhotoshopDecoder.Matted(pixel[at] / 255f, pixel[alphaAt] / 255f) * 255);
                                break;
                            case 2:
                                BinaryPrimitives.WriteUInt16LittleEndian(target, (ushort)Math.Round(PhotoshopDecoder.Matted(
                                    BinaryPrimitives.ReadUInt16LittleEndian(pixel[at..]) / 65535f, BinaryPrimitives.ReadUInt16LittleEndian(pixel[alphaAt..]) / 65535f) * 65535));
                                break;
                            default:
                                BinaryPrimitives.WriteSingleLittleEndian(target, PhotoshopDecoder.Matted(
                                    BinaryPrimitives.ReadSingleLittleEndian(pixel[at..]), BinaryPrimitives.ReadSingleLittleEndian(pixel[alphaAt..])));
                                break;
                        }
                    }
                },
            };
        }

        public void Dispose() => _merged?.Dispose();

        // ── Layers ──────────────────────────────────────────────────────────

        /// <summary>A layer as its records, bottom first: a group as its divider, its layers and itself; a clipping run as its
        /// base and the layers clipped to it.</summary>
        private IEnumerable<PhotoshopLayerToWrite> Records(Layer layer, double? fill = null)
        {
            ct.ThrowIfCancellationRequested();
            switch (layer)
            {
                case GroupLayer run when IsClipRun(run):
                {
                    var head = run.Children[0];
                    _runs[head.Id] = run.Id;
                    var records = Records(head with { Name = run.Name, Opacity = run.Opacity, Blend = run.Blend, Visible = run.Visible }, fill: head.Opacity).ToList();
                    foreach (var clipped in run.Children.Skip(1))
                    {
                        var inner = Records(clipped with { ClipsToBelow = false }).ToList();
                        inner[^1] = inner[^1] with { Clipping = 1 };
                        records.AddRange(inner);
                    }
                    return records;
                }
                case GroupLayer group:
                {
                    var divider = new PhotoshopLayerToWrite
                    {
                        PascalName = "</Layer group>", Channels = EmptyChannels(), Blocks = [PhotoshopTaggedBlock.Of("lsct", Section(3, PhotoshopBlends.Normal))],
                    };
                    var children = group.Children.SelectMany(c => Records(c));
                    var kept = group.Kept.Get<PhotoshopLayerKept>();
                    string key = group.Isolated ? BlendKeyOf(group, out _) : PhotoshopBlends.PassThrough;
                    var own = Own(group);
                    if (!group.Isolated) own.Remove("blend");
                    var record = Record(group, default, EmptyChannels(), key, own, fill, section: kept?.SectionType is 2 ? 2u : 1u);
                    return [divider, .. children, record];
                }
                case PixelLayer pixels:
                {
                    var encoded = Encoded(pixels.Pixels);
                    var kept = pixels.Kept.Get<PhotoshopLayerKept>();
                    var live = kept is { ReadPixels: { } read } && ReferenceEquals(read, pixels.Pixels) ? kept.PixelBlocks : [];
                    return [Record(pixels, encoded.Rect, encoded.Channels, BlendKeyOf(pixels, out _), Own(pixels), fill, pixelBlocks: live)];
                }
                case PictureLayer or MarkupLayer:
                {
                    var rendered = renderer.RenderLayer(layer);
                    TiledPixels drawn;
                    using (rendered?.Pixels)
                        drawn = rendered is { } r ? TiledPixels.FromBitmap(r.Pixels, r.At.X, r.At.Y, _tiles) : TiledPixels.EmptyIn(_tiles);
                    var encoded = Encoded(drawn);
                    var own = Own(layer);
                    own["pixels"] = encoded.Hash;
                    own["kind"] = layer is PictureLayer ? "picture" : "markup";
                    if (layer is PictureLayer picturesLayer)
                    {
                        own["placement"] = PlacementText(picturesLayer.Placement);
                        var source = sources[picturesLayer.Source.Path];
                        var extension = Path.GetExtension(picturesLayer.Source.Path);
                        var name = $"data/nexaflow/source-{Convert.ToHexStringLower(SHA256.HashData(source))[..16]}{(extension.Length > 0 ? extension : ".bin")}";
                        _files[name] = source;
                        own["source"] = name;
                        var recipe = RecipeRegionFiles.WithCoverageFiles(picturesLayer.Recipe, (file, png) => _files[file] = png);
                        own["recipe"] = ImageRecipeJson.Serialize(recipe);
                    }
                    else if (layer is MarkupLayer markup)
                    {
                        own["placement"] = PlacementText(markup.Placement);
                        own["markup"] = ImageRecipeJson.SerializeMarkup(markup.Elements);
                    }
                    return [Record(layer, encoded.Rect, encoded.Channels, BlendKeyOf(layer, out _), own, fill)];
                }
                case UndrawableLayer undrawable when undrawable.Kept.Get<PhotoshopLayerKept>() is { Undrawable: { } raw } kept:
                {
                    // As read: its rectangle, its channels as compressed, its mask record and every block — a shape's outline
                    // too, since its channels are the very ones it describes.
                    var record = Record(undrawable, new SKRectI(raw.Rect.Left, raw.Rect.Top, raw.Rect.Right, raw.Rect.Bottom), raw.Channels,
                                        kept.BlendKey ?? BlendKeyOf(undrawable, out _), Own(undrawable), pixelBlocks: kept.PixelBlocks, keepMask: true);
                    return [record];
                }
                default:
                    // A layer another format could not draw either — a Krita filter — kept as a hidden, empty layer of its name.
                    return [Record(layer with { Visible = false }, default, EmptyChannels(), PhotoshopBlends.Normal, Own(layer))];
            }
        }

        /// <summary>A group that stands for a Photoshop clipping run, still shaped as one: a base, every other layer clipped to it.</summary>
        private static bool IsClipRun(GroupLayer group)
            => group.Kept.Get<PhotoshopClipRun>() is not null && group.Isolated && group.Mask is null && !group.ClipsToBelow
               && group.Children.Length >= 2 && !group.Children[0].ClipsToBelow && group.Children.Skip(1).All(c => c.ClipsToBelow);

        /// <summary>
        /// The record every layer shares: its name, opacity — and fill, as read where neither changed — visibility, blend,
        /// locks, mask, its identity among the file's layers, and what it kept; with Nexaflow's own block beside them.
        /// </summary>
        private PhotoshopLayerToWrite Record(Layer layer, SKRectI rect, ImmutableArray<PhotoshopChannelToWrite> channels, string blendKey, JsonObject own,
                                             double? fill = null, uint section = 0, ImmutableArray<PhotoshopTaggedBlock> pixelBlocks = default, bool keepMask = false)
        {
            var kept = layer.Kept.Get<PhotoshopLayerKept>();
            byte opacity = ToByte(layer.Opacity), fillByte = fill is { } f ? ToByte(f) : (byte)255;
            if (fill is null && kept?.OpacityAndFill is { } written && written.ReadAs == layer.Opacity) (opacity, fillByte) = (written.Opacity, written.Fill);

            uint id = ++_nextId;
            if (picture.ActiveId == layer.Id) _activeId = id;
            var blocks = new List<PhotoshopTaggedBlock> { PhotoshopTaggedBlock.Of("luni", Unicode(layer.Name)), PhotoshopTaggedBlock.Of("lyid", UInt32(id)) };
            if (section != 0) blocks.Add(PhotoshopTaggedBlock.Of("lsct", Section(section, blendKey)));
            if (fillByte != 255) blocks.Add(PhotoshopTaggedBlock.Of("iOpa", new byte[] { fillByte, 0, 0, 0 }));
            if (layer.Locked || layer.AlphaLocked) blocks.Add(PhotoshopTaggedBlock.Of("lspf", UInt32((layer.Locked ? 0x80000000u : 0) | (layer.AlphaLocked ? 1u : 0))));
            if (kept is not null) blocks.AddRange(kept.Blocks);
            if (!pixelBlocks.IsDefaultOrEmpty) blocks.AddRange(pixelBlocks);
            if (_runs.TryGetValue(layer.Id, out var runId)) own["run"] = runId.ToString("D");
            blocks.Add(PhotoshopTaggedBlock.Of(LayerBlockKey, Encoding.UTF8.GetBytes(own.ToJsonString())));

            PhotoshopLayerMask? maskRecord = null;
            var allChannels = channels;
            if (keepMask) maskRecord = kept?.Mask;
            else if (layer.Mask is { } mask)
            {
                var (record, channel) = MaskOf(mask, kept?.Mask);
                maskRecord = record;
                allChannels = allChannels.Add(channel);
            }

            return new PhotoshopLayerToWrite
            {
                Rect = new PhotoshopRect(rect.Top, rect.Left, rect.Bottom, rect.Right),
                Channels = allChannels,
                BlendKey = blendKey,
                Opacity = opacity,
                Clipping = (byte)(layer.ClipsToBelow ? 1 : 0),
                Flags = (byte)((layer.Visible ? 0 : PhotoshopLayerRecord.HiddenFlag) | (layer.AlphaLocked ? PhotoshopLayerRecord.TransparencyProtectedFlag : 0)),
                Mask = maskRecord,
                BlendingRanges = kept?.BlendingRanges ?? ReadOnlyMemory<byte>.Empty,
                PascalName = layer.Name,
                Blocks = [.. blocks],
            };
        }

        /// <summary>The blend key a layer is written under: Photoshop's for its blend; the key read, for one Nexaflow knows
        /// not; else Normal — <paramref name="unwritten"/> then true, its own blend kept in Nexaflow's block.</summary>
        private static string BlendKeyOf(Layer layer, out bool unwritten)
        {
            unwritten = false;
            if (PhotoshopBlends.KeyOf(layer.Blend) is { } key) return key;
            if (layer.Kept.Get<PhotoshopLayerKept>()?.BlendKey is { } read) return read;
            unwritten = true;
            return PhotoshopBlends.Normal;
        }

        /// <summary>Nexaflow's own block for a layer: its identity, and its blend where Photoshop has none of it.</summary>
        private static JsonObject Own(Layer layer)
        {
            var own = new JsonObject { ["id"] = layer.Id.ToString("D") };
            BlendKeyOf(layer, out bool unwritten);
            if (unwritten) own["blend"] = layer.Blend.Op;
            return own;
        }

        /// <summary>The picture's background, which Photoshop has no place for, as its bottom layer: filled with the colour,
        /// locked, and marked so Nexaflow reads it back as the background.</summary>
        private PhotoshopLayerToWrite BackgroundRecord()
        {
            var fill = TiledPixels.EmptyIn(_tiles).Filled(TiledCoverage.Covered(picture.Width, picture.Height, picture.Colour.Depth),
                                                          picture.Colour.FromSrgb(picture.Background.Colour));
            var encoded = Encoded(fill);
            var own = new JsonObject { ["role"] = "background", ["colour"] = picture.Background.Argb.ToString("X8") };
            var background = new PixelLayer { Name = "Background", Locked = true, Pixels = fill };
            return Record(background, encoded.Rect, encoded.Channels, PhotoshopBlends.Normal, own);
        }

        // ── Pixels ──────────────────────────────────────────────────────────

        /// <summary>
        /// A layer's pixels as channels — transparency, then colour — each compressed: the tiles read a band of rows at a time, and
        /// each band's rows handed to every channel's compressor side by side, not premultiplied — so only a band and what is
        /// compressed are ever held. The tiles a file was read as, its channels as it compressed them; the same tiles as last time,
        /// their channels as last time.
        /// </summary>
        private EncodedPixels Encoded(TiledPixels pixels)
        {
        if (_asRead.TryGetValue(pixels, out var asRead)) return asRead;
            if (format._encoded.TryGetValue(pixels, out var cached) && cached.Large == Large) return cached;
            var rect = pixels.Bounds;
            int width = rect.Width, sampleBytes = _sampleBytes, pixelBytes = _tiles.PixelBytes;
            var ids = new short[_colourChannels + 1];
            ids[0] = -1;
            for (int c = 0; c < _colourChannels; c++) ids[c + 1] = (short)c;
            var encoders = ids.Select(_ => new PhotoshopChannelEncoder(width, rect.Height, sampleBytes, Large)).ToArray();
            var lines = ids.Select(_ => new byte[width * sampleBytes]).ToArray();
            // One band's pixels at a time, in a buffer lent for the layer rather than one made for every band.
            var band = ArrayPool<byte>.Shared.Rent(width * BandRows * pixelBytes);
            try
            {
                for (int top = rect.Top; top < rect.Bottom; top += BandRows - (top & (BandRows - 1)))
                {
                    int rows = Math.Min(BandRows - (top & (BandRows - 1)), rect.Bottom - top);
                    pixels.ReadInto(new SKRectI(rect.Left, top, rect.Right, top + rows), band.AsSpan(0, rows * width * pixelBytes));
                    Parallel.For(0, ids.Length, i =>
                    {
                        int slot = ids[i] < 0 ? _colourChannels : ids[i];
                        var line = lines[i];
                        for (int r = 0; r < rows; r++)
                        {
                            var pixelsOfRow = band.AsSpan(r * width * pixelBytes, width * pixelBytes);
                            ChannelOf(pixelsOfRow, slot, line);
                            encoders[i].Add(line);
                        }
                    });
                }
                var channels = ImmutableArray.CreateRange(ids.Select((id, i) => new PhotoshopChannelToWrite(id, encoders[i].Finish())));
                var result = new EncodedPixels(rect, Large, channels);
                format._encoded.AddOrUpdate(pixels, result);
                return result;
            }
            finally
            {
                foreach (var encoder in encoders) encoder.Dispose();
                ArrayPool<byte>.Shared.Return(band);
            }
        }

        /// <summary>One channel of a row of pixels — <paramref name="slot"/> among each pixel's — as its samples, in the machine's
        /// order and not premultiplied: 8-bit colour, held premultiplied, divided by its alpha.</summary>
        private void ChannelOf(ReadOnlySpan<byte> row, int slot, Span<byte> into)
        {
            int sampleBytes = _sampleBytes, pixelBytes = _tiles.PixelBytes, width = into.Length / sampleBytes;
            if (_tiles.Premultiplied)
            {
                for (int x = 0, p = 0; x < width; x++, p += 4)
                {
                    int a = row[p + 3];
                    into[x] = slot == 3 ? (byte)a : TileFormat.StraightByte(row[p + slot], a);
                }
                return;
            }
            for (int x = 0, p = slot * sampleBytes, o = 0; x < width; x++, p += pixelBytes)
                for (int b = 0; b < sampleBytes; b++) into[o++] = row[p + b];
        }

        private ImmutableArray<PhotoshopChannelToWrite> EmptyChannels()
        {
            var channels = ImmutableArray.CreateBuilder<PhotoshopChannelToWrite>(_colourChannels + 1);
            channels.Add(new PhotoshopChannelToWrite(-1, [0, 0]));
            for (short c = 0; c < _colourChannels; c++) channels.Add(new PhotoshopChannelToWrite(c, [0, 0]));
            return channels.MoveToImmutable();
        }

        /// <summary>
        /// A mask as its record and its channel: its pixels over the tiles it holds — the whole canvas where what lies outside
        /// them is neither shown nor hidden, which a Photoshop mask's default colour cannot say — its default colour the rest.
        /// </summary>
        private (PhotoshopLayerMask Record, PhotoshopChannelToWrite Channel) MaskOf(LayerMask mask, PhotoshopLayerMask? read)
        {
            var coverage = mask.Coverage;
            float outside = coverage.OutsideValue;
            bool uniform = outside is 0 or 1;
            var rect = uniform ? coverage.HeldBounds : new SKRectI(0, 0, coverage.Width, coverage.Height);
            byte flags = (byte)(((read?.Flags ?? 0) & PhotoshopLayerMask.InvertFlag) | (mask.Enabled ? 0 : PhotoshopLayerMask.DisabledFlag));
            var record = new PhotoshopLayerMask(new PhotoshopRect(rect.Top, rect.Left, rect.Bottom, rect.Right), (byte)(outside >= 0.5f ? 255 : 0), flags) { Rest = new byte[2] };
            int sampleBytes = _sampleBytes, width = rect.Width;
            var encoded = PhotoshopChannels.Encode(width, rect.Height, sampleBytes, Large, (y, into) =>
            {
                var values = coverage.ReadValues(new SKRectI(rect.Left, rect.Top + y, rect.Right, rect.Top + y + 1));
                for (int x = 0; x < width; x++)
                {
                    float v = values[x];
                    switch (sampleBytes)
                    {
                        case 1: into[x] = (byte)Math.Clamp((int)MathF.Round(v * 255), 0, 255); break;
                        case 2: BinaryPrimitives.WriteUInt16LittleEndian(into[(x * 2)..], (ushort)Math.Clamp((int)MathF.Round(v * 65535), 0, 65535)); break;
                        default: BinaryPrimitives.WriteSingleLittleEndian(into[(x * 4)..], v); break;
                    }
                }
            });
            return (record, new PhotoshopChannelToWrite(-2, encoded));
        }

        /// <summary>The whole picture composited, as straight colour and alpha at its depth, row by row.</summary>
        private SKBitmap MergedBitmap()
        {
            using var composite = renderer.Composite(1);
            var colourType = picture.Colour.Depth switch { ChannelDepth.U8 => SKColorType.Rgba8888, ChannelDepth.U16 => SKColorType.Rgba16161616, _ => SKColorType.RgbaF32 };
            var info = new SKImageInfo(picture.Width, picture.Height, colourType, SKAlphaType.Unpremul, picture.Colour.ColourSpace);
            var bitmap = new SKBitmap(info);
            if (!composite.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
            {
                bitmap.Dispose();
                throw new InvalidOperationException("The picture could not be composited to be saved.");
            }
            return bitmap;
        }

        // ── Resources and blocks ────────────────────────────────────────────

        private IEnumerable<PhotoshopResource> Resources()
        {
            var resolution = new byte[16];
            uint fixedDpi = (uint)Math.Round(picture.Dpi * 65536);
            BinaryPrimitives.WriteUInt32BigEndian(resolution, fixedDpi);
            BinaryPrimitives.WriteUInt16BigEndian(resolution.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt16BigEndian(resolution.AsSpan(6), 1);
            BinaryPrimitives.WriteUInt32BigEndian(resolution.AsSpan(8), fixedDpi);
            BinaryPrimitives.WriteUInt16BigEndian(resolution.AsSpan(12), 1);
            BinaryPrimitives.WriteUInt16BigEndian(resolution.AsSpan(14), 1);
            yield return new PhotoshopResource(PhotoshopResource.ResolutionInfo, "", resolution);
            if (_activeId is { } active)
            {
                var selection = new byte[6];
                BinaryPrimitives.WriteUInt16BigEndian(selection, 1);
                BinaryPrimitives.WriteUInt32BigEndian(selection.AsSpan(2), active);
                yield return new PhotoshopResource(PhotoshopResource.LayerSelectionIds, "", selection);
            }
            if (!picture.Profile.IsEmpty) yield return new PhotoshopResource(PhotoshopResource.IccProfile, "", picture.Profile.ToArray());
        }

        /// <summary>Nexaflow's files as its block holds them: a count, then each name and its bytes.</summary>
        private byte[] FilesData()
        {
            using var data = new MemoryStream();
            Span<byte> number = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(number, (uint)_files.Count);
            data.Write(number);
            foreach (var (name, bytes) in _files)
            {
                var nameBytes = Encoding.UTF8.GetBytes(name);
                BinaryPrimitives.WriteUInt16BigEndian(number, (ushort)nameBytes.Length);
                data.Write(number[..2]);
                data.Write(nameBytes);
                BinaryPrimitives.WriteUInt32BigEndian(number, (uint)bytes.Length);
                data.Write(number);
                data.Write(bytes);
            }
            return data.ToArray();
        }

        private static byte ToByte(double unit) => (byte)Math.Clamp((int)Math.Round(unit * 255), 0, 255);

        private static byte[] UInt32(uint value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
            return bytes;
        }

        /// <summary>A name as <c>luni</c> holds it: its length in characters, then UTF-16, big-endian.</summary>
        private static byte[] Unicode(string name)
        {
            var text = Encoding.BigEndianUnicode.GetBytes(name);
            var bytes = new byte[4 + text.Length];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)name.Length);
            text.CopyTo(bytes, 4);
            return bytes;
        }

        /// <summary>A section block: its type, then the group's blend key.</summary>
        private static byte[] Section(uint type, string blendKey)
        {
            var bytes = new byte[12];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, type);
            "8BIM"u8.CopyTo(bytes.AsSpan(4));
            Encoding.Latin1.GetBytes(blendKey.PadRight(4)[..4]).CopyTo(bytes, 8);
            return bytes;
        }
    }
}
