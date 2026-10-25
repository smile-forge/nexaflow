using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Settings;
using Nexaflow.Markdown.WordCloud;

namespace Nexaflow.Markdown.Layout.WordCloud;

/// <summary>
/// Lays a <c>wordcloud</c> block out onto the renderer-free layout tree: words sized by weight, packed outwards from the middle by
/// <see cref="WordCloudBoard"/>. Placement works on a grid and glyph shape is the medium's; this side sets each word, has the medium
/// outline its letters, fills them onto the grid and hands the result to the packer — <c>wordcloud2.js</c>'s canvas-and-pixels step done
/// with outlines instead of pixels. Every word keeps its own source part so it stays editable; weights are edited in the source, not
/// drawn.
/// </summary>
/// <param name="pictures">The picture a part of the block was found to name — a <c>mask:</c>'s, hung under it before this ever ran.</param>
public sealed class PortableWordCloudBuilder(ContentReading reading, EditState state, PortableStyle style, bool isReadOnly, ILayoutMedium medium,
                                             PortableNesting nesting, Func<ContentPart, LayoutPicture?> pictures)
    : PortableContentBuilder(reading, state, style, isReadOnly, medium, nesting)
{
    /// <summary>The face the source is shown in when the block cannot be read as a cloud at all.</summary>
    private const string SourceFont = "Cascadia Code, Consolas, monospace";

    private const double SourceSize = 12;

    /// <summary>How finely a letter's curves are broken into straight lines before they are filled.</summary>
    private const double Tolerance = 0.25;

    /// <summary>How much smaller a word is tried at when there was no room for it at the size it asked for.</summary>
    private const double Shrink = 0.85;

    /// <summary>The size letters are set at before they are scaled to the room — big enough for their curves to survive filling.</summary>
    private const double StencilSize = 200;

    /// <summary>The widest a picture is read as a shape. A photograph holds the same silhouette at a tenth of the size.</summary>
    private const double StencilPixels = 480;

    private WordCloudSettings _settings = WordCloudSettings.Default;

    private LayoutInk? _black;

    protected override PortableLaid? Build()
    {
        // A cloud is only read where it is drawn, so what its stages found wrong — a setting given something it cannot take, a line that
        // is no word, a block with nothing in it — is put right in its source: shown as written, each part at fault marked and why.
        var troubled = Reading.Root.SelfAndDescendants().Where(part => part.Trouble is not null && !part.Derived)
                              .Select(part => (part, part.Trouble!)).ToList();
        if (troubled.Count > 0 || Reading.Root.Node is not WordCloudBlockNode block) return AsSource(troubled);

        _settings = block.Settings;

        var ink = PortableCloudInk.Of(block.Colours, Style, new WordCloudRandom(_settings.Seed + 2), Medium);
        var words = Reading.Root.SelfAndDescendants().Where(part => part.Node is WordCloudWordNode)
                           .OrderBy(part => ((WordCloudWordNode)part.Node).Rank).ToList();

        return Lay(words, ink, PortableCloudInk.Background(block.Colours, Medium));
    }

    /// <summary>One word, measured and placed: what to draw, where, turned how far, and in what.</summary>
    /// <param name="At">Where the word is set — the origin its turn is about, not the top left of its letters.</param>
    private sealed record Placement(IShapedText Text, double Room, LayoutPoint At, double Turn, LayoutInk Ink, ContentPart Part)
    {
        /// <summary>The rectangle its letters are drawn in, turn and all.</summary>
        public LayoutRect Covers
        {
            get
            {
                var box = new LayoutRect(0, 0, Text.Width, Text.Height);
                if (Turn != 0) box = LayoutRect.Transform(box, LayoutMatrix.Rotation(Turn));

                return LayoutRect.Offset(box, (LayoutVector)At);
            }
        }
    }

    private PortableLaid Lay(IReadOnlyList<ContentPart> words, PortableCloudInk ink, LayoutInk? background)
    {
        var (room, fall) = SettingRoom.Fit(_settings.Width, _settings.Height, Room, WordCloudSettings.HeightShare);

        var stencil = Stencil(room, fall, out var lost);
        if (lost is not null) return AsSource(lost);

        // Fitted without stretching so `letters:` keeps its own proportions rather than the column's; empty room is trimmed away at the
        // end regardless.
        var width = room;
        var height = fall;

        if (stencil is not null)
        {
            var aspect = (double)stencil.Across / stencil.Down;
            if (room / fall > aspect) width = fall * aspect;
            else height = room / aspect;
        }

        // Separate throws from the packing's, so toggling shuffling doesn't also change every word's angle.
        var angles = new WordCloudRandom(_settings.Seed);

        // The stencil is the shape now, so the usual shape/ellipticity squash is set aside — rings must reach every corner of it.
        var packing = stencil is null ? _settings : _settings with { Shape = WordCloudShape.Circle, Ellipticity = 1 };

        var board = new WordCloudBoard(width, height, packing, new WordCloudRandom(_settings.Seed + 1), stencil);

        var trouble = new List<PortableDiagnostic>();
        var placed = new List<Placement>();
        var at = 0;

        foreach (var word in words)
        {
            var turn = angles.Angle(_settings);
            var colour = ink.For(at++);

            if (Place(board, word, ((WordCloudWordNode)word.Node).Size, turn, colour) is { } set) placed.Add(set);
            else
                trouble.Add(PortableDiagnostic.Of(word, string.Format(CultureInfo.CurrentCulture, Style.Says("Text.WordCloud.NoRoom"), word.Text),
                                                  PortableSeverity.Warning));
        }

        // Sized to what was actually drawn, not the room offered — else a small cloud in a wide column would reserve the whole column.
        var drawn = placed.Count == 0 ? new LayoutRect(0, 0, width, height) : Covered(placed);
        var shift = new LayoutVector(-drawn.X, -drawn.Y);

        // A word is a piece, and the cloud holding them is one more.
        var build = new PortableBuilder(Medium, placed.Count + 1);
        build.Open(WordCloudPiece.Cloud);

        var picture = new LayoutRect(0, 0, drawn.Width, drawn.Height);
        if (background is not null) build.Draw(new PortableRuleMark(picture, background));
        build.Covers(picture);

        foreach (var set in placed)
            PortableText.Words(build, set.Text, set.At + shift, set.Room, LayoutAlign.Left, set.Part, WordCloudPiece.Word, maps: true,
                               ink: set.Ink, degrees: set.Turn);

        build.Close();

        return new PortableLaid(build.Seal(), drawn.Width, drawn.Height, trouble);
    }

    /// <summary>
    /// Places a word, shrinking it step by step until it fits or reaches the least size (null if it never fits) —
    /// <c>wordcloud2.js</c>'s <c>shrinkToFit</c>.
    /// </summary>
    private Placement? Place(WordCloudBoard board, ContentPart word, double size, double turn, LayoutInk ink)
    {
        // Set, and outlined, once — at the size it is tried at first, and to the same width the layout sets it to, so the outline is of
        // the word as it is drawn. A word that has to shrink to fit is the same letters smaller, so each smaller size is that outline
        // scaled down rather than the word set and outlined again.
        var first = Set(word.Text, size);
        first.Bound(Math.Max(1, first.Width), LayoutAlign.Left);
        var letters = Letters(first);

        for (var at = size; at >= _settings.MinSize; at *= Shrink)
        {
            var mask = WordMask.Of(Turned(letters, at / size, turn), _settings.GridSize, _settings.Gap);

            if (mask is not null && board.TryPlace(mask, out var spot))
            {
                var text = at == size ? first : Set(word.Text, at);
                var room = Math.Max(1, text.Width);
                text.Bound(room, LayoutAlign.Left);

                return new Placement(text, room, new LayoutPoint(spot.X - mask.Left, spot.Y - mask.Top), turn, ink, word);
            }

            if (!_settings.Fit) return null;
        }

        return null;
    }

    /// <summary>Everything the words between them cover — which is the picture, once it is brought to the origin.</summary>
    private static LayoutRect Covered(IReadOnlyList<Placement> placed)
    {
        var all = placed[0].Covers;
        for (var at = 1; at < placed.Count; at++) all = LayoutRect.Union(all, placed[at].Covers);
        return all;
    }

    /// <summary>
    /// The packing shape (null for the whole picture), or the reason it couldn't be built — which stops the block rather than silently
    /// drawing the wrong shape.
    /// </summary>
    private WordCloudStencil? Stencil(double width, double height, out string? trouble)
    {
        trouble = null;

        if (_settings.Mask is { Length: > 0 } picture) return FromPicture(picture, out trouble);
        if (_settings.Letters is { Length: > 0 } letters) return FromLetters(letters, width, height, out trouble);

        return null;
    }

    /// <summary>Letters filled as a packing shape, sampled a pixel to the cell — finer than the board's own grid, since a letter is mostly curves.</summary>
    private WordCloudStencil? FromLetters(string letters, double width, double height, out string? trouble)
    {
        trouble = null;

        var text = Set(letters, StencilSize);
        text.Bound(Math.Max(1, text.Width), LayoutAlign.Left);

        if (text.Width <= 0 || text.Height <= 0)
        {
            trouble = $"`letters: {letters}` has no letters in it to pack a cloud into.";
            return null;
        }

        var scale = Math.Max(1, Math.Min(width / text.Width, height / text.Height));
        var stencil = WordCloudStencil.Of(Turned(Letters(text), scale, 0), grid: 1);

        if (stencil is null)
            trouble = $"`letters: {letters}` draws nothing a cloud could be packed into.";

        return stencil;
    }

    /// <summary>
    /// The shape a <c>mask:</c> stands for: the opaque part of its picture where it has transparency, and its dark part otherwise — which
    /// covers every mask anybody actually draws, with no setting needed to choose between them. The picture was found before this ever
    /// ran and hung under the setting that named it, so nothing here goes looking for one: a block drawn with no document to look in has
    /// nothing hung there, and says so.
    /// </summary>
    private WordCloudStencil? FromPicture(string named, out string? trouble)
    {
        trouble = null;

        LayoutPixels? found = null;
        foreach (var part in Reading.Root.SelfAndDescendants())
            if (pictures(part)?.Pixels(StencilPixels) is { } read)
            {
                found = read;
                break;
            }

        if (found is null)
        {
            trouble = string.Format(CultureInfo.CurrentCulture, Style.Says("Text.WordCloud.NoPicture"), named);
            return null;
        }

        var (pixels, across, down) = (found.Bgra, found.Across, found.Down);
        var stride = across * 4;

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

    /// <summary>The outlines of the letters set in <paramref name="text"/>, level and at its own size, as closed figures of points.</summary>
    private List<(double X, double Y)[]> Letters(IShapedText text)
    {
        var figures = new List<(double X, double Y)[]>();

        if (Medium.TextOutline(text, default) is not { } drawn) return figures;

        foreach (var figure in Medium.Flattened(drawn, Tolerance))
        {
            if (figure.Length <= 2) continue;

            var points = new (double X, double Y)[figure.Length];
            for (var at = 0; at < figure.Length; at++) points[at] = (figure[at].X, figure[at].Y);

            figures.Add(points);
        }

        return figures;
    }

    /// <summary>Outlines scaled by <paramref name="scale"/> and turned by <paramref name="turn"/> degrees about where the word is set.</summary>
    private static IReadOnlyList<IReadOnlyList<(double X, double Y)>> Turned(List<(double X, double Y)[]> letters, double scale, double turn)
    {
        var about = LayoutMatrix.Identity;
        if (scale != 1) about *= LayoutMatrix.Scaling(scale, scale);
        if (turn != 0) about *= LayoutMatrix.Rotation(turn);

        if (about.IsIdentity) return letters;

        var turned = new List<(double X, double Y)[]>(letters.Count);

        foreach (var figure in letters)
        {
            var moved = new (double X, double Y)[figure.Length];

            for (var at = 0; at < figure.Length; at++)
            {
                var point = about.Transform(new LayoutPoint(figure[at].X, figure[at].Y));
                moved[at] = (point.X, point.Y);
            }

            turned.Add(moved);
        }

        return turned;
    }

    /// <summary>One word, set in the face and at the size the cloud asks for.</summary>
    private IShapedText Set(string word, double size) =>
        Medium.Shape(word, Style.Face(_settings.Font, _settings.Bold), size, _black ??= Medium.Ink(LayoutColor.Of(0, 0, 0)));

    protected override PortableCharacters Characters => new(Style.Face(SourceFont), SourceSize, _black ??= Medium.Ink(LayoutColor.Of(0, 0, 0)));
}
