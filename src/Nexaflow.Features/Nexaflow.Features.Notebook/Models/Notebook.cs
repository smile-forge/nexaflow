using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Nexaflow.Features.Notebook.Models;

/// <summary>What a notebook cell is. Code cells carry source in the kernel language; markdown cells carry
/// markdown; raw cells are verbatim text.</summary>
public enum NotebookCellKind { Code, Markdown, Raw }

/// <summary>One parsed cell: its kind, its (decoded, joined) source, and a code cell's execution count.</summary>
public sealed record NotebookCell(NotebookCellKind Kind, string Source, int? ExecutionCount);

/// <summary>A parsed notebook: the kernel's tree-sitter grammar id (e.g. "python") and its cells in order.</summary>
public sealed record NotebookDocument(string GrammarId, IReadOnlyList<NotebookCell> Cells)
{
    public static readonly NotebookDocument Empty = new("python", []);

    /// <summary>Parses an <c>.ipynb</c> (nbformat JSON). Cell <c>source</c> (a string or array of line-strings)
    /// is decoded — JSON escapes resolved, lines joined — so the real multi-line source is recovered. Never
    /// throws: a malformed notebook yields <see cref="Empty"/>.</summary>
    public static NotebookDocument Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Empty;

            var grammar = Kernel(root) ?? "python";
            var cells = new List<NotebookCell>();
            if (root.TryGetProperty("cells", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var cell in arr.EnumerateArray())
                {
                    if (cell.ValueKind != JsonValueKind.Object) continue;
                    int? exec = cell.TryGetProperty("execution_count", out var ec) && ec.ValueKind == JsonValueKind.Number
                        ? ec.GetInt32() : null;
                    cells.Add(new NotebookCell(CellKind(cell), DecodeSource(cell), exec));
                }
            return new NotebookDocument(grammar, cells);
        }
        catch { return Empty; }
    }

    private static NotebookCellKind CellKind(JsonElement cell)
    {
        var t = cell.TryGetProperty("cell_type", out var ct) && ct.ValueKind == JsonValueKind.String ? ct.GetString() : "code";
        return t switch { "markdown" => NotebookCellKind.Markdown, "raw" => NotebookCellKind.Raw, _ => NotebookCellKind.Code };
    }

    /// <summary>Joins a cell's <c>source</c> (a string or array of line-strings), decoding JSON escapes.</summary>
    private static string DecodeSource(JsonElement cell)
    {
        if (!cell.TryGetProperty("source", out var src)) return "";
        if (src.ValueKind == JsonValueKind.String) return src.GetString() ?? "";
        if (src.ValueKind != JsonValueKind.Array) return "";
        var sb = new StringBuilder();
        foreach (var el in src.EnumerateArray())
            if (el.ValueKind == JsonValueKind.String) sb.Append(el.GetString());
        return sb.ToString();
    }

    /// <summary>The kernel language (<c>metadata.kernelspec.language</c> / <c>language_info.name</c>) mapped to
    /// a tree-sitter grammar id, defaulting to python.</summary>
    private static string? Kernel(JsonElement root)
    {
        if (root.TryGetProperty("metadata", out var meta) && meta.ValueKind == JsonValueKind.Object)
        {
            if (meta.TryGetProperty("kernelspec", out var ks) && ks.ValueKind == JsonValueKind.Object
                && ks.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String)
                return NormalizeKernel(l.GetString());
            if (meta.TryGetProperty("language_info", out var li) && li.ValueKind == JsonValueKind.Object
                && li.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                return NormalizeKernel(n.GetString());
        }
        return "python";
    }

    private static string NormalizeKernel(string? kernel)
    {
        if (string.IsNullOrWhiteSpace(kernel)) return "python";
        var k = kernel.ToLowerInvariant();
        if (k.Contains("python") || k == "ipython") return "python";
        return k switch
        {
            "ruby"                 => "ruby",
            "javascript" or "node" => "javascript",
            "typescript"           => "typescript",
            "csharp" or "c#"       => "c-sharp",
            _                      => "python",
        };
    }
}
