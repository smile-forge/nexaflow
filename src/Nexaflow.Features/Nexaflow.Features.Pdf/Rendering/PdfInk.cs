using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Pdf.Document;

namespace Nexaflow.Features.Pdf.Rendering;

/// <summary>
/// What a page's artwork covers, read from a picture of the page: whether it runs across the lines the page would be cut
/// along, and the part of the page it takes up. A line that crosses nothing — the gutter between two pages printed to a
/// sheet — is one colour from end to end, whatever colour the sheet is; where artwork crosses it, the line meets other
/// colours. Marks printed to cut the sheet by are not artwork: hairlines reaching in from its edge, short of its content.
/// </summary>
internal sealed class PdfInk
{
    /// <summary>How far a channel may stray from the line's own colour and still be that colour: smoothing, a scan's grain.</summary>
    private const int Tolerance = 40;

    /// <summary>The share of a line that may be other colours before artwork is said to cross it: a speck, a crop mark.</summary>
    private const double Allowance = 0.02;

    /// <summary>
    /// How far in from an edge a mark to cut by reaches, as a share of the page's longer side: what lies wholly within it at
    /// the end of a cut line, or reaches in from the page's edge no further, is a mark rather than artwork.
    /// </summary>
    private const double MarkReach = 0.08;

    /// <summary>How thick a mark to cut by is at most, as a share of the page's shorter side: a hairline, smoothed.</summary>
    private const double MarkThickness = 0.012;

    private readonly byte[] _pixels;
    private readonly int _width;
    private readonly int _height;

    private PdfInk(byte[] pixels, int width, int height)
    {
        _pixels = pixels;
        _width = width;
        _height = height;
    }

