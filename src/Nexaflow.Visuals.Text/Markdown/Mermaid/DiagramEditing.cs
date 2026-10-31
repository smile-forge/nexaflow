using System.Collections.Generic;

using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// What editing means in every diagram: that a diagram is written in only where it draws words — the same answer in all of
/// them — and whatever the diagram's own handler says beyond that.
/// </summary>
internal sealed class DiagramEditing(IOnEdit? onEdit) : IContentLanguage
{
    private readonly IContentLanguage? _own = onEdit as IContentLanguage;

    /// <inheritdoc/>
    public IOnEdit? OnEdit => onEdit;

    /// <inheritdoc/>
    public IOnMove? OnMove => onEdit as IOnMove;

    /// <summary>
    /// A diagram draws a picture of what its source meant, so a caret rests in far more places than a reader can write in:
    /// on an axis, a connector, a wedge, the space between two rows. Only its runs of words are written in.
    /// </summary>
    public bool TakesTextOnlyInWords => true;

    /// <inheritdoc/>
    public IReadOnlyList<MiniRibbonControl> Ribbon(ContentAsk ask) => this._own?.Ribbon(ask) ?? [];

    /// <inheritdoc/>
    public ClipboardOffers Clipboard(ContentAsk ask) => this._own?.Clipboard(ask) ?? (onEdit is null ? ClipboardOffers.All : ClipboardOffers.None);

    /// <inheritdoc/>
    public BlockCorner Corner(ContentAsk ask) => this._own?.Corner(ask) ?? BlockCorner.Usual;
}
