using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nexaflow.Syntax;

/// <summary>
/// Edits to one declaration, addressed by what it <i>is</i> rather than by which lines it happens to occupy —
/// replace a method, delete it, change its signature without touching its body, rename it, substitute text
/// inside it, insert one beside it.
/// <para>
/// Every operation re-resolves the declaration in the text it is given, requires the parser to agree that the
/// declaration named is still there, works at the offsets the parse reports, and re-parses the result before
/// handing it back — refusing if the file would no longer parse, or if the half that was meant to stay put
/// moved. Nothing counts a brace and nothing finds a declaration with a regular expression.
/// </para>
/// <para>
/// This knows nothing about the knowledge graph or about files: it takes source text and gives source text
/// back. That is what lets the same operations serve an editor working on the buffer in front of it, a
/// headless CLI addressing a node id, and the assistant doing either.
/// </para>
/// </summary>
public static class StructuralEdit
{
    public enum Op
    {
        /// <summary>Replace the whole declaration.</summary>
        Replace,
        /// <summary>Remove the declaration and the doc comments/attributes attached above it.</summary>
        Delete,
        /// <summary>Replace everything up to the body, leaving the body byte-for-byte as it was.</summary>
        Signature,
        /// <summary>Replace the body, leaving the signature byte-for-byte as it was.</summary>
        Body,
        /// <summary>Rename the declaration itself.</summary>
        Rename,
        /// <summary>Insert a new declaration above this one.</summary>
        InsertBefore,
        /// <summary>Insert a new declaration below this one.</summary>
        InsertAfter,
        /// <summary>Insert a new member at the end of this type's body.</summary>
        Append,
        /// <summary>Replace (or add) the doc comment above the declaration.</summary>
        Doc,
        /// <summary>Replace text <i>inside</i> the declaration — a find-and-replace that cannot escape it.</summary>
        Substitute,
    }

    /// <param name="WithTrivia">For <see cref="Op.Replace"/>, also replace the attached doc comments and
    /// attributes. Off by default, so replacing a method keeps the documentation written for it.</param>
    /// <param name="Expect">Refuse unless the declaration's current text contains this. A caller that has
    /// just read the block can pin the edit to what it saw.</param>
    /// <param name="Find">For <see cref="Op.Substitute"/>, the text to find.</param>
    /// <param name="FindIsRegex">Treat <paramref name="Find"/> as a regular expression. Off by default:
    /// literal is what a caller replacing a fragment of code almost always means, and a stray <c>.</c> or
    /// <c>(</c> in that fragment silently matching something else is the whole hazard of doing this with sed.</param>
    /// <param name="AllOccurrences">Allow more than one match. Off by default, so an ambiguous substitution
    /// is an error rather than a silent multi-edit.</param>
    public sealed record Options(bool WithTrivia = false, string? Expect = null, string? Find = null,
                                 bool FindIsRegex = false, bool AllOccurrences = false);

    /// <summary>The changed region, for display.</summary>
    public sealed record Hunk(int Line, IReadOnlyList<string> Removed, IReadOnlyList<string> Added);

    public sealed record Result(bool Ok, string Message, string? NewText, Hunk? Hunk, IReadOnlyList<string> Notes)
    {
        public static Result Fail(string message) => new(false, message, null, null, []);
    }

