using System.Text;
using System.Text.RegularExpressions;

using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Markdown.Editing;
using Nexaflow.Search;

namespace Nexaflow.Features.Text.ViewModels;

/// <summary>
/// What the assistant's tools ask of the page (<see cref="ClientTools.TextEditorTools"/>): lines of the whole file read, found and written —
/// each on the page's own thread, through the same document the reader writes in, so what the assistant does shows on the page, can be
/// taken back, and is saved as the reader's edits are.
/// </summary>
public sealed partial class TextViewModel
{
    internal IShellServices Shell => _shell;

    /// <summary>How many lines the whole file comes to, as it is written now.</summary>
    internal long TotalLines => _shown.LineCount;

    /// <summary>
    /// Up to <paramref name="count"/> lines of the whole file from <paramref name="startLine"/> (from one), as written now, each after its
    /// number and a tab.
    /// </summary>
    internal Task<string> ReadNumberedLinesAsync(int startLine, int count, CancellationToken ct) =>
        _shell.RunOnUiAsync(() =>
        {
            var first = Math.Max(1, startLine) - 1L;
            var text = new StringBuilder();
            if (_shown.OffsetOfLine(first) is not { } start) return Task.FromResult(string.Empty);

            var end = _shown.OffsetOfLine(first + count) ?? _shown.Length;
            var lines = _shown.Text(start, (int)Math.Min(int.MaxValue, end - start)).Split('\n');

            for (var at = 0; at < lines.Length && at < count; at++)
            {
                if (at == lines.Length - 1 && lines[at].Length == 0 && first + at >= _shown.LineCount) break;
                text.Append(first + at + 1).Append('\t').Append(lines[at].TrimEnd('\r')).Append('\n');
            }

            return Task.FromResult(text.ToString());
        });

    /// <summary>
    /// Finds <paramref name="query"/> in the whole file as written now, for the assistant — the lines it is on, each with what is written on
    /// it — and shows the same search on the page, so the reader sees what the assistant found.
    /// </summary>
    internal async Task<string> ToolFindAsync(string query, bool regex, bool caseSensitive, int max, CancellationToken ct)
    {
        await await _shell.RunOnUiAsync(() => Task.FromResult(regex ? SearchRegexAsync(query, ct) : SearchConventionalAsync(query, ct)));

        var request = new SearchRequest(query, IsRegex: regex, MatchCase: caseSensitive);
        var found = await await _shell.RunOnUiAsync(() => Task.FromResult(_find.ReadAsync(QueryOf(request), max, ct)));

        if (found.Lines.Length == 0) return "No matches.";

        var text = new StringBuilder();
        foreach (var (line, written) in found.First) text.Append("line ").Append(line + 1).Append(": ").Append(written).Append('\n');

        return $"{found.Lines.Length} match(es). Showing {found.First.Count}:\n{text}";
    }

    /// <summary>
    /// Writes <paramref name="newText"/> in place of lines <paramref name="startLine"/> to <paramref name="endLine"/> of the whole file
    /// (from nought, both kept), or before <paramref name="startLine"/> where <paramref name="endLine"/> comes before it — one step to take back.
    /// False, where the lines reach further than one window onto a file too long for one document holds.
    /// </summary>
    internal Task<bool> ApplyAiLineEditAsync(long startLine, long endLine, string newText, CancellationToken ct) =>
        _shell.RunOnUiAsync(() =>
        {
            var start = _shown.OffsetOfLine(Math.Clamp(startLine, 0, _shown.LineCount)) ?? _shown.Length;
            var end = endLine < startLine ? start : _shown.OffsetOfLine(endLine + 1) ?? _shown.Length;

            if (_window is { } window)
            {
                if (end - start > window.Size / 2) return Task.FromResult(false);
                window.Reach(start, end);
            }

            var from = (int)(start - (_window?.Start ?? 0));
            Document.Write(ContentChange.Write(from, (int)(end - start), newText));
            return Task.FromResult(true);
        });

    /// <summary>
    /// Replaces <paramref name="pattern"/> — a regular expression where <paramref name="regex"/> says, otherwise the characters as written — on
    /// lines <paramref name="fromLine"/> to <paramref name="toLine"/> of the whole file, from nought; how many it replaced.
    /// </summary>
    internal Task<int> ApplyAiReplaceAsync(string pattern, string replacement, bool regex, bool caseSensitive, long fromLine, long toLine, CancellationToken ct) =>
        _shell.RunOnUiAsync(() =>
        {
            var expression = regex ? new Regex(pattern, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase) : null;
            var compared = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            IEnumerable<(int Index, int Length, string With)> Replacements(string text)
            {
                if (expression is not null)
                {
                    foreach (Match match in expression.Matches(text)) yield return (match.Index, match.Length, match.Result(replacement));
                    yield break;
                }

                for (var at = text.IndexOf(pattern, compared); at >= 0 && pattern.Length > 0; at = text.IndexOf(pattern, at + pattern.Length, compared))
                    yield return (at, pattern.Length, replacement);
            }

            return Task.FromResult(ReplaceAcross(fromLine, toLine, Replacements));
        });

    internal async Task SaveFromToolAsync()
    {
        if (SaveCommand.CanExecute(null))
            await _shell.RunOnUiAsync(() => SaveCommand.ExecuteAsync(null));
    }
}
