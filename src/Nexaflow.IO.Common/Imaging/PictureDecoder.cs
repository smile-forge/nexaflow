using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;

namespace Nexaflow.IO.Common.Imaging;

/// <summary>A picture as 8-bit RGB: <see cref="Rgb"/> row by row, three bytes a pixel.</summary>
public sealed record RgbPicture(int Width, int Height, byte[] Rgb);

/// <summary>
/// Reads a picture in any format Windows reads — PNG, JPEG, WebP, and whatever codecs are installed — through the
/// Windows Imaging Component, as 8-bit RGB: for code that takes pictures but has no UI stack, such as an owned model's
/// provider handing a clip's first frame to the AI host. Transparency is dropped; the first frame of an animated or
/// multi-page picture is the one read.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PictureDecoder
{
    private static readonly Guid FactoryClass = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
    private static readonly Guid Rgb24 = new("6fddc324-4e03-4bfe-b185-3d77768dc90d");

    private const int DecodeMetadataCacheOnDemand = 0;
    private const int DitherNone = 0;
    private const int PaletteCustom = 0;

    /// <exception cref="InvalidDataException">Windows reads no picture from <paramref name="encoded"/>.</exception>
    public static RgbPicture DecodeRgb(byte[] encoded)
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
            Check(converter.Initialize(frame, Rgb24, DitherNone, IntPtr.Zero, 0.0, PaletteCustom));
            Check(converter.GetSize(out uint width, out uint height));

            int stride = checked((int)width * 3);
            var rgb = new byte[checked(stride * (int)height)];
            Check(converter.CopyPixels(IntPtr.Zero, (uint)stride, (uint)rgb.Length, rgb));
            return new RgbPicture((int)width, (int)height, rgb);
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