    /// <summary>The page as <paramref name="picture"/> shows it.</summary>
    public static PdfInk Of(BitmapSource picture)
    {
        var bgra = picture.Format == PixelFormats.Bgra32 || picture.Format == PixelFormats.Pbgra32
            ? picture
            : new FormatConvertedBitmap(picture, PixelFormats.Bgra32, null, 0);
        var stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);
        return new PdfInk(pixels, bgra.PixelWidth, bgra.PixelHeight);
    }

    /// <summary>
    /// Whether artwork crosses <paramref name="cut"/>: more of the line than a speck is other colours than its own, once the
    /// marks to cut by at its ends — ink lying wholly within the line's first or last stretch — are set aside.
    /// </summary>
    public bool Crosses(PdfPageCut cut)
    {
        var length = cut.Vertical ? _height : _width;
        var breadth = cut.Vertical ? _width : _height;
        if (length == 0 || breadth == 0) return false;

        var at = Math.Clamp((int)(cut.At * breadth), 0, breadth - 1);
        var line = new int[length * 3];
        for (var i = 0; i < length; i++)
        {
            var offset = cut.Vertical ? (i * _width + at) * 4 : (at * _width + i) * 4;
            line[i * 3] = _pixels[offset];
            line[i * 3 + 1] = _pixels[offset + 1];
            line[i * 3 + 2] = _pixels[offset + 2];
        }

        var own = new[] { Median(line, 0, length), Median(line, 1, length), Median(line, 2, length) };
        var differs = new bool[length];
        for (var i = 0; i < length; i++)
        {
            differs[i] = Math.Abs(line[i * 3] - own[0]) > Tolerance
                         || Math.Abs(line[i * 3 + 1] - own[1]) > Tolerance
                         || Math.Abs(line[i * 3 + 2] - own[2]) > Tolerance;
        }

        // Ink at either end of the line, none of it carrying on further in than a mark reaches, is the marks the sheet is cut by.
        var reach = Math.Min(length / 2, (int)Math.Ceiling(MarkReach * Math.Max(_width, _height)));
        SetAsideMark(differs, 0, reach);
        SetAsideMark(differs, length - reach, reach);

        return differs.Count(d => d) > Allowance * length;
    }

    /// <summary>
    /// The ink in the <paramref name="count"/> pixels of a line from <paramref name="from"/> — a stretch at one of its ends — set
    /// aside as a mark to cut by, unless it carries on past the stretch toward the line's middle, as artwork crossing would.
    /// </summary>
    private static void SetAsideMark(bool[] differs, int from, int count)
    {
        if (count <= 0) return;

        var (inner, beyond) = from == 0 ? (count - 1, count) : (from, from - 1);
        if (beyond >= 0 && beyond < differs.Length && differs[inner] && differs[beyond]) return;
        Array.Fill(differs, false, from, count);
    }

    /// <summary>
    /// The part of the page its artwork covers — everything not the paper's own colour, the colour round its edges, but the
    /// marks to cut by, hairlines reaching in from the edge — as fractions of the picture's width and height; null for a page
    /// with nothing on it.
    /// </summary>
    public System.Windows.Rect? Extent()
    {
        if (_width == 0 || _height == 0) return null;

        var paper = PaperColour();
        var inked = new bool[_width * _height];
        for (var i = 0; i < inked.Length; i++)
        {
            var offset = i * 4;
            inked[i] = Math.Abs(_pixels[offset] - paper[0]) > Tolerance
                       || Math.Abs(_pixels[offset + 1] - paper[1]) > Tolerance
                       || Math.Abs(_pixels[offset + 2] - paper[2]) > Tolerance;
        }

        var thin = Math.Max(1, (int)Math.Ceiling(MarkThickness * Math.Min(_width, _height)));
        var reach = (int)Math.Ceiling(MarkReach * Math.Max(_width, _height));
        int left = _width, top = _height, right = -1, bottom = -1;
        var seen = new bool[inked.Length];
        var pending = new Stack<int>();
        for (var start = 0; start < inked.Length; start++)
        {
            if (!inked[start] || seen[start]) continue;

            // One stroke or shape: every inked pixel joined to this one.
            int x0 = _width, y0 = _height, x1 = -1, y1 = -1;
            seen[start] = true;
            pending.Push(start);
            while (pending.Count > 0)
            {
                var at = pending.Pop();
                int x = at % _width, y = at / _width;
                x0 = Math.Min(x0, x); x1 = Math.Max(x1, x);
                y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                if (x > 0) Visit(at - 1);
                if (x < _width - 1) Visit(at + 1);
                if (y > 0) Visit(at - _width);
                if (y < _height - 1) Visit(at + _width);
            }

            int across = x1 - x0 + 1, down = y1 - y0 + 1;
            var atEdge = x0 <= 1 || y0 <= 1 || x1 >= _width - 2 || y1 >= _height - 2;
            if (atEdge && Math.Min(across, down) <= thin && Math.Max(across, down) <= reach) continue;   // a mark to cut by

            left = Math.Min(left, x0); right = Math.Max(right, x1);
            top = Math.Min(top, y0); bottom = Math.Max(bottom, y1);
        }

        if (right < 0) return null;
        return new System.Windows.Rect(
            (double)left / _width, (double)top / _height, (double)(right - left + 1) / _width, (double)(bottom - top + 1) / _height);

        void Visit(int at)
        {
            if (!inked[at] || seen[at]) return;
            seen[at] = true;
            pending.Push(at);
        }
    }

    /// <summary>The paper's own colour: the middle colour round the picture's edges, channel by channel.</summary>
    private int[] PaperColour()
    {
        var edge = new List<int>((_width + _height) * 2 * 3);
        for (var x = 0; x < _width; x++)
        {
            Add(x, 0);
            Add(x, _height - 1);
        }

        for (var y = 1; y < _height - 1; y++)
        {
            Add(0, y);
            Add(_width - 1, y);
        }

        var line = edge.ToArray();
        var count = line.Length / 3;
        return [Median(line, 0, count), Median(line, 1, count), Median(line, 2, count)];

        void Add(int x, int y)
        {
            var offset = (y * _width + x) * 4;
            edge.Add(_pixels[offset]);
            edge.Add(_pixels[offset + 1]);
            edge.Add(_pixels[offset + 2]);
        }
    }

    /// <summary>The middle value of one channel along the line.</summary>
    private static int Median(int[] line, int channel, int length)
    {
        var values = new int[length];
        for (var i = 0; i < length; i++)
            values[i] = line[i * 3 + channel];
        Array.Sort(values);
        return values[length / 2];
    }
}
