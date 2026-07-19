using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Markdown.ViewModels;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Nexaflow.Features.Markdown.ClientTools;

/// <summary>
/// The AI tool surface for a markdown document: read the whole source, rewrite it or find/replace,
/// switch between the rendered and raw-source views, and save — the same operations the user has via
/// the inline/source editors, the toolbar toggle and Save. Every mutating tool funnels through the same
/// <see cref="MarkdownViewModel.Markdown"/> the user edits, so AI edits show in the editor and persist
/// through the same save.
/// </summary>
internal static class MarkdownTools
{
    public static IReadOnlyList<IClientTool> For(MarkdownViewModel vm) =>
    [
        // ── Read / explore (auto-run) ─────────────────────────────────────────
        new DelegateClientTool(
            "read_document",
            "Read the full markdown source (reflects unsaved edits).",
            [],
            ToolSafety.SafeOperation,
            (_, _) =>
            {
                var md = vm.Markdown ?? string.Empty;
                return Task.FromResult(md.Length == 0
                    ? ToolResult.Ok("document is empty", "(empty document)")
                    : ToolResult.Ok($"read {md.Length} char(s)", md));
            }),

        new DelegateClientTool(
            "set_view_mode",
            "Switch the editor between the rendered inline view and the raw markdown source view.",
            [ new ClientToolParameter("mode", "\"rendered\" (inline) or \"source\" (raw markdown).") ],
            ToolSafety.SafeOperation,
            async (args, _) =>
            {
                var mode = ToolArgs.Str(args, "mode", "view") ?? "rendered";
                bool source = mode.StartsWith("source", StringComparison.OrdinalIgnoreCase)
                           || mode.StartsWith("raw", StringComparison.OrdinalIgnoreCase);
                await vm.SetSourceModeAsync(source);
                return ToolResult.Ok(source ? "showing source" : "showing rendered",
                    $"Switched to the {(source ? "raw source" : "rendered inline")} view.");
            }),

        // ── Mutating (approval-gated) ─────────────────────────────────────────
        new DelegateClientTool(
            "set_document",
            "Replace the entire markdown document with new content. Unsaved until save_file.",
            [ new ClientToolParameter("text", "The full new markdown source.") ],
            ToolSafety.RequiresApproval,
            async (args, _) =>
            {
                var text = ToolArgs.Raw(args, "text", "content", "markdown") ?? string.Empty;
                await vm.SetDocumentAsync(text);
                return ToolResult.Ok("replaced document", "Replaced the whole document. Unsaved — call save_file to persist.");
            }),

        new DelegateClientTool(
            "replace_all",
            "Find/replace across the whole document (reflects unsaved edits). Returns the number replaced.",
            [
                new ClientToolParameter("find", "Text or regex to find."),
                new ClientToolParameter("replace", "Replacement text.", Required: false),
                new ClientToolParameter("regex", "Treat 'find' as a regular expression.", Required: false, Type: "boolean"),
                new ClientToolParameter("case_sensitive", "Match case.", Required: false, Type: "boolean"),
            ],
            ToolSafety.RequiresApproval,
            async (args, _) =>
            {
                var find = ToolArgs.Str(args, "find", "pattern", "search");
                if (string.IsNullOrEmpty(find)) return ToolResult.Error("No 'find' provided.");
                var repl = ToolArgs.Str(args, "replace", "replacement", "to") ?? string.Empty;
                var (result, n) = ReplaceAll(vm.Markdown ?? string.Empty, find, repl,
                    ToolArgs.Bool(args, "regex"), ToolArgs.Bool(args, "case_sensitive"));
                if (n > 0) await vm.SetDocumentAsync(result);
                return ToolResult.Ok($"replaced {n} occurrence(s)",
                    n > 0 ? $"Replaced {n} occurrence(s). Unsaved — call save_file to persist." : "Nothing matched.");
            }),

        new DelegateClientTool(
            "save_file",
            "Save the markdown document to disk.",
            [],
            ToolSafety.RequiresApproval,
            async (_, _) =>
            {
                var saved = await vm.SaveFromToolAsync();
                return ToolResult.Ok(saved ? "saved" : "nothing to save",
                    saved ? "Saved the file." : "There are no unsaved edits.");
            }),
    ];

    /// <summary>Whole-document find/replace; returns the new text and the number of occurrences replaced.
    /// An invalid regex yields no change (0 replacements) rather than throwing.</summary>
    private static (string result, int count) ReplaceAll(string src, string find, string repl, bool regex, bool caseSensitive)
    {
        if (regex)
        {
            Regex rx;
            try { rx = new Regex(find, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase); }
            catch { return (src, 0); }
            var n = rx.Matches(src).Count;
            return n == 0 ? (src, 0) : (rx.Replace(src, repl), n);
        }

        var cmp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var sb = new StringBuilder();
        int idx = 0, prev = 0, count = 0;
        while ((idx = src.IndexOf(find, idx, cmp)) >= 0)
        {
            sb.Append(src, prev, idx - prev).Append(repl);
            idx += find.Length;
            prev = idx;
            count++;
        }
        if (count == 0) return (src, 0);
        sb.Append(src, prev, src.Length - prev);
        return (sb.ToString(), count);
    }
}
