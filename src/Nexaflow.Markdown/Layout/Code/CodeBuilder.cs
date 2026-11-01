using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Code;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Markdown.Layout.Code;

/// <summary>
/// A stretch of code onto the renderer-free layout tree, set line for line in a monospaced face, each token in the ink its kind is given.
///
/// <para>
/// It colours by kind and nothing else: a piece called <c>keyword</c> is drawn in whatever the style says a keyword is
/// (<see cref="LayoutStyle.Syntax"/>), and a piece the grammar had no name for — or every piece, when nothing has read it yet — is
/// drawn in the body ink. So the uncoloured case is not a second path, it is the same path with nothing named.
/// </para>
/// <para>
/// Lines are kept and nothing wraps. Code that reflowed would be code that lied about where a statement ended, and a reader counting
/// columns is the whole reason it is set in a monospaced face.
/// </para>
/// <para>
/// A line is a block (<see cref="CodeKinds.Line"/>): one that reads exactly as it did last time — the same characters coloured the same
/// — is set down as it was laid (<see cref="LaidBlocks"/>), so a keystroke lays the line it was typed on.
/// </para>
/// </summary>
public sealed class CodeBuilder(ContentReading reading, EditState state, LayoutStyle style, bool isReadOnly, ILayoutMedium medium,
                                        LayoutHost host)
    : ContentBuilder(reading, state, style, isReadOnly, medium, host)
{
    private readonly Dictionary<string, LayoutInk> _inks = [];

    protected override Laid? Build()
    {
        if (Reading.Root.Length == 0) return null;

        var height = CodeSetting.LineHeight(Style, Medium);
        var laid = LaidBlocks.Of(Reading.Root);

        // A stretch the next carries on from ends where the next begins, which stands for that place: a caret is not put after it too.
        var carriedOn = CodeSetting.CarriedOn(Reading.Root.Node);

        var into = new LayoutBuilder(Medium, Reading.Root.Children.Count + 1);
        var (y, wide) = (0.0, 0.0);

        into.Open(CodeKinds.Code, Reading.Root, stops: carriedOn ? LayoutStops.Before : LayoutStops.Both);

        for (var at = 0; at < Reading.Root.Children.Count; at++)
        {
            var line = Reading.Root.Children[at];
            if (line.Kind != CodeKinds.Line) continue;

            var (mount, reach) = this.Line(line, laid?.For(at), height);
            into.Mount(mount, new LayoutPoint(0, y));

            wide = Math.Max(wide, reach);
            if (line.Children.Count > 0 && line.Children[^1].Kind == CodeKinds.LineEnd) y += height;
        }

        into.Close();

        // The line after the last line end is a line of its own — where a caret goes after a closing line feed — unless the next
        // stretch carries on from this one, whose first line it is.
        return new Laid(into.Seal(), Math.Max(wide, 1), Math.Max(carriedOn ? y : y + height, height), []);
    }

    /// <summary>
    /// One line, as a tree of its own standing at the top of its own frame, its picture kept once painted, put down whole — and how far
    /// across it reaches: the tree it was laid as last time, where it reads exactly as it did, each part it names found again in this
    /// reading only when asked after; and otherwise each of its pieces set in the ink its kind is given, the line end taking no room.
    /// Code does not wrap, so the room never changes a line.
    /// </summary>
    private (LayoutMount Mount, double Reach) Line(ContentPart line, LaidBlock? laid, double height)
    {
        if (laid?.Last is { } last && last.IsReadOnly == IsReadOnly) return (new LayoutMount(last.Tree, last.Part, line), last.Reach);

        var characters = Characters;
        var apart = new LayoutBuilder(Medium);
        var x = 0.0;

        apart.Open(CodeKinds.Line, line, stops: LayoutStops.None, paints: new LayoutPaint(Kept: new LayoutKept()));

        foreach (var token in line.Children)
        {
            if (token.Kind == CodeKinds.LineEnd) continue;

            var ink = Ink(token.Kind);
            var glyphs = Medium.Shape(token.Text, characters.Face, characters.Size, ink);

            // How far along the next token starts: what this one is set in AND the space after it. A grammar names the words and leaves
            // the space between them unnamed, so a run of spaces is a token of its own — and a width that leaves trailing space out
            // measures such a token at nothing and closes up the words either side of it.
            var along = glyphs.WidthWithSpace;

            // The piece is called what the grammar called it, so what a reader pressed and what the theme coloured are the same question
            // asked of the same piece.
            LayoutText.Words(apart, glyphs, new LayoutPoint(x, 0), along + 1, LayoutAlign.Left, token, token.Kind, maps: true, ink: ink);

            x += along;
        }

        apart.Close();

        var made = apart.Seal();
        if (laid is not null) laid.Last = new KeptLaying(line, made, height, x, [], Room, IsReadOnly);

        return (new LayoutMount(made), x);
    }

    protected override LayoutCharacters Characters => CodeSetting.Mono(Style);

    /// <summary>
    /// What a kind is drawn in. A kind the style says nothing about is body text — which is every kind when the grammar has not been
    /// read against this yet, and is the whole of the uncoloured case.
    /// </summary>
    private LayoutInk Ink(string kind)
    {
        if (_inks.TryGetValue(kind, out var known)) return known;

        return _inks[kind] = Style.Syntax(kind) ?? Style.Text;
    }
}
