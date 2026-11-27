using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Localization;
using Nexaflow.Visuals.Common.Controls;
using System.Globalization;
using System.IO;
using System.Text;

namespace Nexaflow.Features.Logs.ViewModels;

/// <summary>
/// What the assistant is told of the log and can do with it: an honest context, a preview for the conversation panel, and tools that read
/// lines of the whole log — beyond the screen, and beyond a window onto a long one — and mirror the toolbar's filters, highlights and tail.
/// </summary>
public sealed partial class LogViewModel
{
    /// <summary>The most lines the context preview shows.</summary>
    private const int PreviewLineCount = 20;

    /// <summary>Where the context preview cuts a long line; it never wraps.</summary>
    private const int PreviewLineLength = 200;

    public string GetContext()
    {
        if (string.IsNullOrEmpty(FilePath)) return "Log viewer: no file loaded.";

        var sb = new StringBuilder();
        sb.Append($"Log file '{FileName}' ({FileSizeText}) at '{FilePath}'. ");
        sb.Append($"{LineCount} line(s) loaded");
        if (IsLoadingHead)
            sb.Append(" — currently only the tail (most recent lines); the head is still streaming in");
        sb.Append($". Format: {ActiveParser.FormatName}. ");

        if (_detectedFirstTimestamp.HasValue && _detectedLastTimestamp.HasValue)
            sb.Append($"Timestamps span {_detectedFirstTimestamp:yyyy-MM-dd HH:mm:ss} to {_detectedLastTimestamp:yyyy-MM-dd HH:mm:ss}. ");
        else if (HasTimestamps)
            sb.Append("Lines carry timestamps. ");
        else
            sb.Append("No timestamps detected. ");

        if (IsMonitoring)
            sb.Append(IsPaused ? "The live tail is paused. " : "Live-tailing the file for new lines. ");
        else
            sb.Append("Live monitoring is off. ");

        var active = new List<string>();
        if (IsFilterActive && !string.IsNullOrWhiteSpace(FilterRegex))
            active.Add($"regex filter /{FilterRegex}/");
        if (FilterStart.HasValue || FilterEnd.HasValue)
            active.Add($"time filter {(FilterStart?.ToString("yyyy-MM-dd HH:mm:ss") ?? "…")} .. {(FilterEnd?.ToString("yyyy-MM-dd HH:mm:ss") ?? "…")}");
        var levels = EnabledHighlightLevels();
        if (levels.Count > 0)
            active.Add($"level highlight: {string.Join(", ", levels)}");
        if (!string.IsNullOrWhiteSpace(CustomHighlightTerm))
            active.Add($"term highlight \"{CustomHighlightTerm}\"");

        sb.Append(active.Count > 0
            ? $"Active view filters/highlights — {string.Join("; ", active)} (non-matching lines are faded, not removed). "
            : "No filters or highlights active. ");

        if (SelectedLineCount > 0)
            sb.Append($"{SelectedLineCount} line(s) selected. ");

        sb.Append("Use read_tail / read_lines to read the log content and search_page to find lines; "
                + "set_regex_filter / set_time_filter / highlight_levels / set_tail_paused mirror the toolbar controls.");
        return sb.ToString();
    }

    /// <summary>
    /// A reminder of the log for the conversation context panel: its size and length, then the lines the user selected — or, with none
    /// selected, the last lines, where a tailing log is read. The panel's header names the file. A fresh control each time the chip is
    /// selected, per the contract; it copies what it shows.
    /// </summary>
    public object CreateContextPreview()
    {
        var meta = SelectedLineCount > 0
            ? LineCount == 1
                ? Str.Format("Logs.Preview.LineSelectedFormat", FileSizeText, LineCount, SelectedLineCount)
                : Str.Format("Logs.Preview.LinesSelectedFormat", FileSizeText, LineCount, SelectedLineCount)
            : LineCount == 1
                ? Str.Format("Logs.Preview.LineFormat", FileSizeText, LineCount)
                : Str.Format("Logs.Preview.LinesFormat", FileSizeText, LineCount);
        return new TextContextPreview(meta, string.Join("\n", PreviewLines()));
    }

    private IEnumerable<string> PreviewLines()
    {
        var last = _shown.LineCount;
        if (last > 1 && _shown.LineText(last - 1).Length == 0) last--;   // the file's closing newline

        var texts = _selectedLines.Count > 0
            ? _selectedLines.Where(n => n >= 1 && n <= last).Take(PreviewLineCount).Select(n => _shown.LineText(n - 1))
            : _shown.LineTexts(Math.Max(0, last - PreviewLineCount), (int)Math.Min(PreviewLineCount, last));

        foreach (var text in texts)
            yield return text.Length > PreviewLineLength ? text[..PreviewLineLength] : text;
    }

