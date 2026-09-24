using System;
using System.Collections.Generic;
using System.Threading;

using Nexaflow.Syntax;

namespace Nexaflow.Visuals.Text.Markdown.Code;

/// <summary>
/// What a grammar makes of a stretch of code — asked for, never waited on.
///
/// <para>
/// <strong>Colouring is never on the way to drawing.</strong> A document re-lays on every keystroke, and
/// compiling a grammar's highlight query costs about fifteen milliseconds the first time it is wanted. So
/// this answers with what it already knows and nothing else: where it knows the reading, the code draws
/// coloured; where it does not, the code draws in one colour exactly as it always has, and the reading is
/// worked out behind the door. When it lands, <see cref="Ready"/> says so and whoever is showing it lays
/// again — the same refresh a ticked item and an opened node already use.
/// </para>
/// <para>
/// One reader per grammar, kept for the life of the process, because the fifteen milliseconds is the
/// <em>query</em> compiling and it compiles once per reader. A parse against a reader already made is
/// microseconds. A reader is not safe to use from two threads at once, so each is used under its own lock.
/// </para>
/// </summary>
internal static class CodeSpans
{
    /// <summary>
    /// How many readings are kept. A document holds a handful of fences and a reader holds a few documents,
    /// so this is generous — and the cost of being wrong is one parse, not one query compile.
    /// </summary>
    private const int Kept = 256;

    private static readonly Dictionary<(string Grammar, string Source), IReadOnlyList<HighlightSpan>> Known = [];
    private static readonly Queue<(string Grammar, string Source)> Order = new();
    private static readonly HashSet<(string Grammar, string Source)> Asked = [];
    private static readonly Dictionary<string, CodeHighlighter?> Readers = [];
    private static readonly Lock Gate = new();

    /// <summary>How many times everything has been forgotten, so a reading in flight can be disowned.</summary>
    private static int Age;

    /// <summary>Raised when a reading that was not known becomes known. Not on the thread that draws.</summary>
    public static event EventHandler? Ready;

    /// <summary>
    /// How <paramref name="source"/> reads under <paramref name="grammar"/>, where that is already worked
    /// out — and null where it is not, which starts working it out.
    /// </summary>
    public static IReadOnlyList<HighlightSpan>? For(string grammar, string source)
    {
        var key = (grammar, source);
        int age;

        lock (Gate)
        {
            if (Known.TryGetValue(key, out var spans)) return spans;
            if (!Asked.Add(key)) return null;

            age = Age;
        }

        ThreadPool.QueueUserWorkItem(static what => Work(what.Key, what.Age), (Key: key, Age: age), preferLocal: false);

        return null;
    }

    /// <summary>
    /// Forgets everything, for a test that wants the unread state back.
    ///
    /// <para>
    /// A reading already under way is forgotten too. It cannot be stopped, but it can be disowned: the age
    /// it was asked at no longer matches, so what it finds is dropped rather than landing a moment later in
    /// a cache somebody has just emptied.
    /// </para>
    /// </summary>
    internal static void Forget()
    {
        lock (Gate)
        {
            Known.Clear();
            Order.Clear();
            Asked.Clear();
            Age++;
        }
    }

    private static void Work((string Grammar, string Source) key, int age)
    {
        IReadOnlyList<HighlightSpan> spans = [];

        try
        {
            if (Reader(key.Grammar) is { } reader)
                lock (reader)
                    spans = reader.Highlight(key.Source);
        }
        catch
        {
            // A grammar that could not read it colours nothing, which is what it looked like a moment ago.
        }

        lock (Gate)
        {
            if (age != Age) return;   // disowned by Forget while this was reading

            Known[key] = spans;
            Order.Enqueue(key);
            Asked.Remove(key);

            while (Order.Count > Kept && Order.TryDequeue(out var oldest)) Known.Remove(oldest);
        }

        Ready?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>The one reader for a grammar, made on first asking and kept.</summary>
    private static CodeHighlighter? Reader(string grammar)
    {
        lock (Gate)
        {
            if (Readers.TryGetValue(grammar, out var known)) return known;
        }

        var made = CodeHighlighter.TryCreate(grammar);

        lock (Gate)
        {
            if (Readers.TryGetValue(grammar, out var raced)) return raced;

            Readers[grammar] = made;

            return made;
        }
    }
}
