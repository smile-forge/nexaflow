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
    /// <remarks>
    /// Read-only whatever it was asked for: written in or not, a block shown as its own characters looks the
    /// same, because there is nothing in it still to be written.
    /// </remarks>
    internal UnknownDiagramBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly)
        : base(reading, state, style, isReadOnly: true) { }

    /// <summary>The same, for a block nobody is writing in.</summary>
    internal static Laid Lay(string source, StyleFormat style, double room = double.PositiveInfinity) =>
        new UnknownDiagramBuilder(MermaidBuilders.Read(source), EditState.For(source), style, isReadOnly: true).Lay(room);

    public static Editing.ContentElement Element(string source, DiagramRenderOptions options) =>
        Host(source, options, static (r, s, f, o) => new UnknownDiagramBuilder(r, s, f, o));

    protected override Size Draw(MermaidBlock block, LayoutBuilder build) => AsWritten(build);

    /// <summary>None: the front matter is part of what is shown, so its title is already on the page.</summary>
    protected override (ContentPart? Part, string? Text) TitleOf(MermaidBlock block) => (null, null);
}
