using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Nexaflow.Services.Initiatives.Graph.Model;

namespace Nexaflow.Services.Initiatives.Graph;

/// <summary>
/// The answers nfi has given in one checkout, numbered in one sequence, so that what comes next can start from one rather
/// than doing the work again.
/// <para>
/// A <see cref="GraphAsk"/> answer is kept as the question that produced it, with any <c>@</c> in it spelled out, and the
/// nodes it found, so a question can start from it — <c>@</c> for the last, <c>@3</c> for the third — instead of asking it
/// again with a stage added. When nothing those nodes were read from has changed since, they are reused as they are; when
/// something has, the question is asked again, so an answer can be continued but never goes stale.
/// </para>
/// <para>
/// An answer too long to print whole — a question's, an edit's diff, a build's errors, a test run's failures — is kept as
/// text, every line of it with how far it has been read, so the rest of it is <c>nfi more @7</c> (or <c>ask '@7 more'</c>),
/// or a search of it, rather than the edit, the build or the run done again.
/// </para>
/// <para>
/// Kept in memory by the resident process: the last <see cref="Kept"/> answers, and of their text no more than
/// <see cref="KeptChars"/> characters, the oldest going first.
/// </para>
/// </summary>
/// <param name="writeTime">When a repo-relative file was last written, or null when it is not there.</param>
public sealed class AnswerHistory(Func<string, DateTime?> writeTime)
{
    /// <summary>How many answers are kept. A session refers back a few answers, rarely more.</summary>
    private const int Kept = 50;

    /// <summary>How much kept text there may be across every answer, in characters — a few dozen build logs.</summary>
    private const long KeptChars = 16L * 1024 * 1024;

    /// <summary>Lines shown either side of each match when a kept answer is searched.</summary>
    private const int Around = 2;

    /// <summary>Past this many files an answer is not stamped, and is asked again whenever it is continued: stat-ing
    /// thousands of files to spare a search is a poor trade.</summary>
    private const int StampedFiles = 400;

    private readonly List<Entry> _entries = [];
    private readonly Dictionary<int, PrintedAnswer> _printed = [];
    private readonly Queue<int> _numbers = new();
    private readonly object _gate = new();
    private int _last;
    private long _chars;

    /// <summary>One question's answer: its number, the question in full, what it found, and when the files it found were written.</summary>
    internal sealed record Entry(int Number, string Question, IReadOnlyList<(string NodeId, List<(int Line, string Text)> Lines)> Found,
                                 IReadOnlyDictionary<string, DateTime?>? Written);

    /// <summary>An answer kept as text: what it answered, every line of it, how many have been read, and the tally it ends on.</summary>
    internal sealed record PrintedAnswer(string Label, IReadOnlyList<string> Lines, int Shown, string Tally);

    /// <summary>The oldest and newest answer numbers still kept — 0 and 0 before the first.</summary>
    internal (int First, int Last) Range
    {
        get { lock (_gate) return (_numbers.Count == 0 ? 0 : _numbers.Peek(), _last); }
    }

    internal int Add(string question, IReadOnlyList<(GraphNode Node, List<(int Line, string Text)> Lines)> found)
    {
        var files   = found.Select(f => f.Node.FilePath).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var written = files.Count <= StampedFiles
            ? files.ToDictionary(f => f, f => writeTime(f), StringComparer.OrdinalIgnoreCase)
            : null;

        lock (_gate)
        {
            var entry = new Entry(++_last, question, [.. found.Select(f => (f.Node.Id, f.Lines))], written);
            _entries.Add(entry);
            Remember(entry.Number);
            return entry.Number;
        }
    }

    /// <summary>
    /// Keeps the whole of an answer that was printed folded — <paramref name="lines"/>, every one of them — as the next
    /// answer, and returns its number. <paramref name="label"/> says what it answered: the command, as it was given.
    /// </summary>
    public int Keep(string label, IReadOnlyList<string> lines)
    {
        lock (_gate)
        {
            var number = ++_last;
            Store(number, new PrintedAnswer(label, lines, 0, ""));
            Remember(number);
            return number;
        }
    }

    /// <summary>Answer <paramref name="number"/>, or the last one when it is null.</summary>
    internal Entry? Get(int? number)
    {
        lock (_gate) return number is null ? _entries.LastOrDefault() : _entries.FirstOrDefault(e => e.Number == number);
    }

    /// <summary>Whether every file the answer was found in is as it was when it was found.</summary>
    internal bool IsCurrent(Entry entry) =>
        entry.Written is { } written && written.All(file => writeTime(file.Key) == file.Value);

    /// <summary>Keeps what question <paramref name="number"/> printed, <paramref name="shown"/> lines of it so far.</summary>
    internal void Keep(int number, IReadOnlyList<string> lines, int shown, string tally)
    {
        lock (_gate)
        {
            if (_entries.Find(e => e.Number == number) is not { } entry) return;
            Store(number, new PrintedAnswer(entry.Question, lines, shown, tally));
            Evict();
        }
    }

    /// <summary>What answer <paramref name="number"/> printed, when it was too long to print whole.</summary>
    internal PrintedAnswer? PageOf(int number)
    {
        lock (_gate) return _printed.GetValueOrDefault(number);
    }

    /// <summary>Records that answer <paramref name="number"/> has now been shown up to line <paramref name="shown"/>.</summary>
    internal void Advance(int number, int shown)
    {
        lock (_gate)
            if (_printed.TryGetValue(number, out var printed)) _printed[number] = printed with { Shown = shown };
    }

