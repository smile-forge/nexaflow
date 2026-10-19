using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Nexaflow.Tests.Fixtures;
using static Nexaflow.Tests.Fixtures.LibJpegTurbo;

namespace Nexaflow.Tests.Components.Media;

/// <summary>
/// A JPEG the decoder's tests read: a source they regenerate sample for sample, encoded by libjpeg-turbo's compressor
/// as <see cref="Encoding"/> says and then, for a damaged one, cut short or corrupted. Each is held to what
/// libjpeg-turbo's TurboJPEG library decodes it to, recorded as a hash in <c>expected.json</c>; a lossless one is held
/// to its source as well, which owes nothing to any decoder. <see cref="JpegFixtureMaker"/> writes them all.
/// </summary>
/// <param name="Name">The file's name, without its extension.</param>
/// <param name="Smooth">A ramp with a single bit of texture, which compresses far better — for the large images.</param>
internal sealed record JpegFixture(string Name, int Width, int Height, LibJpegTurbo.Encoding Encoding, JpegDamage Damage = JpegDamage.None, bool Smooth = false)
{
    private static readonly LibJpegTurbo.Encoding Grey = new(PixelGrey, ColourGrey, SamplingGrey, 75);
    private static readonly LibJpegTurbo.Encoding Ycc420 = new(PixelRgb, ColourYcbcr, Sampling420, 75);
    private static readonly LibJpegTurbo.Encoding Cmyk = new(PixelCmyk, ColourCmyk, Sampling444, 75);

    /// <summary>Every fixture, each chosen for a part of the standard, or of libjpeg's reading of it, the reader can meet.</summary>
    public static IReadOnlyList<JpegFixture> All { get; } =
    [
        // Every colour space TurboJPEG writes and every subsampling, at a size no MCU divides.
        new("grey", 37, 29, Grey),
        new("ycc444", 37, 29, Ycc420 with { Subsampling = Sampling444 }),
        new("ycc422", 37, 29, Ycc420 with { Subsampling = Sampling422 }),
        new("ycc420", 37, 29, Ycc420),
        new("ycc440", 37, 29, Ycc420 with { Subsampling = Sampling440 }),
        new("ycc411", 37, 29, Ycc420 with { Subsampling = Sampling411 }),
        new("ycc441", 37, 29, Ycc420 with { Subsampling = Sampling441 }),
        new("rgb", 37, 29, new(PixelRgb, ColourRgb, Sampling444, 75)),
        new("cmyk", 37, 29, Cmyk),
        new("ycck420", 37, 29, new(PixelCmyk, ColourYcck, Sampling420, 75)),

        // Edges: one pixel, and one row.
        new("ycc420-one-pixel", 1, 1, Ycc420),
        new("grey-one-row", 17, 1, Grey),

        // How the scans are coded: quality's extremes, progressive, arithmetic, restart intervals.
        new("ycc420-q100", 37, 29, Ycc420 with { Quality = 100 }),
        new("ycc420-q5", 37, 29, Ycc420 with { Quality = 5 }),
        new("ycc420-progressive", 37, 29, Ycc420 with { Progressive = true }),
        new("ycc420-arithmetic", 37, 29, Ycc420 with { Arithmetic = true }),
        new("ycc420-arithmetic-progressive", 37, 29, Ycc420 with { Arithmetic = true, Progressive = true }),
        new("ycc420-restart-every-block", 37, 29, Ycc420 with { RestartBlocks = 1 }),
        new("ycc420-progressive-restart-rows", 37, 29, Ycc420 with { Progressive = true, RestartRows = 1 }),
        new("grey-progressive", 37, 29, Grey with { Progressive = true }),
        new("grey-arithmetic", 37, 29, Grey with { Arithmetic = true }),
        new("cmyk-progressive", 37, 29, Cmyk with { Progressive = true }),

        // Big enough that a single-scan image is decoded in several bands and a parallel decode shares rows out.
        new("ycc420-large", 320, 240, Ycc420, Smooth: true),
        new("ycc420-large-progressive", 320, 240, Ycc420 with { Progressive = true }, Smooth: true),

        // Lossless: predictors, point transforms and restarts, in colour spaces no lossy conversion touches.
        new("grey-lossless-1", 37, 29, Grey with { LosslessPsv = 1 }),
        new("grey-lossless-7-pt2-restart", 37, 29, Grey with { LosslessPsv = 7, LosslessPt = 2, RestartRows = 1 }),
        new("rgb-lossless-4", 37, 29, new(PixelRgb, ColourRgb, Sampling444, LosslessPsv: 4)),
        new("rgb-lossless-6-pt1", 37, 29, new(PixelRgb, ColourRgb, Sampling444, LosslessPsv: 6, LosslessPt: 1)),
        new("cmyk-lossless-5", 37, 29, Cmyk with { LosslessPsv = 5 }),

        // Damaged data, which libjpeg pads or patches with a warning rather than refusing.
        new("ycc420-large-truncated", 320, 240, Ycc420, JpegDamage.Truncated, Smooth: true),
        new("ycc420-large-progressive-truncated", 320, 240, Ycc420 with { Progressive = true }, JpegDamage.Truncated, Smooth: true),
        new("ycc420-large-corrupted", 320, 240, Ycc420, JpegDamage.Corrupted, Smooth: true),
        new("ycc420-large-arithmetic-corrupted", 320, 240, Ycc420 with { Arithmetic = true }, JpegDamage.Corrupted, Smooth: true),
        new("grey-lossless-truncated", 37, 29, Grey with { LosslessPsv = 1 }, JpegDamage.Truncated),
    ];