    /// <summary>
    /// Works out the edited text and proves it, returning the result rather than writing anything.
    /// </summary>
    /// <param name="astPath">The declaration's structure-keyed path (see <see cref="CodeStructureExtractor"/>).</param>
    /// <param name="expectedName">What the caller believes the declaration is called. Checked against the
    /// parse — the edit is refused on a disagreement rather than proceeding on position alone.</param>
    /// <param name="text">Replacement/insertion text. Ignored by <see cref="Op.Delete"/>.</param>
    /// <param name="renameTo">The new name, for <see cref="Op.Rename"/>.</param>
    public static Result Apply(string grammarId, string source, string astPath, string expectedName,
                               Op op, string? text, Options? options = null, string? renameTo = null)
    {
        var o = options ?? new Options();

        if (string.IsNullOrEmpty(grammarId))
            return Result.Fail("No tree-sitter grammar covers this file, so an edit cannot be verified.");

        var extractor = new CodeStructureExtractor();
        if (extractor.ResolveSpan(grammarId, source, astPath) is not { } span)
            return Result.Fail(
                $"'{astPath}' no longer resolves. The record is behind the text in hand — the declaration has "
              + "been renamed, moved or removed.");

        var anchors = new DeclarationAnchors();
        var anchor  = anchors.Find(grammarId, source, expectedName, span.Line, span.EndLine);
        if (anchor is null)
            return Result.Fail(
                $"The parser found no declaration named '{expectedName}' at line {span.Line}. Refusing to edit "
              + "on position alone.");

        var declaration = source[anchor.Start..anchor.End];
        if (o.Expect is { Length: > 0 } expect && !declaration.Contains(expect, StringComparison.Ordinal))
            return Result.Fail($"The declaration at line {span.Line} does not contain the expected text {Quote(expect)}.");

        var shape   = SourceText.Of(source);
        var newline = shape.Newline;
        var indent  = IndentAt(source, anchor.Start);

        var edit = op switch
        {
            Op.Replace      => Replace(source, anchor, text, indent, newline, o.WithTrivia),
            Op.Delete       => Delete(source, anchor),
            Op.Signature    => Signature(source, anchor, text, indent, newline),
            Op.Body         => Body(source, anchor, text, indent, newline),
            Op.Rename       => Rename(source, anchor, renameTo),
            Op.InsertBefore => InsertBefore(source, anchor, text, indent, newline),
            Op.InsertAfter  => InsertAfter(source, anchor, text, indent, newline),
            Op.Append       => Append(source, anchor, text, indent, newline, shape),
            Op.Doc          => Doc(source, anchor, text, indent, newline),
            Op.Substitute   => Substitute(source, anchor, text, o),
            _               => (Text: (string?)null, Error: $"Unsupported operation {op}."),
        };

        if (edit.Error is { } error) return Result.Fail(error);
        var updated = edit.Text!;

        if (Verify(grammarId, updated, source, astPath, op, expectedName, renameTo, anchor, extractor, anchors)
            is { } problem)
            return Result.Fail(problem);

        var notes = new List<string>();
        if (op is Op.Replace or Op.Substitute && extractor.ResolveSpan(grammarId, updated, astPath) is null)
            notes.Add($"'{astPath}' no longer resolves after the edit — the declaration was renamed or "
                    + "restructured, so anything holding that path will need to re-resolve it.");

        return new Result(true, $"{Describe(op)} {expectedName}", updated, HunkOf(source, updated), notes);
    }

    // ── The operations ──────────────────────────────────────────────────────

    private static (string? Text, string? Error) Replace(string src, DeclarationAnchor a, string? text,
                                                         string indent, string newline, bool withTrivia)
    {
        if (text is null) return (null, "Replacement text is required.");
        var from = withTrivia ? LineStart(src, a.TriviaStart) : a.Start;
        return (Splice(src, from, a.End, Block(text, indent, newline, indentFirst: withTrivia)), null);
    }

    private static (string? Text, string? Error) Delete(string src, DeclarationAnchor a)
    {
        // Whole lines, so no indentation is left stranded, and one of the surrounding blank lines goes with
        // it — otherwise every deletion leaves a widening gap behind.
        var from = LineStart(src, a.TriviaStart);
        var to   = LineEndInclusive(src, a.End);
        if (BlankLine(src, to + 1, out var following) && BlankBefore(src, from)) to = following;
        return (src[..from] + src[Math.Min(to + 1, src.Length)..], null);
    }

    private static (string? Text, string? Error) Signature(string src, DeclarationAnchor a, string? text,
                                                           string indent, string newline)
    {
        if (text is null) return (null, "Replacement signature is required.");
        if (a.BodyStart is not { } bodyStart)
            return (null, $"The parser gives this {a.NodeType} no body, so its signature cannot be replaced "
                        + "on its own — replace the whole declaration instead.");

        // Stop at the last real character before the body so the whitespace between the two is kept: it is
        // the file's own formatting, and a caller supplying a signature should not have to reproduce it.
        var end = bodyStart;
        while (end > a.Start && char.IsWhiteSpace(src[end - 1])) end--;
        return (Splice(src, a.Start, end, Block(text, indent, newline, indentFirst: false)), null);
    }

    private static (string? Text, string? Error) Body(string src, DeclarationAnchor a, string? text,
                                                      string indent, string newline)
    {
        if (text is null) return (null, "Replacement body is required.");
        if (a.BodyStart is not { } start || a.BodyEnd is not { } end)
            return (null, $"The parser gives this {a.NodeType} no body to replace.");
        return (Splice(src, start, end, Block(text, indent, newline, indentFirst: AtLineStart(src, start))), null);
    }

