using Nexaflow.Features.Notebook.Models;

namespace Nexaflow.Features.Notebook.ViewModels;

/// <summary>One cell as the view binds it: a markdown cell exposes its markdown via <see cref="Source"/>; a
/// code cell exposes its source + the kernel <see cref="GrammarId"/> (for highlighting) + an execution
/// <see cref="Label"/> (<c>In [3]</c> / <c>In [ ]</c>).</summary>
public sealed class NotebookCellViewModel
{
    public NotebookCellViewModel(NotebookCell cell, string grammarId)
    {
        Kind   = cell.Kind;
        Source = cell.Source.TrimEnd('\n');
        GrammarId = cell.Kind == NotebookCellKind.Code ? grammarId : null;
        Label  = cell.Kind == NotebookCellKind.Code ? $"In [{(cell.ExecutionCount?.ToString() ?? " ")}]" : "";
    }

    public NotebookCellKind Kind { get; }
    public string Source { get; }
    public string? GrammarId { get; }
    public string Label { get; }

    public bool IsCode     => Kind == NotebookCellKind.Code;
    public bool IsMarkdown => Kind == NotebookCellKind.Markdown;
}
