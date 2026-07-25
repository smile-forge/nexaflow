using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
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

    // ── IPageViewModel: AI surface ────────────────────────────────────────

    /// <summary>
    /// Parsing happens after the tab paints, so a notebook pinned as context the instant it opens would
    /// otherwise report the empty stub — "python kernel, 0 code cells, 0 markdown cells" — as if that were
    /// the file. Gating on the load holds the send until there is something true to say.
    /// </summary>
    public bool IsContextReady => IsLoaded;

    public string GetContext()
    {
        var sb = new StringBuilder();
        sb.Append($"Jupyter notebook \"{FileName}\" ({_grammarId} kernel): "
                + $"{_codeCells} code cell(s), {_markdownCells} markdown cell(s).");

        if (Cells.Count > 0)
        {
            const int Preview = 15;
            sb.Append("\nCells:");
            for (int i = 0; i < Cells.Count && i < Preview; i++)
                sb.Append($"\n  [{i + 1}] {KindName(Cells[i].Kind)}: {FirstLine(Cells[i].Source)}");
            if (Cells.Count > Preview)
                sb.Append($"\n  … (+{Cells.Count - Preview} more cell(s))");
        }

        sb.Append("\nUse read_notebook to read every cell's full source + outputs, or read_cell for one cell.");
        return sb.ToString();
    }

    /// <summary>The notebook file is this page's scope boundary — required now the page exposes tools.</summary>
    public string? GetSecurityContext() => FilePath;

    public string? GetAiSystemPromptGuidance() =>
        "This is a read-only Jupyter notebook viewer: you can read cell source and any stored outputs, "
      + "but cells cannot be executed or edited here.";

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        // Pure reads over the already-parsed in-memory cells — no UI mutation, so no dispatcher marshaling.
        new DelegateClientTool(
            "read_notebook",
            "Read the entire notebook as text: every cell in order with its 1-based index, kind "
          + "(code/markdown/raw), full source, and any stored outputs (stream/result/error text; image "
          + "outputs are noted by MIME type only).",
            [],
            ToolSafety.SafeOperation,
            (arguments, ct) =>
            {
                if (Cells.Count == 0)
                    return Task.FromResult(ToolResult.Error("The notebook is empty or not loaded."));
                var sb = new StringBuilder();
                for (int i = 0; i < Cells.Count; i++) AppendCell(sb, i + 1, Cells[i]);
                return Task.FromResult(ToolResult.Ok($"read {Cells.Count} cell(s)", sb.ToString().TrimEnd()));
            }),
        new DelegateClientTool(
            "read_cell",
            "Read a single cell by its 1-based index: its kind, full source, and any stored outputs.",
            [new ClientToolParameter("index", "1-based cell index.", Required: true, Type: "number")],
            ToolSafety.SafeOperation,
            (arguments, ct) =>
            {
                int index = ToolArgs.Int(arguments, "index", 0);
                if (index < 1 || index > Cells.Count)
                    return Task.FromResult(ToolResult.Error(
                        $"Cell {index} is out of range — the notebook has {Cells.Count} cell(s)."));
                var sb = new StringBuilder();
                AppendCell(sb, index, Cells[index - 1]);
                return Task.FromResult(ToolResult.Ok($"read cell {index}", sb.ToString().TrimEnd()));
            }),
    ];

    private static string KindName(NotebookCellKind kind) => kind switch
    {
        NotebookCellKind.Code     => "code",
        NotebookCellKind.Markdown => "markdown",
        _                         => "raw",
    };

    /// <summary>First non-empty line of a cell's source, trimmed to one short line for the context digest.</summary>
    private static string FirstLine(string source, int max = 80)
    {
        foreach (var raw in source.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            return line.Length > max ? line[..max] + "…" : line;
        }
        return "(empty)";
    }

    /// <summary>Renders one cell — header, source, then each stored output — for the read tools.</summary>
    private static void AppendCell(StringBuilder sb, int index, NotebookCellViewModel cell)
    {
        sb.Append("── Cell ").Append(index).Append(" (").Append(KindName(cell.Kind)).Append(") ──\n");
        sb.Append(cell.Source).Append('\n');
        if (cell.Outputs.Count > 0)
        {
            sb.Append("Outputs:\n");
            foreach (var o in cell.Outputs)
                sb.Append("  [").Append(OutputLabel(o.Kind)).Append("] ").Append(o.Text.Trim()).Append('\n');
        }
        sb.Append('\n');
    }

    private static string OutputLabel(NotebookOutputKind kind) => kind switch
    {
        NotebookOutputKind.Stream => "stream",
        NotebookOutputKind.Text   => "result",
        NotebookOutputKind.Image  => "image",
        NotebookOutputKind.Error  => "error",
        _                         => "output",
    };
}
