using System.Buffers.Binary;
using System.IO;
using Nexaflow.IO.Common.Imaging;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.SceneDepth;

/// <summary>
/// The scene depth of an upright photo as <b>nearness</b>: 1 where the estimator saw the nearest part, 0 the farthest — its
/// disparity spread over 0 to 1 — at the size the estimator read the photo, its corners on the photo's corners. Disparity is
/// what a lens's blur goes by: how far a part is out of focus grows with how far its disparity is from the focus's. Its
/// scale is the picture's own, so nearness says what is nearer than what, not how many metres.
/// </summary>
public sealed class SceneDepthPlane
{
    private const uint Magic = 0x4453584E;   // "NXSD"
    private const int FormatVersion = 1;

    private readonly float[] _nearness;

    public SceneDepthPlane(float[] nearness, int width, int height)
    {
        if (width <= 0 || height <= 0 || nearness.Length != width * height)
            throw new ArgumentException($"A {width}x{height} plane holds {width * height} values, not {nearness.Length}.", nameof(nearness));
        _nearness = nearness;
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Each value's nearness, row by row.</summary>
    public ReadOnlySpan<float> Nearness => _nearness;

    /// <summary>The estimator's disparity, larger nearer, spread over 0 to 1: its least 0, its most 1 — all 1 where it is flat.</summary>
    public static SceneDepthPlane FromDisparity(ReadOnlySpan<float> disparity, int width, int height)
    {
        float least = float.MaxValue, most = float.MinValue;
        foreach (var d in disparity)
        {
            if (d < least) least = d;
            if (d > most) most = d;
        }
        var nearness = new float[disparity.Length];
        float span = most - least;
        if (span <= 0 || !float.IsFinite(span)) Array.Fill(nearness, 1f);
        else
            for (int i = 0; i < disparity.Length; i++) nearness[i] = Math.Clamp((disparity[i] - least) / span, 0f, 1f);
        return new SceneDepthPlane(nearness, width, height);
    }

    /// <summary>The nearness at (<paramref name="x"/>, <paramref name="y"/>) on a <paramref name="photo"/>-sized upright photo, in
    /// its pixels (the first pixel's centre at 0.5): bilinear, with the corners aligned as the estimator's makers bring its
    /// disparity to the picture.</summary>
    public float At(double x, double y, SKSizeI photo)
    {
        var (mx, my) = MapScale(photo);
        return Sample((x - 0.5) * mx, (y - 0.5) * my);
    }

    /// <summary>The nearness a lens focused at (<paramref name="x"/>, <paramref name="y"/>) on the photo is focused at: the middle of
    /// the nine values about it, so a point put on a thin edge still focuses on one side of it rather than between.</summary>
    public float FocusAt(double x, double y, SKSizeI photo)
    {
        var (mx, my) = MapScale(photo);
        int cx = (int)Math.Round(Math.Clamp((x - 0.5) * mx, 0, Width - 1)), cy = (int)Math.Round(Math.Clamp((y - 0.5) * my, 0, Height - 1));
        Span<float> about = stackalloc float[9];
        int n = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                about[n++] = _nearness[Math.Clamp(cy + dy, 0, Height - 1) * Width + Math.Clamp(cx + dx, 0, Width - 1)];
        about.Sort();
        return about[4];
    }

    /// <summary>The nearness at <paramref name="photo"/>'s own size, every pixel — as the makers' pipeline gives it.</summary>
    public float[] AtSize(SKSizeI photo) => PictureResampling.ResizePlaneBilinearAligned(_nearness, Width, Height, photo.Width, photo.Height);

    /// <summary>
    /// The nearness under every pixel of a <paramref name="canvas"/>-sized picture that <paramref name="photoToCanvas"/> draws the
    /// <paramref name="photo"/>-sized upright photo onto — what a render pass reads beside the drawn photo; past the photo's edge
    /// the edge's.
    /// </summary>
    public float[] OnCanvas(SKMatrix photoToCanvas, SKSizeI photo, SKSizeI canvas)
    {
        if (!photoToCanvas.TryInvert(out var toPhoto)) throw new InvalidOperationException("The photo is drawn to nothing.");
        var (mx, my) = MapScale(photo);
        var plane = new float[canvas.Width * canvas.Height];
        Parallel.For(0, canvas.Height, row =>
        {
            double cy = row + 0.5;
            for (int col = 0; col < canvas.Width; col++)
            {
                double cx = col + 0.5;
                double px = toPhoto.ScaleX * cx + toPhoto.SkewX * cy + toPhoto.TransX;
                double py = toPhoto.SkewY * cx + toPhoto.ScaleY * cy + toPhoto.TransY;
                plane[row * canvas.Width + col] = Sample((px - 0.5) * mx, (py - 0.5) * my);
            }
        });
        return plane;
    }

    /// <summary>How far one photo pixel steps across the plane, each way: the corners of the two meet.</summary>
    private (double X, double Y) MapScale(SKSizeI photo)
        => (photo.Width > 1 ? (Width - 1.0) / (photo.Width - 1) : 0, photo.Height > 1 ? (Height - 1.0) / (photo.Height - 1) : 0);

    /// <summary>Bilinear between the four values about (<paramref name="mx"/>, <paramref name="my"/>), the edge held past it.</summary>
    private float Sample(double mx, double my)
    {
        mx = Math.Clamp(mx, 0, Width - 1);
        my = Math.Clamp(my, 0, Height - 1);
        int x0 = (int)mx, y0 = (int)my;
        int x1 = Math.Min(x0 + 1, Width - 1), y1 = Math.Min(y0 + 1, Height - 1);
        float fx = (float)(mx - x0), fy = (float)(my - y0);
        float top = _nearness[y0 * Width + x0] * (1 - fx) + _nearness[y0 * Width + x1] * fx;
        float bottom = _nearness[y1 * Width + x0] * (1 - fx) + _nearness[y1 * Width + x1] * fx;
        return top * (1 - fy) + bottom * fy;
    }

    /// <summary>The plane as it is kept: a header, then each nearness in 16 bits.</summary>
    public void Write(Stream into)
    {
        Span<byte> header = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], FormatVersion);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], Width);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], Height);
        into.Write(header);
        var values = new byte[_nearness.Length * 2];
        for (int i = 0; i < _nearness.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(values.AsSpan(i * 2), (ushort)Math.Round(Math.Clamp(_nearness[i], 0f, 1f) * ushort.MaxValue));
        into.Write(values);
    }

    /// <summary>A plane as <see cref="Write"/> keeps it.</summary>
    /// <exception cref="InvalidDataException">It is not one, or it is cut short.</exception>
    public static SceneDepthPlane Read(ReadOnlySpan<byte> kept)
    {
        if (kept.Length < 16 || BinaryPrimitives.ReadUInt32LittleEndian(kept) != Magic || BinaryPrimitives.ReadInt32LittleEndian(kept[4..]) != FormatVersion)
            throw new InvalidDataException("This is not a scene depth Nexaflow kept.");
        int width = BinaryPrimitives.ReadInt32LittleEndian(kept[8..]), height = BinaryPrimitives.ReadInt32LittleEndian(kept[12..]);
        if (width <= 0 || height <= 0 || kept.Length != 16 + (long)width * height * 2)
            throw new InvalidDataException("The kept scene depth is cut short.");
        var nearness = new float[width * height];
        var values = kept[16..];
        for (int i = 0; i < nearness.Length; i++) nearness[i] = BinaryPrimitives.ReadUInt16LittleEndian(values[(i * 2)..]) / (float)ushort.MaxValue;
        return new SceneDepthPlane(nearness, width, height);
    }
}
