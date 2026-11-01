using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Syntax;

using TreeSitter;

namespace Nexaflow.Visuals.Text.Markdown.Code;

/// <summary>
/// A code reading's syntax (<see cref="IReadingSyntax"/>): tree-sitter's parse of all of the reading, kept beside its units and carried
/// through every change, and what each stretch of the reading is coloured as in it.
///
/// <para>
/// <strong>A version is an edit, not a tree.</strong> Every change makes another version, which remembers the edit that made it from the
/// one before — where, and the rows and columns tree-sitter wants with it, from the reading's line index. Nothing native is made per
/// keystroke: a version is parsed only when something asks what a stretch of it is coloured as, and then from the last tree parsed,
/// copied with every edit since applied to the copy, so tree-sitter reads again only what the edits reached (<see cref="Find"/>).
/// </para>
/// <para>
/// <strong>A long reading is parsed again once typing pauses.</strong> Handing tree-sitter the reading's characters is one string per
/// parse, so a reading past <see cref="PausedFrom"/> waits <see cref="Pause"/> and is not parsed where a later version has been made
/// meanwhile. Until a stretch's colours land, what it was last found coloured as is moved along by every change since, the stretches a
/// change wrote over left uncoloured (<see cref="Known"/>).
/// </para>
/// </summary>
internal sealed class CodeSyntax : IReadingSyntax
{
    /// <summary>How long a reading has to be before it is parsed again only once typing pauses.</summary>
    internal const int PausedFrom = 128 * 1024;

    /// <summary>How long typing has to pause before a long reading is parsed again.</summary>
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(250);

    private readonly CodeHistory _history;

    private CodeSyntax(CodeHistory history, int version, IContentCharacters characters) =>
        (_history, this.Version, this.Characters) = (history, version, characters);

    /// <summary>The syntax of a reading of <paramref name="characters"/> in <paramref name="grammar"/>, nothing parsed yet.</summary>
    public static CodeSyntax Of(string grammar, IContentCharacters characters) => new(new CodeHistory(grammar), 0, characters);

    /// <summary>Which version of the reading this is.</summary>
    public int Version { get; }

    /// <summary>The reading's characters at this version.</summary>
    public IContentCharacters Characters { get; }

    /// <summary>The lines the reading was last cut into, kept for every version of it (<see cref="CodeLinePool"/>).</summary>
    public CodeLinePool Lines => _history.Lines;

    /// <summary>Whether <paramref name="other"/> is a version of the same reading.</summary>
    public bool Shares(CodeSyntax other) => ReferenceEquals(_history, other._history);

    public IReadingSyntax Edited(int from, int to, int by, IContentCharacters edited)
    {
        var edit = new CodeEdit(from, to, to + by, Point(this.Characters, from), Point(this.Characters, to), Point(edited, to + by));
        return new CodeSyntax(_history, _history.Edited(this.Version, edit), edited);
    }

    /// <summary>
    /// What the stretch from <paramref name="start"/> to <paramref name="end"/> of this version is coloured as — parsing this version
    /// first where nothing has — with what that parse changed from the last one; or null, for a long reading a later version replaced
    /// before typing paused. Slow: called away from the thread that lays content out.
    /// </summary>
    public CodeFound? Find(int start, int end)
    {
        if (this.Characters.Length >= PausedFrom)
        {
            Thread.Sleep(Pause);
            if (_history.Latest != this.Version) return null;
        }

        return _history.Find(this, start, end);
    }

    /// <summary>
    /// What the stretch from <paramref name="start"/> to <paramref name="end"/> of this version was last found coloured as, in any
    /// version before it, moved along by every change since — what a change wrote over left out. Quick: no parse, nothing native.
    /// </summary>
    public IReadOnlyList<HighlightSpan> Known(int start, int end) => _history.Known(this.Version, start, end);

    /// <summary>The stretches <paramref name="changed"/>, found in <paramref name="version"/>, where they stand in this version — none, where this does not follow from it.</summary>
    public IEnumerable<(int Start, int End)> Since(int version, IReadOnlyList<(int Start, int End)> changed) => _history.Mapped(version, this.Version, changed);

    /// <summary>Where <paramref name="offset"/> is in <paramref name="characters"/> as tree-sitter counts it: its row, and how far along the row.</summary>
    private static (int Row, int Column) Point(IContentCharacters characters, int offset)
    {
        var row = characters.LineOf(offset);
        return (row, offset - characters.OffsetOf(row));
    }

