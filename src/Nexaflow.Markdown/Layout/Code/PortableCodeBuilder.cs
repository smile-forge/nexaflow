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
/// (<see cref="PortableStyle.Syntax"/>), and a piece the grammar had no name for — or every piece, when nothing has read it yet — is
/// drawn in the body ink. So the uncoloured case is not a second path, it is the same path with nothing named.
/// </para>
/// <para>
/// Lines are kept and nothing wraps. Code that reflowed would be code that lied about where a statement ended, and a reader counting
/// columns is the whole reason it is set in a monospaced face.
/// </para>
/// </summary>
public sealed class PortableCodeBuilder(ContentReading reading, EditState state, PortableStyle style, bool isReadOnly, ILayoutMedium medium,
                                        PortableHost host)
    : PortableContentBuilder(reading, state, style, isReadOnly, medium, host)
{
    private readonly Dictionary<string, LayoutInk> _inks = [];

    protected override PortableLaid? Build()
    {
        if (Reading.Root.Length == 0) return null;

        var characters = Characters;
        var height = Medium.Shape(" ", characters.Face, characters.Size, Style.Text).Height;

        var into = new PortableBuilder(Medium);
        var (x, y, wide) = (0.0, 0.0, 0.0);

        into.Open(CodeKinds.Code, Reading.Root);

        foreach (var token in Tokens())
        {
            if (token.Kind == CodeKinds.LineEnd)
            {
                y += height;
                x = 0;
                continue;
            }

            var ink = Ink(token.Kind);
            var glyphs = Medium.Shape(token.Text, characters.Face, characters.Size, ink);

            // How far along the next token starts: what this one is set in AND the space after it. A grammar names the words and leaves
            // the space between them unnamed, so a run of spaces is a token of its own — and a width that leaves trailing space out
            // measures such a token at nothing and closes up the words either side of it.
            var along = glyphs.WidthWithSpace;

            // The piece is called what the grammar called it, so what a reader pressed and what the theme coloured are the same question
            // asked of the same piece.
            PortableText.Words(into, glyphs, new LayoutPoint(x, y), along + 1, LayoutAlign.Left, token, token.Kind, maps: true, ink: ink);

            x += along;
            wide = Math.Max(wide, x);
        }

        into.Close();

        return new PortableLaid(into.Seal(), Math.Max(wide, 1), Math.Max(y + height, height), []);
    }

    protected override PortableCharacters Characters => new(Style.Face(Style.MonoFont), Math.Max(1, Style.TextSize * 0.94), Style.Text);

    /// <summary>
    /// The pieces to set, a line at a time with a line end between: what a grammar found, where one has been read against this — and
    /// otherwise the body held as written, which is the uncoloured case and the same path.
    /// </summary>
    private IEnumerable<ContentPart> Tokens()
    {
        if (Reading.Root.Part(Roles.Body) is not { } body) yield break;

        if (body.Children.Count == 0)
        {
            yield return body;

            yield break;
        }

        foreach (var token in body.Children) yield return token;
    }

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
