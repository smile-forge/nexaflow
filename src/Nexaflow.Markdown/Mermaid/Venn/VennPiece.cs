namespace Nexaflow.Markdown.Mermaid.Venn;

/// <summary>The pieces a Venn diagram's layout is made of — its layers, and what is in them.</summary>
public static class VennPiece
{
    /// <summary>The circles, which is the diagram itself.</summary>
    public const string Circles = "Circles";

    /// <summary>One set's circle. It stands for the set as it was written — its line and its items — so pressing it means that.</summary>
    public const string Circle = "Circle";

    /// <summary>The overlaps the unions name.</summary>
    public const string Overlaps = "Overlaps";

    /// <summary>
    /// One union's overlap: where its circles meet and no other circle covers, which is what a press on it means — and what
    /// the circles themselves stand back from. Drawn over the whole lens, where the union's style gives it a fill or an outline.
    /// </summary>
    public const string Overlap = "Overlap";

    /// <summary>What is written on the circles: each region's label and the items in it.</summary>
    public const string Labels = "Labels";

    /// <summary>One region's words: its label, and its items under it.</summary>
    public const string Region = "Region";

    /// <summary>A set's or a union's label — or its name, where it has none.</summary>
    public const string Label = "Label";

    /// <summary>One item, holding what it says.</summary>
    public const string Item = "Item";

    /// <summary>What an item says: its label, or its name where it has none.</summary>
    public const string Text = "Text";

    /// <summary>The layout's workings, drawn where the front matter asks for them: each circle's centre, and where each region's words are set.</summary>
    public const string Debug = "Debug";
}
