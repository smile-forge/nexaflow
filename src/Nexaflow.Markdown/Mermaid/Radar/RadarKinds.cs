namespace Nexaflow.Markdown.Mermaid.Radar;

/// <summary>What a piece of a <c>radar-beta</c> diagram is.</summary>
public static class RadarKinds
{
    /// <summary>The colon Mermaid allows after <c>radar-beta</c> on the header line.</summary>
    public const string Colon = "radar-colon";

    /// <summary>An <c>axis</c> line: its word, and the axes it names.</summary>
    public const string Axes = "radar-axes";

    /// <summary>One axis: its name, and its label where one is written.</summary>
    public const string Axis = "radar-axis";

    /// <summary>A <c>curve</c> line: its word, and the curves it names.</summary>
    public const string Curves = "radar-curves";

    /// <summary>One curve: its name, its label where one is written, and its values.</summary>
    public const string Curve = "radar-curve";

    /// <summary>A curve's values, braces and all.</summary>
    public const string Values = "radar-values";

    /// <summary>One value: a number — or the axis it is for, a colon, and a number.</summary>
    public const string Entry = "radar-entry";

    /// <summary>A line of options — <c>max 100, min 0</c> — a comma between each.</summary>
    public const string Options = "radar-options";

    /// <summary>One option: its word, and what it is set to.</summary>
    public const string Option = "radar-option";

    /// <summary>What a stage worked out about a value: the axis it is for.</summary>
    public const string Fact = "radar-fact";
}

/// <summary>What a piece of a <c>radar-beta</c> diagram is <em>to</em> the piece holding it.</summary>
public static class RadarRoles
{
    /// <summary>An axis's or a curve's name — what a value names its axis by.</summary>
    public const string Id = "radar-id";

    /// <summary>An axis's or a curve's label: what is drawn for it.</summary>
    public const string Label = "radar-label";

    /// <summary>The axis a value names, before its colon.</summary>
    public const string Key = "radar-key";

    /// <summary>A value, or what an option is set to.</summary>
    public const string Value = "radar-value";

    /// <summary>The axis a value is for, worked out from where it stands or from the axis it names.</summary>
    public const string For = "radar-for";
}