    /// <summary>
    /// What every version of one reading shares: the edit each was made by, the last tree parsed and which version it is, and what
    /// stretches were last found coloured as.
    /// </summary>
    private sealed class CodeHistory(string grammar)
    {
        /// <summary>How many stretches' colours are kept, newest first, for colouring a stretch until its own land.</summary>
        private const int FoundKept = 64;

        private readonly Lock _gate = new();

        /// <summary>Each version made by an edit: the version it was made from, and the edit.</summary>
        private readonly Dictionary<int, (int Before, CodeEdit Edit)> _edits = [];

        /// <summary>What stretches were found coloured as, newest first.</summary>
        private readonly List<(int Version, int Start, int End, IReadOnlyList<HighlightSpan> Spans)> _found = [];

        private int _next = 1;

        /// <summary>The last tree parsed, and which version it is — the one the next parse starts from.</summary>
        private (Tree Tree, int Version)? _parsed;

        /// <summary>The newest version made.</summary>
        public int Latest { get; private set; }

        /// <summary>The lines the reading was last cut into.</summary>
        public CodeLinePool Lines { get; } = new();

        /// <summary>Makes the version <paramref name="edit"/> makes of <paramref name="before"/>.</summary>
        public int Edited(int before, CodeEdit edit)
        {
            lock (_gate)
            {
                var version = _next++;
                _edits[version] = (before, edit);
                Latest = version;

                return version;
            }
        }

        public CodeFound? Find(CodeSyntax syntax, int start, int end) => CodeSpans.WithReader(grammar, reader =>
        {
            (Tree Tree, int Version)? parsed;
            lock (_gate) parsed = _parsed;

            // A version older than the last parsed was replaced: what it is coloured as is not worth finding.
            if (parsed is { } last && last.Version > syntax.Version) return null;

            IReadOnlyList<(int Start, int End)> changed = [];
            Tree tree;

            if (parsed is { } same && same.Version == syntax.Version) tree = same.Tree;
            else
            {
                var old = parsed is { } from && this.Edits(from.Version, syntax.Version) is { } edits ? Edited(from.Tree, edits) : null;

                if (reader.Parse(syntax.Characters.Slice(0, syntax.Characters.Length), old) is not { } made)
                {
                    old?.Dispose();
                    return null;
                }

                if (old is not null)
                {
                    changed = [.. old.GetChangedRanges(made).Select(range => (range.StartIndex, range.EndIndex))];
                    old.Dispose();
                }

                lock (_gate)
                {
                    // Every tree is read under the reader's lock, which this holds, so the one replaced is read by nothing now.
                    _parsed?.Tree.Dispose();
                    _parsed = (made, syntax.Version);
                    this.Forget(syntax.Version);
                }

                tree = made;
            }

            var spans = reader.Highlight(tree, tree.Source, start, end);

            lock (_gate)
            {
                _found.RemoveAll(found => found.Version <= syntax.Version && found.Start >= start && found.End <= end);
                _found.Insert(0, (syntax.Version, start, end, spans));
                if (_found.Count > FoundKept) _found.RemoveRange(FoundKept, _found.Count - FoundKept);
            }

            return new CodeFound(syntax, spans, changed);
        });

        public IReadOnlyList<HighlightSpan> Known(int version, int start, int end)
        {
            var spans = new List<HighlightSpan>();
            var covered = new List<(int Start, int End)>();

            lock (_gate)
            {
                foreach (var (was, from, to, found) in _found)
                {
                    if (was > version || this.Edits(was, version) is not { } edits) continue;

                    // Where the stretch found then stands now; a newer finding already said what any part of it it covers is.
                    var (low, high) = (Through(from, edits, before: true), Through(to, edits, before: false));
                    if (high <= start || low >= end) continue;

                    foreach (var span in found)
                    {
                        if (Carried(span.Start, span.Start + span.Length, edits) is not { } carried) continue;

                        var (at, past) = carried;

                        var (cutLow, cutHigh) = (Math.Max(at, start), Math.Min(past, end));
                        if (cutHigh > cutLow && !covered.Any(taken => taken.Start <= cutLow && cutHigh <= taken.End))
                            spans.Add(new HighlightSpan(cutLow, cutHigh - cutLow, span.Capture));
                    }

                    covered.Add((low, high));
                }
            }

            // In the order found, so the later of two spans over the same characters still wins.
            return spans;
        }

