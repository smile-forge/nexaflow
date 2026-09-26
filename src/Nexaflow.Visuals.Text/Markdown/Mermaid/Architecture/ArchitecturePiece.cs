using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Architecture;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Architecture;

/// <summary>The pieces an architecture diagram's layout is made of — its layers, and what is in them.</summary>
public static class ArchitecturePiece
{
    /// <summary>Everything the architecture is made of, which is the diagram itself.</summary>
    public const string Parts = "Parts";

    /// <summary>A group: a box holding whatever is put in it, which are the pieces inside it.</summary>
    public const string Group = "Group";

    /// <summary>A group's own box and what is written on it, behind the services it holds.</summary>
    public const string Holding = "Holding";

    /// <summary>A service: its icon, and what is written under it.</summary>
    public const string Service = "Service";

    /// <summary>A junction: a place edges meet, drawn as a dot.</summary>
    public const string Junction = "Junction";

    /// <summary>The picture drawn over a service, or the words standing in for one nobody has.</summary>
    public const string Icon = "Icon";

    /// <summary>The edges, drawn over everything they join.</summary>
    public const string Edges = "Edges";

    /// <inheritdoc cref="Edges"/>
    public const string Edge = "Edge";

    /// <summary>What is written on an edge, over the middle of it.</summary>
    public const string Label = "Label";
}
