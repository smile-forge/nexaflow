using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Hdf5;

namespace Nexaflow.Features.Hdf5.ViewModels;

/// <summary>
/// The AI's view of the page. <see cref="GetContext"/> carries everything the user can see — the tree as expanded,
/// the selected object with its whole details drawer, the slice, and the table window on screen — and the tools
/// reach anything the user could navigate to. Written in English for the model; the words on screen are localized.
/// </summary>
public sealed partial class Hdf5ViewModel
{
    /// <summary>Most elements one <c>hdf5_read</c> returns.</summary>
    internal const ulong ReadLimit = 2000;

    private const int ContextTreeLines   = 300;
    private const int ContextTableRows   = 60;
    private const int ContextTableColumns = 24;

    public bool IsContextReady => !IsLoading && Table?.IsReading != true;

    /// <summary>The file this page reads — so two viewers pinned at once keep their tools apart.</summary>
    public string? GetSecurityContext() => FilePath;

    public string GetContext()
    {
        if (IsLoading) return $"An HDF5 viewer is opening {FilePath}.";
        if (LoadProblem is not null) return $"An HDF5 viewer that could not open {FilePath}: {LoadProblem}";

        var sb = new StringBuilder();
        sb.AppendLine($"HDF5 viewer on {FilePath}.");
        sb.AppendLine("Object tree as expanded on screen (indented; a group lists the members loaded so far):");
        int lines = 0;
        foreach (var root in Roots) AppendTree(sb, root, 0, ref lines);
        if (lines >= ContextTreeLines) sb.AppendLine("  … (more of the tree is expanded; call hdf5_list for any group)");
        if (TreeFilter.Length > 0) sb.AppendLine($"Tree filter \"{TreeFilter}\" hides objects whose name does not match.");

        if (Current is not { } o)
        {
            sb.AppendLine("Nothing is selected.");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine($"Selected: {o.Path}");
            sb.AppendLine(DetailsOpen ? "Details drawer (open):" : "Details drawer (closed, so the user does not see it now):");
            AppendObject(sb, o);
            AppendAttributes(sb, AllAttributes, AttributesProblem);
            if (AttributeFilter.Length > 0)
                sb.AppendLine($"  Attribute filter \"{AttributeFilter}\" shows {Attributes.Count} of {AllAttributes.Count}.");
            AppendContent(sb);
        }

        sb.AppendLine();
        sb.Append("Call hdf5_list for a group's members, hdf5_describe for any object's details and attributes, ")
          .Append("hdf5_read for dataset elements, and hdf5_select to show the user an object or a slice.");
        return sb.ToString();
    }

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        new DelegateClientTool("hdf5_list",
            "List the members of a group in the open HDF5 file: name, kind, and for datasets type and shape. " +
            "'path' is the group's absolute path (default /); 'skip' and 'take' page through large groups (take at most 1000).",
            [
                new ClientToolParameter("path", "absolute group path, e.g. /measurements", Required: false),
                new ClientToolParameter("skip", "members to skip (default 0)", Required: false, Type: "number"),
                new ClientToolParameter("take", "members to return (default 200, at most 1000)", Required: false, Type: "number"),
            ],
            ToolSafety.SafeOperation, ListToolAsync),

        new DelegateClientTool("hdf5_describe",
            "Describe any object in the open HDF5 file: kind, type, shape, max shape, layout, chunk shape and fill value, " +
            "with every attribute and its value.",
            [new ClientToolParameter("path", "absolute object path, e.g. /measurements/temperature")],
            ToolSafety.SafeOperation, DescribeToolAsync),

        new DelegateClientTool("hdf5_read",
            "Read elements of a dataset in the open HDF5 file. 'start' and 'count' are comma-separated, one number per " +
            $"dimension (e.g. start '0,10', count '5,4'); without them the first elements are read. At most {ReadLimit} elements.",
            [
                new ClientToolParameter("path", "absolute dataset path"),
                new ClientToolParameter("start", "first index per dimension, comma-separated", Required: false),
                new ClientToolParameter("count", "how many per dimension, comma-separated", Required: false),
            ],
            ToolSafety.SafeOperation, ReadToolAsync),