    /// <summary>File-path scope boundary — keeps two log tabs on different files distinguishable when pinned.</summary>
    public string? GetSecurityContext() => FilePath;

    /// <summary>The log this tab shows.</summary>
    public ContextGrant GetContextGrant() => ContextGrant.OfFile(FilePath);

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        // ── Read / explore (auto-run) ──
        new DelegateClientTool(
            "read_tail",
            "Read the most recent (bottom) lines of the log — the live tail. While the earlier history of a large file is still "
          + "loading, only the most recent part is available.",
            [ new ClientToolParameter("lines", "How many trailing lines to return (default 100).", Required: false, Type: "number") ],
            ToolSafety.SafeOperation,
            (args, _) => OnUi(() =>
            {
                var total = _shown.LineCount;
                if (total == 0) return ToolResult.Ok("empty", "(the log is empty)");
                var n     = Math.Clamp(ToolArgs.Int(args, "lines", 100), 1, 5000);
                var start = Math.Max(1, total - n + 1);
                return ToolResult.Ok($"read lines {start}–{total} of {total}", ReadLineRange(start, total));
            })),

        new DelegateClientTool(
            "read_lines",
            "Read a specific 1-based line range from the log.",
            [
                new ClientToolParameter("start", "First line number (1-based)."),
                new ClientToolParameter("count", "How many lines to read (default 100).", Required: false, Type: "number"),
            ],
            ToolSafety.SafeOperation,
            (args, _) => OnUi(() =>
            {
                var total = _shown.LineCount;
                if (total == 0) return ToolResult.Ok("empty", "(the log is empty)");
                var start = Math.Clamp(ToolArgs.Int(args, "start", 1), 1, total);
                var count = Math.Clamp(ToolArgs.Int(args, "count", 100), 1, 5000);
                var end   = Math.Min(total, start + count - 1);
                return ToolResult.Ok($"read lines {start}–{end} of {total}", ReadLineRange(start, end));
            })),

        // No search tool here: the page declares ISearchable, so the shell attaches search_page / show_search_results automatically.
        // A hand-rolled one drifted from what "?" means to the user — it matched a bare term as a substring rather than the word it
        // spells, ignored match-case, and reported a bad pattern as a tool error instead of an unsupported search.

        new DelegateClientTool(
            "read_selected_lines",
            "Read the lines the user has selected (checked in the gutter), in order.",
            [],
            ToolSafety.SafeOperation,
            (_, _) => OnUi(() =>
            {
                if (_selectedLines.Count == 0)
                    return ToolResult.Ok("no selection", "The user has not selected any lines.");
                var total = _shown.LineCount;
                var lines = _selectedLines.Where(n => n >= 1 && n <= total).Select(n => $"{n}: {_shown.LineText(n - 1)}");
                return ToolResult.Ok($"{_selectedLines.Count} selected line(s)", string.Join("\n", lines));
            })),

        // ── View state (auto-run) — mirror the user's filter / highlight / tail controls (reversible, faded) ──
        new DelegateClientTool(
            "set_regex_filter",
            "Set or clear the regex filter that fades non-matching lines — the same box the user types in. Pass "
          + "an empty pattern to clear it. Lines are faded, never removed from the file.",
            [ new ClientToolParameter("pattern", "A regular expression; empty clears the filter.", Required: false) ],
            ToolSafety.SafeOperation,
            async (args, _) =>
            {
                var pattern = ToolArgs.Str(args, "pattern", "regex", "filter") ?? string.Empty;
                await _shell.RunOnUiAsync(() => FilterRegex = pattern);
                if (pattern.Length == 0) return ToolResult.Ok("filter cleared", "Cleared the regex filter.");
                return IsFilterActive
                    ? ToolResult.Ok($"filtering /{pattern}/", $"Applied regex filter '{pattern}'. Non-matching lines are faded.")
                    : ToolResult.Error($"'{pattern}' is not a valid regular expression.");
            }),

