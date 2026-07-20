using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Nexaflow.Features.Notebook.Models;

/// <summary>What a notebook cell is. Code cells carry source in the kernel language; markdown cells carry
/// markdown; raw cells are verbatim text.</summary>
public enum NotebookCellKind { Code, Markdown, Raw }

/// <summary>What a stored code-cell output is: console <see cref="Stream"/> text, a <see cref="Text"/>
/// (execute-result / display-data text/plain) payload, a rendered <see cref="Image"/> (noted by MIME type
/// only — the pixels aren't surfaced to text tools), or an <see cref="Error"/> (exception + traceback).</summary>
public enum NotebookOutputKind { Stream, Text, Image, Error }

/// <summary>One decoded code-cell output. <see cref="Text"/> is the human-readable payload; for an
/// <see cref="NotebookOutputKind.Image"/> output it is a short note of the image's MIME type.</summary>
public sealed record NotebookOutput(NotebookOutputKind Kind, string Text);

/// <summary>One parsed cell: its kind, its (decoded, joined) source, a code cell's execution count, and any
/// stored outputs (empty for markdown/raw cells and un-run code cells).</summary>
public sealed record NotebookCell(
    NotebookCellKind Kind, string Source, int? ExecutionCount, IReadOnlyList<NotebookOutput> Outputs);

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
                    cells.Add(new NotebookCell(CellKind(cell), DecodeSource(cell), exec, DecodeOutputs(cell)));
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
        => cell.TryGetProperty("source", out var src) ? JoinStringOrArray(src) : "";

    /// <summary>Joins an nbformat value that may be a single string or an array of line-strings
    /// (<c>source</c>, stream <c>text</c>, a traceback…), decoding JSON escapes.</summary>
    private static string JoinStringOrArray(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.String) return el.GetString() ?? "";
        if (el.ValueKind != JsonValueKind.Array) return "";
        var sb = new StringBuilder();
        foreach (var line in el.EnumerateArray())
            if (line.ValueKind == JsonValueKind.String) sb.Append(line.GetString());
        return sb.ToString();
    }

    /// <summary>Decodes a code cell's <c>outputs</c> array (nbformat 4): stream text, execute-result /
    /// display-data text (image mimetypes noted by name), and errors (ename/evalue + traceback). Non-code
    /// cells and un-run cells have no <c>outputs</c> and yield an empty list.</summary>
    private static IReadOnlyList<NotebookOutput> DecodeOutputs(JsonElement cell)
    {
        if (!cell.TryGetProperty("outputs", out var outs) || outs.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<NotebookOutput>();
        foreach (var o in outs.EnumerateArray())
        {
            if (o.ValueKind != JsonValueKind.Object) continue;
            var type = o.TryGetProperty("output_type", out var ot) && ot.ValueKind == JsonValueKind.String
                ? ot.GetString() : null;
            switch (type)
            {
                case "stream":
                {
                    var name = o.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                        ? n.GetString() : null;
                    var text = o.TryGetProperty("text", out var t) ? JoinStringOrArray(t) : "";
                    list.Add(new NotebookOutput(NotebookOutputKind.Stream,
                        string.IsNullOrEmpty(name) ? text : $"[{name}] {text}"));
                    break;
                }
                case "execute_result":
                case "display_data":
                    if (o.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                    {
                        if (data.TryGetProperty("text/plain", out var tp))
                            list.Add(new NotebookOutput(NotebookOutputKind.Text, JoinStringOrArray(tp)));
                        foreach (var prop in data.EnumerateObject())
                            if (prop.Name.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                                list.Add(new NotebookOutput(NotebookOutputKind.Image, prop.Name));
                    }
                    break;
                case "error":
                {
                    var ename  = o.TryGetProperty("ename",  out var en) && en.ValueKind == JsonValueKind.String ? en.GetString() : "";
                    var evalue = o.TryGetProperty("evalue", out var ev) && ev.ValueKind == JsonValueKind.String ? ev.GetString() : "";
                    var header = $"{ename}: {evalue}".Trim(':', ' ');
                    var trace  = o.TryGetProperty("traceback", out var tb) ? JoinStringOrArray(tb) : "";
                    list.Add(new NotebookOutput(NotebookOutputKind.Error,
                        string.IsNullOrEmpty(trace) ? header : $"{header}\n{trace}"));
                    break;
                }
            }
        }
        return list;
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
