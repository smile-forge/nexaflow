namespace Nexaflow.Markdown.Mermaid.Requirement;

/// <summary>What a line of a requirement diagram says beyond the lines every diagram shares. What a line is made
/// of — names, values, styles — is <see cref="MermaidKinds"/>'.</summary>
public static class RequirementKinds
{
    /// <summary>A <c>requirement Foo { … }</c> written across several lines, the fields between the braces and all.</summary>
    public const string Block = "requirement-block";

    /// <summary>The <c>requirement Foo {</c> line that opens one, whose word says what kind of thing it is.</summary>
    public const string Opens = "requirement-opens";

    /// <summary>One <c>id: 1</c> field written between the braces.</summary>
    public const string Field = "requirement-field";

    /// <summary>What a field is set to, which is where a hole stands while nothing is.</summary>
    public const string Value = "requirement-value";

    /// <summary>The <c>}</c> that closes a block.</summary>
    public const string Shut = "requirement-shut";

    /// <summary>A relation between two of them: <c>A - satisfies -> B</c>.</summary>
    public const string Relation = "requirement-relation";

    /// <summary>A name written on a line of its own, which writes the box and is where <c>:::</c> gives it a class.</summary>
    public const string Naming = "requirement-naming";

    /// <summary>A <c>direction LR</c> line, which lays the diagram out.</summary>
    public const string Direction = "requirement-direction";

    public const string ClassDef = "requirement-class-def";

    /// <summary>A <c>class A,B blue</c> line, which gives what it names a class.</summary>
    public const string CssClass = "requirement-css";

    public const string Style = "requirement-style";

    /// <summary>One requirement where it is named: what it is called, and the class <c>:::</c> gives it.</summary>
    public const string Named = "requirement-named";
}

/// <summary>What a piece of a requirement diagram's line is to the piece holding it.</summary>
public static class RequirementRoles
{
    /// <summary>What a requirement or an element is called, which a relation and a styling line name it by.</summary>
    public const string Id = "requirement-id";

    /// <summary>What kind of thing it is: the word its block opens with, drawn in guillemets over its name.</summary>
    public const string Kind = "requirement-kind";

    /// <summary>Which field it is: <c>id</c>, <c>text</c>, <c>risk</c>, <c>verifymethod</c>, <c>type</c> or <c>docref</c>.</summary>
    public const string Key = "requirement-key";

    /// <summary>What a field is set to.</summary>
    public const string Value = "requirement-value-text";

    /// <summary>What holds between two of them, drawn on the line joining them.</summary>
    public const string Says = "requirement-says";

    /// <summary>The line and the arrow a relation is drawn with, which also say which end it leaves.</summary>
    public const string Arrow = "requirement-arrow";

    /// <summary>The name of a class, declared by a <c>classDef</c> and given by <c>class</c> or by <c>:::</c>.</summary>
    public const string Class = "requirement-class-name";

    /// <summary>The way the diagram is laid out: <c>TB</c>, <c>LR</c>.</summary>
    public const string Towards = "requirement-towards";
}
