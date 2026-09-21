using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Settings;
using Nexaflow.Markdown.WordCloud;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.WordCloud;

/// <summary>The pieces a cloud's layout is made of.</summary>
public static class WordCloudPiece
{
    /// <summary>The whole picture, whatever of it the words reach.</summary>
    public const string Cloud = "Cloud";

    /// <summary>One word, where it landed.</summary>
    public const string Word = "Word";
}

/// <summary>
/// Lays a <c>wordcloud</c> block out: words sized by weight, packed outwards from the middle by
/// <see cref="WordCloudBoard"/>. Placement (<c>Nexaflow.Markdown</c>, grid-only, desktop-free) and glyph
/// shape (only a type engine knows) are split apart; this side sets each word, gets its letter outlines from
/// the type engine, fills them onto the grid and hands the result to the packer — <c>wordcloud2.js</c>'s
/// canvas-and-pixels step done with outlines instead of pixels. Every word keeps its own source part so it
/// stays editable; weights are edited in the source, not drawn.
/// </summary>
internal sealed class WordCloudBuilder : ContentBuilder
{
    /// <summary>The face the source is shown in when the block cannot be read as a cloud at all.</summary>
    private static readonly FontFamily SourceFont = new("Cascadia Code, Consolas, monospace");

    private const double SourceSize = 12;

    /// <summary>How finely a letter's curves are broken into straight lines before they are filled.</summary>
    private const double Tolerance = 0.25;

    /// <summary>How much smaller a word is tried at when there was no room for it at the size it asked for.</summary>
    private const double Shrink = 0.85;

    /// <summary>The size letters are set at before they are scaled to the room — big enough for their curves to survive filling.</summary>
    private const double StencilSize = 200;

    /// <summary>The widest a picture is read as a shape. A photograph holds the same silhouette at a tenth of the size.</summary>
    private const double StencilPixels = 480;

    private readonly MarkdownPalette _palette;
    private readonly double _room;
    /// <summary>How a picture named by <c>mask:</c> is found, or null where there is nowhere to look.</summary>
    private readonly Func<string, ImageSource?>? _pictures;

    private WordCloudSettings _settings = WordCloudSettings.Default;

    private WordCloudBuilder(ContentReading reading, MarkdownPalette palette, double room,
                             Func<string, ImageSource?>? pictures)
        : base(reading)
    {
        _palette = palette;
        _room = room;
        _pictures = pictures;
    }

    /// <summary>Lays a block's source out. Never null, and never throws.</summary>
    public static Laid Build(string source, MarkdownPalette palette, double room,
                             Func<string, ImageSource?>? pictures = null, int at = 0) =>
        new WordCloudBuilder(ContentReading.Of(WordCloudParser.Parse(source), at), palette, room, pictures).Lay();

