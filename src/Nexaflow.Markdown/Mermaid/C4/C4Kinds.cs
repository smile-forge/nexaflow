namespace Nexaflow.Markdown.Mermaid.C4;

/// <summary>
/// What a line of a C4 diagram says beyond the lines every diagram shares, and beyond the sequence diagram's own — a C4
/// sequence says what a sequence says in C4-PlantUML's words, so most of what it writes is read into
/// <see cref="Sequence.SequenceKinds"/> and only the macro call itself is here.
/// </summary>
public static class C4Kinds
{
    /// <summary>A macro call: its name, the arguments between its brackets, and the brace that may open a block after them.</summary>
    public const string Macro = "c4-macro";

    /// <summary>A macro that opens a boundary round everything written until the line that closes it.</summary>
    public const string Boundary = "c4-boundary";

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
