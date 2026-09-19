namespace Nexaflow.Markdown.Mermaid.Er;

/// <summary>What a line of an entity-relationship diagram says beyond the lines every diagram shares. What a line is
/// made of — names, labels, styles — is <see cref="MermaidKinds"/>'.</summary>
public static class ErKinds
{
    /// <summary>An <c>ENTITY { … }</c> written across several lines, the attributes between the braces and all.</summary>
    public const string Block = "er-block";

    /// <summary>The <c>ENTITY {</c> line that opens one.</summary>
    public const string Opens = "er-opens";

    /// <summary>One attribute written between the braces: its type, its name, the keys it is, and what it says it is for.</summary>
    public const string Attribute = "er-attribute";

    /// <summary>The <c>}</c> that closes a block.</summary>
    public const string Shut = "er-shut";

    /// <summary>An entity written on a line of its own, with no attributes under it.</summary>
    public const string Entity = "er-entity";

    /// <summary>A relationship between two entities: how many of each the other has, and what it is called.</summary>
    public const string Relation = "er-relation";

    /// <summary>What is written on a relationship, after the colon opening it.</summary>
    public const string Said = "er-said";

    /// <summary>A <c>subgraph … </c> line, which boxes the entities written until the <c>end</c> closing it.</summary>
    public const string Subgraph = "er-subgraph";

    /// <summary>The <c>end</c> that closes a subgraph.</summary>
    public const string Ends = "er-ends";

    /// <summary>A <c>direction LR</c> line, which lays the diagram out.</summary>
    public const string Direction = "er-direction";

    public const string ClassDef = "er-class-def";

    /// <summary>A <c>class A,B blue</c> line, which gives what it names a class.</summary>
    public const string CssClass = "er-class";

    public const string Style = "er-style";

    /// <summary>One entity where it is named: its name, what is drawn instead of it, and the classes <c>:::</c> gives it.</summary>
    public const string Named = "er-named";

    /// <summary>What the stages hang under a line: which subgraph it is in, and which one it opens.</summary>
    public const string Fact = "er-fact";
}

/// <summary>What a piece of an entity-relationship diagram's line is to the piece holding it.</summary>
public static class ErRoles
{
    /// <summary>What an entity is called, which is what a relationship and a styling line name it by.</summary>
    public const string Id = "er-id";

    /// <summary>What is drawn in place of its name, from <c>CUSTOMER["The customer"]</c>.</summary>
    public const string Label = "er-label";

    /// <summary>What an attribute holds: <c>string</c>, <c>int</c>, <c>string[]</c>, <c>decimal(10,2)</c>.</summary>
    public const string Type = "er-type";

    /// <summary>What the attribute is called.</summary>
    public const string Field = "er-field";

    /// <summary>What an attribute is: <c>PK</c>, <c>FK</c> or <c>UK</c>.</summary>
    public const string Key = "er-key";

    /// <summary>What an attribute says it is for, written in quotes at the end of its line.</summary>
    public const string Comment = "er-comment";

    /// <summary>How many of the entity at this end the other has, written as a symbol or in words.</summary>
    public const string Card = "er-card";

    /// <summary>The line between the two, which says whether the relationship identifies what it reaches.</summary>
    public const string Arrow = "er-arrow";

    /// <summary>What a relationship is called, drawn over the middle of its line.</summary>
    public const string Said = "er-said-text";

    /// <summary>The name of a class, declared by a <c>classDef</c> and given by <c>class</c> or by <c>:::</c>.</summary>
    public const string Class = "er-class-name";

    /// <summary>
    /// What a subgraph is called. Its own role rather than an entity's, because a subgraph is not an entity: one named the
    /// same as an entity is a different thing, and renaming either leaves the other alone.
    /// </summary>
    public const string Space = "er-space";

    /// <summary>The way the diagram is laid out: <c>TB</c>, <c>LR</c>.</summary>
    public const string Towards = "er-towards";

    /// <summary>The subgraph a line is written in (<see cref="MermaidNesting"/>).</summary>
    public const string Inside = "er-inside";

    /// <summary>The subgraph a <c>subgraph</c> line opens.</summary>
    public const string Opened = "er-opened";

    /// <summary>The subgraph an end of a relationship names, where it names one rather than an entity.</summary>
    public const string Boxed = "er-boxed";
}
