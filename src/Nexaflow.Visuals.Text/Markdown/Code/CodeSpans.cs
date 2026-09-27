using System;
using System.Collections.Generic;
using System.Threading;

using Nexaflow.Syntax;

namespace Nexaflow.Visuals.Text.Markdown.Code;

/// <summary>
/// What a grammar makes of a stretch of code: the code language's second reading (<see cref="ContentLanguage.SlowParser"/>),
/// made away from the thread that draws.
///
/// <para>
/// One reader per grammar, kept for the life of the process, because compiling a grammar's highlight query costs about fifteen
/// milliseconds and it compiles once per reader. A parse against a reader already made is microseconds. A reader is not safe
/// to use from two threads at once, so each is used under its own lock.
/// </para>
/// </summary>
internal static class CodeSpans
{
    private static readonly Dictionary<string, CodeHighlighter?> Readers = [];
    private static readonly Lock Gate = new();

    /// <summary>How <paramref name="source"/> reads under <paramref name="grammar"/> — nothing, where the grammar cannot read it.</summary>
    public static IReadOnlyList<HighlightSpan> Read(string grammar, string source)
    {
        if (Reader(grammar) is not { } reader) return [];

        lock (reader)
            return reader.Highlight(source);
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