        new DelegateClientTool(
            "set_time_filter",
            "Fade lines whose timestamp falls outside a range (only meaningful when the log has timestamps), or "
          + "clear it. Pass ISO-8601 date/times; omit both start and end to clear.",
            [
                new ClientToolParameter("start", "Range start, e.g. 2024-01-15T10:00:00 (optional).", Required: false),
                new ClientToolParameter("end", "Range end, e.g. 2024-01-15T11:00:00 (optional).", Required: false),
            ],
            ToolSafety.SafeOperation,
            async (args, _) =>
            {
                var startStr = ToolArgs.Str(args, "start", "from");
                var endStr   = ToolArgs.Str(args, "end", "to");
                if (startStr is null && endStr is null)
                {
                    await _shell.RunOnUiAsync(() => ClearTimeFilterCommand.Execute(null));
                    return ToolResult.Ok("time filter cleared", "Cleared the time filter.");
                }

                DateTime start = default, end = default;
                if (startStr is not null &&
                    !DateTime.TryParse(startStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out start))
                    return ToolResult.Error($"Could not parse start time '{startStr}'.");
                if (endStr is not null &&
                    !DateTime.TryParse(endStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out end))
                    return ToolResult.Error($"Could not parse end time '{endStr}'.");

                await _shell.RunOnUiAsync(() =>
                {
                    if (startStr is not null) { FilterStartDate = start.Date; FilterStartTime = start.ToString("HH:mm:ss"); }
                    if (endStr   is not null) { FilterEndDate   = end.Date;   FilterEndTime   = end.ToString("HH:mm:ss"); }
                    ApplyTimeFilterCommand.Execute(null);
                });
                return ToolResult.Ok("time filter applied",
                    $"Filtering to timestamps in [{FilterStart?.ToString("s") ?? "…"} .. {FilterEnd?.ToString("s") ?? "…"}]. "
                  + "Lines outside the range are faded.");
            }),

        new DelegateClientTool(
            "highlight_levels",
            "Set which severity levels are colour-highlighted (Fatal, Error, Warning, Info, Debug). Pass a "
          + "comma-separated list to enable exactly those; an empty value turns all level highlighting off.",
            [ new ClientToolParameter("levels", "Comma-separated levels, e.g. \"error,warning\"; empty turns all off.", Required: false) ],
            ToolSafety.SafeOperation,
            async (args, _) =>
            {
                var raw    = ToolArgs.Str(args, "levels", "level") ?? string.Empty;
                var wanted = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Select(x => x.ToLowerInvariant()).ToHashSet();
                await _shell.RunOnUiAsync(() =>
                {
                    HighlightFatal   = wanted.Contains("fatal")   || wanted.Contains("critical");
                    HighlightError   = wanted.Contains("error")   || wanted.Contains("err");
                    HighlightWarning = wanted.Contains("warning") || wanted.Contains("warn");
                    HighlightInfo    = wanted.Contains("info");
                    HighlightDebug   = wanted.Contains("debug")   || wanted.Contains("trace");
                });
                var on = EnabledHighlightLevels();
                return ToolResult.Ok(
                    on.Count > 0 ? $"highlighting {string.Join(", ", on)}" : "highlighting off",
                    on.Count > 0 ? $"Highlighting levels: {string.Join(", ", on)}." : "Turned off all level highlighting.");
            }),

        new DelegateClientTool(
            "set_tail_paused",
            "Pause or resume the live tail (whether newly-appended lines are shown as the file grows). Resuming "
          + "shows lines that arrived while paused. Display only — the file is untouched.",
            [ new ClientToolParameter("paused", "true to pause, false to resume.", Type: "boolean") ],
            ToolSafety.SafeOperation,
            async (args, _) =>
            {
                var paused = ToolArgs.Bool(args, "paused", true);
                await _shell.RunOnUiAsync(() => IsPaused = paused);
                return ToolResult.Ok(paused ? "tail paused" : "tail resumed",
                    paused ? "Paused the live tail." : "Resumed the live tail.");
            }),
    ];

    // Runs a tool body that reads what is shown on the workspace UI thread, which shows it, returning its result — features never touch
    // the dispatcher directly.
    private Task<ToolResult> OnUi(Func<ToolResult> read) => _shell.RunOnUiAsync(() => Task.FromResult(read()));

    /// <summary>Lines <paramref name="startLine"/> to <paramref name="endLine"/> of the whole log, from one, each after its number.</summary>
    private string ReadLineRange(long startLine, long endLine)
    {
        var sb = new StringBuilder();
        var number = startLine;
        foreach (var text in _shown.LineTexts(startLine - 1, (int)(endLine - startLine + 1)))
            sb.Append(number++).Append(": ").Append(text).Append('\n');
        return sb.ToString();
    }

    private List<string> EnabledHighlightLevels()
    {
        var levels = new List<string>(5);
        if (HighlightFatal)   levels.Add("Fatal");
        if (HighlightError)   levels.Add("Error");
        if (HighlightWarning) levels.Add("Warning");
        if (HighlightInfo)    levels.Add("Info");
        if (HighlightDebug)   levels.Add("Debug");
        return levels;
    }

    public IContext? GetContextObject()
    {
        if (string.IsNullOrEmpty(FilePath)) return null;
        var dir = Path.GetDirectoryName(FilePath);
        if (string.IsNullOrEmpty(dir)) return null;
        return new FileSystemContext
        {
            RootPath      = dir,
            CurrentPath   = dir,
            SelectedItems = [FilePath]
        };
    }
}
