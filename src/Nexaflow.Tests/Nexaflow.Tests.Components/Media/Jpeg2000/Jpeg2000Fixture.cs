using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Nexaflow.Tests.Components.Media;

/// <summary>
/// A JPEG 2000 image the decoder's tests read: a source they regenerate sample for sample, encoded by libopenjp2's
/// <c>opj_compress</c> with <see cref="Options"/>. A lossless image's answer is its source, which owes nothing to any
/// decoder; a lossy one is held to what libopenjp2's <c>opj_decompress</c> decodes it to, recorded as a hash in
/// <c>expected.json</c>. <see cref="Jpeg2000FixtureMaker"/> writes them all.
/// </summary>
/// <param name="Name">The file's name, without its extension.</param>
/// <param name="Width">Each component's width, in its own samples.</param>
/// <param name="Height">Each component's height, in its own samples.</param>
/// <param name="Options">The <c>opj_compress</c> options; <c>{mct}</c> stands for the custom transform's file.</param>
/// <param name="Smooth">A ramp with a single bit of texture, which compresses far better — for the large images.</param>
/// <param name="Container"><c>j2k</c> for a bare codestream, <c>jp2</c> for a JP2 file.</param>
/// <param name="HeldToLibopenjp2">
/// Checked against <c>opj_decompress</c>'s output — after its sYCC conversion, where it makes one — rather than
/// against the source: every lossy image, and a lossless one whose answer is a conversion.
/// </param>
internal sealed record Jpeg2000Fixture(
    string Name,
    int Width,
    int Height,
    int Components,
    int Precision,
    string Options,
    bool Signed = false,
    bool Lossless = true,
    bool Smooth = false,
    string Container = "j2k",
    bool HeldToLibopenjp2 = false)
{
    /// <summary>Every fixture, each chosen for a part of the standard the reader can meet.</summary>
    public static IReadOnlyList<Jpeg2000Fixture> All { get; } =
    [
        // Six resolutions, the default, need 32 samples a side; smaller images and tiles take fewer.
        // Precisions and signedness, one component and three, with and without the reversible colour transform.
        new("grey8-64x64-blocks", 150, 130, 1, 8, ""),
        new("rgb8", 45, 37, 3, 8, ""),
        new("rgb8-no-mct", 45, 37, 3, 8, "-mct 0"),
        new("grey16", 45, 37, 1, 16, ""),
        new("signed12", 45, 37, 1, 12, "", Signed: true),
        new("grey1", 34, 21, 1, 1, "-n 4"), // opj_compress refuses raw sizes whose width, height, components and depth share a set bit
        new("grey2", 33, 21, 1, 2, "-n 4"),
        new("grey4", 33, 21, 1, 4, "-n 4"),

        // Where the samples sit: subsampled components, tiles off the grid's origin, one resolution, one sample.
        new("rgb8-subsampled", 23, 19, 3, 8, "-s 2,2 -n 4"),
        new("rgb8-tiles-offset", 45, 37, 3, 8, "-t 16,16 -d 5,3 -T 2,1 -n 3"),
        new("grey8-one-resolution", 45, 37, 1, 8, "-n 1"),
        new("grey8-one-sample", 1, 1, 1, 8, "-n 1"),

        // How the packets are laid out: precincts, every progression order over quality layers, markers, tile-parts.
        // opj_compress halves the last precinct size for each lower resolution, so three resolutions keep it legal.
        new("rgb8-precincts-rpcl", 45, 37, 3, 8, "-n 3 -c [16,16],[8,8] -b 8,8 -p RPCL"),
        new("rgb8-layers-lrcp", 45, 37, 3, 8, "-r 20,8,1 -p LRCP"),
        new("rgb8-layers-rlcp", 45, 37, 3, 8, "-r 20,8,1 -p RLCP"),
        new("rgb8-layers-rpcl", 45, 37, 3, 8, "-r 20,8,1 -p RPCL"),
        new("rgb8-layers-pcrl", 45, 37, 3, 8, "-r 20,8,1 -p PCRL"),
        new("rgb8-layers-cprl", 45, 37, 3, 8, "-r 20,8,1 -p CPRL"),
        new("rgb8-sop-eph", 45, 37, 3, 8, "-SOP -EPH"),
        new("rgb8-tile-parts", 45, 37, 3, 8, "-t 24,24 -n 4 -TP R -TLM -PLT"),

        // Code blocks: every style switch on its own and all together, a block wider than tall, a region upshift.
        new("grey8-style-bypass", 45, 37, 1, 8, "-b 16,16 -r 10,1 -M 1"),
        new("grey8-style-reset", 45, 37, 1, 8, "-b 16,16 -r 10,1 -M 2"),
        new("grey8-style-terminate-all", 45, 37, 1, 8, "-b 16,16 -r 10,1 -M 4"),
        new("grey8-style-stripe-causal", 45, 37, 1, 8, "-b 16,16 -r 10,1 -M 8"),
        new("grey8-style-predictable-termination", 45, 37, 1, 8, "-b 16,16 -r 10,1 -M 16"),
        new("grey8-style-segmentation-symbols", 45, 37, 1, 8, "-b 16,16 -r 10,1 -M 32"),
        new("grey8-style-all", 45, 37, 1, 8, "-b 16,16 -r 10,1 -M 63"),
        new("grey8-128x32-blocks", 150, 70, 1, 8, "-b 128,32"),
        new("grey8-region-upshift", 45, 37, 1, 8, "-ROI c=0,U=8"),

        // Big enough that a parallel decode shares the wavelet's rows and columns out.
        new("grey8-large", 272, 260, 1, 8, "", Smooth: true),

        // The JP2 container and what it says of colour.
        new("rgb8-jp2", 45, 37, 3, 8, "", Container: "jp2"),
        new("grey8-jp2", 45, 37, 1, 8, "", Container: "jp2"),
        new("ycc8-jp2", 45, 37, 3, 8, "-mct 0", Container: "jp2", HeldToLibopenjp2: true),

        // The irreversible path: the 9/7 wavelet, the float colour transform, a custom one, and larger precisions.
        new("rgb8-irreversible", 45, 37, 3, 8, "-I -r 8", Lossless: false, HeldToLibopenjp2: true),
        new("grey8-irreversible-tiles", 45, 37, 1, 8, "-I -q 36 -t 24,16 -n 3", Lossless: false, HeldToLibopenjp2: true),
        new("grey16-irreversible", 45, 37, 1, 16, "-I -r 6", Lossless: false, HeldToLibopenjp2: true),
        new("rgb8-custom-mct", 45, 37, 3, 8, "-I -r 6 -m {mct}", Lossless: false, HeldToLibopenjp2: true),
        new("grey8-irreversible-64x64-blocks", 150, 130, 1, 8, "-I -r 12", Lossless: false, HeldToLibopenjp2: true),
        new("grey8-irreversible-large", 272, 260, 1, 8, "-I -r 20", Lossless: false, Smooth: true, HeldToLibopenjp2: true),
    ];

    /// <summary>The custom transform <c>rgb8-custom-mct</c> uses: the YCbCr matrix, then each component's offset.</summary>
    public const string CustomTransform =
        "0.299,0.587,0.114,-0.168736,-0.331264,0.5,0.5,-0.418688,-0.081312,0,0,0";

    /// <summary>Where the fixtures are beside the tests.</summary>
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Media", "Jpeg2000", "Fixtures");

    /// <summary>The fixtures' names, for <c>[DynamicData]</c>.</summary>
    public static IEnumerable<object[]> Names(Func<Jpeg2000Fixture, bool> which) =>
        All.Where(which).Select(fixture => new object[] { fixture.Name });

    public static Jpeg2000Fixture Named(string name) => All.Single(fixture => fixture.Name == name);

    public string FileName => $"{Name}.{Container}";

    public byte[] Bytes() => File.ReadAllBytes(Path.Combine(Folder, FileName));

    /// <summary>The hash <c>opj_decompress</c>'s output had, from <c>expected.json</c>.</summary>
    public string ExpectedHash() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Folder, "expected.json")))![Name];

    /// <summary>
    /// Component <paramref name="component"/>'s samples, row by row: a diagonal ramp across the range, each
    /// component's offset by a third of it, wrapping into sharp edges, with a fine texture over it so every
    /// bit-plane has something to code.
    /// </summary>
    public int[] Plane(int component)
    {
        var range = 1L << Precision;
        var texture = Smooth ? 2u : (uint)Math.Max(2, range / 32);
        var span = Math.Max(1, Width * 3 + Height * 2);
        var plane = new int[Width * Height];
        for (int y = 0, at = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++, at++)
            {
                var ramp = (x * 3L + y * 2L) * (range - 1) / span + component * (range / 3);
                var value = (int)((ramp + Texture(x, y, component) % texture) % range);
                plane[at] = Signed ? value - (int)(range / 2) : value;
            }
        }

        return plane;
    }

    /// <summary>The source as <c>opj_compress</c> reads raw input: plane after plane, a byte a sample up to 8 bits, two little-endian bytes past it.</summary>
    public byte[] RawSource()
    {
        var wide = Precision > 8;
        using var raw = new MemoryStream();
        for (var c = 0; c < Components; c++)
        {
            foreach (var sample in Plane(c))
            {
                raw.WriteByte((byte)sample);
                if (wide) raw.WriteByte((byte)(sample >> 8));
            }
        }

        return raw.ToArray();
    }

    /// <summary>
    /// A hash of decoded planes — each one's width and height, then its samples, as little-endian 32-bit integers — so
    /// a decode can be held to another without keeping its samples.
    /// </summary>
    public static string HashOf(IEnumerable<(int Width, int Height, ReadOnlyMemory<int> Samples)> planes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var word = new byte[4];
        foreach (var (width, height, samples) in planes)
        {
            Append(width);
            Append(height);
            foreach (var sample in samples.Span[..(width * height)]) Append(sample);
        }

        return Convert.ToHexString(hash.GetHashAndReset());

        void Append(int value)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(word, value);
            hash.AppendData(word);
        }
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
