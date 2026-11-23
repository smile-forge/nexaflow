using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Controls;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.AIChat.Views;

/// <summary>
/// How the conversation's context panel draws a <see cref="TextContextPreview"/>, the preview a text-like page hands it:
/// its summary line over a scrollable monospace snippet.
/// </summary>
[ViewFor(typeof(TextContextPreview))]
public sealed class TextContextPreviewView : ContentControl
{
    public TextContextPreviewView(TextContextPreview preview) => Content = new ReadOnlyTextPreview(preview.Meta, preview.Body);
}
