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

namespace Nexaflow.Features.Images.Editing.Codecs;

public sealed partial class PhotoshopFormat
{
    /// <summary>Each pixel layer's channels as last written, by its pixels: a save after an edit compresses only the layers
    /// it changed, every other layer's tiles being the very ones written before.</summary>
    private readonly ConditionalWeakTable<TiledPixels, EncodedPixels> _encoded = new();

    /// <summary>A layer's pixels as written: where they lie, whether as a PSB, each channel compressed, and their hash.</summary>
    private sealed record EncodedPixels(SKRectI Rect, bool Large, ImmutableArray<PhotoshopChannelToWrite> Channels, string Hash);

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

        private bool Large => format.Extension == PsbExtension;

        public PhotoshopDocumentToWrite Document()
        {
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
                // Colour, then transparency: channel c of each pixel's red, green, blue and alpha — a grey picture's grey its red.
                Merged = (channel, y, into) =>
                {
                    var row = merged.GetPixelSpan().Slice(y * merged.RowBytes, width * pixelBytes);
                    int at = (channel < colourChannels ? channel : 3) * sampleBytes;
                    for (int x = 0; x < width; x++) row.Slice(x * pixelBytes + at, sampleBytes).CopyTo(into[(x * sampleBytes)..]);
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
                    // As read: its rectangle, its channels as compressed, its mask record and every block.
                    var record = Record(undrawable, new SKRectI(raw.Rect.Left, raw.Rect.Top, raw.Rect.Right, raw.Rect.Bottom), raw.Channels,
                                        kept.BlendKey ?? BlendKeyOf(undrawable, out _), Own(undrawable), keepMask: true);
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

        /// <summary>A layer's pixels as channels — transparency, then colour — each compressed; the same tiles as last time,
        /// their channels as last time.</summary>
        private EncodedPixels Encoded(TiledPixels pixels)
        {
            if (format._encoded.TryGetValue(pixels, out var cached) && cached.Large == Large) return cached;
            var rect = pixels.Bounds;
            var ids = new short[_colourChannels + 1];
            ids[0] = -1;
            for (int c = 0; c < _colourChannels; c++) ids[c + 1] = (short)c;
            var channels = ImmutableArray.CreateBuilder<PhotoshopChannelToWrite>(ids.Length);
            var bands = new Bands(pixels, rect, _tiles);
            foreach (short id in ids)
            {
                int slot = id < 0 ? _colourChannels : id;
                channels.Add(new PhotoshopChannelToWrite(id, PhotoshopChannels.Encode(rect.Width, rect.Height, _sampleBytes, Large,
                    (y, into) => bands.Channel(y, slot, _sampleBytes, into))));
            }
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var channel in channels.OrderBy(c => c.Id)) hash.AppendData(channel.Encoded);
            var encoded = new EncodedPixels(rect, Large, channels.MoveToImmutable(), Convert.ToHexStringLower(hash.GetHashAndReset()));
            format._encoded.AddOrUpdate(pixels, encoded);
            return encoded;
        }

        /// <summary>A layer's pixels read a band of rows at a time, each row's channel handed out not premultiplied.</summary>
        private sealed class Bands(TiledPixels pixels, SKRectI rect, TileFormat tiles)
        {
            private byte[] _band = [];
            private int _top = int.MinValue, _rows;

            public void Channel(int y, int slot, int sampleBytes, Span<byte> into)
            {
                int row = rect.Top + y;
                if (row < _top || row >= _top + _rows)
                {
                    _top = row;
                    _rows = Math.Min(BandRows - (row & (BandRows - 1)), rect.Bottom - row);
                    _band = pixels.Read(new SKRectI(rect.Left, _top, rect.Right, _top + _rows));
                }
                int width = rect.Width, pixelBytes = tiles.PixelBytes;
                var line = _band.AsSpan((row - _top) * width * pixelBytes, width * pixelBytes);
                for (int x = 0; x < width; x++)
                {
                    var pixel = line.Slice(x * pixelBytes, pixelBytes);
                    if (!tiles.Premultiplied)
                    {
                        pixel.Slice(slot * sampleBytes, sampleBytes).CopyTo(into[(x * sampleBytes)..]);
                        continue;
                    }
                    // 8-bit colour is held premultiplied; the file holds it straight.
                    int a = pixel[3];
                    into[x] = slot == 3 ? (byte)a : a == 0 ? (byte)0 : (byte)Math.Min(255, (pixel[slot] * 255 + a / 2) / a);
                }
            }
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
