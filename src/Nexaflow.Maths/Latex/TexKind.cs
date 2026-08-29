namespace Nexaflow.Maths.Latex;

/// <summary>What a piece of a formula is.</summary>
/// <remarks>
/// Deliberately about syntax, not meaning: a <see cref="Command"/> is a backslash and a name with
/// arguments after it, whether it turns out to be a fraction or something nobody has heard of. What it
/// <em>means</em> is the role its parts carry (<see cref="TexRole"/>) and the table that assigned them.
/// </remarks>
public enum TexKind
{
    /// <summary>Several things in a row: the whole formula, a group's contents, a cell's contents.</summary>
    Sequence,

    /// <summary>A braced group — the braces are its own first and last children.</summary>
    Group,

    /// <summary>A control word or control symbol, together with any arguments the table gives it.</summary>
    Command,

    /// <summary>A <c>\begin</c>…<c>\end</c> pair and what is between them.</summary>
    Environment,

    /// <summary>One line of a grid environment, up to and including its <c>\\</c>.</summary>
    Row,

    /// <summary>One cell of a row, up to and including its <c>&amp;</c>.</summary>
    Cell,

    /// <summary>
    /// A base and everything written onto it: its superscript, its subscript, and its marks.
    /// <para>
    /// One node because it is one thing — one atom to select, to move and to delete. Both scripts of
    /// <c>x^2_3</c> are on the same x, and so are both primes and the subscript of <c>x''_{i}</c>; a
    /// node per attachment would nest them, and then a subscript would land on the prime standing
    /// immediately before it rather than on the x. Which is why <c>f''</c> is one of these despite
    /// having no script written on it at all: what makes it this kind is that something was attached.
    /// </para>
    /// </summary>
    Script,

    /// <summary>A <c>\left</c>…<c>\right</c> pair and what is between them.</summary>
    Fence,

    /// <summary>One ordinary character of content.</summary>
    Char,

    /// <summary>A character that is machinery rather than content: a brace, an <c>&amp;</c>, a <c>^</c>,
    /// a command's own name.</summary>
    Token,

    /// <summary>A run of whitespace.</summary>
    Space,

    /// <summary>A <c>%</c> comment, to the end of its line.</summary>
    Comment,

    /// <summary>
    /// Held as written rather than read: a <c>}</c> that closes nothing, a command nothing can draw, or
    /// a stretch somebody is in the middle of typing. Never an exception and never dropped —
    /// half-finished input is what an editor holds all day.
    /// <para>
    /// Which of those it is shows in whether the piece has anything to say for itself: a reading nobody
    /// could make carries the reason, and gets a line drawn under it, where a stretch under the caret
    /// carries nothing and is simply shown.
    /// </para>
    /// </summary>
    Verbatim,

    /// <summary>
    /// Somewhere something still has to go: an argument or a cell written empty, on a surface that is
    /// being written on.
    /// <para>
    /// Stands for nothing anybody typed, so it takes up none of the source and the tree still prints as
    /// what it came from — the same contract a macro's expansion keeps, for the same reason.
    /// </para>
    /// </summary>
    Hole,
}
