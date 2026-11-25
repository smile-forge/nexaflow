namespace Nexaflow.Interaction;

/// <summary>
/// Something a paste can land in. The window's router finds it from where the paste was asked for — the keyboard focus, or the
/// control that asked — and hands it what the clipboard held, taken, so a target neither reads the clipboard nor handles a key.
/// <para>
/// A target registers by where it is, as an <see cref="IDropTarget"/> does: a page's view or its view-model implementing this
/// takes pastes anywhere on the page, and a control implementing it takes them while it has the focus. The innermost that can
/// take what the clipboard holds wins, and a text box nearer still pastes for itself. One that also takes drops shares one way
/// of taking content between the two.
/// </para>
/// </summary>
public interface IPasteTarget
{
    /// <summary>
    /// The kinds it can take (<see cref="DragKinds"/>, or the app's own). It is asked only while the clipboard holds one of the
    /// shared ones (<see cref="IShellClipboard.Kinds"/>), and handed every one of these it holds.
    /// </summary>
    IReadOnlyList<string> PasteKinds { get; }

    /// <summary>
    /// Takes what was pasted: what the clipboard held of <see cref="PasteKinds"/>, taken, so it may wait before using it. Files
    /// cut rather than copied carry <see cref="DragKinds.FilesCut"/>, and a paste moves them. A throw is reported to the reader by
    /// the window.
    /// </summary>
    Task PasteAsync(IDragPayload pasted);
}
