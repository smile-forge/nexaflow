namespace Nexaflow.Markdown.Ast;

/// <summary>
/// What a piece is <em>to</em> the thing holding it — the roles every language needs, whatever it is
/// written in.
///
/// <para>
/// The point of the whole tree. A <c>3</c> is a 3 wherever it appears; a 3 whose role is a root's degree
/// is the degree of a root, and only the second reading lets it be copied onto something else. Each
/// language declares its own meaning-bearing roles beside these — a numerator, a radicand, a note's
/// accidental — because those are facts about that language and nothing shared could enumerate them.
/// </para>
/// <para>
/// What is here is the machinery every reader has: the mark that makes a construct what it is, the pair
/// of brackets round it, the separator between its parts, the space that fell inside it, and the one
/// role that stands for nothing written at all.
/// </para>
/// <para>
/// Strings rather than an enum, so a language can name a part without editing a type every consumer
/// switches over.
/// </para>
/// </summary>
public static class Roles
{
    /// <summary>One of several things in a row, which is all a sequence can say about what is in it.</summary>
    public const string Element = "element";

    /// <summary>What is inside a construct rather than part of its machinery.</summary>
    public const string Body = "body";

    /// <summary>One line of a grid, a staff, or anything else made of lines.</summary>
    public const string Row = "row";

    /// <summary>One cell of a row.</summary>
    public const string Cell = "cell";

    // ── Machinery ───────────────────────────────────────────────────────────
    //
    // Carried so the tree can be printed back, and so that nothing has to go looking at the characters
    // around a span to find out how the writer wrote it. Nothing points at one of these on its own: a
    // bracket without its partner cannot be read.

    /// <summary>The mark that makes a construct what it is: a command's name, a script's <c>^</c>, an
    /// ABC field's <c>K:</c>.</summary>
    public const string Name = "name";

    public const string Open = "open";
    public const string Close = "close";

    /// <summary>What stands between two parts: a <c>&amp;</c>, a bar line, a line break.</summary>
    public const string Separator = "separator";

    /// <summary>
    /// Space or a comment that fell inside a construct. Kept where it was found, because it is where the
    /// writer put it.
    /// </summary>
    public const string Trivia = "trivia";

    // ── The one role that was never written ─────────────────────────────────

    /// <summary>
    /// What a piece <em>amounts to</em>, hung underneath the piece itself.
    ///
    /// <para>
    /// A macro is one command the writer typed and two glyphs to whoever sets it. A note is one letter
    /// the writer typed and a pitch, a duration and possibly a printed accidental to whoever engraves it.
    /// The tree carries both: what was written, with what it means beneath it. So a reader of the tree
    /// can ask what was typed, a builder can walk down and find what to draw, and neither has to know
    /// what the other wanted.
    /// </para>
    /// <para>
    /// <strong>A derived part is not source, and nothing that measures source may see it.</strong> It has
    /// no width, prints as nothing, is not placed anywhere and holds no leaves — which is exactly what
    /// keeps <c>Print(Parse(s)) == s</c> true however much a pipeline stage hangs underneath. It is still
    /// a part like any other to anything asking what the content <em>means</em>.
    /// </para>
    /// </summary>
    public const string Derived = "derived";

    /// <summary>
    /// Content a binding supplied (<see cref="Kinds.BoundContent"/>): derived, since nobody wrote it here and it stands for none of the
    /// source, and set apart from what a stage works out because it is read-only in a way worked-out content is not — it can be
    /// picked out, but there is nowhere in it to put the caret or type.
    /// </summary>
    public const string Supplied = "supplied";

    /// <summary>
    /// What a piece says while the pointer rests on it — what an abbreviation stands for. Held by a
    /// <see cref="Derived"/> part, so it is found by asking the tree when the pointer arrives and is never laid out.
    /// </summary>
    public const string Tip = "tip";

    /// <summary>
    /// Where the thing holding this leads: a link's address, wherever a language lets a reader write one — a markdown link's
    /// target, a Mermaid <c>click</c> line's <c>href</c>.
    ///
    /// <para>
    /// Shared, because following a link is the engine's and not a language's: it has to know that a press on something means
    /// going somewhere without knowing which language wrote it, and the layout cannot answer — a piece says what it was drawn
    /// for, not what it stands over. Every language that lets a reader write an address marks it with this.
    /// </para>
    /// </summary>
    public const string Destination = "destination";
}

