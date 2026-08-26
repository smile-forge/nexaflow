namespace Nexaflow.Maths.Latex;

/// <summary>
/// What a piece is <em>to</em> the thing holding it.
///
/// <para>
/// The point of the whole tree. A <c>3</c> is a 3 wherever it appears; a 3 whose role is
/// <see cref="Degree"/> is the degree of a root, and only the second reading lets it be copied onto
/// something else and produce a cube root of that something.
/// </para>
/// <para>
/// Strings rather than an enum, so that teaching the table a new command does not mean editing a type
/// every consumer switches over — and so these can be compared directly with the roles the typesetter's
/// own slots carry while both trees are in play. The meaning-bearing names below are exactly its names.
/// </para>
/// </summary>
public static class TexRole
{
    // ── Parts that mean something to the construct holding them ─────────────

    /// <summary>What a script, an accent or a limit is attached to.</summary>
    public const string Base = "base";

    public const string Superscript = "superscript";
    public const string Subscript = "subscript";

    /// <summary>
    /// A mark written after what it marks, and belonging to it: the <c>'</c> of <c>f'</c>.
    /// <para>
    /// Named for where it sits and not for what it draws, deliberately. A prime is set as a superscript,
    /// and it also means a derivative, a transpose, a minute of arc or a second copy of a thing —
    /// readings that belong to whoever is reading, not to the tree. What the tree is saying is only that
    /// this was written onto the <see cref="Base"/> beside it and is part of the same thing, which is
    /// what makes <c>f'</c> one unit to select and lets a script written after it land on the <c>f</c>.
    /// </para>
    /// </summary>
    public const string Mark = "mark";
    public const string Numerator = "numerator";
    public const string Denominator = "denominator";

    /// <summary>The <c>3</c> of <c>\sqrt[3]{x}</c>.</summary>
    public const string Degree = "degree";

    /// <summary>The <c>x</c> of <c>\sqrt{x}</c>.</summary>
    public const string Radicand = "radicand";

    public const string Over = "over";
    public const string Under = "under";

    /// <summary>An argument of a command the table knows no better name for.</summary>
    public const string Argument = "argument";

    /// <summary>A bracketed argument: <c>[3]</c>, and the column spec of an <c>array</c>.</summary>
    public const string Option = "option";

    /// <summary>One of several things in a row, which is all a sequence can say about what is in it.</summary>
    public const string Element = "element";

    /// <summary>One cell of a grid.</summary>
    public const string Cell = "cell";

    /// <summary>One line of a grid.</summary>
    public const string Row = "row";

    /// <summary>What is between <c>\begin</c> and <c>\end</c>, or between a fence's delimiters.</summary>
    public const string Body = "body";

    // ── Parts that are machinery ────────────────────────────────────────────
    //
    // Carried so the tree can be printed back, and so that nothing has to go looking at the characters
    // around a span to find out whether the writer used braces. Nothing points at one of these on its
    // own: a brace without its partner cannot be read, and neither can half a \begin.

    /// <summary>The mark that makes a construct what it is: a command's <c>\name</c>, a script's
    /// <c>^</c> or <c>_</c>.</summary>
    public const string Name = "name";

    public const string Open = "open";
    public const string Close = "close";

    /// <summary>An <c>&amp;</c> or a <c>\\</c>.</summary>
    public const string Separator = "separator";

    /// <summary>The <c>\begin{matrix}</c> and <c>\end{matrix}</c> of an environment.</summary>
    public const string Begin = "begin";

    public const string End = "end";

    /// <summary>
    /// Space or a comment that fell inside a construct — between a command and its argument, say. Kept
    /// where it was found, because it is where the writer put it.
    /// </summary>
    public const string Trivia = "trivia";
}
