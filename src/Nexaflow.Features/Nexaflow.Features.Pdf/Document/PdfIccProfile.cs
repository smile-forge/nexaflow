using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using UglyToad.PdfPig.Graphics.Colors.Icc;
using UglyToad.PdfPig.Graphics.Core;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// The colour space an ICC profile's colours are in, as its header names it (ICC.1:2022, 7.2.6): the ones Windows'
/// colour engine takes colours in.
/// </summary>
internal enum PdfIccSpace
{
    Gray,
    Rgb,
    Cmyk,
    Lab,
    Xyz,
}

/// <summary>
/// An ICC profile Windows' colour engine converts through (<see cref="PdfColourEngine"/>): its colour space read from
/// its header, and a transform to sRGB made the first time a document asks for each rendering intent.
/// </summary>
internal sealed class PdfIccProfile : IIccProfile
{
    private const int HeaderLength = 128;

    private readonly ConcurrentDictionary<RenderingIntent, Lazy<PdfColourTransform?>> _transforms = new();
    private readonly PdfIccTargets _targets;

    private PdfIccProfile(byte[] bytes, PdfIccSpace space, PdfIccTargets targets)
    {
        Bytes = bytes;
        Space = space;
        _targets = targets;
        NumberOfComponents = space switch { PdfIccSpace.Gray => 1, PdfIccSpace.Cmyk => 4, _ => 3 };

        var ranges = new double[2 * NumberOfComponents];
        for (var c = 0; c < NumberOfComponents; c++)
            (ranges[2 * c], ranges[2 * c + 1]) = EncodingRange(c);
        ComponentRanges = ranges;
    }

    /// <summary>The profile's bytes, header first.</summary>
    public byte[] Bytes { get; }

    /// <summary>The colour space its colours are in.</summary>
    public PdfIccSpace Space { get; }

    public int NumberOfComponents { get; }

    /// <summary>
    /// The values each component spans in the profile's own encoding (ICC.1:2022, 6.3.4.2 and 10.11): L* over [0, 100]
    /// and a* and b* over [-128, 127] for CIELAB, [0, 1 + 32767/32768] for CIEXYZ, [0, 1] for device spaces.
    /// </summary>
    public IReadOnlyList<double> ComponentRanges { get; }

    /// <summary>
    /// The profile <paramref name="bytes"/> hold, or <see langword="null"/> where they hold none, or one whose colours
    /// are in a space Windows takes none in.
    /// </summary>
    public static PdfIccProfile? TryRead(ReadOnlyMemory<byte> bytes, PdfIccTargets targets)
    {
        var header = bytes.Span;
        if (header.Length < HeaderLength || !header.Slice(36, 4).SequenceEqual("acsp"u8)) return null;

        var signature = header.Slice(16, 4);
        PdfIccSpace? space = signature switch
        {
            _ when signature.SequenceEqual("GRAY"u8) => PdfIccSpace.Gray,
            _ when signature.SequenceEqual("RGB "u8) => PdfIccSpace.Rgb,
            _ when signature.SequenceEqual("CMYK"u8) => PdfIccSpace.Cmyk,
            _ when signature.SequenceEqual("Lab "u8) => PdfIccSpace.Lab,
            _ when signature.SequenceEqual("XYZ "u8) => PdfIccSpace.Xyz,
            _ => null,
        };
        return space is { } known ? new PdfIccProfile(bytes.ToArray(), known, targets) : null;
    }

    public bool TryGetTransform(RenderingIntent intent, [NotNullWhen(true)] out IIccTransform? transform)
    {
        transform = _transforms.GetOrAdd(intent,
            asked => new Lazy<PdfColourTransform?>(() => PdfColourTransform.TryCreate(this, asked, _targets))).Value;
        return transform is not null;
    }

    /// <summary>The span component <paramref name="component"/> has in the profile's own encoding.</summary>
    public (double Min, double Max) EncodingRange(int component) => Space switch
    {
        PdfIccSpace.Lab => component == 0 ? (0.0, 100.0) : (-128.0, 127.0),
        PdfIccSpace.Xyz => (0.0, 1.0 + 32767.0 / 32768.0),
        _               => (0.0, 1.0),
    };
}

/// <summary>
/// What a profile's colours are converted to: sRGB, and the CIELAB a colour passes through on the way where black
/// points are compensated (<see cref="PdfLabProfile"/>).
/// </summary>
internal sealed record PdfIccTargets(byte[] Srgb, byte[] Lab);
