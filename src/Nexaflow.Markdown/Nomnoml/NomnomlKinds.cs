namespace Nexaflow.Markdown.Nomnoml;

/// <summary>What a nomnoml line is made of. A nomnoml block is drawn as a class diagram, so what these
/// stand for is read into <see cref="Mermaid.Class.ClassDiagram"/> — see <see cref="NomnomlDiagram"/>.</summary>
public static class NomnomlKinds
{
    /// <summary>One node in brackets, with whatever it holds: its classifier, its name, its compartments.</summary>
    public const string Node = "nomnoml-node";

    /// <summary>A node opened on one line and closed on another, with the nodes written between them inside it.</summary>
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
