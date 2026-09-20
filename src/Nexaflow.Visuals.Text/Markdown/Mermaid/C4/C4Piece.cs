using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.C4;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.C4;

/// <summary>The pieces a structural C4 diagram is drawn out of.</summary>
public static class C4Piece
{
    /// <summary>Everything but the relationships: the boundaries, and the cards inside them.</summary>
    public const string Elements = "C4Elements";

    /// <summary>One element, card and all.</summary>
    public const string Element = "C4Element";

    /// <summary>A boundary, and everything drawn inside it.</summary>
    public const string Boundary = "C4Boundary";

    /// <summary>The box a boundary is drawn as.</summary>
    public const string Holding = "C4Holding";

    /// <summary>The line in brackets under a name — what a card is, or what a boundary is.</summary>
    public const string Stereotype = "C4Stereotype";

    /// <summary>The sentence under that.</summary>
    public const string Describes = "C4Describes";

    /// <summary>Every relationship, drawn over the rest.</summary>
    public const string Relations = "C4Relations";

    /// <summary>One relationship.</summary>
    public const string Relation = "C4Relation";

    /// <summary>What is written over one.</summary>
    public const string Label = "C4Label";
}
