using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using UglyToad.PdfPig.Graphics.Colors.Icc;
using UglyToad.PdfPig.Graphics.Core;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// One profile's colours converted to sRGB under one rendering intent by Windows' colour engine
/// (<see cref="PdfColourEngine"/>). Single colours go through it sixteen bits a component and are kept, since a page
/// asks for the same few again and again; samples go through it a whole image at a time.
/// <para>
/// Relative colorimetric rendering keeps colours as they are, white to white, and Windows' does nothing about black: a
/// press's black is a dark grey, and drawn as itself on a screen, whose black is black, every page of it looks washed
/// out. So, as Acrobat and every colour-managed reader do, its black point is compensated: where a device profile's
/// darkest colour is lighter than black, colours go through CIELAB, are scaled there so that its black is sRGB's, and go
/// on to sRGB (<see cref="Compensated"/>). Every other conversion is one Windows transform (<see cref="Direct"/>).
/// </para>
/// </summary>
internal abstract class PdfColourTransform : IIccTransform
{
    private const int SolidsKept = 4096;

    private readonly PdfIccProfile _profile;
    private readonly int _colours;
    private readonly ConcurrentDictionary<ulong, (double R, double G, double B)> _solids = new();

    // Windows promises nothing about one transform used by two threads at once, and documents read in parallel share it.
    private protected readonly Lock Translating = new();

    private PdfColourTransform(PdfIccProfile profile)
    {
        _profile = profile;
        _colours = profile.Space switch
        {
            PdfIccSpace.Gray => IcmNative.GrayColour,
            PdfIccSpace.Rgb  => IcmNative.RgbColour,
            PdfIccSpace.Cmyk => IcmNative.CmykColour,
            PdfIccSpace.Lab  => IcmNative.LabColour,
            _                => IcmNative.XyzColour,
        };
        Samples = profile.Space switch
        {
            PdfIccSpace.Gray => IcmNative.GraySamples,
            PdfIccSpace.Rgb  => IcmNative.RgbSamples,
            PdfIccSpace.Cmyk => IcmNative.CmykSamples,
            PdfIccSpace.Lab  => IcmNative.LabSamples,
            _                => IcmNative.XyzSamples,
        };
    }

    public int NumberOfComponents => _profile.NumberOfComponents;

    /// <summary>The bitmap format the profile's samples are in.</summary>
    private protected int Samples { get; }

    /// <summary>
    /// The transform from <paramref name="profile"/> to sRGB under <paramref name="intent"/>, or <see langword="null"/>
    /// where Windows cannot read the profile.
    /// </summary>
    public static PdfColourTransform? TryCreate(PdfIccProfile profile, RenderingIntent intent, PdfIccTargets targets)
    {
        if (intent == RenderingIntent.RelativeColorimetric && profile.Space is PdfIccSpace.Gray or PdfIccSpace.Rgb or PdfIccSpace.Cmyk
            && Compensated.TryCreate(profile, targets) is { } compensated)
        {
            return compensated;
        }

        var handle = Link(profile.Bytes, targets.Srgb, intent);
        return handle is null ? null : new Direct(profile, handle);
    }

    public (double r, double g, double b) ToRgb(ReadOnlySpan<double> values)
    {
        // Sixteen bits a component, each across its span in the profile's encoding, is how Windows takes one colour;
        // the (at most) four of them are the key it is kept under.
        Span<ushort> words = stackalloc ushort[4];
        ulong key = 0;
        for (var c = 0; c < NumberOfComponents; c++)
        {
            var (min, max) = _profile.EncodingRange(c);
            words[c] = Word((values[c] - min) / (max - min));
            key = key << 16 | words[c];
        }

        if (!_solids.TryGetValue(key, out var rgb))
        {
            var colour = new IcmNative.Colour { First = words[0], Second = words[1], Third = words[2], Fourth = words[3] };
            rgb = Translate(colour, _colours);
            if (_solids.Count >= SolidsKept) _solids.Clear();
            _solids[key] = rgb;
        }

        return rgb;
    }

    public void Transform(ReadOnlySpan<byte> src, Span<byte> dstRgb)
    {
        var pixels = src.Length / NumberOfComponents;
        if (pixels > 0) Translate(src[..(pixels * NumberOfComponents)], pixels, dstRgb[..(pixels * 3)]);
    }

    /// <summary>One colour, of colour type <paramref name="type"/>, in sRGB.</summary>
    private protected abstract (double R, double G, double B) Translate(IcmNative.Colour colour, int type);

    /// <summary><paramref name="pixels"/> samples in the profile's format, into RGB three bytes a pixel.</summary>
    private protected abstract void Translate(ReadOnlySpan<byte> samples, int pixels, Span<byte> rgb);

    private protected static ushort Word(double unit) => (ushort)Math.Round(Math.Clamp(unit, 0, 1) * ushort.MaxValue);

    private protected static (double R, double G, double B) Rgb(IcmNative.Colour rgb) =>
        (rgb.First / (double)ushort.MaxValue, rgb.Second / (double)ushort.MaxValue, rgb.Third / (double)ushort.MaxValue);

