namespace Nexaflow.Markdown.Mermaid.Pie;

/// <summary>What a piece of a <c>pie</c> diagram is.</summary>
public static class PieKinds
{
    /// <summary>What follows <c>pie</c> on the header line: <c>showData</c>, a title, or both.</summary>
    public const string Options = "pie-options";

    /// <summary>The <c>showData</c> word, which puts each slice's value in the legend beside its share.</summary>
    public const string ShowData = "pie-show-data";

    /// <summary>One slice: its label, its colon and its value — a <see cref="PieSliceNode"/> once its stages have run.</summary>
    public const string Slice = "pie-slice";

    /// <summary>A backslash and the character it keeps, inside a label: <c>\"</c>, <c>\\</c>, <c>\n</c>.</summary>
    public const string Escape = "pie-escape";

    /// <summary>A line ending inside a label, which a label written in quotes may run across.</summary>
    public const string Break = "pie-break";
}

/// <summary>What a piece of a <c>pie</c> diagram is <em>to</em> the piece holding it.</summary>
public static class PieRoles
{
    /// <summary>A slice's label, or a title's text.</summary>
    public const string Label = "pie-label";

    /// <summary>A slice's value.</summary>
    public const string Value = "pie-value";
}
