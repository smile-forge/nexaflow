namespace Nexaflow.Markdown.Mermaid.C4;

/// <summary>
/// What a line of a C4 diagram says. The macro set is one shape throughout, so there are few kinds: a call, a call that opens
/// a boundary, the line closing one, and a line a pasted diagram brought with it.
///
/// <para>
/// A <c>C4Sequence</c> is written in two languages at once, and everything in it that is a sequence diagram's own is read
/// into <see cref="Sequence.SequenceKinds"/> — which is why a boundary's <c>}</c> is a kind of C4's own rather than the
/// sequence diagram's <c>end</c>: one nesting closes on both words, and each language keeps its own.
/// </para>
/// </summary>
public static class C4Kinds
{
    /// <summary>A macro call: its name, the arguments between its brackets, and the brace that may open a block after them.</summary>
    public const string Macro = "c4-macro";

    /// <summary>A macro that opens a boundary round everything written until the line that closes it.</summary>
    public const string Boundary = "c4-boundary";

    /// <summary>The line closing a boundary — a <c>}</c>, a <c>})</c>, or a <c>Boundary_End()</c>.</summary>
    public const string Ends = "c4-end";

    /// <summary>A line a diagram pasted from PlantUML brings with it — <c>@startuml</c>, <c>!include</c> — read and drawn as nothing.</summary>
    public const string Aside = "c4-aside";

}

/// <summary>What a piece of a C4 line is to the piece holding it.</summary>
public static class C4Roles
{
    /// <summary>The name of the macro being called.</summary>
    public const string Macro = "c4-macro-name";

    /// <summary>The name of an argument given by name — the <c>techn</c> of <c>$techn="JDBC"</c>.</summary>
    public const string Key = "c4-key";

    /// <summary>What an argument is, where it does not name an element.</summary>
    public const string Value = "c4-value";

    /// <summary>What a boundary is called, which is its own name rather than a participant's.</summary>
    public const string Alias = "c4-alias";

    /// <summary>A line read and drawn as nothing.</summary>
    public const string Aside = "c4-aside-text";

}
