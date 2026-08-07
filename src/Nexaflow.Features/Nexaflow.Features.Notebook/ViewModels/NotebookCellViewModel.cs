using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Notebook.Models;

namespace Nexaflow.Features.Notebook.ViewModels;

/// <summary>One cell as the view binds it: a markdown cell exposes its markdown via <see cref="Source"/>; a
/// code cell exposes its source + the kernel <see cref="GrammarId"/> (for highlighting) + an execution
/// <see cref="Label"/> (<c>In [3]</c> / <c>In [ ]</c>).</summary>
public sealed partial class NotebookCellViewModel : ObservableObject
{
    public NotebookCellViewModel(NotebookCell cell, string grammarId)
    {
        Kind   = cell.Kind;
        Source = cell.Source.TrimEnd('\n');
        GrammarId = cell.Kind == NotebookCellKind.Code ? grammarId : null;
        Label  = cell.Kind == NotebookCellKind.Code ? $"In [{(cell.ExecutionCount?.ToString() ?? " ")}]" : "";
        Outputs = cell.Outputs;
    }

    public NotebookCellKind Kind { get; }
    public string Source { get; }
    public string? GrammarId { get; }
    public string Label { get; }

    /// <summary>The cell's stored outputs (code cells only; empty otherwise) — surfaced to the AI read tools.</summary>
    public IReadOnlyList<NotebookOutput> Outputs { get; }

    public bool IsCode     => Kind == NotebookCellKind.Code;
    public bool IsMarkdown => Kind == NotebookCellKind.Markdown;

    // ── "?" page search ───────────────────────────────────────────────────────

    /// <summary>True when a page search matched somewhere in this cell — the cell is the unit the page can
    /// scroll to, so it is what gets marked.</summary>
    [ObservableProperty] private bool _isSearchHit;

    /// <summary>Matched spans within <see cref="Source"/>, painted inside a <b>code</b> cell (which shows the
    /// source verbatim). A markdown cell renders to a flow document whose offsets are not the source's, so it
    /// carries the cell mark alone and this stays empty.</summary>
    [ObservableProperty] private IReadOnlyList<(int Offset, int Length)> _searchSpans = [];
}
