using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Layers;

/// <summary>What a layered picture's pixels hold: colour, or one grey.</summary>
public enum ColourModel
{
    Rgb,
    Gray,
}

/// <summary>How deep each channel of a layered picture's pixels is.</summary>
public enum ChannelDepth
{
    /// <summary>8-bit, 0–255.</summary>
    U8,

    /// <summary>16-bit, 0–65535.</summary>
    U16,

    /// <summary>32-bit float, light as it is — linear, and brighter than white where it is.</summary>
    F32,
}

/// <summary>
/// The colour a layered picture is held in, end to end: its pixels, masks and composite. 8- and 16-bit pictures are in
/// sRGB's encoding, composited as Photoshop does in it; 32-bit pictures are linear light, unclamped.
/// </summary>
public readonly record struct PictureColour(ColourModel Model, ChannelDepth Depth)
{
    private static readonly SKColorSpace Srgb = SKColorSpace.CreateSrgb();
    private static readonly SKColorSpace LinearSrgb = SKColorSpace.CreateSrgbLinear();

    /// <summary>8-bit colour: every picture before it was asked otherwise.</summary>
    public static PictureColour Rgb8 { get; } = new(ColourModel.Rgb, ChannelDepth.U8);

    /// <summary>The bits of each channel.</summary>
    public int Bits => Depth switch { ChannelDepth.U8 => 8, ChannelDepth.U16 => 16, _ => 32 };

    /// <summary>Its values are linear light — a 32-bit picture's.</summary>
    public bool IsLinear => Depth == ChannelDepth.F32;

    /// <summary>The space its values are in, which every surface it is drawn on is tagged with.</summary>
    public SKColorSpace ColourSpace => IsLinear ? LinearSrgb : Srgb;

    /// <summary>What a composite of it is drawn into — deep enough to keep every value it holds.</summary>
    public SKColorType SurfaceType => Depth switch
    {
        ChannelDepth.U8 => SKColorType.Rgba8888,
        ChannelDepth.U16 => SKColorType.Rgba16161616,
        _ => SKColorType.RgbaF32,
    };

    /// <summary>The tiles its pixels are held in.</summary>
    public TileFormat Tiles => TileFormat.Of(this);

    /// <summary>A surface of <paramref name="width"/> × <paramref name="height"/> to composite it onto.</summary>
    public SKImageInfo SurfaceInfo(int width, int height) => new(width, height, SurfaceType, SKAlphaType.Premul, ColourSpace);

    /// <summary>
    /// <paramref name="colour"/> — an sRGB colour as a person picks it — as this picture's values: as it is for 8 and 16 bits,
    /// made linear for 32.
    /// </summary>
    public SKColorF FromSrgb(SKColorF colour)
        => IsLinear ? new SKColorF(ToLinear(colour.Red), ToLinear(colour.Green), ToLinear(colour.Blue), colour.Alpha) : colour;

    /// <summary>This picture's value <paramref name="colour"/> as sRGB — encoded again for 32 bits, light past white kept past 1.</summary>
    public SKColorF ToSrgb(SKColorF colour)
        => IsLinear ? new SKColorF(ToEncoded(colour.Red), ToEncoded(colour.Green), ToEncoded(colour.Blue), colour.Alpha) : colour;

    /// <summary>sRGB's curve undone: an encoded value as light.</summary>
    public static float ToLinear(float encoded)
    {
        float v = Math.Abs(encoded);
        float linear = v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
        return MathF.CopySign(linear, encoded);
    }

    /// <summary>sRGB's curve: light as an encoded value.</summary>
    public static float ToEncoded(float linear)
    {
        float v = Math.Abs(linear);
        float encoded = v <= 0.0031308f ? v * 12.92f : 1.055f * MathF.Pow(v, 1 / 2.4f) - 0.055f;
        return MathF.CopySign(encoded, linear);
    }

    /// <summary>A colour's grey: Rec. 709's weighting of red, green and blue.</summary>
    public static float GreyOf(float red, float green, float blue) => 0.2126f * red + 0.7152f * green + 0.0722f * blue;
}
