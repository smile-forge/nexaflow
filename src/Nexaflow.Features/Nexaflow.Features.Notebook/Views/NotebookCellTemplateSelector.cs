using System.Windows;
using System.Windows.Controls;
using Nexaflow.Features.Notebook.ViewModels;

namespace Nexaflow.Features.Notebook.Views;

/// <summary>Picks the markdown vs. code cell template so only the needed control is built per cell (no wasted
/// markdown render for code cells, or vice-versa).</summary>
public sealed class NotebookCellTemplateSelector : DataTemplateSelector
{
    public DataTemplate? CodeTemplate { get; set; }
    public DataTemplate? MarkdownTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is NotebookCellViewModel { IsMarkdown: true } ? MarkdownTemplate : CodeTemplate;
}
