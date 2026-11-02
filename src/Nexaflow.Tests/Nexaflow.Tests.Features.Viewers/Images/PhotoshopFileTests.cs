using System.IO;
using System.Runtime.InteropServices;
using Nexaflow.Media.Photoshop;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// The Photoshop file itself, below what its layers mean: PackBits and every channel compression back to the samples they
/// held at 8, 16 and 32 bits; a file written and read back — PSD and PSB, layers in the layer info or in <c>Lr16</c> —
/// carrying every field of every record, every tagged block and resource byte for byte, and the merged image.
/// </summary>
[TestClass]
[CoversNode("images-psd")]
public class PhotoshopFileTests
{
    private static byte[] Samples(int count, int sampleBytes, int seed)
    {
        var random = new Random(seed);
        var bytes = new byte[count * sampleBytes];
        if (sampleBytes == 4)
        {
            var floats = MemoryMarshal.Cast<byte, float>(bytes.AsSpan());
            for (int i = 0; i < floats.Length; i++) floats[i] = (float)(random.NextDouble() * 6 - 1);
            return bytes;
        }
        random.NextBytes(bytes);
        // Runs, so PackBits has something to repeat.
        for (int i = 0; i < bytes.Length / 3; i++) bytes[i] = 7;
        return bytes;
    }

    [TestMethod]
    public void PackBits_RoundTripsRunsAndLiterals_OfEveryLength()
    {
        var random = new Random(1);
        foreach (int length in new[] { 1, 2, 3, 127, 128, 129, 300, 1000 })
        {
            var row = new byte[length];
            random.NextBytes(row);
            for (int i = 0; i < length / 2; i++) row[i] = 0xAA;
            var back = new byte[length];
            PhotoshopChannels.UnpackBits(PhotoshopChannels.PackBits(row), back);
            CollectionAssert.AreEqual(row, back, $"a row of {length}");
        }
    }

    [TestMethod]
    [DataRow(1, false)]
    [DataRow(2, false)]
    [DataRow(4, false)]
    [DataRow(1, true)]
    [DataRow(2, true)]
    [DataRow(4, true)]
    public void AChannel_EncodedAndDecoded_IsTheSamplesItHeld(int sampleBytes, bool large)
    {
        const int width = 37, height = 11;
        var samples = Samples(width * height, sampleBytes, sampleBytes);
        var encoded = PhotoshopChannels.Encode(samples, width, height, sampleBytes, large);
        using var stream = new MemoryStream(encoded);
        var decoded = new byte[samples.Length];
        PhotoshopChannels.Decode(stream, 0, encoded.Length, width, height, sampleBytes, large,
                                 (y, row) => row.CopyTo(decoded.AsSpan(y * width * sampleBytes)));
        CollectionAssert.AreEqual(samples, decoded);
    }

