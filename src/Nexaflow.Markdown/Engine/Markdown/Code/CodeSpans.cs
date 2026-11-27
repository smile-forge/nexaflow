using System;
using System.Collections.Generic;
using System.Threading;

using Nexaflow.Syntax;

namespace Nexaflow.Visuals.Text.Markdown.Code;

/// <summary>
/// The readers a grammar's code is parsed and coloured by, made away from the thread that draws (<see cref="CodeSyntax"/>).
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

    /// <summary>What <paramref name="use"/> makes of the one reader for <paramref name="grammar"/>, used under its lock — nothing, where the grammar cannot be read.</summary>
    public static T? WithReader<T>(string grammar, Func<CodeHighlighter, T> use)
    {
        if (Reader(grammar) is not { } reader) return default;

        lock (reader)
            return use(reader);
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
