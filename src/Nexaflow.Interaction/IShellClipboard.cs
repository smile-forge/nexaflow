namespace Nexaflow.Interaction;

/// <summary>
/// The system clipboard: what it holds, read in the <see cref="DragKinds"/> a drag is read in, and what to put there, made as a
/// <see cref="ComposedPayload"/>. A view-model reaches it through the shell's services; a control with no view-model through
/// its window, which pastes and copies for it.
/// <para>
/// Every read and write is asynchronous, as a clipboard on any platform may be: another application can hold it open, or render
/// what it put there only when asked. Each may be called from any thread. None throws for a clipboard that is busy: a read
/// gives what it could, a write says whether it happened.
/// </para>
/// <para>
/// What it holds is known without opening it (<see cref="Kinds"/>), so whether a Paste could do anything is a question a
/// command answers whenever it is asked, and <see cref="KindsChanged"/> says when to ask again.
/// </para>
/// </summary>
public interface IShellClipboard
{
    /// <summary>
    /// The <see cref="DragKinds.Shared"/> kinds it holds, and <see cref="DragKinds.FilesCut"/> when its files were cut. Never
    /// opens the clipboard. Current after the app's own writes, and read again at the latest when the app is next activated
    /// (another application changes the clipboard only while the person is in it), or sooner where the platform says so.
    /// </summary>
    IReadOnlySet<string> Kinds { get; }

    /// <summary><see cref="Kinds"/> changed. Raised on the UI thread.</summary>
    event EventHandler? KindsChanged;

    /// <summary>
    /// What it holds of <paramref name="kinds"/>, the shared kinds or the app's own, taken (<see cref="DragPayloadReading.Take"/>):
    /// a payload that stays as it was read whatever the clipboard holds next. Empty when it holds none of them or could not be
    /// opened.
    /// </summary>
    Task<IDragPayload> ReadAsync(IReadOnlyCollection<string> kinds, CancellationToken ct = default);

    /// <summary>
    /// Puts <paramref name="payload"/> on the clipboard in place of what it held: its words, HTML, picture and files for any
    /// application, and the app's own kinds that are words for the app's own paste. False when it could not be opened.
    /// </summary>
    Task<bool> WriteAsync(IDragPayload payload, CancellationToken ct = default);

    /// <summary>Empties the clipboard, once files cut there have been moved. False when it could not be opened.</summary>
    Task<bool> ClearAsync(CancellationToken ct = default);
}
