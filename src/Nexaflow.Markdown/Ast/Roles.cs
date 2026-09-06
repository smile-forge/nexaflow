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
    /// Somewhere something still has to go: an argument or a cell written empty, on a surface that is
    /// being written on.
    /// <para>
    /// Stands for nothing anybody typed, so it takes up none of the source and the tree still prints as
    /// what it came from — the same contract a derived part keeps, for the same reason.
    /// </para>
    /// </summary>
    public const string Hole = "hole";
}
