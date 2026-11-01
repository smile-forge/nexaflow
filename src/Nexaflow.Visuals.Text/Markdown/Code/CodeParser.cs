using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Code;
using Nexaflow.Syntax;
using System;
using System.Collections.Generic;

namespace Nexaflow.Visuals.Text.Markdown.Code;

/// <summary>
/// Code, read: one run of characters, held as written — or, read against a grammar (<see cref="CodeSpans"/>), cut into what the
/// grammar calls each stretch of it, each a piece whose kind is what the grammar called it, so a keyword is a keyword in the tree
/// and not a colour, and the builder colours by kind knowing nothing about tree-sitter. Where its lines break is a stage's
/// (<see cref="Stages.CodeLines"/>).
///
/// <para>
/// The grammar's spans may overlap and the last one wins, which is what a grammar means by a pattern later in its query being
/// more specific. So they are flattened a character at a time and the runs that agree are joined back up — a token is one piece,
/// as a run of words is.
/// </para>
/// </summary>
public static class CodeParser
{
    /// <summary>Code, read as written: one run of its characters, which a stage colours from its reading's syntax (<see cref="Stages.CodeHighlights"/>).</summary>
    /// <param name="language">The grammar the code is read against, or nothing for code in a language no grammar reads.</param>
    public static ContentNode Parse(string? source, string language) =>
        new BlockNode(language, [ContentNode.Leaf(Kinds.Verbatim, source ?? string.Empty, Roles.Body, offset: 0)], CodeKinds.Code, offset: 0);

    /// <summary>
    /// A parse reading code a stretch at a time (<see cref="ContentStretch"/>): once the stretch's length is behind, it stops after the
    /// next line end that a line with something on it follows, carrying nothing — code read as written has nothing open where a line
    /// starts. A stretch that stops says the next carries on from it (<see cref="CodeKinds.CarriedOn"/>); a line, or a run of empty
    /// lines, longer than the window asks for a longer one.
    /// </summary>
    /// <param name="language">The grammar the code is read against, or nothing for code in a language no grammar reads.</param>
    public static Func<ContentStretch, ContentParse> Windowed(string language) => stretch => Stretched(stretch, language);

    private static ContentParse Stretched(ContentStretch stretch, string language)
    {
        var window = stretch.Window;
        var end = stretch.SoftMaxLength < window.Length ? window.IndexOf('\n', Math.Max(stretch.SoftMaxLength - 1, 0)) : -1;

        // A stretch stops before a line with something written on it, so the next starts where a caret stands before words, as it does
        // in the code read whole — an empty line has nothing for a caret to stand against.
        while (end >= 0 && end + 1 < window.Length && window[end + 1] is '\n' or '\r') end = window.IndexOf('\n', end + 1);

        if (end < 0 || end + 1 == window.Length)
            return stretch.Ends ? ContentParse.Of(Parse(window, language)) : ContentParse.Of(Parse(null, language)) with { NeedsMore = true };

        var stop = end + 1;
        var read = Parse(window[..stop], language);

        return ContentParse.Of(read.With([.. read.Children, ContentNode.Holding(CodeKinds.CarriedOn, Roles.Derived, true)])) with { Stopped = stop };
    }

    /// <summary>
    /// <paramref name="tree"/>, a stretch of code, with every body of it cut into what <paramref name="spans"/> — in the tree's own
    /// offsets — call each part of it; a body none of them reaches is left as it was.
    /// </summary>
    public static ContentNode Coloured(ContentNode tree, IReadOnlyList<HighlightSpan> spans)
    {
        ContentNode[]? parts = null;

        for (var index = 0; index < tree.Children.Count; index++)
        {
            var body = tree.Children[index];
            if (body.Role != Roles.Body || body.Offset is not { } from) continue;

            var within = spans.Where(span => span.Start < from + body.Width && span.Start + span.Length > from)
                              .Select(span => span with { Start = span.Start - from })
                              .ToList();
            if (within.Count == 0) continue;

            parts ??= [.. tree.Children];
            parts[index] = Tokens(body.Text, within, from);
        }

        return parts is null ? tree : tree.With(parts);
    }

    /// <summary>
    /// <paramref name="text"/>, read at <paramref name="at"/>, cut into what <paramref name="spans"/> — counted from its start — call each
    /// part of it: flattened a character at a time, the last span over a character winning, and the runs that agree joined back up.
    /// </summary>
    private static ContentNode Tokens(string text, IReadOnlyList<HighlightSpan> spans, int at)
    {
        var called = new string?[text.Length];

        foreach (var span in spans)
        {
            var to = Math.Min(span.Start + span.Length, text.Length);

            for (var place = Math.Max(span.Start, 0); place < to; place++) called[place] = span.Capture;
        }

        var parts = new List<ContentNode>();
        var from = 0;

        for (var place = 1; place <= text.Length; place++)
            if (place == text.Length || called[place] != called[from])
            {
                // What the grammar had no name for is held as written, which is what it looks like: text.
                parts.Add(ContentNode.Leaf(called[from] ?? Kinds.Verbatim, text[from..place], offset: at + from));
                from = place;
            }

        return ContentNode.Branch(Kinds.Sequence, parts, Roles.Body, offset: at);
    }
}
