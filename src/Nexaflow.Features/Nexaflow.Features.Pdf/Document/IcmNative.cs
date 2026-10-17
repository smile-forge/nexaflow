using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// Windows' colour engine, Image Color Management (<c>mscms.dll</c>): profiles opened from memory, a transform from one
/// to another, and colours and samples put through it.
/// </summary>
internal static class IcmNative
{
    public const uint ProfileInMemory = 2;          // PROFILE_MEMBUFFER
    public const uint ReadAccess = 1;               // PROFILE_READ
    public const uint ShareRead = 1;                // FILE_SHARE_READ
    public const uint OpenExisting = 3;             // OPEN_EXISTING
    public const uint Perceptual = 0;               // INTENT_PERCEPTUAL
    public const uint RelativeColorimetric = 1;     // INTENT_RELATIVE_COLORIMETRIC
    public const uint Saturation = 2;               // INTENT_SATURATION
    public const uint AbsoluteColorimetric = 3;     // INTENT_ABSOLUTE_COLORIMETRIC
    public const uint BestMode = 3;                 // BEST_MODE
    public const uint AnyEngine = 0;                // INDEX_DONT_CARE

    // Bitmap formats, by the order their bytes lie in memory, which is not always the order the name says.
    public const int GraySamples = 0x209;           // BM_GRAY
    public const int RgbSamples = 0x04;             // BM_BGRTRIPLETS: red, green, blue
    public const int CmykSamples = 0x305;           // BM_KYMCQUADS: cyan, magenta, yellow, black
    public const int LabSamples = 0x203;            // BM_LabTRIPLETS: L*, a*, b*
    public const int XyzSamples = 0x201;            // BM_XYZTRIPLETS: X, Y, Z
    public const int Lab16Samples = 0x503;          // BM_16b_Lab: L*, a*, b*, sixteen bits each

    // Single colours, sixteen bits a component.
    public const int GrayColour = 1;                // COLOR_GRAY
    public const int RgbColour = 2;                 // COLOR_RGB
    public const int XyzColour = 3;                 // COLOR_XYZ
    public const int LabColour = 5;                 // COLOR_Lab
    public const int CmykColour = 7;                // COLOR_CMYK

    /// <summary><c>PROFILE</c>: where a profile is, here a buffer in memory.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ProfileLocation
    {
        public uint Type;
        public nint Data;
        public uint Size;
    }

    /// <summary>
    /// <c>COLOR</c>: one colour, its components sixteen bits each in the order its colour type names them. The union
    /// is sixteen bytes on x64, as large as its widest member, a DWORD and a pointer.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct Colour
    {
        [FieldOffset(0)] public ushort First;
        [FieldOffset(2)] public ushort Second;
        [FieldOffset(4)] public ushort Third;
        [FieldOffset(6)] public ushort Fourth;
    }

    [DllImport("mscms.dll", SetLastError = true, ExactSpelling = true)]
    public static extern nint OpenColorProfileW(ref ProfileLocation profile, uint access, uint share, uint creation);

    [DllImport("mscms.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseColorProfile(nint profile);

    [DllImport("mscms.dll", SetLastError = true, ExactSpelling = true)]
    public static extern IcmTransformHandle CreateMultiProfileTransform(
        nint[] profiles, uint profileCount, uint[] intents, uint intentCount, uint flags, uint preferredEngine);

    [DllImport("mscms.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteColorTransform(nint transform);

    [DllImport("mscms.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateBitmapBits(
        IcmTransformHandle transform, ref byte input, int inputFormat, uint width, uint height, uint inputStride,
        ref byte output, int outputFormat, uint outputStride, nint callback, nint callbackData);

    [DllImport("mscms.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateColors(
        IcmTransformHandle transform, ref Colour input, uint count, int inputType, out Colour output, int outputType);
}

/// <summary>A colour transform Windows made, deleted when nothing holds it.</summary>
internal sealed class IcmTransformHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public IcmTransformHandle() : base(ownsHandle: true) { }

    protected override bool ReleaseHandle() => IcmNative.DeleteColorTransform(handle);
}