/// <summary>
/// What a piece <em>is</em> — the kinds every language has. Each declares its own beside these.
/// </summary>
/// <remarks>
/// Deliberately about syntax, not meaning. What a piece means is the roles its parts carry.
/// </remarks>
public static class Kinds
{
    /// <summary>Several things in a row: the whole content, a group's contents, a line of music.</summary>
    public const string Sequence = "sequence";

    /// <summary>One ordinary character of content.</summary>
    public const string Char = "char";

    /// <summary>
    /// A run of text a reader writes in: what a label, a title, a name or a lyric says, without the quotes or brackets
    /// around it.
    ///
    /// <para>
    /// Shared because the engine has to ask it. What a key means where nothing is written is the same question in every
    /// language, and the layout cannot answer it — a layout is a picture of what the source meant, and every piece of one
    /// offers the caret a stop. So a language says which of its pieces are runs of text, and the engine does the rest.
    /// </para>
    /// <para>
    /// Not a value. A number, a date, a setting and an identifier are written in too, and each has its own rule about what
    /// may go in it; this is the kind whose only rule is that it holds words.
    /// </para>
    /// </summary>
    public const string Words = "words";

    /// <summary>
    /// A number as it was written: an amount, a share, a count, a figure of a meter.
    ///
    /// <para>
    /// Written in as a run of words is, and by a rule of the language's rather than one of its own: what may go in a number
    /// is digits and whatever that language writes between them, and the point a reader of one language types is the comma
    /// a reader of another does. So what was typed is offered, and the language's parser is the judge of whether it goes in.
    /// </para>
    /// </summary>
    public const string Number = "number";

    /// <summary>A character that is machinery rather than content.</summary>
    public const string Token = "token";

    /// <summary>A run of whitespace.</summary>
    public const string Space = "space";

    /// <summary>A comment, however that language writes one.</summary>
    public const string Comment = "comment";

    /// <summary>
    /// Held as written rather than read: something that closes nothing, a construct nothing can draw, or
    /// a stretch somebody is in the middle of typing. Never an exception and never dropped —
    /// half-finished input is what an editor holds all day.
    /// <para>
    /// Which of those it is shows in whether the piece has anything to say for itself: a reading nobody
    /// could make carries the reason and gets a line drawn under it, where a stretch under the caret
    /// carries nothing and is simply shown.
    /// </para>
    /// </summary>
    public const string Verbatim = "verbatim";

    /// <summary>
    /// A block of content in one language (<see cref="BlockNode"/>) that holds content written in another: a fence in a document,
    /// a formula in a sentence, a tune on a flowchart node. The language it names is the one its <see cref="Nested"/> body is
    /// written in.
    /// </summary>
    public const string Block = "block";

    /// <summary>
    /// The characters of a whole other content written inside this one, held as written and unread — the body of a
    /// <see cref="Block"/> naming their language. Reading them is that language's, and happens only when a builder asks the
    /// engine to lay them out (<see cref="ContentNested"/>).
    /// </summary>
    public const string Nested = "nested";

    /// <summary>
    /// A binding standing where content would be (<c>{{Path}}</c>) — a diagram's lines, or the whole of it. The parser holds it as
    /// written; the engine asks whatever the content is shown against what it comes to, and the language reads that into its
    /// place as content of its own (<see cref="Roles.Supplied"/>), so the tree still prints as its author wrote it. Not
    /// <see cref="Bound"/>, which is a binding among words, standing for words.
    /// </summary>
    public const string BoundContent = "bound-content";

    /// <summary>
    /// A run standing in for a value somebody else holds, rather than for itself: what a document says about data
    /// it is shown against.
    ///
    /// <para>
    /// The path it names is a part of its own, so reading one is asking the tree for that part and never looking at
    /// a brace. What it stands for is worked out when the content is laid, so the same tree says different things
    /// against different data, and the characters it was written as are still all there to be written in.
    /// </para>
    /// </summary>
    public const string Bound = "bound";

    /// <summary>
    /// Somewhere something still has to go: an argument or a cell written empty, on a surface that is
    /// being written on.
    /// <para>
    /// Stands for nothing anybody typed, so it takes up none of the source and the tree still prints as
    /// what it came from — the same contract a derived part keeps, for the same reason.
    /// </para>
    /// </summary>
    public const string Hole = "hole";
}