    private static (string? Text, string? Error) Rename(string src, DeclarationAnchor a, string? renameTo)
    {
        if (renameTo is not { Length: > 0 } name) return (null, "A new name is required.");
        if (a.NameStart is not { } start || a.NameEnd is not { } end)
            return (null, $"The parser gives this {a.NodeType} no name field, so it cannot be renamed.");
        return (src[..start] + name + src[end..], null);
    }

    private static (string? Text, string? Error) InsertBefore(string src, DeclarationAnchor a, string? text,
                                                              string indent, string newline)
    {
        if (text is null) return (null, "Text to insert is required.");
        var at = LineStart(src, a.TriviaStart);   // above the doc comment, not between it and its declaration
        return (src[..at] + Block(text, indent, newline) + newline + newline + src[at..], null);
    }

    private static (string? Text, string? Error) InsertAfter(string src, DeclarationAnchor a, string? text,
                                                             string indent, string newline)
    {
        if (text is null) return (null, "Text to insert is required.");
        var at = Math.Min(LineEndInclusive(src, a.End) + 1, src.Length);
        return (src[..at] + newline + Block(text, indent, newline) + newline + src[at..], null);
    }

    private static (string? Text, string? Error) Append(string src, DeclarationAnchor a, string? text,
                                                        string indent, string newline, SourceText shape)
    {
        if (text is null) return (null, "Text to append is required.");
        if (a.BodyEnd is not { } bodyEnd)
            return (null, $"The parser gives this {a.NodeType} no body, so there is nothing to append into. "
                        + "Append targets a type; use insert-after for a free-standing declaration.");

        // Before the line the body closes on — after the last member, not after the closing brace.
        var at     = LineStart(src, Math.Max(bodyEnd - 1, 0));
        var inner  = MemberIndent(src, a) ?? indent + shape.IndentUnit();
        var spacer = BlankBefore(src, at) ? "" : newline;
        return (src[..at] + spacer + Block(text, inner, newline) + newline + src[at..], null);
    }

    private static (string? Text, string? Error) Doc(string src, DeclarationAnchor a, string? text,
                                                     string indent, string newline)
    {
        if (text is null) return (null, "Doc comment text is required.");
        var from = LineStart(src, a.TriviaStart);
        var to   = LineStart(src, a.Start);       // an empty range when there is no doc yet, so this inserts
        return (src[..from] + Block(text, indent, newline) + newline + src[to..], null);
    }

    /// <summary>
    /// Find-and-replace, bounded to the declaration. This is the safe form of the stream edit people reach
    /// for otherwise: the search is literal unless asked otherwise, so a <c>(</c> or a <c>.</c> in the
    /// fragment cannot quietly match something else; the range cannot run past the declaration, so a common
    /// identifier cannot be rewritten across the file; an unexpected number of matches is refused rather
    /// than applied; and the result still has to parse.
    /// </summary>
    private static (string? Text, string? Error) Substitute(string src, DeclarationAnchor a, string? replacement,
                                                            Options o)
    {
        if (o.Find is not { Length: > 0 } find) return (null, "Text to find is required for a substitution.");
        if (replacement is null) return (null, "Replacement text is required (use an empty string to delete).");

        var body = src[a.Start..a.End];

        string edited;
        int count;
        if (o.FindIsRegex)
        {
            Regex regex;
            try { regex = new Regex(find, RegexOptions.None, TimeSpan.FromSeconds(2)); }
            catch (ArgumentException ex) { return (null, $"'{find}' is not a valid regular expression: {ex.Message}"); }

            count  = regex.Matches(body).Count;
            edited = count == 0 ? body
                   : o.AllOccurrences ? regex.Replace(body, replacement)
                   : regex.Replace(body, replacement, 1);
        }
        else
        {
            count = Occurrences(body, find);
            edited = count == 0 ? body
                   : o.AllOccurrences ? body.Replace(find, replacement, StringComparison.Ordinal)
                   : ReplaceFirst(body, find, replacement);
        }

        if (count == 0)
            return (null, $"{Quote(find)} does not occur in this declaration, so nothing was changed.");
        if (count > 1 && !o.AllOccurrences)
            return (null, $"{Quote(find)} occurs {count} times in this declaration. Make the search text "
                        + "unique, or ask for all occurrences explicitly.");

        return (src[..a.Start] + edited + src[a.End..], null);
    }

