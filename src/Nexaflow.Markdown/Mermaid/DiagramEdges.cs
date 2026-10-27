namespace Nexaflow.Markdown.Mermaid;

/// <summary>The room a panel's furniture takes on each of its four sides.</summary>
internal readonly record struct DiagramEdges(double Left, double Top, double Right, double Bottom)
{
    public static DiagramEdges operator +(DiagramEdges room, DiagramEdges beyond) =>
        new(room.Left + beyond.Left, room.Top + beyond.Top, room.Right + beyond.Right, room.Bottom + beyond.Bottom);
}
