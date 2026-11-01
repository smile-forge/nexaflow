using Nexaflow.Markdown.Ast;

namespace Nexaflow.Features.Markdown.ViewModels;

/// <summary>
/// One heading of the document as the outline panel lists it: what it says, indented by how far below the document's highest heading
/// it ranks, and the heading itself — which the rendered document is asked to go to as it is, wherever it now stands
/// (<see cref="Nexaflow.Visuals.Text.Markdown.MarkdownSurface.Show(OutlineHeading)"/>).
/// </summary>
/// <param name="heading">The heading, as the rendered document's outline handed it on.</param>
/// <param name="highest">The highest rank of any heading the document has — the rank listed flush left.</param>
public sealed class MarkdownOutlineItem(OutlineHeading heading, int highest)
{
    /// <summary>How many levels deep a row is indented, at most; deeper headings line up with the deepest.</summary>
    private const int MaxIndentLevels = 6;

    /// <summary>The heading, as the rendered document's outline handed it on.</summary>
    public OutlineHeading Heading { get; } = heading;

    /// <summary>What the heading says.</summary>
    public string Title => this.Heading.Title;

    /// <summary>How far the row is indented: fourteen for each rank it stands below the document's highest.</summary>
    public double IndentWidth => Math.Min(this.Heading.Rank - highest, MaxIndentLevels) * 14.0;
}
