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
    /// Whether what this draws is still the words that were written. Code is — it is set in another face
    /// and coloured, but every character a writer typed is on the page. A diagram, a formula, a score and
    /// a barcode are not: what they draw is a picture of what the source <em>meant</em>.
    ///
    /// <para>
    /// The question anything reading a document as text has to ask — a search index, a plain-text
    /// extractor, a screen reader — and it is the language's to answer, because only it knows what it
    /// made of the source. Default is no, because a language worth registering usually draws something.
    /// </para>
    /// </summary>
    bool ShowsWhatWasWritten => false;

    /// <summary>
    /// That source, read and laid out at the size it was asked for — or null where there is nothing in it to
    /// draw, which a block that will not read at all is.
    ///
    /// <para>
    /// <strong>Null does not mean nothing appears.</strong> It means this language has no picture of the
    /// source to offer, and whoever asked draws the characters instead — so a block nothing could make sense
    /// of is still on the page, still where it was written, and still somewhere the caret can go and repair
    /// it. Every language answers, and every answer leads to something drawn.
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
    FrameworkElement Draw(string language, string source, DiagramRenderOptions options);
}

/// <summary>
/// What a language is being asked to lay out: the source, how it is drawn, how much room it has, and where it
/// was written in the document that holds it.
/// </summary>
/// <param name="Source">The characters, exactly as the writer typed them.</param>
/// <param name="Style">What this showing of it is drawn in.</param>
public sealed record ContentRequest(string Source, StyleFormat Style)
{
    /// <summary>
    /// What the fence called itself. A language that answers to a family of names — code does, to dozens —
    /// needs the one that was written, not just the fact that it answered.
    /// </summary>
    public string? Named { get; init; }

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
