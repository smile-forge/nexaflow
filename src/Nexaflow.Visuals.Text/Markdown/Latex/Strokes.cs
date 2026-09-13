using System;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>What <see cref="Set.Arrow"/> draws on top of its shaft.</summary>
[Flags]
internal enum ArrowDecoration
{
    None = 0,

    /// <summary>An arrowhead at the left end.</summary>
    HeadLeft = 1,

    /// <summary>An arrowhead at the right end.</summary>
    HeadRight = 2,

    /// <summary>Two parallel shafts instead of one, for the \Rightarrow family.</summary>
    DoubleShaft = 4,

    /// <summary>A vertical bar at the left end, for \mapsto.</summary>
    TailBarLeft = 8,
}

/// <summary>Which way <see cref="Set.Stroke"/> crosses out what it lies over: <c>\cancel</c>, <c>\bcancel</c>, <c>\xcancel</c>.</summary>
[Flags]
internal enum StrokeMode
{
    None = 0,
    Normal = 1,
    Back = 2,
    Both = 3,
}
