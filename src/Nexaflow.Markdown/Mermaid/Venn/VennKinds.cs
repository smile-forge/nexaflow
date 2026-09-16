namespace Nexaflow.Markdown.Mermaid.Venn;

/// <summary>What a piece of a <c>venn-beta</c> diagram is.</summary>
public static class VennKinds
{
    /// <summary>
    /// A set or a union with the items written in it: its own line, then each item's. What the diagram is made of, where the
    /// lines are only how it was written down.
    /// </summary>
    public const string Region = "venn-region";

    /// <summary>A <c>set</c> line: its name, and its label and size where they are written.</summary>
    public const string Set = "venn-set";

    /// <summary>A <c>union</c> line: the sets it is the overlap of, and its label and size where they are written.</summary>
    public const string Union = "venn-union";

    /// <summary>A <c>text</c> line: an item written inside a set or a union — its region where it names one, its name and its label.</summary>
    public const string Text = "venn-text";

    /// <summary>A <c>style</c> line: what it styles, and the properties it sets.</summary>
    public const string Style = "venn-style";

    /// <summary>What a stage worked out: the key a region is known by, the region an item sits in, what a style styles.</summary>
    public const string Fact = "venn-fact";
}

/// <summary>What a piece of a <c>venn-beta</c> diagram is <em>to</em> the piece holding it.</summary>
public static class VennRoles
{
    /// <summary>A set's or an item's name.</summary>
    public const string Id = "venn-id";

    /// <summary>A label's text, or a title's.</summary>
    public const string Label = "venn-label";

    /// <summary>A set's or a union's size.</summary>
    public const string Size = "venn-size";

    /// <summary>The set or union a <c>text</c> line names as the one its item sits in.</summary>
    public const string Region = "venn-region";

    /// <summary>What a <c>style</c> line styles.</summary>
    public const string Target = "venn-target";

    /// <summary>
    /// The region a part stands for, worked out: a set's name, or a union's names sorted and with a comma between each —
    /// which is how the same overlap written <c>B,A</c> and <c>A,B</c> is known to be one.
    /// </summary>
    public const string Key = "venn-key";
}
