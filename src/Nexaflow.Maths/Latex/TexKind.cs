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

    /// <summary>Something with a superscript, a subscript, or both.</summary>
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
