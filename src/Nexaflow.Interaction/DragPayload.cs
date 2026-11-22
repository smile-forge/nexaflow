namespace Nexaflow.Interaction;

/// <summary>A payload the app made itself: what is handed to something that takes drops without any drag happening.</summary>
public sealed class DragPayload : IDragPayload
{
    private readonly Dictionary<string, object> _carried;

    public DragPayload(params (string Kind, object Value)[] carried)
        => _carried = carried.ToDictionary(c => c.Kind, c => c.Value, StringComparer.Ordinal);

    public bool Contains(string kind) => _carried.ContainsKey(kind);

    public object? Read(string kind) => _carried.GetValueOrDefault(kind);
}
