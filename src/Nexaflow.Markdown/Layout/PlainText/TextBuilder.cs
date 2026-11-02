using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Lines;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Lines;
using Nexaflow.Markdown.PlainText;

namespace Nexaflow.Markdown.Layout.PlainText;

/// <summary>
/// Plain text onto the renderer-free layout tree, laid line by line as everything read so is (<see cref="LinedBuilder"/>), in the body
/// ink.
///
/// <para>
/// A line is set as one run of its words, as prose sets a run of them. The words are the reading's (<see cref="TextKinds.Word"/>), for
/// whatever looks into them, and nothing is drawn differently for any one of them yet — so setting each on its own would only shape
/// them one at a time.
/// </para>
/// </summary>
public sealed class TextBuilder(ContentReading reading, EditState state, LayoutStyle style, bool isReadOnly, ILayoutMedium medium,
                                        LayoutHost host)
    : LinedBuilder(reading, state, style, isReadOnly, medium, host)
{
    protected override string StretchKind => TextKinds.Text;

    protected override string LineKind => TextKinds.Line;

    protected override LayoutCharacters Characters => TextSetting.Characters(Style);

    protected override LayoutInk Ink(string kind) => Style.Text;

    /// <summary>The line's words and the spaces between them, as one run standing for all of it but its line end.</summary>
    protected override IEnumerable<(ISourcePart Part, string Text, string Kind)> Runs(ContentPart line)
    {
        var words = line.Children.Where(piece => piece.Kind != LineKinds.LineEnd).ToList();
        if (words.Count == 0) yield break;

        var text = words.Count == 1 ? words[0].Text : string.Concat(words.Select(piece => piece.Text));

        yield return (new PartSlice(line, 0, text.Length), text, Kinds.Words);
    }
}
