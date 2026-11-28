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
/// A line is set as one run of what is written on it, as the reading holds it (<see cref="TextParser.InLines"/>): nothing is drawn
/// differently for any one word of it, so setting each on its own would only shape them one at a time.
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

    /// <summary>What is written on the line, as the one run standing for all of it but its line end.</summary>
    protected override IEnumerable<(ISourcePart Part, string Text, string Kind)> Runs(ContentPart line)
    {
        if (line.Children.Count == 0 || line.Children[0] is not { Kind: Kinds.Words } written) yield break;

        yield return (new PartSlice(line, 0, written.Text.Length), written.Text, Kinds.Words);
    }
}
