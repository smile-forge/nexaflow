using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Latex;

/// <summary>
/// What a piece is <em>to</em> the thing holding it — the roles only LaTeX has.
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
/// <para>
/// The machinery every language has — a command's name, a group's braces, a separator, the space inside a
/// construct, a row and a cell, what a construct holds, and what a macro stands for — is the shared
/// <see cref="Roles"/>. A macro's expansion is <see cref="Roles.Derived"/>: it stands for no source.
/// </para>
/// </summary>
public static class TexRole
{
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

    /// <summary>The <c>\begin{matrix}</c> and <c>\end{matrix}</c> of an environment.</summary>
    public const string Begin = "begin";

    public const string End = "end";
}
