using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What is slow to work out, worked out away from the thread that lays content out: what a slow stage finds (<see cref="ISlowStage"/> —
/// a stretch of code coloured from its reading's syntax, a spelling checked).
///
/// <para>
/// <strong>What is slow is asked for, never waited on.</strong> Where it is known, it is used; where it is not, the content is
/// laid without it, the work is started, and every engine that asked for it is told when it lands (<see cref="Reread"/>) —
/// whatever shows the content moves to its own thread and lays again the units that waited on it. What landed is kept for every engine
/// by the language, the stage, the characters and what else the stage turns on, a few hundred deep.
/// </para>
/// </summary>
public sealed partial class ContentEngine
{
    /// <summary>How many findings are kept. A document holds a handful of fences, a long one a unit for each screen read, and a reader a few documents.</summary>
    private const int SlowKept = 256;

    private static readonly Lock Slow = new();
    private static readonly Dictionary<Kept, object> Landed = [];
    private static readonly Queue<Kept> LandedOrder = new();

    /// <summary>
    /// What slow work is kept by: the language, the stage whose finding it is, the characters, and what else the stage says its finding
    /// turns on (<see cref="ISlowStage.Basis"/>).
    /// </summary>
    private readonly record struct Kept(ContentLanguage Language, string Stage, ContentDocument Text, object? Basis);

    /// <summary>Slow work under way, with every engine to tell when each lands — held only while it is worked out.</summary>
    private static readonly Dictionary<Kept, List<ContentEngine>> Waiting = [];

    /// <summary>
    /// Raised, on any thread, when slow work this engine asked for has landed. Whatever shows the content moves to its own thread and
    /// lays again what waited on it (<see cref="RelayLanded"/>).
    /// </summary>
    internal event EventHandler? Reread;

    /// <summary>How many pieces of slow work this engine has asked for and has not yet been told the end of.</summary>
    private int _reading;

    /// <summary>The unit being worked over or laid — whatever slow work it asks for and has to wait on, it is the one laid again when that lands.</summary>
    private ContentNode? _asking;

    /// <summary>Which units, by the tree each was read into, wait on each piece of slow work this engine asked for.</summary>
    private readonly Dictionary<Kept, List<ContentNode>> _waits = [];

    /// <summary>The units, by the tree each was read into, that slow work landed for since they were last laid — laid again wherever they stand.</summary>
    private readonly HashSet<ContentNode> _relanded = new(ReferenceEqualityComparer.Instance);

    /// <summary>The slow work this engine asked for that has been done since it last laid what waited on it, landed or failed.</summary>
    private readonly ConcurrentQueue<Kept> _landed = new();

    /// <summary>
    /// Whether slow work this content asked for is still to land — code coloured by its grammar, a spelling being checked.
    /// False once everything asked for has come back, whether or not it came to anything.
    ///
    /// <para>
    /// What <see cref="Reread"/> cannot say on its own: that event says one reading landed, not whether more are
    /// coming. Something that must act on the content as it finally reads — measuring it, printing it, photographing
    /// it, comparing it with the same source laid afresh — waits on this rather than counting events.
    /// </para>
    /// </summary>
    public bool Rereading { get { lock (Slow) return _reading > 0; } }

    /// <summary>
    /// <paramref name="slow"/> as the engine runs it over <paramref name="text"/>, read in <paramref name="language"/>: what it found
    /// there, once it has landed, applied — and until then, the tree as it was.
    /// </summary>
    private IAstStage Staging(ContentLanguage language, ContentDocument text, ISlowStage slow) =>
        new AstStage(slow.Name, tree => Later(new(language, slow.Name, text, slow.Basis), () => slow.Find(tree)) is { } found ? slow.Apply(tree, found) : slow.Meanwhile(tree));

    /// <summary>
    /// What the slow work kept by <paramref name="key"/> came to, where it has landed — or null, starting <paramref name="work"/>
    /// where nobody has, and saying so to this engine when it lands.
    /// </summary>
    private object? Later(Kept key, Func<object?> work)
    {
        lock (Slow)
        {
            if (Landed.TryGetValue(key, out var landed)) return landed;

            if (Waiting.TryGetValue(key, out var waiting))
            {
                if (!waiting.Contains(this)) { waiting.Add(this); _reading++; }
                this.Waits(key);
                return null;
            }

            Waiting[key] = [this];
            _reading++;
        }

        this.Waits(key);
        ThreadPool.QueueUserWorkItem(static job => Land(job.Key, job.Work), (Key: key, Work: work), preferLocal: false);
        return null;
    }

    /// <summary>Remembers that the unit being worked over or laid, if any, waits on the slow work kept by <paramref name="key"/>.</summary>
    private void Waits(Kept key)
    {
        if (_asking is not { } unit) return;

        if (!_waits.TryGetValue(key, out var asked)) _waits[key] = asked = [];
        if (!asked.Contains(unit)) asked.Add(unit);
    }

    /// <summary>
    /// Lays again what the slow work that has landed since was asked for by, and nothing else: each unit that asked, and each unit what
    /// it found reaches, worked over and laid afresh.
    /// </summary>
    internal void RelayLanded()
    {
        if (MarkLanded()) Relay();
    }

    /// <summary>
    /// Says of every unit the slow work that has landed since was asked for by, and of every unit what it found reaches, that it is to
    /// be worked over and laid again where it is next laid — and whether anything had landed. What <see cref="RelayLanded"/> lays.
    /// </summary>
    internal bool MarkLanded()
    {
        var landed = false;

        while (_landed.TryDequeue(out var key))
        {
            landed = true;

            // What finding it changed past the units that asked — code parsed again — is found again where those units are worked over.
            object? found;
            lock (Slow) Landed.TryGetValue(key, out found);
            if (found is ISlowFindingReaches reaching && _readingSyntax is { } now) this.Unstaged(reaching.Reaches(now));

            if (!_waits.Remove(key, out var asked)) continue;

            foreach (var read in asked)
            {
                if (!_staged.TryGetValue(read, out var staged)) continue;

                _unchanged.Forget(staged.Tree, static _ => true);
                staged.Laid = null;
                staged.Stale = true;
                _relanded.Add(read);
            }
        }

        return landed;
    }

    /// <summary>Each unit worked over that <paramref name="stretches"/> of the reading laid reach, worked over and laid again wherever it stands.</summary>
    private void Unstaged(IEnumerable<(int Start, int End)> stretches)
    {
        foreach (var (start, end) in stretches)
        {
            var first = this.Holding(start);
            if (first < 0) continue;

            for (var index = first; index < _units.Count && (index == first || _units[index].Start < end); index++)
            {
                var read = _units[index].Unit.Tree;
                if (!_staged.TryGetValue(read, out var staged)) continue;

                _unchanged.Forget(staged.Tree, static _ => true);
                staged.Laid = null;
                staged.Stale = true;
                _relanded.Add(read);
            }
        }
    }

    /// <summary>Does the slow work kept by <paramref name="key"/>, keeps what it came to, and tells every engine that asked for it.</summary>
    private static void Land(Kept key, Func<object?> work)
    {
        object? done = null;

        try
        {
            done = work();
        }
        catch
        {
            // Slow work that fails leaves the content as it was laid without it, which is what was drawn a moment ago.
        }

        List<ContentEngine>? told;

        lock (Slow)
        {
            Waiting.Remove(key, out told);

            // Asked for and answered, whatever it came to: an engine is no longer reading this even where the work
            // failed and there is nothing new to tell it about — nor waiting on it.
            foreach (var engine in told ?? [])
            {
                engine._reading--;
                engine._landed.Enqueue(key);
            }

            if (done is null) return;

            Landed[key] = done;
            LandedOrder.Enqueue(key);

            while (LandedOrder.Count > SlowKept && LandedOrder.TryDequeue(out var oldest)) Landed.Remove(oldest);
        }

        foreach (var engine in told ?? []) engine.Reread?.Invoke(engine, EventArgs.Empty);
    }
}