    /// <summary>
    /// Kept answer <paramref name="number"/> read back — the newest kept one when it is null: the lines matching
    /// <paramref name="search"/> with <see cref="Around"/> either side; or the whole of it when <paramref name="whole"/>;
    /// or else its next part, no longer than <paramref name="budget"/> characters. Not ok when there is no such answer.
    /// </summary>
    public (string Text, bool Ok) Read(int? number, Regex? search, bool whole, int budget)
    {
        int n;
        PrintedAnswer? kept;
        lock (_gate)
        {
            n    = number ?? (_printed.Count == 0 ? 0 : _printed.Keys.Max());
            kept = _printed.GetValueOrDefault(n);
        }
        if (kept is null) return (Missing(number), false);

        var stale = Get(n) is { } entry && !IsCurrent(entry)
            ? "note: something this answer was found in has changed since - this is the answer as it was printed; ask it again for how it is now.\n"
            : "";
        var tally = kept.Tally.Length > 0 ? $"{kept.Tally}   @{n}" : $"@{n}";

        if (search is not null) return (stale + Matches(n, kept, search, budget) + tally, true);
        if (whole) return ($"{stale}{string.Join('\n', kept.Lines)}\n{tally}", true);

        if (kept.Shown >= kept.Lines.Count)
            return ($"all of @{n} has been read - --all prints it again, --grep <regex> searches it.   @{n}", true);

        var (page, next) = Page(kept.Lines, kept.Shown, budget);
        Advance(n, next);
        var left = kept.Lines.Count - next;
        return ($"{stale}{page}{(left > 0 ? $"… {left} more line(s): nfi more @{n}\n" : "")}{tally}", true);
    }

    /// <summary>
    /// The lines from <paramref name="from"/> that fit in <paramref name="budget"/> characters, ending where a block starts
    /// when it can, and the line the next part begins at.
    /// </summary>
    internal static (string Page, int Next) Page(IReadOnlyList<string> lines, int from, int budget)
    {
        var end  = from;
        var used = 0;
        while (end < lines.Count && (end == from || used + lines[end].Length + 1 <= budget))
            used += lines[end++].Length + 1;

        if (end < lines.Count)
        {
            // A block starts at a header: `// id`, or a line that is not indented (a file a group of ids is under).
            var floor = from + (end - from) * 2 / 3;
            for (var i = end; i > floor; i--)
                if (lines[i].StartsWith("// ", StringComparison.Ordinal) || (lines[i].Length > 0 && lines[i][0] != ' ' && !char.IsDigit(lines[i][0])))
                {
                    end = i;
                    break;
                }
        }

        var sb = new StringBuilder();
        for (var i = from; i < end; i++) sb.Append(lines[i]).Append('\n');
        return (sb.ToString(), end);
    }

    /// <summary>Every line of <paramref name="kept"/> matching <paramref name="search"/>, numbered, with the lines around it.</summary>
    private static string Matches(int number, PrintedAnswer kept, Regex search, int budget)
    {
        var lines = kept.Lines;
        var hits  = Enumerable.Range(0, lines.Count).Where(i => search.IsMatch(lines[i])).ToList();
        if (hits.Count == 0) return $"nothing in @{number} ({lines.Count} line(s), {kept.Label}) matches {search}.\n";

        var sb    = new StringBuilder($"// @{number}  {kept.Label} - {hits.Count} of its {lines.Count} line(s) match {search}\n");
        var shown = 0;
        var last  = -1;
        foreach (var hit in hits)
        {
            var from = Math.Max(Math.Max(0, hit - Around), last + 1);
            var to   = Math.Min(lines.Count - 1, hit + Around);
            if (sb.Length > budget) break;
            if (last >= 0 && from > last + 1) sb.Append("     ⋮\n");
            for (var i = from; i <= to; i++) sb.Append($"{i + 1,6}  {lines[i]}\n");
            last = to;
            shown++;
        }
        if (shown < hits.Count)
            sb.Append($"… {hits.Count - shown} more match(es) not shown - narrow the pattern, or nfi more @{number} --all.\n");
        return sb.ToString();
    }

    /// <summary>Why there is nothing to read back for <paramref name="number"/>.</summary>
    private string Missing(int? number)
    {
        var (oldest, newest) = Range;
        if (number is null) return "no answer is kept here yet - an answer is kept when it is too long to print whole.";
        if (Get(number) is not null) return $"@{number} printed all of itself - there is no more of it.";
        return newest == 0 ? $"there is no answer @{number} - none has been given here yet."
                           : $"there is no answer @{number} - the ones kept are @{oldest} to @{newest}.";
    }

    /// <summary>Puts <paramref name="printed"/> under <paramref name="number"/>, counting its text against the cap.</summary>
    private void Store(int number, PrintedAnswer printed)
    {
        if (_printed.TryGetValue(number, out var before)) _chars -= CharsOf(before.Lines);
        _printed[number] = printed;
        _chars += CharsOf(printed.Lines);
    }

    /// <summary>Counts a new answer, letting the oldest go past either cap.</summary>
    private void Remember(int number)
    {
        _numbers.Enqueue(number);
        Evict();
    }

    /// <summary>Lets the oldest answers go until both caps hold — never the newest, however long it is.</summary>
    private void Evict()
    {
        while (_numbers.Count > Kept || (_chars > KeptChars && _numbers.Count > 1))
        {
            var oldest = _numbers.Dequeue();
            _entries.RemoveAll(e => e.Number == oldest);
            if (_printed.Remove(oldest, out var printed)) _chars -= CharsOf(printed.Lines);
        }
    }

    private static long CharsOf(IReadOnlyList<string> lines) => lines.Sum(l => (long)l.Length + 1);
}