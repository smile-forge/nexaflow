namespace Nexaflow.Markdown.Mermaid.Cynefin;

/// <summary>The pieces a Cynefin diagram's layout is made of — its layers, and what is in them.</summary>
public static class CynefinPiece
{
    /// <summary>The four practised domains, and one — standing for the line opening it.</summary>
    public const string Domains = "Domains";
    public const string Domain = "Domain";

    /// <summary>The line round the grid, and the boundaries sweeping from its edges into the disorder in the middle.</summary>
    public const string Boundaries = "Boundaries";

    /// <summary>The movements between domains, and one — standing for the transition as written.</summary>
    public const string Moves = "Moves";
    public const string Move = "Move";

    /// <summary>Disorder: the confusion domain, drawn as a cloud in the middle with what is in it inside.</summary>
    public const string Centre = "Centre";

    /// <summary>The items, and one — standing for the item as written.</summary>
    public const string Items = "Items";
    public const string Item = "Item";

    /// <summary>What is written on the grid: each domain's word, how it is worked, an item's words, a movement's label.</summary>
    public const string Words = "Words";
    public const string Name = "Name";
    public const string About = "About";
    public const string Says = "Says";
    public const string Label = "Label";
}
