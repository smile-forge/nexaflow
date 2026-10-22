using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// The sections a document's headings make (<see cref="MarkdownSections"/>), read once and hung on the document.
///
/// <para>
/// A stage, because which blocks a heading stands over is a fact about what the document amounts to rather than anything
/// written: a heading says only how deep it is. Whatever folds a section away, or reads a document a section at a time, asks
/// the tree for it — so nothing after the reading works it out again, and nothing draws from where characters sit.
/// </para>
/// <para>
/// After <see cref="WithTargets"/>, whose names for the headings the sections carry. Hung only where the document has a
/// heading; a document without one is read as it was.
/// </para>
/// </summary>
public sealed class WithSections : IAstStage
{
    public string Name => "markdown:sections";

    public ContentNode Run(ContentNode tree)
    {
        if (MarkdownSections.Held(tree) is not null) return tree;

        var sections = MarkdownSections.Read(tree);
        return sections.Sections.Count == 0 ? tree : tree.Holding(MarkdownKinds.Sections, Roles.Derived, sections);
    }
}
