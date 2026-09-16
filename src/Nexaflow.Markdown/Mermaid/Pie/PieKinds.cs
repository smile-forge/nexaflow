namespace Nexaflow.Markdown.Mermaid.Pie;

/// <summary>What a piece of a <c>pie</c> diagram is.</summary>
public static class PieKinds
{
    /// <summary>What follows <c>pie</c> on the header line: <c>showData</c>, a title, or both.</summary>
    public const string Options = "pie-options";

    /// <summary>The <c>showData</c> word, which puts each slice's value in the legend beside its share.</summary>
    public const string ShowData = "pie-show-data";

    /// <summary>One slice: its label, its colon and its value.</summary>
    public const string Slice = "pie-slice";

    /// <summary>What a stage worked out about a slice: the colour it takes, which key that came from, whether it is picked out.</summary>
    public const string Fact = "pie-fact";
}

/// <summary>What a piece of a <c>pie</c> diagram is <em>to</em> the piece holding it.</summary>
public static class PieRoles
{
    /// <summary>A slice's label, or a title's text.</summary>
    public const string Label = "pie-label";

    /// <summary>A slice's value.</summary>
    public const string Value = "pie-value";

    /// <summary>The colour a slice is drawn in, worked out from the diagram's config and its place in the order.</summary>
    public const string Colour = "pie-colour";

    /// <summary>Which of <c>pie1</c>…<c>pieN</c> a slice's colour came from, where one was written.</summary>
    public const string Swatch = "pie-swatch";

    /// <summary>Hung under a slice the config picks out.</summary>
    public const string Highlighted = "pie-highlighted";
}
