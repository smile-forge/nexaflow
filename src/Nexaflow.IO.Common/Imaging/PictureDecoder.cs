using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;

namespace Nexaflow.IO.Common.Imaging;

/// <summary>A picture as 8-bit RGB: <see cref="Rgb"/> row by row, three bytes a pixel.</summary>
public sealed record RgbPicture(int Width, int Height, byte[] Rgb);

/// <summary>A picture's transparency: <see cref="Alpha"/> row by row, a byte a pixel, 255 opaque.</summary>
public sealed record AlphaPicture(int Width, int Height, byte[] Alpha);

/// <summary>
/// Reads a picture in any format Windows reads — PNG, JPEG, WebP, and whatever codecs are installed — through the
/// Windows Imaging Component, as 8-bit RGB: for code that takes pictures but has no UI stack, such as an owned model's
/// provider handing a clip's first frame to the AI host. Transparency is dropped, or read alone (<see cref="DecodeAlpha"/>);
/// the first frame of an animated or multi-page picture is the one read.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PictureDecoder
{
    private static readonly Guid FactoryClass = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
    private static readonly Guid Rgb24 = new("6fddc324-4e03-4bfe-b185-3d77768dc90d");
    private static readonly Guid Rgba32 = new("f5c7ad2d-6a8d-43dd-a7a8-a29935261ae9");

    private const int DecodeMetadataCacheOnDemand = 0;
    private const int DitherNone = 0;
    private const int PaletteCustom = 0;

    /// <exception cref="InvalidDataException">Windows reads no picture from <paramref name="encoded"/>.</exception>
    public static RgbPicture DecodeRgb(byte[] encoded)
    {
        var (width, height, rgb) = Decode(encoded, Rgb24, 3);
        return new RgbPicture(width, height, rgb);
    }

    /// <summary>A picture's transparency alone: a byte a pixel, row by row, 0 where it is wholly transparent and 255 where it
    /// is opaque — for a mask whose transparent part says what is drawn again. A picture with no transparency is opaque.</summary>
    /// <exception cref="InvalidDataException">Windows reads no picture from <paramref name="encoded"/>.</exception>
    public static AlphaPicture DecodeAlpha(byte[] encoded)
    {
        var (width, height, rgba) = Decode(encoded, Rgba32, 4);
        var alpha = new byte[width * height];
        for (int i = 0; i < alpha.Length; i++) alpha[i] = rgba[i * 4 + 3];
        return new AlphaPicture(width, height, alpha);
    }

    /// <summary>The first frame of <paramref name="encoded"/> converted to <paramref name="format"/>, row by row.</summary>
    private static (int Width, int Height, byte[] Pixels) Decode(byte[] encoded, Guid format, int channels)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        var factory = (IWICImagingFactory)Activator.CreateInstance(Type.GetTypeFromCLSID(FactoryClass, throwOnError: true)!)!;
        IStream? stream = null;
        IWICBitmapDecoder? decoder = null;
        IWICBitmapSource? frame = null;
        IWICFormatConverter? converter = null;
        try
        {
            stream = SHCreateMemStream(encoded, (uint)encoded.Length) ?? throw new OutOfMemoryException("Windows could not hold the picture to read it.");
            if (factory.CreateDecoderFromStream(stream, IntPtr.Zero, DecodeMetadataCacheOnDemand, out decoder) < 0)
                throw new InvalidDataException("Windows reads no picture from these bytes.");
            Check(decoder.GetFrame(0, out frame));
            Check(factory.CreateFormatConverter(out converter));
            Check(converter.Initialize(frame, format, DitherNone, IntPtr.Zero, 0.0, PaletteCustom));
            Check(converter.GetSize(out uint width, out uint height));

            int stride = checked((int)width * channels);
            var pixels = new byte[checked(stride * (int)height)];
            Check(converter.CopyPixels(IntPtr.Zero, (uint)stride, (uint)pixels.Length, pixels));
            return ((int)width, (int)height, pixels);
        }
        finally
        {
            foreach (var com in new object?[] { converter, frame, decoder, stream, factory })
                if (com is not null) Marshal.ReleaseComObject(com);
        }
    }

    private static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    [DllImport("shlwapi.dll", ExactSpelling = true)]
    private static extern IStream? SHCreateMemStream(byte[] data, uint size);

    [ComImport, Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICImagingFactory
    {
        void _VtblGap1_1();   // CreateDecoderFromFilename
        [PreserveSig] int CreateDecoderFromStream(IStream stream, IntPtr vendor, int options, out IWICBitmapDecoder decoder);
        void _VtblGap2_5();   // CreateDecoderFromFileHandle, CreateComponentInfo, CreateDecoder, CreateEncoder, CreatePalette
        [PreserveSig] int CreateFormatConverter(out IWICFormatConverter converter);
    }

    [ComImport, Guid("9edde9e7-8dee-47ea-99df-e6faf2ed44bf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICBitmapDecoder
    {
        void _VtblGap1_10();  // QueryCapability … GetFrameCount
        [PreserveSig] int GetFrame(uint index, out IWICBitmapSource frame);
    }

    [ComImport, Guid("00000120-a8f2-4877-ba0a-fd2b6645fb94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICBitmapSource;

    [ComImport, Guid("00000301-a8f2-4877-ba0a-fd2b6645fb94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICFormatConverter
    {
        [PreserveSig] int GetSize(out uint width, out uint height);
        void _VtblGap1_3();   // GetPixelFormat, GetResolution, CopyPalette
        [PreserveSig] int CopyPixels(IntPtr rectangle, uint stride, uint bufferSize, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] buffer);
        [PreserveSig] int Initialize(IWICBitmapSource source, in Guid format, int dither, IntPtr palette, double alphaThreshold, int paletteType);
    }
}
