namespace Nexaflow.Markdown.Mermaid.Venn;

/// <summary>What a piece of a <c>venn-beta</c> diagram is.</summary>
public static class VennKinds
{
    /// <summary>
    /// A set or a union with the items written in it: its own line, then each item's. What the diagram is made of, where the
    /// lines are only how it was written down.
    /// </summary>
    public const string Region = "venn-region";

    /// <summary>A <c>title …</c> line.</summary>
    public const string Title = "venn-title";

    /// <summary>A <c>set</c> line: its name, and its label and size where they are written.</summary>
    public const string Set = "venn-set";

    /// <summary>A <c>union</c> line: the sets it is the overlap of, and its label and size where they are written.</summary>
    public const string Union = "venn-union";

    /// <summary>A <c>text</c> line: an item written inside a set or a union — its region where it names one, its name and its label.</summary>
    public const string Text = "venn-text";

    /// <summary>A <c>style</c> line: what it styles, and the properties it sets.</summary>
    public const string Style = "venn-style";

    /// <summary>
    /// A name — a set's, an item's — bare or in quotes. Quotes included, so a name with nothing between its quotes yet is
    /// somewhere a hole stands.
    /// </summary>
    public const string Id = "venn-id";

    /// <summary>Several names with a comma between each: the sets a union overlaps, a region, what a style styles.</summary>
    public const string Sets = "venn-sets";

    /// <summary>A label in its brackets, with the quotes inside them where it has any.</summary>
    public const string Label = "venn-label";

    /// <summary>Text in quotes, quotes included.</summary>
    public const string Quoted = "venn-quoted";

    /// <summary>What a name, a label or a title says, without its quotes or brackets.</summary>
    public const string Name = "venn-name";

    /// <summary>
    /// Where a size is written: the number, or — while none has been — the place after the colon it goes, which is where a
    /// hole stands for it.
    /// </summary>
    public const string Weight = "venn-weight";

    /// <summary>A size, as it was written.</summary>
    public const string Size = "venn-size";

    /// <summary>The properties of a style, with a comma between each.</summary>
    public const string Properties = "venn-properties";

    /// <summary>One <c>name:value</c> of a style.</summary>
    public const string Property = "venn-property";

    /// <summary>What a property is set to, as it was written.</summary>
    public const string Setting = "venn-setting";

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

    /// <summary>What a property is set to.</summary>
    public const string Value = "venn-value";

    /// <summary>
    /// The region a part stands for, worked out: a set's name, or a union's names sorted and with a comma between each —
    /// which is how the same overlap written <c>B,A</c> and <c>A,B</c> is known to be one.
    /// </summary>
    public const string Key = "venn-key";
}