    private protected static void Check(bool translated)
    {
        if (!translated) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    /// <summary>
    /// Windows' transform from the profile <paramref name="from"/> holds to the one <paramref name="to"/> holds, or
    /// <see langword="null"/> where it cannot read either.
    /// </summary>
    private protected static IcmTransformHandle? Link(byte[] from, byte[] to, RenderingIntent intent)
    {
        using var source = OpenProfile.Of(from);
        using var target = OpenProfile.Of(to);
        if (source.Handle == 0 || target.Handle == 0) return null;

        var handle = IcmNative.CreateMultiProfileTransform(
            [source.Handle, target.Handle], 2, [Intent(intent)], 1, IcmNative.BestMode, IcmNative.AnyEngine);
        if (!handle.IsInvalid) return handle;

        handle.Dispose();
        return null;
    }

    private static uint Intent(RenderingIntent intent) => intent switch
    {
        RenderingIntent.Perceptual           => IcmNative.Perceptual,
        RenderingIntent.Saturation           => IcmNative.Saturation,
        RenderingIntent.AbsoluteColorimetric => IcmNative.AbsoluteColorimetric,
        _                                    => IcmNative.RelativeColorimetric,
    };

    /// <summary>A profile's colours through one Windows transform straight to sRGB.</summary>
    private sealed class Direct(PdfIccProfile profile, IcmTransformHandle handle) : PdfColourTransform(profile)
    {
        private protected override (double R, double G, double B) Translate(IcmNative.Colour colour, int type)
        {
            IcmNative.Colour rgb;
            lock (Translating) Check(IcmNative.TranslateColors(handle, ref colour, 1, type, out rgb, IcmNative.RgbColour));
            return Rgb(rgb);
        }

        private protected override void Translate(ReadOnlySpan<byte> samples, int pixels, Span<byte> rgb)
        {
            lock (Translating)
            {
                Check(IcmNative.TranslateBitmapBits(handle, ref MemoryMarshal.GetReference(samples), Samples, (uint)pixels, 1,
                    (uint)samples.Length, ref MemoryMarshal.GetReference(rgb), IcmNative.RgbSamples, (uint)rgb.Length, 0, 0));
            }
        }
    }

    /// <summary>
    /// A device profile's colours, relative colorimetrically, with its black point compensated: each goes to CIELAB,
    /// is scaled in CIEXYZ so that the profile's darkest colour becomes black while white stays white (Adobe's black
    /// point compensation, sRGB's own black point being black), and goes on from CIELAB to sRGB.
    /// </summary>
    private sealed class Compensated : PdfColourTransform
    {
        // The profile connection space's white, D50 (ICC.1:2022, 7.2.16), which relative colorimetric colours are to.
        private const double WhiteX = 0.9642, WhiteZ = 0.8249;

        private readonly IcmTransformHandle _toLab;
        private readonly IcmTransformHandle _fromLab;
        private readonly double _black;

        private Compensated(PdfIccProfile profile, IcmTransformHandle toLab, IcmTransformHandle fromLab, double black)
            : base(profile)
        {
            _toLab = toLab;
            _fromLab = fromLab;
            _black = black;
        }

        /// <summary>
        /// The compensated transform for <paramref name="profile"/>, or <see langword="null"/> where its darkest colour
        /// is already black, and Windows' own transform is all it needs, or Windows cannot read it.
        /// </summary>
        public static Compensated? TryCreate(PdfIccProfile profile, PdfIccTargets targets)
        {
            var toLab = Link(profile.Bytes, targets.Lab, RenderingIntent.RelativeColorimetric);
            var fromLab = Link(targets.Lab, targets.Srgb, RenderingIntent.RelativeColorimetric);
            if (toLab is null || fromLab is null)
            {
                toLab?.Dispose();
                fromLab?.Dispose();
                return null;
            }

            var black = BlackPoint(profile, toLab);
            if (black < 0.0005)
            {
                toLab.Dispose();
                fromLab.Dispose();
                return null;
            }

            return new Compensated(profile, toLab, fromLab, black);
        }

        /// <summary>
        /// The luminance of the profile's darkest colour — no light for grey and RGB, every ink for CMYK, the darker of a
        /// grey's two ends where which is which depends on the device — taken as neutral and no lighter than L* 50, as
        /// black point detection takes it.
        /// </summary>
        private static double BlackPoint(PdfIccProfile profile, IcmTransformHandle toLab)
        {
            IcmNative.Colour[] darkest = profile.Space switch
            {
                PdfIccSpace.Gray => [new() { First = 0 }, new() { First = ushort.MaxValue }],
                PdfIccSpace.Cmyk => [new() { First = ushort.MaxValue, Second = ushort.MaxValue, Third = ushort.MaxValue, Fourth = ushort.MaxValue }],
                _                => [new()],
            };

            var lightness = 100.0;
            foreach (var colour in darkest)
            {
                var device = colour;
                Check(IcmNative.TranslateColors(toLab, ref device, 1, profile.Space == PdfIccSpace.Gray ? IcmNative.GrayColour
                    : profile.Space == PdfIccSpace.Cmyk ? IcmNative.CmykColour : IcmNative.RgbColour, out var lab, IcmNative.LabColour));
                lightness = Math.Min(lightness, lab.First / (double)ushort.MaxValue * 100);
            }

            return Inverse((Math.Min(lightness, 50) + 16) / 116);
        }

        private protected override (double R, double G, double B) Translate(IcmNative.Colour colour, int type)
        {
            IcmNative.Colour rgb;
            lock (Translating)
            {
                Check(IcmNative.TranslateColors(_toLab, ref colour, 1, type, out var lab, IcmNative.LabColour));
                Compensate(ref lab.First, ref lab.Second, ref lab.Third);
                Check(IcmNative.TranslateColors(_fromLab, ref lab, 1, IcmNative.LabColour, out rgb, IcmNative.RgbColour));
            }

            return Rgb(rgb);
        }

        private protected override void Translate(ReadOnlySpan<byte> samples, int pixels, Span<byte> rgb)
        {
            var lab = new ushort[pixels * 3];
            var bytes = MemoryMarshal.AsBytes(lab.AsSpan());
            lock (Translating)
            {
                Check(IcmNative.TranslateBitmapBits(_toLab, ref MemoryMarshal.GetReference(samples), Samples, (uint)pixels, 1,
                    (uint)samples.Length, ref MemoryMarshal.GetReference(bytes), IcmNative.Lab16Samples, (uint)bytes.Length, 0, 0));
            }

            for (var p = 0; p < lab.Length; p += 3) Compensate(ref lab[p], ref lab[p + 1], ref lab[p + 2]);

            lock (Translating)
            {
                Check(IcmNative.TranslateBitmapBits(_fromLab, ref MemoryMarshal.GetReference(bytes), IcmNative.Lab16Samples,
                    (uint)pixels, 1, (uint)bytes.Length, ref MemoryMarshal.GetReference(rgb), IcmNative.RgbSamples, (uint)rgb.Length, 0, 0));
            }
        }

        /// <summary>
        /// A CIELAB colour, sixteen bits a component (L* across [0, 100], a* and b* across [-128, 127]), scaled in CIEXYZ
        /// so that the profile's black point becomes black and white stays white.
        /// </summary>
        private void Compensate(ref ushort l, ref ushort a, ref ushort b)
        {
            var fy = (l / (double)ushort.MaxValue * 100 + 16) / 116;
            var fx = fy + (a / (double)ushort.MaxValue * 255 - 128) / 500;
            var fz = fy - (b / (double)ushort.MaxValue * 255 - 128) / 200;

            var scale = 1 / (1 - _black);
            var x = Math.Max(0, (Inverse(fx) - _black) * scale);
            var y = Math.Max(0, (Inverse(fy) - _black) * scale);
            var z = Math.Max(0, (Inverse(fz) - _black) * scale);

            fx = Forward(x);
            fy = Forward(y);
            fz = Forward(z);
            l = Word((116 * fy - 16) / 100);
            a = Word((500 * (fx - fy) + 128) / 255);
            b = Word((200 * (fy - fz) + 128) / 255);
        }

        // CIELAB's f and its inverse (CIE 15), over each component relative to the white, which the D50 ratios below
        // fold in: x, y and z here are X/Xw, Y/Yw and Z/Zw, so the white is 1, 1, 1 and the neutral black point _black
        // throughout.
        private static double Forward(double t) => t > 216.0 / 24389 ? Math.Cbrt(t) : t * 841.0 / 108 + 4.0 / 29;

        private static double Inverse(double f) => f > 6.0 / 29 ? f * f * f : (f - 4.0 / 29) * 108.0 / 841;
    }

    /// <summary>
    /// A profile Windows has open from its bytes, which stay where they are until it is closed; its handle is 0 where
    /// Windows cannot read them.
    /// </summary>
    private readonly struct OpenProfile : IDisposable
    {
        private readonly GCHandle _bytes;

        private OpenProfile(GCHandle bytes, nint handle)
        {
            _bytes = bytes;
            Handle = handle;
        }

        public nint Handle { get; }

        public static OpenProfile Of(byte[] profile)
        {
            var bytes = GCHandle.Alloc(profile, GCHandleType.Pinned);
            var location = new IcmNative.ProfileLocation
            {
                Type = IcmNative.ProfileInMemory,
                Data = bytes.AddrOfPinnedObject(),
                Size = (uint)profile.Length,
            };
            return new OpenProfile(bytes,
                IcmNative.OpenColorProfileW(ref location, IcmNative.ReadAccess, IcmNative.ShareRead, IcmNative.OpenExisting));
        }

        public void Dispose()
        {
            if (Handle != 0) IcmNative.CloseColorProfile(Handle);
            _bytes.Free();
        }
    }
}
