using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Code;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Lines;
using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Markdown.Layout.Code;

/// <summary>
/// A stretch of code onto the renderer-free layout tree, laid line by line as everything read so is (<see cref="LinedBuilder"/>), each
/// token in the ink its kind is given.
///
/// <para>
/// It colours by kind and nothing else: a piece called <c>keyword</c> is drawn in whatever the style says a keyword is
/// (<see cref="LayoutStyle.Syntax"/>), and a piece the grammar had no name for — or every piece, when nothing has read it yet — is
/// drawn in the body ink. So the uncoloured case is not a second path, it is the same path with nothing named.
/// </para>
/// </summary>
public sealed class CodeBuilder(ContentReading reading, EditState state, LayoutStyle style, bool isReadOnly, ILayoutMedium medium,
                                        LayoutHost host)
    : LinedBuilder(reading, state, style, isReadOnly, medium, host)
{
    private readonly Dictionary<string, LayoutInk> _inks = [];

    protected override string StretchKind => CodeKinds.Code;

    protected override string LineKind => CodeKinds.Line;

    protected override LayoutCharacters Characters => CodeSetting.Mono(Style);

    /// <summary>
    /// What a kind is drawn in. A kind the style says nothing about is body text — which is every kind when the grammar has not been
    /// read against this yet, and is the whole of the uncoloured case.
    /// </summary>
    protected override LayoutInk Ink(string kind)
    {
        if (_inks.TryGetValue(kind, out var known)) return known;

        return _inks[kind] = Style.Syntax(kind) ?? Style.Text;
    }
}