        new DelegateClientTool("hdf5_select",
            "Show the user an object: selects it in the tree and, for a dataset, optionally scrolls the table to 'row' " +
            "and holds the dimensions that are neither rows nor columns at 'indices' (comma-separated, one per dimension).",
            [
                new ClientToolParameter("path", "absolute object path"),
                new ClientToolParameter("row", "0-based row to scroll the table to", Required: false, Type: "number"),
                new ClientToolParameter("indices", "an index per dimension, comma-separated", Required: false),
            ],
            ToolSafety.SafeOperation, SelectToolAsync),
    ];

    // ── Tools ─────────────────────────────────────────────────────────────

    private async Task<ToolResult> ListToolAsync(JsonObject args, CancellationToken ct)
    {
        if (Source is not { } src) return ToolResult.Error("No HDF5 file is open.");
        var path = Hdf5Path.Normalize(ToolArgs.Str(args, "path"));
        int skip = Math.Max(0, ToolArgs.Int(args, "skip", 0));
        int take = Math.Clamp(ToolArgs.Int(args, "take", 200), 1, 1000);
        try
        {
            var listing = await Task.Run(() => src.ListChildren(path, skip, take, ct), ct);
            var sb = new StringBuilder();
            foreach (var o in listing.Items) sb.AppendLine(OneLine(o));
            if (listing.HasMore) sb.AppendLine($"… more members follow; call again with skip {skip + listing.Items.Count}.");
            if (listing.Problem is { } problem) sb.AppendLine($"Listing stopped at a member the reader could not decode: {problem}");
            return ToolResult.Ok($"{listing.Items.Count} members of {path}", sb.ToString());
        }
        catch (Hdf5Exception ex) { return ToolResult.Error(ex.Message); }
    }

    private async Task<ToolResult> DescribeToolAsync(JsonObject args, CancellationToken ct)
    {
        if (Source is not { } src) return ToolResult.Error("No HDF5 file is open.");
        var path = Hdf5Path.Normalize(ToolArgs.Str(args, "path"));
        try
        {
            var (o, attributes) = await Task.Run(() =>
            {
                var target = src.Stat(ResolveObjectPath(src, path));
                return (target, target is null ? (IReadOnlyList<Hdf5Attribute>)[] : src.Attributes(target.Path, ct));
            }, ct);
            if (o is null) return ToolResult.Error($"Nothing in this file is at {path}.");
            var sb = new StringBuilder();
            AppendObject(sb, o);
            AppendAttributes(sb, [.. attributes.Select(RowOf)], null);
            return ToolResult.Ok($"{o.Path}: {o.Kind}", sb.ToString());
        }
        catch (Hdf5Exception ex) { return ToolResult.Error(ex.Message); }
    }

    private async Task<ToolResult> ReadToolAsync(JsonObject args, CancellationToken ct)
    {
        if (Source is not { } src) return ToolResult.Error("No HDF5 file is open.");
        var path = Hdf5Path.Normalize(ToolArgs.Str(args, "path"));
        try
        {
            return await Task.Run(() =>
            {
                if (src.Stat(ResolveObjectPath(src, path)) is not { Dataset: { } info } dataset)
                    return ToolResult.Error($"{path} is not a dataset.");
                if (!TrySelection(info.Space, ToolArgs.Str(args, "start"), ToolArgs.Str(args, "count"), out var selection, out var why))
                    return ToolResult.Error(why);
                if (selection.ElementCount > ReadLimit)
                    return ToolResult.Error($"That is {selection.ElementCount} elements; read at most {ReadLimit} at a time.");
                var block = src.ReadBlock(dataset.Path, selection, ct);
                return ToolResult.Ok($"{block.Count} elements of {dataset.Path}", FormatBlock(block, selection));
            }, ct);
        }
        catch (Exception ex) when (ex is Hdf5Exception or ArgumentException) { return ToolResult.Error(ex.Message); }
    }

    private async Task<ToolResult> SelectToolAsync(JsonObject args, CancellationToken ct)
    {
        var path    = ToolArgs.Str(args, "path") ?? Hdf5Path.Root;
        var row     = ToolArgs.IntOrNull(args, "row");
        var indices = ToolArgs.Str(args, "indices");
        return await _shell.RunOnUiAsync(async () =>
        {
            if (await NavigateToAsync(path) is not { Object: { } o }) return ToolResult.Error("No HDF5 file is open.");
            if (!string.Equals(o.Path, Hdf5Path.Normalize(ResolveOrSelf(path)), StringComparison.Ordinal))
                return ToolResult.Error($"Nothing is at {path}; the nearest object shown is {o.Path}.");
            await (ShowTask ?? Task.CompletedTask);
            if (Table is { } table)
            {
                if (indices is { Length: > 0 } && TryParse(indices, out var at)) table.Slice.SetIndices(at);
                if (row is { } r) await table.ScrollToAsync(r);
            }
            return ToolResult.Ok($"Showing {o.Path}", ContextOfSelection());
        });
    }

    private string ResolveOrSelf(string path) => Source is { } src ? ResolveObjectPath(src, path) : path;

    // ── Context text ──────────────────────────────────────────────────────

    private string ContextOfSelection()
    {
        var sb = new StringBuilder();
        if (Current is { } o) AppendObject(sb, o);
        AppendContent(sb);
        return sb.ToString();
    }

    private static void AppendTree(StringBuilder sb, Hdf5NodeViewModel node, int depth, ref int lines)
    {
        if (lines >= ContextTreeLines || !node.IsVisible) return;
        var indent = new string(' ', 2 + depth * 2);
        sb.Append(indent).AppendLine(node.Role switch
        {
            Hdf5NodeRole.Object when node.Parent is null => "/ (root group)",
            Hdf5NodeRole.Object                          => OneLine(node.Object!),
            Hdf5NodeRole.More                            => "… more members not yet listed",
            Hdf5NodeRole.Stopped                         => $"listing stopped: {node.Detail}",
            _                                            => "(not expanded)",
        });
        lines++;
        if (node.Role != Hdf5NodeRole.Object || (!node.IsExpanded && node.Parent is not null)) return;
        foreach (var child in node.Children)
        {
            if (child.Role == Hdf5NodeRole.Loading) continue;
            AppendTree(sb, child, depth + 1, ref lines);
        }
    }

    private static string OneLine(Hdf5Object o) => o switch
    {
        { Dataset: { } d } => $"{o.Name} — dataset {d.Type.DisplayName}, shape {ShapeInEnglish(d.Space)}",
        { Problem: { } p } => $"{o.Name} — {o.Kind}: {p}",
        _                  => $"{o.Name} — {o.Kind}",
    };

    private static string ShapeInEnglish(Hdf5Dataspace space) => space.Kind switch
    {
        Hdf5SpaceKind.Scalar => "scalar",
        Hdf5SpaceKind.Null   => "null (no elements)",
        _                    => Hdf5Dataspace.FormatDims(space.Shape.Select(d => (ulong?)d)),
    };

    private static void AppendObject(StringBuilder sb, Hdf5Object o)
    {
        sb.AppendLine($"  Path: {o.Path}");
        sb.AppendLine($"  Kind: {o.Kind}");
        if (o.Dataset is { } d)
        {
            sb.AppendLine($"  Type: {d.Type.DisplayName} ({d.Type.Size} bytes per element)");
            sb.AppendLine($"  Shape: {ShapeInEnglish(d.Space)}");
            if (d.Space.Kind == Hdf5SpaceKind.Simple) sb.AppendLine($"  Max shape: {Hdf5Dataspace.FormatDims(d.Space.MaxShape)}");
            sb.AppendLine($"  Elements: {d.Space.ElementCount}");
            sb.AppendLine($"  Layout: {d.Layout}");
            if (d.ChunkShape.Count > 0) sb.AppendLine($"  Chunk shape: {Hdf5Dataspace.FormatDims(d.ChunkShape.Select(c => (ulong?)c))}");
            sb.AppendLine($"  Fill value: {d.FillValue ?? "not defined"}");
        }
        if (o.Problem is { } p) sb.AppendLine($"  Problem: {p}");
    }

    private static void AppendAttributes(StringBuilder sb, IReadOnlyList<Hdf5AttributeRow> attributes, string? problem)
    {
        if (problem is not null) { sb.AppendLine($"  Attributes could not be read: {problem}"); return; }
        sb.AppendLine(attributes.Count == 0 ? "  No attributes." : $"  Attributes ({attributes.Count}):");
        foreach (var a in attributes)
            sb.AppendLine($"    {a.Name}: {a.TypeText} = {a.Value}" + (a.HasNote ? $" ({a.Note})" : string.Empty));
    }

    private void AppendContent(StringBuilder sb)
    {
        if (ContentMessage is { } message) sb.AppendLine($"Content pane: {message}");
        if (IsGroupContent)
        {
            sb.AppendLine("Content pane: the group's members by kind — " +
                          (GroupMembers.Count == 0 ? "none" : string.Join(", ", GroupMembers.Select(m => $"{m.Kind} {m.Count}"))) + ".");
            if (GroupProblem is { } gp) sb.AppendLine($"  Listing stopped early: {gp}");
        }
        if (Table is not { } t) return;

        var v = t.Slice.Snapshot();
        var layout = new StringBuilder($"Table: {t.TotalRowCount} rows");
        if (v.RowAxis >= 0 && v.Space.Rank > 1) layout.Append($" along dimension {v.RowAxis}");
        if (v.ColumnAxis >= 0)
            layout.Append($", columns along dimension {v.ColumnAxis} showing {v.ColumnOffset}–{v.ColumnOffset + v.ColumnCount - 1} of {t.Slice.ColumnExtent}");
        foreach (var f in t.Slice.FixedIndices) layout.Append($", dimension {f.Dimension} held at {(ulong)Math.Round(f.Index)}");
        sb.AppendLine(layout.Append('.').ToString());
        if (t.Problem is { } problem) { sb.AppendLine($"The table could not read its rows: {problem}"); return; }
        if (t.Window.Count == 0) return;

        var columns = t.Columns.Take(ContextTableColumns).Select(c => c.Header).ToList();
        int rows = Math.Min(t.Window.Count, ContextTableRows);
        sb.AppendLine($"Rows {t.Window[0].AbsoluteIndex}–{t.Window[rows - 1].AbsoluteIndex} as loaded on screen (index: {string.Join(" | ", columns)}):");
        foreach (var row in t.Window.Take(rows))
            sb.AppendLine($"  {row.AbsoluteIndex}: {string.Join(" | ", row.Cells.Take(ContextTableColumns))}");
        if (t.Window.Count > rows || t.Columns.Count > columns.Count)
            sb.AppendLine("  … the loaded window is larger than shown here; call hdf5_read for any part of it.");
    }

    // ── Reading helpers ───────────────────────────────────────────────────

    private static bool TrySelection(Hdf5Dataspace space, string? start, string? count, out Hdf5Selection selection, out string why)
    {
        why = string.Empty;
        selection = Hdf5Selection.Scalar;
        if (space.Kind != Hdf5SpaceKind.Simple) return true;
        if (start is null && count is null)
        {
            selection = Hdf5Selection.Walk(space.Shape, ReadLimit).FirstOrDefault() ?? Hdf5Selection.All(space);
            return true;
        }
        if (!TryParse(start ?? string.Join(',', space.Shape.Select(_ => 0)), out var s)
            || !TryParse(count ?? string.Join(',', s.Select((_, d) => space.Shape[d] - s[d])), out var c)
            || s.Count != space.Rank || c.Count != space.Rank)
        {
            why = $"'start' and 'count' need {space.Rank} comma-separated numbers each.";
            return false;
        }
        selection = new Hdf5Selection(s, c);
        try { selection.Validate(space); }
        catch (ArgumentException ex) { why = ex.Message; return false; }
        return true;
    }

    private static bool TryParse(string text, out IReadOnlyList<ulong> values)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var parsed = new ulong[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!ulong.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed[i])) { values = []; return false; }
        values = parsed;
        return true;
    }

    /// <summary>One line per run of the last dimension, each prefixed with the index of its first element.</summary>
    private static string FormatBlock(Hdf5Block block, Hdf5Selection selection)
    {
        var sb = new StringBuilder();
        if (selection.Rank == 0) return block.Count == 0 ? "(no elements)" : block.Format(0);
        long run = (long)selection.Count[^1];
        var index = selection.Start.ToArray();
        for (long e = 0; e < block.Count; e += run)
        {
            sb.Append('[').Append(string.Join(", ", index)).Append("]: ");
            for (long i = 0; i < run; i++) sb.Append(i == 0 ? string.Empty : ", ").Append(block.Format(e + i));
            sb.AppendLine();
            for (int d = selection.Rank - 2; d >= 0; d--)
            {
                if (++index[d] < selection.Start[d] + selection.Count[d]) break;
                index[d] = selection.Start[d];
            }
        }
        return sb.ToString();
    }
}
