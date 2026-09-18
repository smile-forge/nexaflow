using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Plot;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid;

namespace Nexaflow.Visuals.Text.Markdown.Plot;

/// <summary>The pieces a plot's layout is made of.</summary>
public static class PlotPiece
{
    /// <summary>The whole drawing.</summary>
    public const string Plot = "Plot";

    /// <summary>Everything drawn from the table, as one layer.</summary>
    public const string Marks = "Marks";

    /// <summary>One of them — a point, a bubble, a tile — standing for the row or cell it was drawn from.</summary>
    public const string Mark = "Mark";

    public const string XAxis = "XAxis";
    public const string YAxis = "YAxis";
    public const string Tick = "Tick";
    public const string AxisTitle = "AxisTitle";
    public const string Title = "Title";

    /// <summary>The name over one panel of a faceted plot, saying which value its rows share.</summary>
    public const string Strip = "Strip";

    /// <summary>A name in the key.</summary>
    public const string Name = "Name";

    /// <summary>The lines across the panel behind the marks.</summary>
    public const string Grid = "Grid";

    /// <summary>The values written on the marks, as one layer.</summary>
    public const string Labels = "Labels";

    /// <summary>One of them.</summary>
    public const string Label = "Label";

    /// <summary>One bin of a binned heat map, standing for a count rather than for any one row.</summary>
    public const string Bin = "Bin";

    /// <summary>One contour of a density, or one crossing of its grid — a count of rows, not one of them.</summary>
    public const string Cloud = "Cloud";

    /// <summary>The line fitted through the points.</summary>
    public const string Fit = "Fit";

    /// <summary>The band its own uncertainty makes.</summary>
    public const string Band = "Band";

    /// <summary>What the points say about each other, written on the panel.</summary>
    public const string Stats = "Stats";
}
