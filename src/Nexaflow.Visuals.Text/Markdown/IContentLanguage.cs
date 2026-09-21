using System.Windows;

using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// One language a fence can be written in, and everything it takes to show it: the names it answers to, the
/// reading and laying out of its source, and what writing into it means.
///
/// <para>
/// <strong>One thing, not four.</strong> A language is a parser that only copies, a pipeline of stages that
/// re-nest what it read, a builder that decides geometry, and the rules for writing into the result — and all
/// four are that language's own business. Anything holding a language needs none of them separately; it needs
/// to know it found one. So the parser and the stages are inside <see cref="Lay"/>, where they belong, and
/// what comes out is a laid tree that grafts into whatever asked for it.
/// </para>
/// <para>
/// A feature brings its own by implementing this. Nothing else is needed: the implementation is found where
/// every other contributed contract is found, at assembly load, and registered into
/// <see cref="ContentLanguages"/>.
/// </para>
/// </summary>
public interface IContentLanguage
{
    /// <summary>Whether a fence calling itself <paramref name="language"/> is this one.</summary>
    bool Reads(string? language);

    /// <summary>
    /// That source, read and laid out at the size it was asked for — or null for a language whose builder has
    /// no entry point of its own yet.
    ///
    /// <para>
    /// Three are in that state (a barcode, the correlation plots, nomnoml), because their builders were only
    /// ever reached through <see cref="Draw"/>. Until they have one they draw only where a document is made
    /// of elements, which is what null says here rather than leaving a caller to find out.
    /// </para>
    /// </summary>
    Laid? Lay(ContentRequest request) => null;

    /// <summary>
    /// The same content as something a document made of WPF elements can hold.
    ///
    /// <para>
    /// The older of the two surfaces. It goes when the last document made of elements does — when
    /// <c>SelectableMarkdownView</c> and <c>InlineMarkdownEditor</c> are both laid on the shared tree — and
    /// every language's <see cref="Lay"/> is what is left.
    /// </para>
    /// </summary>
    FrameworkElement Draw(string source, DiagramRenderOptions options);
}

/// <summary>
/// What a language is being asked to lay out: the source, how it is drawn, how much room it has, and where it
/// was written in the document that holds it.
/// </summary>
/// <param name="Source">The characters, exactly as the writer typed them.</param>
/// <param name="Style">What this showing of it is drawn in.</param>
public sealed record ContentRequest(string Source, StyleFormat Style)
{
    /// <summary>How wide it may be laid out — infinity where nothing says.</summary>
    public double Room { get; init; } = double.PositiveInfinity;

    /// <summary>Where <see cref="Source"/> begins in the document holding it, so every part names the characters a reader is selecting.</summary>
    public int At { get; init; }

    /// <summary>
    /// What the host said about diagrams — what a press on a node means, what it is bound against, how much
    /// of it to fold. Null where nobody said anything, which is every surface that only reads.
    /// </summary>
    public DiagramRenderOptions? Options { get; init; }
}
