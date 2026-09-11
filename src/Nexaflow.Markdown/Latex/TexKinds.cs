using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Latex;

/// <summary>What a piece of a formula is — the kinds only LaTeX has.</summary>
/// <remarks>
/// Deliberately about syntax, not meaning: a <see cref="Command"/> is a backslash and a name with
/// arguments after it, whether it turns out to be a fraction or something nobody has heard of. What it
/// <em>means</em> is the role its parts carry (<see cref="TexRole"/>) and the table that assigned them.
/// The kinds every language has — a sequence, a character, a token, space, a comment, a stretch shown as
/// written, a hole — are the shared <see cref="Kinds"/>.
/// </remarks>
public static class TexKinds
{
    /// <summary>A braced group — the braces are its own first and last children.</summary>
    public const string Group = "group";

    /// <summary>A control word or control symbol, together with any arguments the table gives it.</summary>
    public const string Command = "command";

    /// <summary>A <c>\begin</c>…<c>\end</c> pair and what is between them.</summary>
    public const string Environment = "environment";

    /// <summary>One line of a grid environment, up to and including its <c>\\</c>.</summary>
    public const string Row = "row";

    /// <summary>One cell of a row, up to and including its <c>&amp;</c>.</summary>
    public const string Cell = "cell";

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
    public const string Script = "script";

    /// <summary>A <c>\left</c>…<c>\right</c> pair and what is between them.</summary>
    public const string Fence = "fence";
}
