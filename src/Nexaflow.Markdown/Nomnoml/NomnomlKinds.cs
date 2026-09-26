namespace Nexaflow.Markdown.Nomnoml;

/// <summary>What a nomnoml block is made of, as <see cref="NomnomlParser"/> reads it.</summary>
public static class NomnomlKinds
{
    /// <summary>The whole block: its lines, and the groups gathered with the lines written in them.</summary>
    public const string Diagram = "nomnoml-diagram";

    /// <summary>One line: the space before what it says, what it says, and the characters that end it.</summary>
    public const string Line = "nomnoml-line";

    /// <summary>A run of words: a name, a member, a count, what an association says, a directive's name or value.</summary>
    public const string Words = "nomnoml-words";

    /// <summary>How two nodes are joined: an end, a line and another end, each written or not.</summary>
    public const string Operator = "nomnoml-operator";
    /// <summary>One node in brackets, with whatever it holds: its classifier, its name, its compartments.</summary>
    public const string Node = "nomnoml-node";

    /// <summary>A node opened on one line and closed on another: the line opening it, the lines between, and the line closing it.</summary>
    public const string Group = "nomnoml-group";

    /// <summary>What a node says it is, written in angle brackets before its name.</summary>
    public const string Classifier = "nomnoml-classifier";

    /// <summary>One compartment of a node, past its name: its members, or the nodes written inside it.</summary>
    public const string Compartment = "nomnoml-compartment";

    /// <summary>One node joined to another, and everything written along the way.</summary>
    public const string Association = "nomnoml-association";

    /// <summary>A setting written with a hash: <c>#direction: right</c>.</summary>
    public const string Directive = "nomnoml-directive";

    /// <summary>A line of comment, which nomnoml opens with two slashes.</summary>
    public const string Comment = "nomnoml-comment";
}

/// <summary>What each piece of a nomnoml line is.</summary>
public static class NomnomlRoles
{
    /// <summary>A node's name, which is what an association names it by.</summary>
    public const string Id = "nomnoml-id";

    /// <summary>What a node says it is: <c>abstract</c>, <c>note</c>, <c>frame</c>.</summary>
    public const string Classifier = "nomnoml-classifier";

    /// <summary>One member written in a compartment past the name.</summary>
    public const string Member = "nomnoml-member";

    /// <summary>How two nodes are joined, which is what the line between them is drawn as.</summary>
    public const string Operator = "nomnoml-operator";

    /// <summary>What an operator draws at the node on its left: <c>&lt;:</c>, <c>+</c>, <c>o</c>, <c>&lt;</c>, a ball or a socket.</summary>
    public const string Head = "nomnoml-head";

    /// <summary>The line an operator draws: a single dash solid, anything else dashed.</summary>
    public const string Drawn = "nomnoml-drawn";

    /// <summary>What an operator draws at the node on its right.</summary>
    public const string Tail = "nomnoml-tail";

    /// <summary>What is written between a node and the operator after it — how many of it there are.</summary>
    public const string Near = "nomnoml-near";

    /// <summary>What is written between the operator and the node after it.</summary>
    public const string Far = "nomnoml-far";

    /// <summary>What the association itself says, written after a colon at the end of the line.</summary>
    public const string Said = "nomnoml-said";

    /// <summary>A directive's name.</summary>
    public const string Key = "nomnoml-key";

    /// <summary>What a directive is set to.</summary>
    public const string Setting = "nomnoml-setting";
}