        public IEnumerable<(int Start, int End)> Mapped(int from, int to, IReadOnlyList<(int Start, int End)> changed)
        {
            List<CodeEdit>? edits;
            lock (_gate) edits = this.Edits(from, to);

            if (edits is null) yield break;

            foreach (var (start, end) in changed)
                yield return (Through(start, edits, before: true), Math.Max(Through(end, edits, before: false), Through(start, edits, before: true)));
        }

        /// <summary>The edits that make <paramref name="to"/> from <paramref name="from"/>, in order — or null where it was not made from it.</summary>
        private List<CodeEdit>? Edits(int from, int to)
        {
            var edits = new List<CodeEdit>();

            for (var version = to; version != from;)
            {
                if (version < from || !_edits.TryGetValue(version, out var step)) return null;

                edits.Add(step.Edit);
                version = step.Before;
            }

            edits.Reverse();
            return edits;
        }

        /// <summary>Lets go of the edits no version still asked about needs: those before the oldest of the last tree parsed and the stretches found.</summary>
        private void Forget(int parsed)
        {
            var oldest = _found.Count == 0 ? parsed : Math.Min(parsed, _found.Min(found => found.Version));

            foreach (var version in _edits.Keys.Where(version => version <= oldest).ToList()) _edits.Remove(version);
        }

        /// <summary><paramref name="tree"/> copied, with <paramref name="edits"/> applied to the copy in order.</summary>
        private static Tree Edited(Tree tree, List<CodeEdit> edits)
        {
            var copy = tree.Copy();

            foreach (var edit in edits)
                copy.Edit(new Edit
                {
                    StartIndex = edit.Start,
                    OldEndIndex = edit.OldEnd,
                    NewEndIndex = edit.NewEnd,
                    StartPosition = new Point(edit.StartPoint.Row, edit.StartPoint.Column),
                    OldEndPosition = new Point(edit.OldEndPoint.Row, edit.OldEndPoint.Column),
                    NewEndPosition = new Point(edit.NewEndPoint.Row, edit.NewEndPoint.Column),
                });

            return copy;
        }

        /// <summary>
        /// Where the stretch from <paramref name="start"/> to <paramref name="end"/> stands after <paramref name="edits"/> — null where one of
        /// them wrote over or inside it. Written at its start, it is moved along; at its end, it stays.
        /// </summary>
        private static (int Start, int End)? Carried(int start, int end, List<CodeEdit> edits)
        {
            foreach (var edit in edits)
            {
                if (end <= edit.Start) continue;

                if (start < edit.OldEnd) return null;

                (start, end) = (start + edit.NewEnd - edit.OldEnd, end + edit.NewEnd - edit.OldEnd);
            }

            return (start, end);
        }

        /// <summary>Where <paramref name="offset"/> stands after <paramref name="edits"/>, an offset an edit wrote over taken to where it started (<paramref name="before"/>) or ended.</summary>
        private static int Through(int offset, List<CodeEdit> edits, bool before)
        {
            foreach (var edit in edits)
                offset = offset <= edit.Start ? offset
                       : offset >= edit.OldEnd ? offset + edit.NewEnd - edit.OldEnd
                       : before ? edit.Start : edit.NewEnd;

            return offset;
        }
    }
}

/// <summary>
/// An edit as tree-sitter is told it: the characters from <paramref name="Start"/> to <paramref name="OldEnd"/> written over, now
/// ending at <paramref name="NewEnd"/>, with the row and column of each.
/// </summary>
internal readonly record struct CodeEdit(int Start, int OldEnd, int NewEnd, (int Row, int Column) StartPoint, (int Row, int Column) OldEndPoint,
                                         (int Row, int Column) NewEndPoint);

/// <summary>
/// What a stretch of a code reading was found coloured as, in the version <paramref name="Of"/>, and what parsing that version changed
/// from the last one parsed (<paramref name="Changed"/>, empty for a parse that reused nothing): every unit those stretches reach is
/// coloured again, not only the one that asked (<see cref="ISlowFindingReaches"/>).
/// </summary>
internal sealed record CodeFound(CodeSyntax Of, IReadOnlyList<HighlightSpan> Spans, IReadOnlyList<(int Start, int End)> Changed) : ISlowFindingReaches
{
    public IEnumerable<(int Start, int End)> Reaches(IReadingSyntax now) =>
        now is CodeSyntax later && later.Shares(this.Of) && this.Changed.Count > 0 ? later.Since(this.Of.Version, this.Changed) : [];
}
