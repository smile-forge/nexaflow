namespace Nexaflow.Markdown.Mermaid;

/// <summary>The outline a card is drawn with, where a diagram draws something richer than a box of words.</summary>
internal enum DiagramCardShape
{
    /// <summary>A rounded box, which is what a card is unless it says otherwise.</summary>
    Box,

    /// <summary>A box with a head above it — somebody rather than something.</summary>
    Person,

    /// <summary>A cylinder — a store.</summary>
    Database,

    /// <summary>A box with one end rounded away — a queue, which things go into and come out of.</summary>
    Queue,
}
