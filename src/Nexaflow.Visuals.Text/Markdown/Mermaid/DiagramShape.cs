using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>The shapes a diagram draws a node as — Mermaid's, named for what they look like, with the brackets or the name a flowchart writes each with.</summary>
internal enum DiagramShape
{
    /// <summary><c>[text]</c></summary>
    Rectangle,

    /// <summary><c>(text)</c></summary>
    Rounded,

    /// <summary><c>([text])</c> — a pill.</summary>
    Stadium,

    /// <summary><c>[[text]]</c> — a rectangle with a second line down each side.</summary>
    Subroutine,

    /// <summary><c>[(text)]</c> — a drum, as a database is drawn.</summary>
    Cylinder,

    /// <summary><c>((text))</c></summary>
    Circle,

    /// <summary><c>(((text)))</c></summary>
    DoubleCircle,

    /// <summary><c>&gt;text]</c> — a flag, notched on its left.</summary>
    Asymmetric,

    /// <summary><c>{text}</c> — a rhombus, as a decision is drawn.</summary>
    Diamond,

    /// <summary><c>{{text}}</c></summary>
    Hexagon,

    /// <summary><c>[/text/]</c> — leaning right.</summary>
    Parallelogram,

    /// <summary><c>[\text\]</c> — leaning left.</summary>
    ParallelogramAlt,

    /// <summary><c>[/text\]</c> — wider at the bottom.</summary>
    Trapezoid,

    /// <summary><c>[\text/]</c> — wider at the top.</summary>
    TrapezoidAlt,

    /// <summary>A page with a wavy foot.</summary>
    Document,

    /// <summary><c>lin-doc</c> — a document with a line down its left side.</summary>
    LinedDocument,

    /// <summary><c>docs</c> — documents stacked one behind another.</summary>
    StackedDocument,

    /// <summary><c>tag-doc</c> — a document with its lower right corner turned.</summary>
    TaggedDocument,

    /// <summary>A rectangle with its top left corner folded down.</summary>
    Card,

    /// <summary><c>notch-pent</c> — a rectangle with both top corners cut off.</summary>
    NotchedPentagon,

    /// <summary><c>lin-rect</c> — a rectangle with a second line down its left side.</summary>
    LinedRectangle,

    /// <summary><c>div-rect</c> — a rectangle with a line across its top.</summary>
    DividedRectangle,

    /// <summary><c>win-pane</c> — a rectangle with a line across its top and down its left.</summary>
    WindowPane,

    /// <summary><c>tag-rect</c> — a rectangle with its lower right corner turned.</summary>
    TaggedRectangle,

    /// <summary><c>st-rect</c> — rectangles stacked one behind another.</summary>
    StackedRectangle,

    /// <summary><c>sl-rect</c> — a rectangle whose top slopes up to the right.</summary>
    SlopedRectangle,

    /// <summary><c>delay</c> — a rectangle rounded off at its right end.</summary>
    Delay,

    /// <summary><c>curv-trap</c> — pointed at its left end and rounded at its right.</summary>
    CurvedTrapezoid,

    /// <summary><c>bow-rect</c> — both ends bowing to the left.</summary>
    BowTie,

    /// <summary><c>flag</c> — a band whose top and foot wave together.</summary>
    Flag,

    /// <summary><c>tri</c> — a triangle on its base, its words in the foot of it.</summary>
    Triangle,

    /// <summary><c>flip-tri</c> — a triangle on its point, its words in the top of it.</summary>
    FlippedTriangle,

    /// <summary><c>hourglass</c> — two triangles point to point.</summary>
    Hourglass,

    /// <summary><c>bolt</c> — a lightning bolt.</summary>
    Bolt,

    /// <summary><c>fork</c> — a solid bar.</summary>
    Fork,

    /// <summary><c>sm-circ</c> — a small circle.</summary>
    SmallCircle,

    /// <summary><c>fr-circ</c> — a small ring round a dot.</summary>
    FramedCircle,

    /// <summary><c>f-circ</c> — a small solid circle.</summary>
    FilledCircle,

    /// <summary><c>cross-circ</c> — a circle crossed through.</summary>
    CrossedCircle,

    /// <summary><c>h-cyl</c> — a cylinder lying on its side.</summary>
    HorizontalCylinder,

    /// <summary><c>lin-cyl</c> — a cylinder with a second rim under its lid.</summary>
    LinedCylinder,

    /// <summary><c>datastore</c> — a band ruled along its top and its foot.</summary>
    DataStore,

    /// <summary><c>bucket</c> — a pail.</summary>
    Bucket,

    /// <summary><c>brace</c> — a curly brace left of the words.</summary>
    Brace,

    /// <summary><c>brace-r</c> — a curly brace right of the words.</summary>
    BraceRight,

    /// <summary><c>braces</c> — curly braces either side of the words.</summary>
    Braces,

    /// <summary><c>browser</c> — a browser window.</summary>
    Browser,

    /// <summary><c>console</c> — a terminal window.</summary>
    Console,

    /// <summary><c>folder</c> — a folder with its tab on top.</summary>
    Folder,

    /// <summary><c>person</c> — a head over a body.</summary>
    Person,

    /// <summary><c>)text(</c> — a cloud, as a mindmap draws one.</summary>
    Cloud,

    /// <summary><c>))text((</c> — a starburst, as a mindmap draws a bang.</summary>
    Bang,
}
