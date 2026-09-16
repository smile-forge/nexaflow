using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// What one kind of diagram reads that the others do not: what follows its keyword, and what its own lines say.
///
/// <para>
/// <see cref="MermaidParser"/> reads everything the types share — the front matter, the comments and directives, the
/// header, the accessibility lines — and hands the rest here. A type with no grammar of its own keeps its lines whole,
/// as <see cref="MermaidKinds.Statement"/>, which is a diagram that draws from what it was given rather than from what
/// it was read as.
/// </para>
/// <para>
/// <strong>A grammar only ever copies</strong>, like the parser it is part of: whatever it hands back has to print as
/// exactly the characters it was given, or the block no longer says what was written. Anything it cannot read is held
/// as written with the reason, never dropped and never repaired.
/// </para>
/// </summary>
public interface IMermaidGrammar
{
    /// <summary>
    /// What follows the keyword on the header line, read — or null to hold it as written. The text never has space at
    /// either end: the line's own space belongs to the line.
    /// </summary>
    ContentNode? Header(string arguments) => null;

    /// <summary>
    /// One line of the diagram, read — or null to hold it whole, as a statement.
    ///
    /// <para>
    /// The text runs from the line's first character to the end of its row, space and all, and what is read is as much of
    /// it as the node handed back prints: the rest is the line's own. Where a line stops being written is the grammar's to
    /// say, because only it knows when the space at the end is still part of what is being written — a slice that ends at
    /// its colon has its value to come, after the space a reader leaves for it.
    /// </para>
    /// </summary>
    ContentNode? Statement(string text);

    /// <summary>
    /// What a new line written under <paramref name="above"/> starts as before anything is filled in, and how far into it the
    /// caret goes — what Enter starts. <paramref name="above"/> is what the line the caret is on says, as this grammar read
    /// it, or null for a line that says nothing: a diagram whose lines come in several shapes starts the one that follows it.
    /// Null where no shape of line follows it.
    /// </summary>
    (string Text, int Caret)? Blank(ContentNode? above) => null;

    /// <summary>
    /// What writing <paramref name="text"/> at <paramref name="caret"/>, in <paramref name="part"/> — a part this grammar read,
    /// or a hole standing where one goes — is written as, where it cannot go in as it is without the line saying something
    /// else: a name put in quotes so it can hold a space, a quote written as the entity code that stands for it
    /// (<see cref="MermaidText"/>). Null where the text goes in as it is.
    /// </summary>
    MermaidWriting? Escaping(ContentPart part, int caret, string text) => null;

    /// <summary>
    /// The names <paramref name="block"/> declares that its other lines use: where each is declared, and every place it is
    /// used — so a name renamed where it is declared can be renamed wherever it is used. Nothing, for a diagram whose lines
    /// name nothing another line uses.
    /// </summary>
    IReadOnlyList<MermaidName> Names(ContentPart block) => [];

    /// <summary>How a name is written where it is used.</summary>
    string Naming(string name) => name;
}

/// <summary>What is written in place of a stretch of a block, and where the caret goes after it.</summary>
/// <param name="Start">Where the stretch written over starts.</param>
/// <param name="End">Where it ends: the same as <paramref name="Start"/> where nothing is written over.</param>
/// <param name="Text">What is written there.</param>
/// <param name="Caret">Where the caret goes, in the block as it reads afterwards.</param>
public readonly record struct MermaidWriting(int Start, int End, string Text, int Caret)
{
    /// <summary>
    /// Text written at the caret inside quotes, which hold anything but a quote — or null where there is no quote in it
    /// and it goes in as it is.
    /// </summary>
    public static MermaidWriting? InQuotes(int caret, string text)
    {
        if (!text.Contains('"')) return null;

        var quoted = MermaidText.Quoted(text);
        return new MermaidWriting(caret, caret, quoted, caret + quoted.Length);
    }

    /// <summary>
    /// A stretch written over with what it said and what is typed into it, put in quotes — <paramref name="before"/> the
    /// caret and <paramref name="after"/> it — and the caret between the two.
    /// </summary>
    public static MermaidWriting Quoting(int start, int end, string before, string after)
    {
        var head = "\"" + MermaidText.Quoted(before);
        return new MermaidWriting(start, end, head + MermaidText.Quoted(after) + "\"", start + head.Length);
    }
}

/// <summary>A name a block declares, and the places it is used.</summary>
/// <param name="Name">What it is called, without the quotes it may be written in.</param>
/// <param name="Declared">Where it is declared, quotes and all.</param>
/// <param name="Uses">Every other place it is written, quotes and all.</param>
public sealed record MermaidName(string Name, ContentPart Declared, IReadOnlyList<ContentPart> Uses);