    // ── Verification ────────────────────────────────────────────────────────

    /// <summary>
    /// Whether the result is safe to hand back, or why not. The universal check is that the text still
    /// parses: an edit that produces a syntax error must not be applied, because the next tool to read that
    /// file sees an error node and every declaration in it disappears.
    /// </summary>
    private static string? Verify(string grammar, string updated, string original, string astPath, Op op,
                                  string name, string? renameTo, DeclarationAnchor before,
                                  CodeStructureExtractor extractor, DeclarationAnchors anchors)
    {
        // "Worse than it was", not "not perfect": text that already fails to parse is not this edit's fault,
        // and refusing to touch it would make the one tool that could fix it unusable.
        if (extractor.Extract(grammar, updated).ParseFailed
            || (anchors.ParsesCleanly(grammar, original) && !anchors.ParsesCleanly(grammar, updated)))
            return "The edit would leave the file unparseable, so it has not been applied. The replacement "
                 + "text is probably unbalanced.";

        switch (op)
        {
            case Op.Delete when extractor.ResolveSpan(grammar, updated, astPath) is not null:
                return $"'{name}' is still declared after the delete — refusing a half-applied edit.";

            case Op.Rename:
                if (extractor.ResolveSpan(grammar, updated, astPath) is not null)
                    return $"'{name}' is still declared under its old path after the rename.";
                if (SpanOf(extractor.Extract(grammar, updated), renameTo!) is null)
                    return $"No declaration named '{renameTo}' exists after the rename.";
                break;

            // The whole promise of these two is that the other half is untouched, so it is checked rather
            // than trusted: a caller who supplies a body as a "signature" would otherwise land a mangled file.
            case Op.Signature:
                if (Part(grammar, updated, name, x => x.BodyStart, x => x.BodyEnd) is not { } newBody
                    || newBody != original[before.BodyStart!.Value..before.BodyEnd!.Value])
                    return "The body changed while replacing the signature, so the edit was not applied. The "
                         + "replacement should be the signature alone, with no body.";
                break;

            case Op.Body:
                if (Part(grammar, updated, name, x => x.Start, x => x.BodyStart) is not { } newHeader
                    || newHeader.TrimEnd() != original[before.Start..before.BodyStart!.Value].TrimEnd())
                    return "The signature changed while replacing the body, so the edit was not applied. The "
                         + "replacement should be the body alone, braces included.";
                break;
        }
        return null;
    }

    /// <summary>Re-finds the declaration in the edited text and returns one of its parts, for a
    /// before/after comparison.</summary>
    private static string? Part(string grammar, string updated, string name,
                                Func<DeclarationAnchor, int?> from, Func<DeclarationAnchor, int?> to)
    {
        var outline = new CodeStructureExtractor().Extract(grammar, updated);
        if (SpanOf(outline, name) is not { } span) return null;

        var anchor = new DeclarationAnchors().Find(grammar, updated, name, span.Line, span.EndLine);
        if (anchor is null) return null;

        var a = from(anchor);
        var b = to(anchor);
        return a is null || b is null ? null : updated[a.Value..b.Value];
    }

    private static (int Line, int EndLine)? SpanOf(CodeOutline outline, string name)
    {
        foreach (var type in outline.Types)
        {
            if (type.Name == name) return (type.Line, type.EndLine);
            foreach (var member in type.Members)
                if (member.Name == name) return (member.Line, member.EndLine);
        }
        foreach (var member in outline.TopLevel)
            if (member.Name == name) return (member.Line, member.EndLine);
        return null;
    }

    // ── Text mechanics ──────────────────────────────────────────────────────

    /// <summary>
    /// Caller text, re-indented to its destination and given the destination's line endings.
    /// <para>
    /// <paramref name="indentFirst"/> is false when the splice starts part-way along a line — replacing a
    /// declaration begins after the indentation already sitting in front of it, so indenting the first line
    /// again would double it. Every later line does begin a fresh line, and does need it.
    /// </para>
    /// </summary>
    private static string Block(string text, string indent, string newline, bool indentFirst = true)
    {
        var lines = SourceText.Reindent(SourceText.BlockOf(text), indent);
        if (!indentFirst && lines.Count > 0 && lines[0].StartsWith(indent, StringComparison.Ordinal))
            lines = [lines[0][indent.Length..], .. lines.Skip(1)];
        return string.Join(newline, lines);
    }

    private static string Splice(string src, int from, int to, string replacement) =>
        src[..from] + replacement + src[to..];

