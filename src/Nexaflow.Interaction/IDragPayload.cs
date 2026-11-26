namespace Nexaflow.Interaction;

/// <summary>
/// What a drag carries, by kind (<see cref="DragKinds"/>, or a kind of the app's own).
/// <para>
/// The drag's own lives only as long as the drag. What a target is handed at the drop, and what a paste is handed, was taken
/// from it (<see cref="DragPayloadReading.Take"/>) and may be kept.
/// </para>
/// </summary>
public interface IDragPayload
{
    /// <summary>Whether it carries <paramref name="kind"/>. Cheap: asked on every move of the pointer.</summary>
    bool Contains(string kind);

    /// <summary>What it carries of <paramref name="kind"/>, or null.</summary>
    object? Read(string kind);
}
