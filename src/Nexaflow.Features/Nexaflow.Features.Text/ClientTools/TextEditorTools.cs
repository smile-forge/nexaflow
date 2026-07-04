using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Text.ViewModels;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Nexaflow.Features.Text.ClientTools;

/// <summary>
/// The AI tool surface for a text file: page through it, search it (edit-aware), and — with approval —
/// edit it by line number or find/replace. Every mutating tool funnels through the same
/// <see cref="TextViewModel"/>/<c>OverlayTextFile</c> the user edits, so AI edits show in the viewport,
/// are searchable, and persist via the same streaming save.
/// </summary>
internal static class TextEditorTools
{
    public static IReadOnlyList<IClientTool> For(TextViewModel vm) =>
    [
        // ── Read-only (auto-run) ──────────────────────────────────────────────
        new DelegateClientTool(
            "copy_visible_text",
            "Copy the text currently visible in the editor to the clipboard.",
            [],
            ToolSafety.ReadOnly,
            (_, _) =>
            {
                System.Windows.Clipboard.SetText(vm.GetVisibleText());
                return Task.FromResult(ToolResult.Ok("copied", "Copied the visible text to the clipboard."));
            }),

        new DelegateClientTool(
            "read_lines",
            "Read a range of lines from the file (the rest of the file, page by page). Returns each line " +
            "prefixed by its 1-based number. Reflects unsaved edits.",
            [
                new ClientToolParameter("start_line", "First line to read (1-based).", Type: "number"),
                new ClientToolParameter("count", "How many lines to read (max 1000).", Required: false, Type: "number"),
            ],
            ToolSafety.ReadOnly,
            async (args, ct) =>
            {
                int start = System.Math.Max(1, ToolArgs.Int(args, "start_line", 1));
                int count = System.Math.Clamp(ToolArgs.Int(args, "count", 200), 1, 1000);
                var body  = await vm.ReadNumberedLinesAsync(start, count, ct);
                int total = vm.LineCount;
                long next = start + count;
                var note  = next <= total ? $"\n(File has {total} lines; continue at start_line {next}.)" : $"\n(End of file; {total} lines total.)";
                return ToolResult.Ok($"read lines {start}–{start + count - 1}", body + note);
            }),

        new DelegateClientTool(
            "find_text",
            "Search the file for text or a regex (results reflect unsaved edits) and highlight the matches " +
            "in the editor. Returns the matching line numbers with a preview.",
            [
                new ClientToolParameter("query", "Text or regular expression to find."),
                new ClientToolParameter("regex", "Treat the query as a regular expression.", Required: false, Type: "boolean"),
                new ClientToolParameter("case_sensitive", "Match case.", Required: false, Type: "boolean"),
                new ClientToolParameter("max_results", "Maximum matches to return (default 50).", Required: false, Type: "number"),
            ],
            ToolSafety.ReadOnly,
            async (args, ct) =>
            {
                var query = ToolArgs.Str(args, "query", "q", "text");
                if (string.IsNullOrEmpty(query)) return ToolResult.Error("No query provided.");
                bool rx = ToolArgs.Bool(args, "regex");
                bool cs = ToolArgs.Bool(args, "case_sensitive");
                int max = System.Math.Clamp(ToolArgs.Int(args, "max_results", 50), 1, 500);
                var report = await vm.ToolFindAsync(query, rx, cs, max, ct);
                return ToolResult.Ok($"found '{query}'", report);
            }),

        // ── Mutating (approval-gated) ─────────────────────────────────────────
        new DelegateClientTool(
            "edit_lines",
            "Replace lines [start_line, end_line] (1-based, inclusive) with new text. Pass empty new_text to " +
            "delete the lines. To insert without removing, set end_line = start_line - 1.",
            [
                new ClientToolParameter("start_line", "First line to replace (1-based).", Type: "number"),
                new ClientToolParameter("end_line", "Last line to replace (1-based, inclusive).", Type: "number"),
                new ClientToolParameter("new_text", "Replacement text (include trailing newlines to keep line breaks).", Required: false),
            ],
            ToolSafety.RequiresApproval,
            async (args, ct) =>
            {
                if (!vm.CanEdit) return ToolResult.Error("This file can't be edited.");
                vm.EnsureEditing();
                int start = ToolArgs.Int(args, "start_line", 1);
                int end   = ToolArgs.Int(args, "end_line", start);
                var text  = ToolArgs.Str(args, "new_text", "text", "content") ?? string.Empty;
                await vm.ApplyAiLineEditAsync(start - 1, end - 1, text, ct);
                return ToolResult.Ok($"edited lines {start}–{end}", $"Replaced lines {start}–{end}. Unsaved — call save_file to persist.");
            },
            parallelizable: false),

        new DelegateClientTool(
            "replace_in_range",
            "Find/replace within lines [start_line, end_line] (1-based, inclusive). Reflects and updates the " +
            "current content. Returns the number of occurrences replaced.",
            [
                new ClientToolParameter("start_line", "First line of the range (1-based).", Type: "number"),
                new ClientToolParameter("end_line", "Last line of the range (1-based, inclusive).", Type: "number"),
                new ClientToolParameter("find", "Text or regex to find."),
                new ClientToolParameter("replace", "Replacement text.", Required: false),
                new ClientToolParameter("regex", "Treat 'find' as a regular expression.", Required: false, Type: "boolean"),
                new ClientToolParameter("case_sensitive", "Match case.", Required: false, Type: "boolean"),
            ],
            ToolSafety.RequiresApproval,
            async (args, ct) =>
            {
                if (!vm.CanEdit) return ToolResult.Error("This file can't be edited.");
                var find = ToolArgs.Str(args, "find", "pattern", "search");
                if (string.IsNullOrEmpty(find)) return ToolResult.Error("No 'find' provided.");
                vm.EnsureEditing();
                int start = ToolArgs.Int(args, "start_line", 1);
                int end   = ToolArgs.Int(args, "end_line", vm.LineCount);
                var repl  = ToolArgs.Str(args, "replace", "replacement", "to") ?? string.Empty;
                int n = await vm.ApplyAiReplaceAsync(find, repl, ToolArgs.Bool(args, "regex"),
                    ToolArgs.Bool(args, "case_sensitive"), start - 1, end - 1, ct);
                return ToolResult.Ok($"replaced {n} in lines {start}–{end}", $"Replaced {n} occurrence(s). Unsaved — call save_file to persist.");
            },
            parallelizable: false),

        new DelegateClientTool(
            "replace_all",
            "Find/replace across the whole file (reflects unsaved edits). Returns the number replaced.",
            [
                new ClientToolParameter("find", "Text or regex to find."),
                new ClientToolParameter("replace", "Replacement text.", Required: false),
                new ClientToolParameter("regex", "Treat 'find' as a regular expression.", Required: false, Type: "boolean"),
                new ClientToolParameter("case_sensitive", "Match case.", Required: false, Type: "boolean"),
            ],
            ToolSafety.RequiresApproval,
            async (args, ct) =>
            {
                if (!vm.CanEdit) return ToolResult.Error("This file can't be edited.");
                var find = ToolArgs.Str(args, "find", "pattern", "search");
                if (string.IsNullOrEmpty(find)) return ToolResult.Error("No 'find' provided.");
                vm.EnsureEditing();
                var repl = ToolArgs.Str(args, "replace", "replacement", "to") ?? string.Empty;
                long toLine = vm.Engine?.TotalLines ?? vm.LineCount;
                int n = await vm.ApplyAiReplaceAsync(find, repl, ToolArgs.Bool(args, "regex"),
                    ToolArgs.Bool(args, "case_sensitive"), 0, toLine, ct);
                return ToolResult.Ok($"replaced {n} occurrence(s)", $"Replaced {n} occurrence(s) file-wide. Unsaved — call save_file to persist.");
            },
            parallelizable: false),

        new DelegateClientTool(
            "save_file",
            "Save the file, merging the edits into it (streaming bulk-copy of unchanged regions).",
            [],
            ToolSafety.RequiresApproval,
            async (_, _) =>
            {
                if (!vm.IsDirty) return ToolResult.Ok("nothing to save", "There are no unsaved edits.");
                await vm.SaveFromToolAsync();
                return ToolResult.Ok("saved", $"Saved the file.");
            },
            parallelizable: false),
    ];
}
