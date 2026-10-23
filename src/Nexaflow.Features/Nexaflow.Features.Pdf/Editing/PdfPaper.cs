using System.Windows;

namespace Nexaflow.Features.Pdf.Editing;

/// <summary>Which way up a page is set on its paper.</summary>
public enum PdfOrientation
{
    /// <summary>Whichever way the page fits larger.</summary>
    Auto,
    Portrait,
    Landscape,
}

/// <summary>What happens to a page's content when it is set on paper of another size.</summary>
public enum PdfContentFit
{
    /// <summary>Made as large as fits the paper, kept in proportion, and centred.</summary>
    Scale,

    /// <summary>Kept its size and centred: a margin round it on larger paper, its edges cut off on smaller.</summary>
    Keep,
}

/// <summary>A page set on paper: the paper, in points, which way up, and what its content does.</summary>
internal readonly record struct PdfPageSizing(Size Paper, PdfOrientation Orientation, PdfContentFit Fit)
{
    /// <summary>
    /// How a page <paramref name="content"/> large in points is set on the paper: the transform that puts it there, and
    /// the paper turned the way it is used.
    /// </summary>
    public (PdfPageTransform Place, Size Paper) SetOnPaper(Size content)
    {
        var portrait = new Size(Math.Min(Paper.Width, Paper.Height), Math.Max(Paper.Width, Paper.Height));
        var landscape = new Size(portrait.Height, portrait.Width);
        var paper = Orientation switch
        {
            PdfOrientation.Portrait  => portrait,
            PdfOrientation.Landscape => landscape,
            _                        => ScaleOn(content, landscape) > ScaleOn(content, portrait) ? landscape : portrait,
        };

        var scale = Fit == PdfContentFit.Scale ? ScaleOn(content, paper) : 1;
        var place = PdfPageTransform.Scaled(scale, scale)
            .Then(PdfPageTransform.Translate((paper.Width - content.Width * scale) / 2, (paper.Height - content.Height * scale) / 2));
        return (place, paper);
    }

    private static double ScaleOn(Size content, Size paper) => Math.Min(paper.Width / content.Width, paper.Height / content.Height);
}

/// <summary>
/// What a sheet of pages merged is made as large as: its pages put side by side, nothing scaled; the paper its pages are
/// printed on, the largest of them, the pages scaled down to share it; or <see cref="Paper"/>.
/// </summary>
/// <param name="Paper">The paper, in points, for a sheet made as large as a paper chosen.</param>
/// <param name="OfThePages">Whether the paper is the pages' own.</param>
public readonly record struct PdfMergePaper(Size? Paper, bool OfThePages = false)
{
    /// <summary>As large as its pages put side by side.</summary>
    public static PdfMergePaper None => default;

    /// <summary>The paper its pages are printed on.</summary>
    public static PdfMergePaper ThePagesOwn => new(null, OfThePages: true);

    public static PdfMergePaper Of(Size paper) => new(paper);
}

/// <summary>A paper size by name, in points.</summary>
/// <param name="Key">What it is called, as the end of its string's key: <c>Pdf.Paper.{Key}</c>.</param>
public sealed record PdfPaper(string Key, Size Size)
{
    private const double PointsPerMillimetre = 72 / 25.4;

    public static PdfPaper A3 { get; } = Millimetres("A3", 297, 420);

    public static PdfPaper A4 { get; } = Millimetres("A4", 210, 297);

    public static PdfPaper A5 { get; } = Millimetres("A5", 148, 210);

    public static PdfPaper Letter { get; } = new("Letter", new Size(612, 792));

    public static PdfPaper Legal { get; } = new("Legal", new Size(612, 1008));

    public static PdfPaper Tabloid { get; } = new("Tabloid", new Size(792, 1224));

    /// <summary>The papers offered, ISO's then North America's.</summary>
    public static IReadOnlyList<PdfPaper> Named { get; } = [A3, A4, A5, Letter, Legal, Tabloid];

    private static PdfPaper Millimetres(string key, double width, double height) =>
        new(key, new Size(width * PointsPerMillimetre, height * PointsPerMillimetre));
}
