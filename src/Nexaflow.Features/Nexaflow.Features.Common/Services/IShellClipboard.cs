using Nexaflow.Interaction;

namespace Nexaflow.Features.Common;

/// <summary>
/// The system clipboard, as <see cref="IShellServices.Clipboard"/> offers it to a view-model: what it holds, read in the
/// <see cref="DragKinds"/> a drag is read in, and what to put there, made as a <see cref="ComposedPayload"/>. Use it from
/// the UI thread. Another application can hold the clipboard open for a moment, so a write or a clear says whether it
/// happened rather than throwing.
/// </summary>
public interface IShellClipboard
{
    /// <summary>What the clipboard holds now; <see cref="ComposedPayload.Empty"/> when it holds nothing or cannot be opened.</summary>
    IDragPayload Read();

    /// <summary>
    /// Puts <paramref name="payload"/>'s words, HTML, picture and files on the clipboard in place of what it held; false when
    /// another application held it open.
    /// </summary>
    bool Write(IDragPayload payload);

    /// <summary>Empties the clipboard — once files cut there have been moved; false when another application held it open.</summary>
    bool Clear();
}
