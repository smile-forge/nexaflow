namespace Nexaflow.Markdown.Mermaid.Class;

/// <summary>What a line of a class diagram says beyond the lines every diagram shares. What a line is made
/// of — names, labels, styles — is <see cref="MermaidKinds"/>'.</summary>
public static class ClassKinds
{
    /// <summary>A <c>class Foo</c> line, declaring a class and nothing inside it.</summary>
    public const string Class = "class-class";

    /// <summary>A <c>class Foo { … }</c> written across several lines, the members between the braces and all.</summary>
    public const string Body = "class-body";

    /// <summary>The <c>class Foo {</c> line that opens a body.</summary>
    public const string Opens = "class-opens";

    /// <summary>One member written inside a body: a field, a method, or an annotation on its own line.</summary>
    public const string Member = "class-member";

    /// <summary>The <c>}</c> that closes a body.</summary>
    public const string Shut = "class-shut";

    /// <summary>A <c>Foo : +int age</c> line, which gives a class one member without opening a body.</summary>
    public const string Says = "class-says";

    /// <summary>A <c>namespace N {</c> line, which boxes the classes written until the <c>}</c> closing it.</summary>
    public const string Namespace = "class-namespace";

    /// <summary>The <c>}</c> that closes a namespace.</summary>
    public const string Ends = "class-ends";

    /// <summary>A relation between two classes: the operator drawn between them, and what is written on it.</summary>
    public const string Relation = "class-relation";

    /// <summary>A <c>&lt;&lt;interface&gt;&gt; Shape</c> line, which annotates a class declared elsewhere.</summary>
    public const string Annotation = "class-annotation";

    /// <summary>A <c>note</c> line: one beside a class, or one floating with nothing to be beside.</summary>
    public const string Note = "class-note";

    /// <summary>A <c>direction LR</c> line, which lays the diagram out.</summary>
    public const string Direction = "class-direction";

    /// <summary>A <c>cssClass "A,B" blue</c> line, which is how a class diagram gives something a class.</summary>
    public const string CssClass = "class-css";

    public const string ClassDef = "class-class-def";

    public const string Style = "class-style";

    /// <summary>A <c>click</c>, <c>callback</c> or <c>link</c> line: where pressing a class leads, and what it says while pointed at.</summary>
    public const string Click = "class-click";

    /// <summary>A relation's arrow: what it draws at either end, and its line.</summary>
    public const string Arrow = "class-arrow";

    /// <summary>One class where it is named: its id, its type parameters, and the class <c>:::</c> gives it.</summary>
    public const string Named = "class-named";

    /// <summary>What is written on a relation, after the colon opening it: the rest of the line, as it is written.</summary>
    public const string Said = "class-said-text";
}

/// <summary>What a piece of a class diagram's line is to the piece holding it.</summary>
public static class ClassRoles
{
    /// <summary>What a class is called, which is what a relation, a note and a styling line name it by.</summary>
    public const string Id = "class-id";

    /// <summary>What is drawn in place of a class's id, from <c>class Foo["Shown instead"]</c>.</summary>
    public const string Label = "class-label";

    /// <summary>The type parameters written between tildes, drawn between angle brackets.</summary>
    public const string Generic = "class-generic";

    /// <summary>One member: a field where it has no brackets, a method where it has.</summary>
    public const string Member = "class-member-text";

    /// <summary>What <c>&lt;&lt;interface&gt;&gt;</c> says a class is, drawn over its name.</summary>
    public const string Kind = "class-kind";

    /// <summary>The name of a class, declared by a <c>classDef</c> and given by <c>cssClass</c> or by <c>:::</c>.</summary>
    public const string Class = "class-class-name";

    /// <summary>The operator drawn between two classes, its line and both its ends.</summary>
    public const string Arrow = "class-arrow-drawn";

    /// <summary>What an arrow draws at the class on its left: <c>&lt;|</c>, <c>*</c>, <c>o</c>, <c>&lt;</c> or <c>()</c>.</summary>
    public const string Head = "class-head";

    /// <summary>An arrow's line: <c>--</c> solid, <c>..</c> dotted.</summary>
    public const string Line = "class-line";

    /// <summary>What an arrow draws at the class on its right.</summary>
    public const string Tail = "class-tail";

    /// <summary>How many of one class the other has, written in quotes at that end of the relation.</summary>
    public const string Count = "class-count";

    /// <summary>What is written on a relation, after the colon opening it.</summary>
    public const string Said = "class-said";

    /// <summary>The way the diagram is laid out: <c>TB</c>, <c>LR</c>.</summary>
    public const string Towards = "class-towards";

    /// <summary>Where pressing a class leads.</summary>
    public const string Href = "class-href";

    /// <summary>What a class says while it is pointed at.</summary>
    public const string Tip = "class-tip";

    /// <summary>Where a link a <c>click</c> line writes opens: <c>_blank</c>, <c>_self</c>.</summary>
    public const string Target = "class-target";

    /// <summary>The function a <c>callback</c> or a <c>click … call</c> line names, which nothing here calls.</summary>
    public const string Call = "class-call";

    /// <summary>
    /// What a namespace is called. Its own role rather than an id's, because a namespace is not a class: one named the same
    /// as a class is a different thing, and renaming either leaves the other alone.
    /// </summary>
    public const string Space = "class-space";
}
