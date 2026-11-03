using System.Buffers.Binary;
using System.IO;
using Nexaflow.IO.Common.Imaging;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.SceneDepth;

/// <summary>
/// The scene depth of an upright photo as <b>nearness</b>: 1 where the estimator saw the nearest part, 0 the farthest — its
/// disparity spread over 0 to 1 — at the size the estimator read the photo, its corners on the photo's corners. Disparity is
/// what a lens's blur goes by: how far a part is out of focus grows with how far its disparity is from the focus's.
/// <para>
/// Disparity falls as one over distance, so on its own scale everything past a few times the nearest part's distance crowds
/// together near 0. What a person — or an assistant — means by near and far is told on <b>distance</b> instead
/// (<see cref="Distance"/>): how many times farther a part is than the nearest, on a log scale from 0 at the nearest to 1 at
/// <see cref="DistanceRange"/> times as far, so each third of it is ten times as far as the last. The scale is the
/// picture's own: it says how many times farther, not how many metres.
/// </para>
/// </summary>
public sealed class SceneDepthPlane
{
    private const uint Magic = 0x4453584E;   // "NXSD"
    private const int FormatVersion = 2;

    private const int HeaderLength = 24;

    /// <summary>How many times the nearest part's distance a distance of 1 stands for; anything farther — the sky — is 1 too.</summary>
    public const double DistanceRange = 1000;

    private readonly float[] _nearness;

    /// <param name="farthestToNearest">The farthest part's disparity over the nearest's, 0 to 1: what turns a nearness back into
    /// how many times farther than the nearest part it is. 0 where the farthest is as good as endlessly far, as a sky is.</param>
    public SceneDepthPlane(float[] nearness, int width, int height, double farthestToNearest = 0)
    {
        if (width <= 0 || height <= 0 || nearness.Length != width * height)
            throw new ArgumentException($"A {width}x{height} plane holds {width * height} values, not {nearness.Length}.", nameof(nearness));
        _nearness = nearness;
        Width = width;
        Height = height;
        FarthestToNearest = Math.Clamp(farthestToNearest, 0, 1);
    }

    /// <summary>The farthest part's disparity over the nearest's, 0 to 1.</summary>
    public double FarthestToNearest { get; }

    /// <summary>
    /// How far a part of nearness <paramref name="nearness"/> lies, 0 (the nearest part) to 1 (<see cref="DistanceRange"/> times
    /// as far, or farther): the log of how many times farther it is than the nearest, its disparity being one over its distance.
    /// </summary>
    public float Distance(float nearness)
    {
        double disparity = FarthestToNearest + Math.Clamp(nearness, 0f, 1f) * (1 - FarthestToNearest);
        return disparity <= 1 / DistanceRange ? 1f : (float)Math.Clamp(-Math.Log(disparity) / Math.Log(DistanceRange), 0, 1);
    }

    /// <summary>Each of <paramref name="nearness"/> as a distance (<see cref="Distance"/>).</summary>
    public float[] Distances(float[] nearness)
    {
        var distances = new float[nearness.Length];
        Parallel.For(0, nearness.Length / 4096 + 1, block =>
        {
            for (int i = block * 4096, end = Math.Min(nearness.Length, i + 4096); i < end; i++) distances[i] = Distance(nearness[i]);
        });
        return distances;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Each value's nearness, row by row.</summary>
    public ReadOnlySpan<float> Nearness => _nearness;

    /// <summary>The estimator's disparity, larger nearer, spread over 0 to 1: its least 0, its most 1 — all 1 where it is flat —
    /// with the least over the most kept, for distance.</summary>
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
        return new SceneDepthPlane(nearness, width, height, span > 0 && most > 0 ? Math.Max(0, least) / most : 1);
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

    /// <summary>The plane as it is kept: a header — its size and the farthest over the nearest — then each nearness in 16 bits.</summary>
    public void Write(Stream into)
    {
        Span<byte> header = stackalloc byte[HeaderLength];
        BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], FormatVersion);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], Width);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], Height);
        BinaryPrimitives.WriteDoubleLittleEndian(header[16..], FarthestToNearest);
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
        if (kept.Length < HeaderLength || BinaryPrimitives.ReadUInt32LittleEndian(kept) != Magic || BinaryPrimitives.ReadInt32LittleEndian(kept[4..]) != FormatVersion)
            throw new InvalidDataException("This is not a scene depth this build keeps.");
        int width = BinaryPrimitives.ReadInt32LittleEndian(kept[8..]), height = BinaryPrimitives.ReadInt32LittleEndian(kept[12..]);
        if (width <= 0 || height <= 0 || kept.Length != HeaderLength + (long)width * height * 2)
            throw new InvalidDataException("The kept scene depth is cut short.");
        double farthestToNearest = BinaryPrimitives.ReadDoubleLittleEndian(kept[16..]);
        var nearness = new float[width * height];
        var values = kept[HeaderLength..];
        for (int i = 0; i < nearness.Length; i++) nearness[i] = BinaryPrimitives.ReadUInt16LittleEndian(values[(i * 2)..]) / (float)ushort.MaxValue;
        return new SceneDepthPlane(nearness, width, height, farthestToNearest);
    }
}
