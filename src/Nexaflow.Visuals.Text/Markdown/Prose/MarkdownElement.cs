using System.Windows;
using System.Windows.Input;

using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Visuals.Text.Markdown.Prose;

/// <summary>
/// A markdown document, drawn where it was written and written in where it is drawn.
///
/// <para>
/// <strong>Almost nothing is here.</strong> Laying out, painting, the caret, the selection, the pointer, the arrow
/// keys, backspace, dragging a stretch somewhere else, the wash and the wave — all of it is
/// <see cref="ContentElement"/>, and it is the same code a formula, a tune and twenty-six diagrams run. What is left
/// is the one thing only a markdown document knows: what a press on a tick means.
/// </para>
/// <para>
/// <strong>The block answers its own verbs first, and the host is offered what it did not claim.</strong> That is the
/// way round that matters. Ticking an item off is this document's business and needs nobody's help, so a host that
/// wants none writes none; a host with a reason to take it on — a list that lives somewhere other than in the file —
/// says so and is asked.
/// </para>
/// </summary>
/// <param name="host">What the host answers, for the verbs this document does not answer itself.</param>
public sealed class MarkdownElement : LinkedElement
{
    /// <param name="host">What the host answers, for the verbs this document does not answer itself.</param>
    /// <param name="engine">What lays it out — the host's, where the host keeps one for longer than an element lasts.</param>
    /// <param name="language">What it is written in, where that is one language rather than a markdown document.</param>
    /// <param name="source">What to show, loaded in place of what <paramref name="engine"/>'s document holds — or null to show what it holds as it is.</param>
    public MarkdownElement(string? source, LayoutStyle palette, ILayoutActions? host = null, ContentEngine? engine = null,
                           string? language = null)
        : this(engine ?? new ContentEngine(WpfMedium.OnThisThread, palette), source, palette, host, language)
    {
    }

    private MarkdownElement(ContentEngine engine, string? source, LayoutStyle palette, ILayoutActions? host, string? language)
        : base(source, palette, engine, language, host)
    {
    }

    /// <summary>
    /// The markdown this is showing. Setting it re-reads and re-lays, without telling anybody the document
    /// changed — because it did not: this is a host showing something else, not somebody writing.
    /// </summary>
    public string Markdown
    {
        get => State.Document.ToString();
        set
        {
            if (State.Document.HoldsExactly(value ?? string.Empty)) return;

            Load(value ?? string.Empty);
        }
    }

    /// <summary>The document as it is being written: its source, the caret, what is picked out, and what is shown as typed.</summary>
    public EditState Current => State;

    /// <summary>
    /// Puts the document back as <paramref name="state"/> had it — an undo, a redo, a host holding it open as source — without
    /// telling anybody it was written in, because whoever asked for it already knows.
    /// </summary>
    public void Restore(EditState state) => Apply(state, notify: false);

    /// <summary>Brings a stretch of the source into view and picks it out, and says whether the document had it to show (<see cref="ContentEngine.ShowPlace"/>).</summary>
    public bool Show((int Start, int Length) what, bool choose = true) => ShowPlace(what, choose);
}
