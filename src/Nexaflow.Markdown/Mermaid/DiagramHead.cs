namespace Nexaflow.Markdown.Mermaid;

/// <summary>What a connector ends in: Mermaid's arrowheads, a class diagram's relations, and an ER diagram's cardinalities.</summary>
internal enum DiagramHead
{
    None,

    /// <summary>A filled triangle: a flowchart's <c>--&gt;</c>, a sequence diagram's <c>-&gt;&gt;</c>.</summary>
    Arrow,

    /// <summary>Two strokes meeting at the tip: a sequence diagram's <c>-)</c>, a class diagram's dependency.</summary>
    Open,

    /// <summary>A hollow triangle: a class diagram's inheritance, <c>&lt;|--</c>.</summary>
    Triangle,

    /// <summary>A filled diamond: a class diagram's composition, <c>*--</c>.</summary>
    Diamond,

    /// <summary>A hollow diamond: a class diagram's aggregation, <c>o--</c>.</summary>
    HollowDiamond,

    /// <summary>A hollow circle: a flowchart's <c>--o</c>.</summary>
    Circle,

    /// <summary>A cross: a flowchart's <c>--x</c>, a sequence diagram's <c>-x</c>.</summary>
    Cross,

    /// <summary>A cross in a circle: SysML's composite containment, at the end holding the other.</summary>
    CrossCircle,

    /// <summary>Half a filled triangle, above the line: a sequence diagram's <c>-|\</c>.</summary>
    HalfTop,

    /// <summary>And below it: <c>-|/</c>.</summary>
    HalfBottom,

    /// <summary>One stroke back from the tip, above the line: a sequence diagram's <c>-\\</c>.</summary>
    StickTop,

    /// <summary>And below it: <c>-//</c>.</summary>
    StickBottom,

    /// <summary>An ER diagram's zero or one: <c>|o</c>.</summary>
    ZeroOrOne,

    /// <summary>An ER diagram's exactly one: <c>||</c>.</summary>
    ExactlyOne,

    /// <summary>An ER diagram's zero or more: <c>}o</c>.</summary>
    ZeroOrMore,

    /// <summary>An ER diagram's one or more: <c>}|</c>.</summary>
    OneOrMore,
}
