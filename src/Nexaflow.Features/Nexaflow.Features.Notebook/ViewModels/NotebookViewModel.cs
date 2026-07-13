using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common;
using Nexaflow.Features.Notebook.Models;
using Nexaflow.IO.Common;
using Nexaflow.Syntax;

namespace Nexaflow.Features.Notebook.ViewModels;

/// <summary>
/// The Notebook page's view-model: parses an <c>.ipynb</c> into ordered cells (markdown + code) and builds a
/// per-cell outline of the code structure. A notebook is a structured cell document, not a flat source file —
/// it gets its own feature rather than riding the code editor.
/// </summary>
public sealed partial class NotebookViewModel : ObservableObject, IPageViewModel
{
    private readonly CodeStructureExtractor _extractor = new();
    private string _grammarId = "python";
    private int _codeCells, _markdownCells;

    public NotebookViewModel(string filePath) => FilePath = filePath;

    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);

    public ObservableCollection<NotebookCellViewModel> Cells { get; } = [];

    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private string _outlineMarkdown = string.Empty;
    [ObservableProperty] private bool _hasOutline;

    /// <summary>Reads + parses the notebook off the UI thread, then populates the cells + outline. Safe to call
    /// from the view's Loaded handler (the continuation runs on the UI thread).</summary>
    public async Task LoadAsync()
    {
        string text;
        // Read through the VFS (off the UI thread) so an .ipynb inside a disk image / archive resolves.
        try { text = await Task.Run(() => VirtualFileSystem.Instance.ReadAllText(FilePath)).ConfigureAwait(true); }
        catch { IsLoaded = true; return; }

        var notebook = NotebookDocument.Parse(text);
        _grammarId = notebook.GrammarId;

        Cells.Clear();
        _codeCells = _markdownCells = 0;
        foreach (var cell in notebook.Cells)
        {
            Cells.Add(new NotebookCellViewModel(cell, notebook.GrammarId));
            if (cell.Kind == NotebookCellKind.Code) _codeCells++;
            else if (cell.Kind == NotebookCellKind.Markdown) _markdownCells++;
        }

        BuildOutline(notebook);
        IsLoaded = true;
    }

    /// <summary>A markdown outline of the notebook's code structure — each code cell's declared types and
    /// top-level functions — extracted from the decoded cell source.</summary>
    private void BuildOutline(NotebookDocument notebook)
    {
        var sb = new StringBuilder();
        int cellNo = 0;
        foreach (var cell in notebook.Cells)
        {
            cellNo++;
            if (cell.Kind != NotebookCellKind.Code) continue;
            var outline = _extractor.Extract(notebook.GrammarId, cell.Source);
            if (!outline.HasContent) continue;

            sb.Append("**Cell ").Append(cellNo).AppendLine("**").AppendLine();
            foreach (var t in outline.Types)
            {
                sb.Append("- `").Append(t.Name).AppendLine("`");
                foreach (var m in t.Members) sb.Append("  - `").Append(m.Signature).AppendLine("`");
            }
            foreach (var m in outline.TopLevel) sb.Append("- `").Append(m.Signature).AppendLine("`");
            sb.AppendLine();
        }

        OutlineMarkdown = sb.ToString();
        HasOutline = sb.Length > 0;
    }

    public string GetContext() =>
        $"Jupyter notebook \"{FileName}\" ({_grammarId} kernel): {_codeCells} code cell(s), {_markdownCells} markdown cell(s).";
}
