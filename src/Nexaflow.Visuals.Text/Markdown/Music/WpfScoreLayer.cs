using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Scores;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Music.Rendering;

namespace Nexaflow.Visuals.Text.Markdown.Music;

/// <summary>
/// The engraver's layer onto the WPF layout tree: each piece opened in a <see cref="LayoutBuilder"/>, each glyph a filled outline
/// of Bravura (<see cref="SmuflOutlines"/>), each word set by WPF's type engine (<see cref="ScoreText"/>).
/// </summary>
internal sealed class WpfScoreLayer(StyleFormat style) : IScoreLayer
{
    private readonly LayoutBuilder _build = new();

    /// <summary>What was engraved, once it is sealed.</summary>
    public LayoutTree? Tree { get; private set; }

    public int Open(string kind, ISourcePart? part, LayoutPoint at) => _build.Open(kind, part, Wpf(at));

    public void Close() => _build.Close();

    public LayoutVector Anchor => new(_build.Anchor.X, _build.Anchor.Y);

    public LayoutRect Reached => Layout(_build.Reached);

    public void Reserves(double top, double height) => _build.Reserves(top, height);

    public void Covers(LayoutRect what) => _build.Covers(Wpf(what));

    public void Runs(IReadOnlyList<int> members, bool vertical) => _build.Runs(members, vertical);

    public LayoutRect Seal()
    {
        Tree = _build.Seal();
        return Layout(Tree.Root.Box);
    }

    public bool Glyph(int codepoint, LayoutPoint baseline, double staffSpace, double scale)
    {
        if (SmuflOutlines.Outline(codepoint, Wpf(baseline), staffSpace, scale) is not { } outline) return false;

        _build.Draw(GeometryMark.Filled(outline));
        return true;
    }

    public void Rule(LayoutRect box) => _build.Draw(new RuleMark(Wpf(box), null));

    public void Box(LayoutRect box)
    {
        var stem = new RectangleGeometry(Wpf(box));
        stem.Freeze();
        _build.Draw(GeometryMark.Filled(stem));
    }

    public void Inked(LayoutRect box) => _build.Draw(new GeometryMark(new RectangleGeometry(Wpf(box)), style.Text, null, 0));

    public void Dot(LayoutRect box)
    {
        var dot = new EllipseGeometry(Wpf(box));
        dot.Freeze();
        _build.Draw(GeometryMark.Filled(dot));
    }

    public void Ring(LayoutPoint centre, double radiusX, double radiusY, double thickness)
    {
        var ring = new EllipseGeometry(Wpf(centre), radiusX, radiusY);
        ring.Freeze();
        _build.Draw(new GeometryMark(ring, null, null, thickness));
    }

    public void Polygon(LayoutPoint first, LayoutPoint second, LayoutPoint third, LayoutPoint fourth)
    {
        var shape = new PathGeometry();
        var figure = new PathFigure { StartPoint = Wpf(first), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new LineSegment(Wpf(second), false));
        figure.Segments.Add(new LineSegment(Wpf(third), false));
        figure.Segments.Add(new LineSegment(Wpf(fourth), false));
        shape.Figures.Add(figure);
        shape.Freeze();

        _build.Draw(GeometryMark.Filled(shape));
    }

    public void Crescent(LayoutPoint from, LayoutPoint bow, LayoutPoint to, LayoutPoint back)
    {
        var path = new PathGeometry();
        var figure = new PathFigure { StartPoint = Wpf(from), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new QuadraticBezierSegment(Wpf(bow), Wpf(to), false));
        figure.Segments.Add(new QuadraticBezierSegment(Wpf(back), Wpf(from), false));
        path.Figures.Add(figure);
        path.Freeze();

        _build.Draw(GeometryMark.Filled(path));
    }

    public void Stroke(LayoutPoint from, LayoutPoint to, double thickness)
    {
        var line = new LineGeometry(Wpf(from), Wpf(to));
        line.Freeze();
        _build.Draw(new GeometryMark(line, null, null, thickness));
    }

    public IScoreWords Words(string text, double size, bool semibold = false, bool italic = false) =>
        new WpfScoreWords(ScoreText.Build(text, size, semibold ? FontWeights.SemiBold : null, italic ? FontStyles.Italic : null));

    public IScoreWords Chord(string text, double size) => new WpfScoreWords(ScoreText.Chord(text, size));

    public void Text(IScoreWords words, LayoutPoint at) => _build.Draw(new TextMark(((WpfScoreWords)words).Glyphs, Wpf(at), null));

    public void Prose(IScoreWords words, LayoutPoint at, double room, LayoutAlign align, ISourcePart? part, string kind,
                      IReadOnlyList<ISourcePart>? letters) =>
        LayoutText.Place(_build, ((WpfScoreWords)words).Glyphs, Wpf(at), room, align switch
        {
            LayoutAlign.Centre => TextAlignment.Center,
            LayoutAlign.Right => TextAlignment.Right,
            LayoutAlign.Justify => TextAlignment.Justify,
            _ => TextAlignment.Left,
        }, part, kind, letters);

    public double Advance(int codepoint, double staffSpace, double scale = 1.0) => SmuflOutlines.Advance(codepoint, staffSpace, scale);

    public double Width(string text, double size) => ScoreText.Width(text, size);

    private static Point Wpf(LayoutPoint point) => new(point.X, point.Y);

    private static Rect Wpf(LayoutRect rect) => rect.IsEmpty ? Rect.Empty : new Rect(rect.X, rect.Y, rect.Width, rect.Height);

    private static LayoutRect Layout(Rect rect) => rect.IsEmpty ? LayoutRect.Empty : new LayoutRect(rect.X, rect.Y, rect.Width, rect.Height);
}

/// <summary>Words WPF's type engine set for a score.</summary>
internal sealed class WpfScoreWords(FormattedText glyphs) : IScoreWords
{
    public FormattedText Glyphs { get; } = glyphs;

    public double Width => Glyphs.Width;

    public double Height => Glyphs.Height;

    public (double Top, double Height) Letters => (Glyphs.Height + Glyphs.OverhangAfter - Glyphs.Extent, Glyphs.Extent);
}
