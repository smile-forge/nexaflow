using System;
using System.Collections.Generic;
using System.Threading;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What is slow to work out, worked out away from the thread that lays content out: a language's second reading
/// (<see cref="ContentLanguage.SlowParser"/> — code read by its grammar), and what a slow stage finds (<see cref="ISlowStage"/> —
/// a spelling checked).
///
/// <para>
/// <strong>What is slow is asked for, never waited on.</strong> Where it is known, it is used; where it is not, the content is
/// laid without it, the work is started, and every engine that asked for it is told when it lands (<see cref="Reread"/>) —
/// whatever shows the content moves to its own thread and lays it again. What landed is kept for every engine by the language,
/// the stage and the characters, a few hundred deep, so content shown again is shown as it was left.
/// </para>
/// </summary>
public sealed partial class ContentEngine
{
    /// <summary>How many second readings are kept. A document holds a handful of fences and a reader a few documents.</summary>
    private const int SlowKept = 256;

    private static readonly Lock Slow = new();
    private static readonly Dictionary<Kept, object> Landed = [];
    private static readonly Queue<Kept> LandedOrder = new();

    /// <summary>What slow work is kept by: the language, the stage whose finding it is (none, for a second reading), and the characters.</summary>
    private readonly record struct Kept(ContentLanguage Language, string? Stage, string Text);

    /// <summary>Slow work under way, with every engine to tell when each lands — held only while it is worked out.</summary>
    private static readonly Dictionary<Kept, List<ContentEngine>> Waiting = [];

    /// <summary>
    /// Raised, on any thread, when a second reading of something this engine read has landed. Whatever shows the content moves to
    /// its own thread and lays it again (<see cref="Refresh"/>).
    /// </summary>
    internal event EventHandler? Reread;

    /// <summary>How many second readings this engine has asked for and has not yet been told the end of.</summary>
    private int _reading;

    /// <summary>
    /// Whether a second reading this content asked for is still to land — code fenced in a grammar, a spelling being
    /// checked. False once every reading asked for has come back, whether or not it came to anything.
    ///
    /// <para>
    /// What <see cref="Reread"/> cannot say on its own: that event says one reading landed, not whether more are
    /// coming. Something that must act on the content as it finally reads — measuring it, printing it, photographing
    /// it, comparing it with the same source laid afresh — waits on this rather than counting events.
    /// </para>
    /// </summary>
    public bool Rereading { get { lock (Slow) return _reading > 0; } }

    /// <summary>
    /// <paramref name="text"/>, read: by the language's second reading where it has landed, and otherwise by
    /// <paramref name="parse"/> — starting the second reading where the language has one and nobody has asked for it yet.
    /// </summary>
    private ContentParse Read(ContentLanguage language, Func<string, ContentParse> parse, string text) =>
        language.SlowParser is { } slow && Later(new(language, null, text), () => slow(text)) is ContentParse landed ? landed : parse(text);

    /// <summary>
    /// A slow stage, where it stands among the stages <paramref name="read"/> is worked over by: what it found hung on the tree where
    /// that has landed, and otherwise the tree as it is, with the finding started.
    /// </summary>
    private IAstStage Staging(Parsed read, ISlowStage slow) =>
        new AstStage(slow.Name, tree => Later(new(read.Language, slow.Name, read.Text), () => slow.Find(tree)) is { } found ? slow.Apply(tree, found) : tree);

    /// <summary>
    /// What the slow work kept by <paramref name="key"/> came to, where it has landed — or null, starting <paramref name="work"/>
    /// where nobody has, and saying so to this engine when it lands.
    /// </summary>
    private object? Later(Kept key, Func<object> work)
    {
        lock (Slow)
        {
            if (Landed.TryGetValue(key, out var landed)) return landed;

            if (Waiting.TryGetValue(key, out var waiting))
            {
                if (!waiting.Contains(this)) { waiting.Add(this); _reading++; }
                return null;
            }

            Waiting[key] = [this];
            _reading++;
        }

        ThreadPool.QueueUserWorkItem(static job => Land(job.Key, job.Work), (Key: key, Work: work), preferLocal: false);
        return null;
    }

    /// <summary>Does the slow work kept by <paramref name="key"/>, keeps what it came to, and tells every engine that asked for it.</summary>
    private static void Land(Kept key, Func<object> work)
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
            // failed and there is nothing new to tell it about.
            foreach (var engine in told ?? []) engine._reading--;

            if (done is null) return;

            Landed[key] = done;
            LandedOrder.Enqueue(key);

            while (LandedOrder.Count > SlowKept && LandedOrder.TryDequeue(out var oldest)) Landed.Remove(oldest);
        }

        foreach (var engine in told ?? []) engine.Reread?.Invoke(engine, EventArgs.Empty);
    }
}
