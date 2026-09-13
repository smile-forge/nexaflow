using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Services.Initiatives.Graph.Model;

namespace Nexaflow.Services.Initiatives.Graph;

/// <summary>
/// The answers <see cref="GraphAsk"/> has given, so a question can start from one — <c>@</c> for the last, <c>@3</c> for
/// the third — instead of asking it again with a stage added. Narrowing an answer is most of what a search is for, and
/// every narrowing used to repeat the whole search.
/// <para>
/// An answer is kept as the question that produced it, with any <c>@</c> in it spelled out, and the nodes it found. When
/// nothing those nodes were read from has changed since, they are reused as they are; when something has, the question
/// is asked again, so an answer can be continued but never goes stale.
/// </para>
/// </summary>
/// <param name="writeTime">When a repo-relative file was last written, or null when it is not there.</param>
public sealed class AnswerHistory(Func<string, DateTime?> writeTime)
{
    /// <summary>How many answers are kept. A session refers back a few questions, rarely more.</summary>
    private const int Kept = 50;

    /// <summary>Past this many files an answer is not stamped, and is asked again whenever it is continued: stat-ing
    /// thousands of files to spare a search is a poor trade.</summary>
    private const int StampedFiles = 400;

    private readonly List<Entry> _entries = [];
    private readonly object _gate = new();
    private int _last;

    /// <summary>One answer: its number, the question in full, what it found, and when the files it found were written.</summary>
    internal sealed record Entry(int Number, string Question, IReadOnlyList<(string NodeId, List<(int Line, string Text)> Lines)> Found,
                                 IReadOnlyDictionary<string, DateTime?>? Written);

    /// <summary>The oldest and newest answer numbers still kept — 0 and 0 before the first.</summary>
    internal (int First, int Last) Range
    {
        get { lock (_gate) return (_entries.Count == 0 ? 0 : _entries[0].Number, _last); }
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
            if (_entries.Count > Kept) _entries.RemoveAt(0);
            return entry.Number;
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
}