    [TestMethod]
    [DataRow(8, false)]
    [DataRow(16, false)]
    [DataRow(32, false)]
    [DataRow(8, true)]
    [DataRow(16, true)]
    public void AFile_WrittenAndRead_KeepsEveryRecordBlockResourceAndSample(int depth, bool large)
    {
        const int width = 20, height = 9;
        int sampleBytes = depth / 8;
        var header = new PhotoshopFileHeader(large, 4, width, height, depth, PhotoshopColourMode.Rgb);
        var layerSamples = Enumerable.Range(0, 4).Select(c => Samples(10 * 5, sampleBytes, 10 + c)).ToArray();
        var maskSamples = Samples(4 * 3, sampleBytes, 99);
        var unicode = PhotoshopTaggedBlock.Of("luni", new byte[] { 0, 0, 0, 2, 0, (byte)'H', 0, (byte)'i' });
        // Blocks hold whole multiples of four bytes, as Photoshop pads them and counts the padding.
                var unknown = new PhotoshopTaggedBlock("8BIM", "TySh", new byte[] { 1, 2, 3, 4, 5, 0, 0, 0 });
        var layer = new PhotoshopLayerToWrite
        {
            Rect = new PhotoshopRect(2, 3, 7, 13),
            Channels =
            [
                new(-1, PhotoshopChannels.Encode(layerSamples[3], 10, 5, sampleBytes, large)),
                new(0, PhotoshopChannels.Encode(layerSamples[0], 10, 5, sampleBytes, large)),
                new(1, PhotoshopChannels.Encode(layerSamples[1], 10, 5, sampleBytes, large)),
                new(2, PhotoshopChannels.Encode(layerSamples[2], 10, 5, sampleBytes, large)),
                new(-2, PhotoshopChannels.Encode(maskSamples, 4, 3, sampleBytes, large)),
            ],
            BlendKey = "lbrn", Opacity = 200, Clipping = 1, Flags = PhotoshopLayerRecord.HiddenFlag,
            Mask = new PhotoshopLayerMask(new PhotoshopRect(1, 1, 4, 5), 255, PhotoshopLayerMask.DisabledFlag) { Rest = new byte[2] },
            BlendingRanges = new byte[] { 0, 0, 255, 255, 0, 0, 255, 255 },
            PascalName = "Hi",
            Blocks = [unicode, unknown],
        };
        var group = new PhotoshopLayerToWrite { PascalName = "</Layer group>", Blocks = [PhotoshopTaggedBlock.Of("lsct", new byte[] { 0, 0, 0, 3 })] };
        var merged = Enumerable.Range(0, 4).Select(c => Samples(width * height, sampleBytes, 50 + c)).ToArray();
        var written = new PhotoshopDocumentToWrite
        {
            Header = header,
            Resources = [new PhotoshopResource(PhotoshopResource.ResolutionInfo, "", new byte[16]), new PhotoshopResource(4000, "kept", new byte[] { 9, 8, 7 })],
            Layers = [group, layer],
            MergedHasTransparency = true,
            GlobalBlocks = [PhotoshopTaggedBlock.Of("Patt", new byte[] { 1, 2, 3, 0 })],
            Merged = merged,
        };

        using var stream = new MemoryStream();
        PhotoshopWriter.Write(stream, written);
        var read = PhotoshopReader.Read(stream);

        Assert.AreEqual(header, read.Header);
        Assert.IsTrue(read.MergedHasTransparency);
        CollectionAssert.AreEqual(written.Resources.ToArray(), read.Resources.Select(r => r with { Data = r.Data.ToArray() }).ToArray(), new ResourceComparer(), "every resource");
        CollectionAssert.AreEqual(written.GlobalBlocks.ToArray(), read.GlobalBlocks.ToArray(), "the file's own blocks, the layers' holder not among them");
        Assert.AreEqual(2, read.Layers.Length);

        var back = read.Layers[1];
        Assert.AreEqual((layer.Rect, layer.BlendKey, layer.Opacity, layer.Clipping, layer.Flags, layer.PascalName),
                        (back.Rect, back.BlendKey, back.Opacity, back.Clipping, back.Flags, back.PascalName));
        Assert.AreEqual(layer.Mask, back.Mask, "the mask, its flags and the rest of its record");
        CollectionAssert.AreEqual(layer.BlendingRanges.ToArray(), back.BlendingRanges.ToArray());
        CollectionAssert.AreEqual(layer.Blocks.ToArray(), back.Blocks.ToArray(), "every tagged block, the one not understood too");
        CollectionAssert.AreEqual(new short[] { -1, 0, 1, 2, -2 }, back.Channels.Select(c => c.Id).ToArray());
        foreach (var (channel, expected, w, h) in new[] { (back.Channels[1], layerSamples[0], 10, 5), (back.Channels[4], maskSamples, 4, 3) })
        {
            var samples = new byte[expected.Length];
            PhotoshopChannels.Decode(stream, channel.Offset, channel.Length, w, h, sampleBytes, large, (y, row) => row.CopyTo(samples.AsSpan(y * w * sampleBytes)));
            CollectionAssert.AreEqual(expected, samples, $"channel {channel.Id}'s samples");
        }

        var mergedBack = Enumerable.Range(0, 4).Select(_ => new byte[width * height * sampleBytes]).ToArray();
        PhotoshopChannels.DecodeMerged(stream, read.MergedOffset, 4, width, height, sampleBytes, large,
                                       (c, y, row) => row.CopyTo(mergedBack[c].AsSpan(y * width * sampleBytes)));
        for (int c = 0; c < 4; c++) CollectionAssert.AreEqual(merged[c], mergedBack[c], $"merged channel {c}");
    }

    [TestMethod]
    public void AFileShortOfItsStructure_IsRefused()
    {
        Assert.IsFalse(PhotoshopReader.IsPhotoshop("8BPS\0\u0003"u8));
        Assert.ThrowsExactly<InvalidDataException>(() => PhotoshopReader.Read(new MemoryStream("8BPS\0\u0001\0\0"u8.ToArray())));
    }

    private sealed class ResourceComparer : System.Collections.IComparer
    {
        public int Compare(object? x, object? y)
            => x is PhotoshopResource a && y is PhotoshopResource b && a.Id == b.Id && a.Name == b.Name && a.Data.Span.SequenceEqual(b.Data.Span) ? 0 : 1;
    }
}