    /// <summary>
    /// How this type indents its members, read off the members it already has. Measuring beats inferring a
    /// file-wide indent unit and adding it: a nested type, a member inside a namespace block and a top-level
    /// one all sit at different depths, and the existing members are the only thing that knows. Null when the
    /// body is empty, which is the one case with nothing to measure.
    /// </summary>
    private static string? MemberIndent(string src, DeclarationAnchor type)
    {
        if (type.BodyStart is not { } start || type.BodyEnd is not { } end) return null;

        var i = LineEndInclusive(src, start) + 1;    // past the line the body opens on
        while (i < end && i < src.Length)
        {
            var lineEnd = LineEndInclusive(src, i);
            var line    = src[i..Math.Min(lineEnd + 1, src.Length)];
            if (line.Trim().Length > 0) return SourceText.IndentOf(line);
            i = lineEnd + 1;
        }
        return null;
    }

    /// <summary>Whether only indentation separates <paramref name="offset"/> from the start of its line — the
    /// test for whether a splice there begins a line or continues one.</summary>
    private static bool AtLineStart(string src, int offset) =>
        src[LineStart(src, offset)..Math.Clamp(offset, 0, src.Length)].Trim().Length == 0;

    /// <summary>The indentation of the line <paramref name="offset"/> sits on — what an edit there must match.</summary>
    private static string IndentAt(string src, int offset)
    {
        var start = LineStart(src, offset);
        var i     = start;
        while (i < src.Length && i < offset && (src[i] == ' ' || src[i] == '\t')) i++;
        return src[start..i];
    }

    private static int LineStart(string src, int offset)
    {
        var i = Math.Clamp(offset, 0, src.Length);
        while (i > 0 && src[i - 1] != '\n') i--;
        return i;
    }

    /// <summary>The offset of the last character of the line <paramref name="offset"/> ends on, newline included.</summary>
    private static int LineEndInclusive(string src, int offset)
    {
        var i = Math.Clamp(offset, 0, src.Length);
        while (i < src.Length && src[i] != '\n') i++;
        return Math.Min(i, Math.Max(src.Length - 1, 0));
    }

    /// <summary>Whether the line beginning at <paramref name="at"/> is blank, and where it ends.</summary>
    private static bool BlankLine(string src, int at, out int end)
    {
        end = at;
        if (at >= src.Length) return false;
        var i = at;
        while (i < src.Length && src[i] != '\n') { if (src[i] is not (' ' or '\t' or '\r')) return false; i++; }
        end = Math.Min(i, src.Length - 1);
        return true;
    }

    private static bool BlankBefore(string src, int at)
    {
        if (at <= 0) return true;                       // the start of the file counts as separated
        var previous = LineStart(src, at - 1);
        return src[previous..(at - 1)].Trim().Length == 0;
    }

    private static int Occurrences(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    private static string ReplaceFirst(string haystack, string needle, string replacement)
    {
        var at = haystack.IndexOf(needle, StringComparison.Ordinal);
        return at < 0 ? haystack : haystack[..at] + replacement + haystack[(at + needle.Length)..];
    }

    private static string Quote(string s) => "\"" + s.Replace("\n", "\\n") + "\"";

    private static string Describe(Op op) => op switch
    {
        Op.Replace => "replace",   Op.Delete       => "delete",        Op.Signature  => "re-sign",
        Op.Body    => "re-body",   Op.Rename       => "rename",        Op.Doc        => "document",
        Op.Append  => "append to", Op.InsertBefore => "insert before", Op.Substitute => "substitute in",
        _          => "insert after",
    };

    // ── Diff ────────────────────────────────────────────────────────────────

    /// <summary>The changed region, found by trimming the common head and tail. The edit's own offsets would
    /// be cheaper, but a hunk derived from the two texts cannot disagree with what will be written.</summary>
    private static Hunk HunkOf(string before, string after)
    {
        var a = SourceText.Of(before).Lines;
        var b = SourceText.Of(after).Lines;

        var head = 0;
        while (head < a.Count && head < b.Count && a[head] == b[head]) head++;

        var tail = 0;
        while (tail < a.Count - head && tail < b.Count - head
               && a[a.Count - 1 - tail] == b[b.Count - 1 - tail]) tail++;

        return new Hunk(head + 1,
                        [.. a.Skip(head).Take(a.Count - head - tail)],
                        [.. b.Skip(head).Take(b.Count - head - tail)]);
    }
}