    /// <summary>
    /// The element a cloud is shown in. Editable, because the words in it are words somebody typed — unlike a
    /// 2D code, where everything drawn was worked out from what was typed and none of it can be typed back
    /// into.
    /// </summary>
    public static Editing.ContentElement Element(string source, DiagramRenderOptions options) =>
        new Editing.ContentElement(source, options.Palette,
            (state, room) => Build(state.Source, options.Palette, room, options.Pictures))
        {
            // Where the block's lines sit inside the fence that produced them, so an edit to a word is
            // spliced back where it came from rather than a couple of lines early.
            SourceStart = options.SourceOffset,
            SourceLength = source.Length,

            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 10),
        };

    protected override Laid? Build()
    {
        if (!WordCloudReader.TryRead(Reading.Root, out var chart, out var error)) return Stopped(error!);

        _settings = chart!.Settings;

        var colours = new WordCloudRandom(_settings.Seed + 2);
        if (!WordCloudInk.TryRead(_settings, _palette, colours, out var ink, out error)) return Stopped(error!);
        if (!WordCloudInk.TryBackground(_settings, out var background, out error)) return Stopped(error!);

        if (chart.Words.Count == 0)
            return Stopped("An empty word cloud. It takes a `word: weight` line for each word, "
                         + "and settings written the same way.");

        return Lay(chart, ink!, background);
    }

    /// <summary>One word, measured and placed: what to draw, where, turned how far, and in what.</summary>
    /// <param name="At">Where the word is set — the origin its turn is about, not the top left of its letters.</param>
    private sealed record Placement(FormattedText Text, double Room, Point At, double Turn, Brush Ink, ContentPart Part)
    {
        /// <summary>The rectangle its letters are drawn in, turn and all.</summary>
        public Rect Covers
        {
            get
            {
                var box = new Rect(0, 0, Text.Width, Text.Height);
                if (Turn != 0) box = new RotateTransform(Turn).TransformBounds(box);

                box.Offset(At.X, At.Y);
                return box;
            }
        }
    }

    private Laid Lay(WordCloudChart chart, WordCloudInk ink, Brush? background)
    {
        var (room, fall) = SettingRoom.Fit(_settings.Width, _settings.Height, _room,
                                                  WordCloudSettings.HeightShare);

        var stencil = Stencil(room, fall, out var lost);
        if (lost is not null) return Stopped(lost);

        // Fitted without stretching so `letters:` keeps its own proportions rather than the column's; empty
        // room is trimmed away at the end regardless.
        var width = room;
        var height = fall;

        if (stencil is not null)
        {
            var aspect = (double)stencil.Across / stencil.Down;
            if (room / fall > aspect) width = fall * aspect;
            else height = room / aspect;
        }

        // Separate RNG from the packing's, so toggling shuffling doesn't also change every word's angle.
        var angles = new WordCloudRandom(_settings.Seed);

        // The stencil is the shape now, so the usual shape/ellipticity squash is set aside — rings must reach
        // every corner of it.
        var packing = stencil is null
            ? _settings
            : _settings with { Shape = WordCloudShape.Circle, Ellipticity = 1 };

        var board = new WordCloudBoard(width, height, packing, new WordCloudRandom(_settings.Seed + 1), stencil);

        var trouble = new List<Diagnostic>();

        // Unparseable lines (e.g. mid-edit) are flagged in place; the rest of the cloud still renders.
        foreach (var word in chart.Words)
            if (word.Trouble is { } reason)
                trouble.Add(Say(word.Number ?? word.Word, reason, DiagnosticSeverity.Error));

        var placed = new List<Placement>();
        var at = 0;

        foreach (var word in chart.Drawable)
        {
            var turn = angles.Angle(_settings);
            var colour = ink.For(at++);

            if (Place(board, word, chart.SizeOf(word.Weight), turn, colour) is { } set) placed.Add(set);
            else trouble.Add(Say(word.Word, $"There was no room left in the cloud for {word.Text}.",
                                 DiagnosticSeverity.Warning));
        }

        // Sized to what was actually drawn, not the room offered — else a small cloud in a wide column would
        // reserve the whole column's width.
        var drawn = placed.Count == 0 ? new Rect(0, 0, width, height) : Covered(placed);
        var shift = new Vector(-drawn.X, -drawn.Y);

        var build = new LayoutBuilder();
        build.Open(WordCloudPiece.Cloud);

        var picture = new Rect(drawn.Size);
        if (background is not null) build.Draw(new RuleMark(picture, background));
        build.Covers(picture);

        foreach (var set in placed)
            LayoutText.Words(build, set.Text, set.At + shift, set.Room, TextAlignment.Left,
                             set.Part, WordCloudPiece.Word, maps: true, ink: set.Ink, degrees: set.Turn);

        build.Close();

        return new Laid(build.Seal(), drawn.Size, trouble);
    }

    /// <summary>Places a word, shrinking it step by step until it fits or hits MinSize (null if it never
    /// fits) — <c>wordcloud2.js</c>'s <c>shrinkToFit</c>.</summary>
    private Placement? Place(WordCloudBoard board, WordCloudEntry word, double size, double turn, Brush ink)
    {
        for (var at = size; at >= _settings.MinSize; at *= Shrink)
        {
            var text = Set(word.Text, at);
            var room = Math.Max(1, text.Width);

            // Set to the same width the layout will set it to, so the outlines filled onto the grid are the
            // outlines of the word as it is finally drawn.
            text.MaxTextWidth = room;

            var mask = WordMask.Of(Outline(text, turn), _settings.GridSize, _settings.Gap);

            if (mask is not null && board.TryPlace(mask, out var spot))
                return new Placement(text, room, new Point(spot.X - mask.Left, spot.Y - mask.Top), turn, ink, word.Word);

            if (!_settings.Fit) return null;
        }

        return null;
    }

    /// <summary>Everything the words between them cover — which is the picture, once it is brought to the origin.</summary>
    private static Rect Covered(IReadOnlyList<Placement> placed)
    {
        var all = placed[0].Covers;
        for (var at = 1; at < placed.Count; at++) all.Union(placed[at].Covers);
        return all;
    }

    /// <summary>The packing shape (null for the whole picture), or the reason it couldn't be built — stops
    /// the block rather than silently drawing the wrong shape.</summary>
    private WordCloudStencil? Stencil(double width, double height, out string? trouble)
    {
        trouble = null;

        if (_settings.Mask is { Length: > 0 } picture) return FromPicture(picture, out trouble);
        if (_settings.Letters is { Length: > 0 } letters) return FromLetters(letters, width, height, out trouble);

        return null;
    }

    /// <summary>Letters filled as a packing shape, sampled a pixel to the cell — finer than the board's own
    /// grid since a letter is mostly curves.</summary>
    private WordCloudStencil? FromLetters(string letters, double width, double height, out string? trouble)
    {
        trouble = null;

        var text = Set(letters, StencilSize);
        text.MaxTextWidth = Math.Max(1, text.Width);

        if (text.Width <= 0 || text.Height <= 0)
        {
            trouble = $"`letters: {letters}` has no letters in it to pack a cloud into.";
            return null;
        }

        var scale = Math.Max(1, Math.Min(width / text.Width, height / text.Height));
        var stencil = WordCloudStencil.Of(Outline(text, 0, scale), grid: 1);

        if (stencil is null)
            trouble = $"`letters: {letters}` draws nothing a cloud could be packed into.";

        return stencil;
    }

    /// <summary>A picture's silhouette: the opaque part if it has transparency, else the dark part — covers
    /// every mask anybody actually draws, with no setting needed to pick between them.</summary>
    private WordCloudStencil? FromPicture(string named, out string? trouble)
    {
        trouble = null;

        if (_pictures is null)
        {
            trouble = $"`mask: {named}` cannot be looked for here — this block was rendered with no document to find it beside.";
            return null;
        }

        BitmapSource? picture;
        try
        {
            picture = _pictures(named) as BitmapSource;
        }
        catch
        {
            picture = null;
        }

        if (picture is null)
        {
            trouble = $"There is no picture at `{named}`.";
            return null;
        }

        // Downscaled: the silhouette is the same at a tenth of the size, and a full photo is too many cells.
        if (picture.PixelWidth > StencilPixels)
            picture = new TransformedBitmap(picture,
                new ScaleTransform(StencilPixels / (double)picture.PixelWidth,
                                   StencilPixels / (double)picture.PixelWidth));

        var square = new FormatConvertedBitmap(picture, PixelFormats.Bgra32, null, 0);
        var across = square.PixelWidth;
        var down = square.PixelHeight;
        var stride = across * 4;
        var pixels = new byte[stride * down];
        square.CopyPixels(pixels, stride, 0);

        var seeThrough = false;
        for (var at = 3; at < pixels.Length && !seeThrough; at += 4)
            if (pixels[at] < 250) seeThrough = true;

        var stencil = WordCloudStencil.Of((x, y) =>
        {
            var at = y * stride + x * 4;
            if (pixels[at + 3] < 128) return false;
            if (seeThrough) return true;

            // Rec. 601, which is what the eye reads as dark rather than what the channels add up to.
            var light = (0.299 * pixels[at + 2] + 0.587 * pixels[at + 1] + 0.114 * pixels[at]) / 255;
            return light < 0.55;
        }, across, down);

        if (stencil is null)
            trouble = $"`mask: {named}` holds no shape — it is blank, or every part of it is the same.";

        return stencil;
    }

    /// <summary>The word's letter outlines, rotated to its set angle, as closed pixel figures for the mask.</summary>
    private static IReadOnlyList<IReadOnlyList<(double X, double Y)>> Outline(FormattedText text, double turn, double scale = 1)
    {
        var figures = new List<IReadOnlyList<(double X, double Y)>>();

        if (text.BuildGeometry(new Point(0, 0)) is not { } drawn) return figures;

        var flattened = drawn.GetFlattenedPathGeometry(Tolerance, ToleranceType.Absolute);

        var about = System.Windows.Media.Matrix.Identity;
        if (scale != 1) about.Scale(scale, scale);
        if (turn != 0) about.Rotate(turn);

        foreach (var figure in flattened.Figures)
        {
            var points = new List<(double X, double Y)> { Turned(about, figure.StartPoint) };

            foreach (var segment in figure.Segments)
                switch (segment)
                {
                    case PolyLineSegment poly:
                        foreach (var point in poly.Points) points.Add(Turned(about, point));
                        break;

                    case LineSegment line:
                        points.Add(Turned(about, line.Point));
                        break;
                }

            if (points.Count > 2) figures.Add(points);
        }

        return figures;
    }

    private static (double X, double Y) Turned(System.Windows.Media.Matrix about, Point at)
    {
        var turned = about.Transform(at);
        return (turned.X, turned.Y);
    }

    /// <summary>One word, set in the face and at the size the cloud asks for.</summary>
    private FormattedText Set(string word, double size) =>
        new(word,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily(_settings.Font), FontStyles.Normal,
                         _settings.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
            size,
            Brushes.Black,
            Editing.LayoutText.Density);

    private static Diagnostic Say(ContentPart part, string reason, DiagnosticSeverity severity) =>
        new(part.Start, Math.Max(part.Length, 1), severity, reason);

    /// <summary>Shows the block as-typed with the error above it, for a block that isn't a cloud at all.</summary>
    private Laid Stopped(string reason) =>
        LayoutText.Shown(Source, Characters(Source.Length == 0 ? " " : Source),
                         [new Diagnostic(0, Math.Max(Source.Length, 1), DiagnosticSeverity.Error, reason)]);

    protected override FormattedText Characters(string text) =>
        new(text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(SourceFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            SourceSize,
            Brushes.Black,
            Editing.LayoutText.Density);
}
