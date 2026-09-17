namespace Nexaflow.Markdown.Mermaid.Xy;

/// <summary>What a piece of an <c>xychart</c> diagram is.</summary>
public static class XyKinds
{
    /// <summary>What follows <c>xychart</c> on the header line: <c>horizontal</c> or <c>vertical</c>.</summary>
    public const string Options = "xy-options";

    /// <summary>The word saying which way the chart runs.</summary>
    public const string Orientation = "xy-orientation";

    /// <summary>An <c>x-axis</c> or <c>y-axis</c> line: its word, its title, and its categories or its range.</summary>
    public const string Axis = "xy-axis";

    /// <summary>An axis's categories, brackets and all.</summary>
    public const string Categories = "xy-categories";

    /// <summary>An axis's range: <c>0 --&gt; 100</c>.</summary>
    public const string Range = "xy-range";

    /// <summary>A <c>bar</c> or <c>line</c> line: its word, its name, and its values.</summary>
    public const string Series = "xy-series";

    /// <summary>A series' values, brackets and all.</summary>
    public const string Values = "xy-values";

    /// <summary>One value, and the label written after it where one is.</summary>
    public const string Point = "xy-point";
}

/// <summary>What a piece of an <c>xychart</c> diagram is <em>to</em> the piece holding it.</summary>
public static class XyRoles
{
    /// <summary>An axis's title, or a series' name.</summary>
    public const string Title = "xy-title";

    /// <summary>One of an axis's categories.</summary>
    public const string Category = "xy-category";

    /// <summary>Where a range starts.</summary>
    public const string Min = "xy-min";

    /// <summary>Where a range ends.</summary>
    public const string Max = "xy-max";

    /// <summary>A series' value.</summary>
    public const string Value = "xy-value";

    /// <summary>The label a point of a line carries.</summary>
    public const string Label = "xy-label";
}
