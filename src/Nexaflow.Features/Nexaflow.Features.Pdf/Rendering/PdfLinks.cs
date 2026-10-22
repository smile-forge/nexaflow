using System.Globalization;
using System.Text;
using Nexaflow.Features.Pdf.Document;

namespace Nexaflow.Features.Pdf.Rendering;

/// <summary>
/// How a link to a place in the same document is written as a link target: <c>#page=N</c>, the fragment form PDF
/// readers already open, with <c>&amp;top=y</c> and <c>&amp;left=x</c> when the link names a height or a left edge on
/// the page (in the page's default user space). The viewer reads it back with <see cref="TryRead"/>.
/// </summary>
internal static class PdfLinks
{
    private const string Prefix = "#page=";
    private const string TopKey = "top=";
    private const string LeftKey = "left=";

    public static string ToPage(PdfPageTarget target)
    {
        var href = new StringBuilder(Prefix).Append(target.PageNumber.ToString(CultureInfo.InvariantCulture));
        if (target.Top is { } top) href.Append('&').Append(TopKey).Append(top.ToString("R", CultureInfo.InvariantCulture));
        if (target.Left is { } left) href.Append('&').Append(LeftKey).Append(left.ToString("R", CultureInfo.InvariantCulture));
        return href.ToString();
    }

    /// <summary>The page target <paramref name="href"/> names, when it names one.</summary>
    public static bool TryRead(string? href, out PdfPageTarget target)
    {
        target = new PdfPageTarget(0, null);
        if (href is null || !href.StartsWith(Prefix, System.StringComparison.Ordinal)) return false;

        var pieces = href[Prefix.Length..].Split('&');
        if (!int.TryParse(pieces[0], NumberStyles.None, CultureInfo.InvariantCulture, out var page) || page < 1) return false;

        double? top = null, left = null;
        foreach (var piece in pieces.Skip(1))
        {
            if (piece.StartsWith(TopKey, System.StringComparison.Ordinal)) top = Coordinate(piece[TopKey.Length..]);
            else if (piece.StartsWith(LeftKey, System.StringComparison.Ordinal)) left = Coordinate(piece[LeftKey.Length..]);
        }

        target = new PdfPageTarget(page, top, left);
        return true;
    }

    private static double? Coordinate(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
}
