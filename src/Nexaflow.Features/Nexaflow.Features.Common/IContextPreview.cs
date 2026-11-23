

namespace Nexaflow.Features.Common;

/// <summary>
/// Implemented by a page's view-model (alongside <see cref="IPageViewModel"/>) when the page can show a compact,
/// <b>read-only</b> preview of itself in the conversation's context-preview panel — the panel that opens when a
/// context chip is selected.
/// <para>
/// The preview is a model, drawn by the view marked as drawing its type: <see cref="TextContextPreview"/> for a page that
/// is essentially text, <see cref="PagePreview{TPage}"/> for a preview bound to the page's own state, or a model of the
/// feature's own. Why a separate preview rather than the page's own view: a pinned tab's view is a live element already
/// parented in the tab host, and an element cannot have two parents — so the panel draws something built for the job:
/// narrow (the panel is ~35% of the width), read-only, and cheap — a summary, not a second working copy of the editor.
/// </para>
/// <para>
/// A page that doesn't implement this shows a plain identity placeholder instead, so implementing it is a pure
/// enhancement — never a requirement.
/// </para>
/// </summary>
public interface IContextPreview
{
    /// <summary>A fresh preview of this page for the context-preview panel. Asked each time the chip is selected; the
    /// preview is let go on deselect, so it must not take ownership of anything the page still needs.</summary>
    object CreateContextPreview();
}
