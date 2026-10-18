using System.Globalization;
using Nexaflow.Features.Pdf.Document;

namespace Nexaflow.Features.Pdf.Rendering;

/// <summary>
/// How a link to a place in the same document is written as a link target: <c>#page=N</c>, the fragment form PDF
/// readers already open, with <c>&amp;top=y</c> when the link names a height on the page (in the page's default user
/// space). The viewer reads it back with <see cref="TryRead"/>.
/// </summary>
internal static class PdfLinks
{
    private const string Prefix = "#page=";
    private const string TopKey = "&top=";

    public static string ToPage(PdfPageTarget target) => target.Top is { } top
        ? $"{Prefix}{target.PageNumber}{TopKey}{top.ToString("R", CultureInfo.InvariantCulture)}"
        : $"{Prefix}{target.PageNumber}";

    /// <summary>The page target <paramref name="href"/> names, when it names one.</summary>
    public static bool TryRead(string? href, out PdfPageTarget target)
    {
        target = new PdfPageTarget(0, null);
        if (href is null || !href.StartsWith(Prefix, System.StringComparison.Ordinal)) return false;

        var rest = href[Prefix.Length..];
        var split = rest.IndexOf(TopKey, System.StringComparison.Ordinal);
        var pageText = split < 0 ? rest : rest[..split];
        if (!int.TryParse(pageText, NumberStyles.None, CultureInfo.InvariantCulture, out var page) || page < 1) return false;

        double? top = null;
        if (split >= 0 && double.TryParse(rest[(split + TopKey.Length)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            top = y;

        target = new PdfPageTarget(page, top);
        return true;
    }
}
