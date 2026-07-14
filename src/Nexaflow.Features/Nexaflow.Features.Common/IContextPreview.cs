using System.Windows.Controls;

namespace Nexaflow.Features.Common;

/// <summary>
/// Implemented by a page's view-model (alongside <see cref="IPageViewModel"/>) when the page can render a
/// compact, <b>read-only</b> view of itself for the conversation's context-preview panel — the panel that
/// opens when a context chip is selected.
/// <para>
/// Why a separate view rather than the page's own content: a pinned tab's <see cref="Page.Content"/> is a
/// live WPF element already parented in the tab host, and an element cannot have two parents — so the panel
/// can never simply re-host it. A page that wants to be previewable therefore hands back a <em>fresh</em>
/// element built for the job: narrow (the panel is ~35% of the width), read-only, and cheap — a summary, not
/// a second working copy of the editor.
/// </para>
/// <para>
/// A page that doesn't implement this shows a plain identity placeholder instead, so implementing it is a
/// pure enhancement — never a requirement.
/// </para>
/// </summary>
public interface IContextPreview
{
    /// <summary>Builds a fresh read-only view of this page for the context-preview panel. Called each time
    /// the chip is selected; the returned control is discarded on deselect, so it must not assume it is a
    /// singleton and must not take ownership of anything the page still needs.</summary>
    UserControl CreateContextPreview();
}
