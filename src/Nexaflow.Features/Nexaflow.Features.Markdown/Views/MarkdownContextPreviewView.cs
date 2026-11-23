
using Nexaflow.Features.Markdown.ViewModels;
using Nexaflow.Markdown.Editing;
using Nexaflow.Visuals.Common.Viewing;
using Nexaflow.Visuals.Text.Markdown;
using System.Windows.Controls;

namespace Nexaflow.Features.Markdown.Views;

/// <summary>Draws a <see cref="MarkdownContextPreview"/>: the document rendered read-only, scrolling, its pictures found beside it.</summary>
[ViewFor(typeof(MarkdownContextPreview))]
public sealed class MarkdownContextPreviewView : ContentControl
{
    public MarkdownContextPreviewView(MarkdownContextPreview preview) => Content = new MarkdownSurface
    {
        Document                    = new EditedDocument(preview.Markdown),
        BaseDirectory               = preview.Folder,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
}
