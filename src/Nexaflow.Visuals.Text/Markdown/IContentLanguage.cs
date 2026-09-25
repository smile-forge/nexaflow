using System.Windows;

using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What editing means in a language, where it means something of its own: what a key does in its source, what a gesture
/// on its content offers, and what its block's corner holds.
///
/// <para>
/// Asked by the engine and nothing else, and only about content already laid out. It lays nothing out and reads nothing:
/// parsing, working over and laying out a language's content is the engine's, from what the language describes
/// (<see cref="ContentLanguage"/>).
/// </para>
/// </summary>
public interface IContentLanguage
{
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
    /// What an edit means in this language's own source, where that is something other than its characters — or
    /// null, which is nearly every language: what is typed is inserted and what is taken back is a character.
    ///
    /// <para>
    /// Asked only of the language an edit landed in. Editing is shared — the caret, the layout above it and the part
    /// it stands in are the same whatever drew them — and which language wrote that part is on the syntax tree
    /// (<see cref="Nexaflow.Markdown.Ast.ContentNested"/>), so the edit finds it rather than anything looking it up.
    /// </para>
    /// </summary>
    Editing.IOnEdit? OnEdit => null;
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
