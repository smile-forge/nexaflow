using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Flowchart;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Flowchart;

/// <summary>The pieces a flowchart's layout is made of — its layers, and what is in them.</summary>
public static class FlowchartPiece
{
    /// <summary>The chart itself: the nodes, and the subgraphs they are gathered into.</summary>
    public const string Nodes = "Nodes";

    /// <summary>One node, standing for what was written for it.</summary>
    public const string Node = "Node";

    /// <summary>A subgraph: the box, and the nodes it holds drawn inside its piece.</summary>
    public const string Group = "Group";

    /// <summary>A subgraph's own box and what is written at the top of it, behind the nodes it holds.</summary>
    public const string Holding = "Holding";

    /// <summary>A lane: the band work runs through, standing for the whole of the subgraph that opened it.</summary>
    public const string Lane = "Lane";

    /// <summary>The strip at the near end of a lane's band, with the lane's own name in it.</summary>
    public const string Title = "Title";

    /// <summary>The links, drawn over the chart.</summary>
    public const string Links = "Links";

    /// <inheritdoc cref="Links"/>
    public const string Link = "Link";

    /// <summary>What is written on a link, over the middle of its line.</summary>
    public const string Label = "Label";
}