    /// <summary>Where the fixtures are beside the tests.</summary>
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Media", "Jpeg", "Fixtures");

    /// <summary>The fixtures' names, for <c>[DynamicData]</c>.</summary>
    public static IEnumerable<object[]> Names(Func<JpegFixture, bool> which) =>
        All.Where(which).Select(fixture => new object[] { fixture.Name });

    public static JpegFixture Named(string name) => All.Single(fixture => fixture.Name == name);

    public string FileName => $"{Name}.jpg";

    public bool Lossless => Encoding.LosslessPsv > 0;

    /// <summary>How many components the source and the decoded image have.</summary>
    public int Components => Encoding.PixelFormat switch { PixelGrey => 1, PixelCmyk => 4, _ => 3 };

    public byte[] Bytes() => File.ReadAllBytes(Path.Combine(Folder, FileName));

    /// <summary>What libjpeg-turbo made of the fixture, from <c>expected.json</c>.</summary>
    public JpegExpectation Expected() =>
        JsonSerializer.Deserialize<Dictionary<string, JpegExpectation>>(File.ReadAllText(Path.Combine(Folder, "expected.json")))![Name];

    /// <summary>
    /// The source, interleaved: a diagonal ramp across the range, each component's offset by a third of it, flipped
    /// over in a checkerboard of hard edges, with a texture over it so every frequency has something to code.
    /// </summary>
    public byte[] Source()
    {
        var components = Components;
        var texture = Smooth ? 2u : 48u;
        var span = Math.Max(1, Width * 3 + Height * 2);
        var source = new byte[Width * Height * components];
        for (int y = 0, at = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                for (var c = 0; c < components; c++, at++)
                {
                    var value = (x * 3 + y * 2) * 255 / span + c * 85 + (int)(Texture(x, y, c) % texture);
                    if (!Smooth && (x / 5 + y / 3 + c) % 4 == 0) value = 255 - value % 256;
                    source[at] = (byte)value;
                }
            }
        }

        return source;
    }

    /// <summary>
    /// The encoded image as the fixture keeps it: whole, or cut short partway through its scans, or with bytes of its
    /// entropy-coded data overwritten — the same bytes every time.
    /// </summary>
    public byte[] Damaged(byte[] jpeg)
    {
        switch (Damage)
        {
            case JpegDamage.Truncated:
                return jpeg[..(jpeg.Length * 3 / 5)];
            case JpegDamage.Corrupted:
                var damaged = (byte[])jpeg.Clone();
                var start = damaged.Length / 3;
                var span = damaged.Length - 2 - start;
                for (var k = 1; k <= 6; k++) damaged[start + k * 7919 % span] = (byte)(k * 37 + 11);
                return damaged;
            default:
                return jpeg;
        }
    }

    /// <summary>
    /// A hash of decoded samples — the width, height and component count, then the samples — so a decode can be held
    /// to libjpeg-turbo's without keeping its samples.
    /// </summary>
    public static string HashOf(int width, int height, int components, ReadOnlySpan<byte> samples)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> shape = stackalloc byte[12];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(shape, width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(shape[4..], height);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(shape[8..], components);
        hash.AppendData(shape);
        hash.AppendData(samples);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static uint Texture(int x, int y, int component)
    {
        var h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(component * 83492791);
        h ^= h >> 13;
        h *= 0x5BD1E995;
        h ^= h >> 15;
        return h;
    }
}

/// <summary>What is done to a fixture's JPEG after it is encoded.</summary>
internal enum JpegDamage
{
    None,

    /// <summary>Cut short at three fifths of its length.</summary>
    Truncated,

    /// <summary>Six bytes of its entropy-coded data overwritten.</summary>
    Corrupted,
}

/// <summary>
/// What libjpeg-turbo made of a fixture: the hash of the samples it decoded — or wrote before a fault it found only
/// once the image was out — and whether it warned; or that it refused the file.
/// </summary>
internal sealed record JpegExpectation(string? Hash, bool Warns, bool Refused);
