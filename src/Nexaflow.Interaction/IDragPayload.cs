namespace Nexaflow.Interaction;

/// <summary>
/// What a drag carries, by kind (<see cref="DragKinds"/>, or a kind of the app's own).
/// <para>
/// It lives only as long as the drag: what a drop needs is read inside <see cref="IDropTarget.AcceptDrop"/>, before that
/// first awaits. What is read may be kept; the payload may not.
/// </para>
/// </summary>
public interface IDragPayload
{
    /// <summary>Whether it carries <paramref name="kind"/>. Cheap: asked on every move of the pointer.</summary>
    bool Contains(string kind);

    /// <summary>What it carries of <paramref name="kind"/>, or null.</summary>
    object? Read(string kind);
}
