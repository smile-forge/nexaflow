using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Class;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Class;

/// <summary>The pieces a class diagram's layout is made of — its layers, and what is in them.</summary>
public static class ClassPiece
{
    /// <summary>The diagram itself: the classes, and the namespaces they are boxed into.</summary>
    public const string Classes = "Classes";

    /// <summary>One class, standing for everything written for it.</summary>
    public const string Class = "Class";

    /// <summary>A namespace: the box, and the classes it holds drawn inside its piece.</summary>
    public const string Space = "Space";

    /// <summary>A namespace's own box and its name, behind the classes it holds.</summary>
    public const string Holding = "Holding";

    /// <summary>One member of a class, which is a piece of its own where it leads somewhere.</summary>
    public const string Member = "Member";

    /// <summary>One interface a class offers, drawn as a circle on a stub off the top or the bottom of it.</summary>
    public const string Lollipop = "Lollipop";

    /// <summary>A note written beside a class.</summary>
    public const string Note = "Note";

    /// <summary>The dashed line holding a note to the class it is about.</summary>
    public const string Tether = "Tether";

    /// <summary>The relations, drawn over the diagram.</summary>
    public const string Relations = "Relations";

    /// <inheritdoc cref="Relations"/>
    public const string Relation = "Relation";

    /// <summary>What is written on a relation, over the middle of its line.</summary>
    public const string Label = "Label";

    /// <summary>How many of one class the other has, written at that end of the relation.</summary>
    public const string Count = "Count";
}
