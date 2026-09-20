using System.Windows;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// Lays a <c>mermaid</c> block out whose header names no diagram type: the block as it was written, a wave under the
/// word in the header's place, and the reason beneath.
///
/// <para>
/// Shown rather than guessed at. A header that names nothing is usually a type being typed, or one from a newer
/// Mermaid — either way the reader wants their source back and a reason, not a flowchart of whatever the lines happen
/// to look like.
/// </para>
/// </summary>
internal sealed class UnknownDiagramBuilder : MermaidBuilder
{
    private UnknownDiagramBuilder(EditState state, DiagramLaying laying) : base(state, laying) { }

    /// <summary>Lays a block's source out. Never null, and never throws.</summary>
    /// <remarks>Written in or not, it looks the same, so it is laid out as nobody were: a block shown as its own
    /// characters has nothing still to be written in it.</remarks>
    public static Laid Build(EditState state, DiagramLaying laying) =>
        new UnknownDiagramBuilder(state, laying with { Writing = false }).Lay();

    /// <summary>The same, for a block nobody is writing in.</summary>
    public static Laid Build(string source, MarkdownPalette palette, double pixelsPerDip, double room = double.PositiveInfinity) =>
        Build(EditState.For(source), new DiagramLaying(palette, pixelsPerDip, room));

    public static Editing.ContentElement Element(string source, DiagramRenderOptions options) =>
        Host(source, options, Build);

    protected override Size Draw(MermaidBlock block, LayoutBuilder build) => AsWritten(build);

    /// <summary>None: the front matter is part of what is shown, so its title is already on the page.</summary>
    protected override (ContentPart? Part, string? Text) TitleOf(MermaidBlock block) => (null, null);
}
