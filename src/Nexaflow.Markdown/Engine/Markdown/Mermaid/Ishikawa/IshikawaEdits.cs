using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Ishikawa;

/// <summary>
/// What an edit means in an Ishikawa diagram: what is shown as written stays shown while it is written in. Every other key does what
/// it does anywhere.
/// </summary>
internal sealed class IshikawaEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => DiagramWriting.Typed(edit, escaping: null);
}
