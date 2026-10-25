namespace Nexaflow.Markdown.Mermaid;

/// <summary>How a value becomes a distance along an axis.</summary>
internal enum DiagramTransform
{
    Linear,

    Log10,
    Log2,
    NaturalLog,

    Sqrt,

    /// <summary>Linear, with the axis running the other way.</summary>
    Reverse,
}
