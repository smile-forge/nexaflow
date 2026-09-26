using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.State;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.State;

/// <summary>The pieces a state diagram's layout is made of — its layers, and what is in them.</summary>
public static class StatePiece
{
    /// <summary>The diagram itself: the states, and the composite states they are gathered into.</summary>
    public const string States = "States";

    /// <summary>One state, standing for what was written for it.</summary>
    public const string State = "State";

    /// <summary>A composite state: the box, and the states it holds drawn inside its piece.</summary>
    public const string Group = "Group";

    /// <summary>A composite state's own box and what is written at the top of it, behind the states it holds.</summary>
    public const string Holding = "Holding";

    /// <summary>The line dividing two regions of a composite state, which run at the same time.</summary>
    public const string Divider = "Divider";

    /// <summary>A note written beside a state.</summary>
    public const string Note = "Note";

    /// <summary>The transitions, drawn over the diagram.</summary>
    public const string Steps = "Steps";

    /// <inheritdoc cref="Steps"/>
    public const string Step = "Step";

    /// <summary>What is written on a transition, over the middle of its line.</summary>
    public const string Label = "Label";
}
