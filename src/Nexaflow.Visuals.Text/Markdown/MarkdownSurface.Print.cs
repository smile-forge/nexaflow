using System.Windows;
using System.Windows.Documents;

using Nexaflow.Visuals.Common.Theming;
using Nexaflow.Visuals.Text.Markdown.Printing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Visuals.Text.Markdown;

public sealed partial class MarkdownSurface
{
    /// <summary>
    /// The document this surface shows, on sheets whose printable area is <paramref name="sheet"/> — what printing it prints.
    /// It is laid out afresh for paper: the whole of it, at the sheet's width, in the light palette paper is printed in, with
    /// the pictures, links and data this surface reads it with and the faces it is written in. The zoom, the theme and
    /// what is on screen have no say.
    /// </summary>
    public DocumentPaginator Paginator(Size sheet) =>
        new MarkdownPaginator(Markdown ?? string.Empty, sheet, Paper, Asked, Named);

    /// <summary>What paper is printed in: white, in this surface's faces, at the size a document is read at.</summary>
    private LayoutStyle Paper =>
        WpfStyles.Light with { TextFont = Drawn.TextFont, MonoFont = Drawn.MonoFont, TextSize = TextTypography.BaseFontSize };
}
