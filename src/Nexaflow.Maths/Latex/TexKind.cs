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
    /// Held as written, because it could not be read: a <c>}</c> that closes nothing, an <c>\end</c>
    /// naming an environment that was never begun. Never an exception and never dropped — half-finished
    /// input is what an editor holds all day.
    /// </summary>
    Verbatim,
}
