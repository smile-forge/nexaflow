namespace Nexaflow.Interaction;

/// <summary>
/// Something that can be dropped on. The shell's router finds it under the pointer and passes the drag along, so a
/// target neither sets a surface up for drops nor handles a drag event.
/// <para>
/// A target registers by where it is: a page's view or its view-model implementing this takes drops anywhere on the
/// page, a control implementing it takes them on itself, and an area is given one in XAML with
/// <c>DropSurface.Target</c>. The innermost that answers wins; one that returns null is passed over for the surface
/// around it.
/// </para>
/// </summary>
public interface IDropTarget
{
    /// <summary>The kinds it can take (<see cref="DragKinds"/>, or the app's own). It is asked only of a drag carrying one.</summary>
    IReadOnlyList<string> AcceptedKinds { get; }

    /// <summary>
    /// What a drop here would do, or null where this is not its to take. Asked as the pointer moves and once more at
    /// the drop, so it touches no disk and changes nothing but its own hover feedback.
    /// </summary>
    DropOffer? OfferDrop(DropRequest request);

    /// <summary>
    /// Takes the drop it offered. Runs inside Windows' drop callback: read what is wanted from the payload and return,
    /// leaving slow work and any question to afterwards. A throw is reported to the reader by the shell.
    /// </summary>
    void AcceptDrop(DropRequest request);

    /// <summary>The drag is no longer over it, or has been dropped: whatever the hover showed goes.</summary>
    void EndDropHover() { }
}
