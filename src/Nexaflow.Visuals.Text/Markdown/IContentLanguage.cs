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
    /// That source, read and laid out at the size it was asked for — or, where there is nothing in it to draw, which a
    /// block that will not read at all is, a layout that draws nothing and says why in its trouble; null where it has
    /// nothing to say about it either.
    ///
    /// <para>
    /// <strong>Drawing nothing does not mean nothing appears.</strong> It means this language has no picture of the
    /// source to offer, and whoever asked draws the characters instead, with the reason it gave — so a block nothing could
    /// make sense of is still on the page, still where it was written, still somewhere the caret can go and repair it, and
    /// says what to repair. Every language answers, and every answer leads to something drawn.
    /// </para>
    /// </summary>
    Laid? Lay(ContentRequest request) => null;

    /// <summary>
    /// What may be done to this content where a gesture landed — the things a reader can add, and the
    /// things they can do to what is already there.
    ///
    /// <para>
    /// A language's own, because only it knows what its content is made of: a slice may be restyled and a
    /// note may be sharpened, and neither means anything to the other. What comes back is offered beside
    /// whatever the pieces themselves answer to and whatever the host adds.
    /// </para>
    /// </summary>
    IReadOnlyList<Editing.LayoutIntent> Offers(ContentAsk ask) => [];

    /// <summary>
    /// What the buttons in the corner of a block are: whichever of the usual ones this content allows, and
    /// whatever it adds of its own.
    ///
    /// <para>
    /// The usual ones are not usual for everything. A picture of a diagram is worth keeping; a picture of a
    /// code fence is a worse copy of the code. So a language says which of them make sense for it rather
    /// than every surface having to know.
    /// </para>
    /// </summary>
    BlockCorner Corner(ContentAsk ask) => BlockCorner.Usual;

    /// <summary>
    /// Where this content puts <paramref name="looking"/> on the page although the source does not say it —
    /// offsets into <see cref="ContentAsk.Source"/>.
    ///
    /// <para>
    /// The escape hatch, and it should stay one. Almost everything is caught without asking: a search reads
    /// the source, which is where the words are whole, and anything drawn as something other than what was
    /// typed already says so on its run, so both are found centrally for every language at once.
    /// </para>
    /// <para>
    /// What is left is content that shows a reader something which is neither of those — worked out rather
    /// than written, and drawn as a picture rather than as a run. A language that has one of those says so
    /// here, the way it says what an edit means rather than leaving a host to guess.
    /// </para>
    /// </summary>
    IReadOnlyList<(int Start, int Length)> Finds(ContentAsk ask, string looking) => [];

    /// <summary>
    /// What an edit means in this language's own source, where that is something other than its characters — or
    /// null, which is nearly every language: what is typed is inserted and what is taken back is a character.
    ///
    /// <para>
    /// Asked only of the language an edit landed in. Editing is shared — the caret, the layout above it and the part
    /// it stands in are the same whatever drew them — and which language wrote that part was settled by a stage and
    /// hangs on the syntax tree, so the edit finds it rather than anything looking it up.
    /// </para>
    /// </summary>
    Editing.IOnEdit? OnEdit => null;
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

    /// <summary>
    /// The stretch of <see cref="Source"/> being shown as the characters that were typed, because somebody is typing
    /// in it — named in the document's offsets, as every part is — or null where all of it is read.
    /// </summary>
    public Editing.RawZone? Shown { get; init; }

    /// <summary>
    /// Whether the content is only being looked at. Content being written draws what is still to be written — a hole
    /// where a label goes, a place for an argument — and content being read does not.
    /// </summary>
    public bool IsReadOnly { get; init; } = true;
}

/// <summary>
/// What a language is being asked about a place in its own content: where the gesture landed, and what is
/// picked out.
/// </summary>
/// <param name="Named">What the fence called itself.</param>
/// <param name="Source">The characters, exactly as the writer typed them.</param>
public sealed record ContentAsk(string Named, string Source)
{
    /// <summary>The piece of the content the gesture landed on, where it landed on one.</summary>
    public Nexaflow.Markdown.Ast.ContentPart? Part { get; init; }

    /// <summary>What is picked out, where anything is — offsets into <see cref="Source"/>.</summary>
    public (int Start, int Length)? Chosen { get; init; }

    /// <summary>Whether the reader may write in it, so nothing that changes it is offered where they may not.</summary>
    public bool IsReadOnly { get; init; }
}

/// <summary>
/// What a block offers in its own corner: the semi-transparent buttons that appear where the pointer rests
/// on it.
/// </summary>
/// <param name="Saves">Whether keeping a picture of it means anything.</param>
/// <param name="Copies">Whether it can be handed over to be copied.</param>
public sealed record BlockCorner(bool Saves = true, bool Copies = true)
{
    /// <summary>What this block adds of its own, beside the usual.</summary>
    public IReadOnlyList<Editing.LayoutIntent> Adds { get; init; } = [];

    /// <summary>What a block that has said nothing about itself offers.</summary>
    public static BlockCorner Usual { get; } = new();

    /// <summary>
    /// No corner at all: what content says whose blocks are read rather than handled — a paragraph, a list, a quote —
    /// where a button beside every one of them would only be in the way.
    /// </summary>
    public static BlockCorner None { get; } = new(Saves: false, Copies: false);
}
