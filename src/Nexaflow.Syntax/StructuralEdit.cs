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
        /// <summary>Add an import to the file. File-level: see <see cref="AddImport"/>, which is what
        /// implements it — this member exists so a caller with one <c>op</c> parameter can ask for it.</summary>
        Import,
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

    /// <summary>
    /// The edit as a single splice: replace <paramref name="Length"/> characters at
    /// <paramref name="Offset"/> with <paramref name="Inserted"/>.
    /// <para>
    /// A live editor needs this rather than the whole new document. Assigning the full text back would work,
    /// but it collapses the undo stack into "everything changed", moves the caret to the top and re-renders
    /// the file; a minimal splice is one undo step, leaves the caret where it was, and only redraws the lines
    /// that moved.
    /// </para>
    /// </summary>
    public sealed record TextChange(int Offset, int Length, string Inserted);

    /// <summary>One declaration the text contains, and the path that addresses it.</summary>
    public sealed record Declaration(string Name, string Kind, string AstPath, int Line, int EndLine);

    public sealed record Result(bool Ok, string Message, string? NewText, Hunk? Hunk,
                                IReadOnlyList<string> Notes, TextChange? Change = null)
    {
        public static Result Fail(string message) => new(false, message, null, null, []);
    }

    /// <summary>
    /// Every type and member the text declares, in document order — the addressing table for a caller with
    /// no knowledge graph to look node ids up in, which is any editor working on the buffer in front of it.
    /// </summary>
    public static IReadOnlyList<Declaration> Declarations(string grammarId, string source)
    {
        if (string.IsNullOrEmpty(grammarId) || string.IsNullOrEmpty(source)) return [];

        var outline = new CodeStructureExtractor().Extract(grammarId, source);
        var found   = new List<Declaration>();

        foreach (var type in outline.Types)
        {
            found.Add(new Declaration(type.Name, type.Kind.ToString(), type.AstPath, type.Line, type.EndLine));
            foreach (var member in type.Members)
                found.Add(new Declaration(member.Name, member.Kind.ToString(), member.AstPath,
                                          member.Line, member.EndLine));
        }
        foreach (var member in outline.TopLevel)
            found.Add(new Declaration(member.Name, member.Kind.ToString(), member.AstPath,
                                      member.Line, member.EndLine));

        return [.. found.OrderBy(d => d.Line).ThenBy(d => d.AstPath, StringComparer.Ordinal)];
    }

    /// <summary>
    /// A substitution over the whole file rather than one declaration — for the things that are not inside a
    /// declaration at all: a namespace or package statement, a file-level attribute, a licence header.
    /// <para>
    /// Declaration scope is the better default and stays the default, because it is what stops a common
    /// identifier being rewritten somewhere you did not mean. But refusing to go wider leaves the caller with
    /// nothing for "rename the namespace this file declares", and the answer to that should not be a hand
    /// edit. The rest of the guarantees are unchanged: literal unless asked otherwise, refused unless it
    /// matches exactly once, and the result has to parse.
    /// </para>
    /// </summary>
    public static Result SubstituteInFile(string grammarId, string source, string? replacement, Options options)
    {
        if (string.IsNullOrEmpty(grammarId))
            return Result.Fail("No tree-sitter grammar covers this file, so an edit cannot be verified.");

        var notes = new List<string>();
        var whole = new DeclarationAnchor("file", 0, source.Length, 0, null, null, null, null, null, null);

        var (text, error) = Substitute(grammarId, source, whole, replacement, options,
                                       SourceText.Of(source).Newline, "", notes);
        if (error is { } why) return Result.Fail(why);

        var anchors = new DeclarationAnchors();
        if (anchors.ParsesCleanly(grammarId, source) && !anchors.ParsesCleanly(grammarId, text!))
            return Result.Fail("The edit would leave the file unparseable, so it has not been applied.");

        return new Result(true, "substitute in the file", text, HunkOf(source, text!), notes,
                          ChangeOf(source, text!));
    }

    /// <summary>
    /// Adds an import (a <c>using</c>, an <c>import</c>, a <c>#include</c>) in the place the file already
    /// keeps them: after the last one, or — when there are none — above the first declaration but below any
    /// header comment, so a licence block stays at the top.
    /// <para>
    /// This is file-level rather than declaration-level, which is why it is not one of the
    /// <see cref="Op"/>s. Reaching it through <see cref="Op.InsertBefore"/> on the first declaration was
    /// possible and wrong: in a file with a file-scoped namespace it put the <c>using</c> underneath the
    /// <c>namespace</c>, which compiles and looks like a mistake.
    /// </para>
    /// </summary>
    public static Result AddImport(string grammarId, string source, string importText)
    {
        if (string.IsNullOrEmpty(grammarId))
            return Result.Fail("No tree-sitter grammar covers this file, so an import cannot be placed.");
        if (importText is not { Length: > 0 })
            return Result.Fail("The import to add is required.");

        var wanted = importText.Trim();
        if (SourceText.Of(source).Lines.Any(l => l.Trim() == wanted))
            return Result.Fail($"{Quote(wanted)} is already imported.");

        var (lastImportEnd, firstDeclaration) = new DeclarationAnchors().ImportRegion(grammarId, source);
        var shape   = SourceText.Of(source);
        var newline = shape.Newline;

        int at;
        string trailing;
        if (lastImportEnd is { } end)
        {
            at       = Math.Min(LineEndInclusive(source, end) + 1, source.Length);
            trailing = "";                    // it joins an existing block; no blank line inside one
        }
        else if (firstDeclaration is { } first)
        {
            at       = LineStart(source, first);
            trailing = newline;               // starting a block, so separate it from what follows
        }
        else
        {
            at       = 0;
            trailing = newline;
        }

        var updated = source[..at] + Block(wanted, IndentAt(source, at), newline) + newline + trailing
                    + source[at..];

        var anchors = new DeclarationAnchors();
        if (anchors.ParsesCleanly(grammarId, source) && !anchors.ParsesCleanly(grammarId, updated))
            return Result.Fail("Adding that import would leave the file unparseable, so it was not applied.");

        return new Result(true, $"import {wanted}", updated, HunkOf(source, updated), [],
                          ChangeOf(source, updated));
    }

    /// <summary>
    /// The same edit, for a caller addressing a declaration by path alone. The name to verify against is
    /// taken from this very parse, which is right when the text in hand <i>is</i> the source of truth — an
    /// open editor has no stale record to catch out, unlike a graph built from another checkout.
    /// </summary>
    public static Result Apply(string grammarId, string source, string astPath, Op op, string? text,
                               Options? options = null, string? renameTo = null)
    {
        var declarations = Declarations(grammarId, source);

        if (declarations.FirstOrDefault(d => d.AstPath == astPath) is { } named)
            return Apply(grammarId, source, astPath, named.Name, op, text, options, renameTo);

        // The path is out of date — the caller listed the file, then changed it, and is working from the
        // older listing. The last segment still says what it meant, so recover from that instead of sending
        // it back to re-list for a name it has already told us.
        if (NameInPath(astPath) is not { Length: > 0 } wanted)
            return Result.Fail($"'{astPath}' does not name a declaration in this file. List them first.");

        var candidates = declarations.Where(d => d.Name == wanted).ToList();

        if (candidates.Count == 1)
            return Apply(grammarId, source, candidates[0].AstPath, wanted, op, text, options, renameTo);

        if (candidates.Count > 1)
            return Result.Fail(
                $"'{astPath}' no longer resolves and '{wanted}' is declared {candidates.Count} times here "
              + $"({string.Join(", ", candidates.Select(d => d.AstPath))}) — name one of those.");

        return Result.Fail(
            $"Nothing named '{wanted}' is declared in this file.{ConstructorHint(wanted)} "
          + "List the declarations to see what is there now.");
    }

    /// <summary>
    /// The declared name an AST path ends in — <c>Add</c> for <c>T:C/M:Add#1</c>. The path is
    /// <c>&lt;kind&gt;:&lt;name&gt;</c> segments with an overload position on the last one, and the name is
    /// the part of it that survives the file being edited.
    /// </summary>
    /// <summary>
    /// A nudge for the one name that is guessed wrong from habit. IL calls a constructor <c>.ctor</c> and
    /// the JVM calls it <c>&lt;init&gt;</c>; an AST path names it after its type, because that is what the
    /// source says.
    /// </summary>
    private static string ConstructorHint(string name) =>
        name is ".ctor" or "ctor" or "<init>" or ".cctor"
            ? " A constructor is addressed by its type's own name — M:TypeName, not M:.ctor."
            : "";

    private static string? NameInPath(string astPath)
    {
        if (string.IsNullOrEmpty(astPath)) return null;

        var last  = astPath.Split('/')[^1];
        var colon = last.IndexOf(':');
        var name  = colon >= 0 ? last[(colon + 1)..] : last;
        var hash  = name.IndexOf('#');
        return hash >= 0 ? name[..hash] : name;
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

        var notes     = new List<string>();
        var extractor = new CodeStructureExtractor();
        var resolved  = extractor.ResolveSpan(grammarId, source, astPath);

        if (resolved is null)
        {
            // The recorded path is stale — the declaration moved between types, or its container was
            // renamed. Treat that as ordinary rather than as a failure: the record this came from was built
            // from a checkout that is not this working tree, and refreshing it takes a minute and a half.
            // The NAME is the durable half of the record, so re-find by that and carry on. What is never
            // guessed is which of several same-named declarations was meant.
            var candidates = Declarations(grammarId, source).Where(d => d.Name == expectedName).ToList();

            if (candidates.Count == 0)
                return Result.Fail(
                    $"Nothing named '{expectedName}' is declared in this file.{ConstructorHint(expectedName)} "
                  + "It has been renamed or "
                  + "removed. List the declarations to see what is there now.");

            if (candidates.Count > 1)
                return Result.Fail(
                    $"'{astPath}' no longer resolves and '{expectedName}' is declared {candidates.Count} "
                  + $"times here ({string.Join(", ", candidates.Select(d => d.AstPath))}) — name one of those.");

            notes.Add($"'{astPath}' had moved; '{expectedName}' was re-found at '{candidates[0].AstPath}' "
                    + "and edited there.");
            astPath  = candidates[0].AstPath;
            resolved = (candidates[0].Line, candidates[0].EndLine);
        }

        var span    = resolved.Value;
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
            Op.Replace      => Replace(source, anchor, text, indent, newline, o.WithTrivia, grammarId, notes),
            Op.Delete       => Delete(source, anchor),
            Op.Signature    => Signature(source, anchor, text, indent, newline),
            Op.Body         => Body(source, anchor, text, indent, newline),
            Op.Rename       => Rename(source, anchor, renameTo),
            Op.InsertBefore => InsertBefore(source, anchor, text, indent, newline),
            Op.InsertAfter  => InsertAfter(source, anchor, text, indent, newline),
            Op.Append       => Append(source, anchor, text, indent, newline, shape),
            Op.Doc          => Doc(source, anchor, text, indent, newline),
            Op.Substitute   => Substitute(grammarId, source, anchor, text, o, newline, astPath, notes),
            Op.Import       => (null, "An import belongs to the file, not to a declaration — call AddImport."),
            _               => (Text: (string?)null, Error: $"Unsupported operation {op}."),
        };

        if (edit.Error is { } error) return Result.Fail(error);
        var updated = edit.Text!;

        if (Verify(grammarId, updated, source, astPath, op, expectedName, renameTo, anchor, extractor, anchors)
            is { } problem)
            return Result.Fail(problem);

        // A `#N` in an ast path is an overload's POSITION among its same-named siblings, so removing or
        // adding one silently renumbers the rest. A caller working from a listing it took before this edit
        // would then aim `#1` at what used to be `#2` — the one way a sequence of edits to one file can go
        // wrong without anything refusing, since the name check still passes. Say so rather than assume the
        // caller knows the path format.
        if (astPath.Contains('#', StringComparison.Ordinal)
            && op is Op.Delete or Op.InsertBefore or Op.InsertAfter or Op.Replace)
            notes.Add($"'{expectedName}' is overloaded, and the #N in an ast path is its position among the "
                    + "overloads — this edit renumbers the others. List the declarations again before making "
                    + "further edits to this file.");

        if (op is Op.Replace or Op.Substitute && extractor.ResolveSpan(grammarId, updated, astPath) is null)
            notes.Add($"'{astPath}' no longer resolves after the edit — the declaration was renamed or "
                    + "restructured, so anything holding that path will need to re-resolve it.");

        return new Result(true, $"{Describe(op)} {expectedName}", updated, HunkOf(source, updated), notes,
                          ChangeOf(source, updated));
    }

    /// <summary>
    /// The edit expressed as one splice, found by trimming the common prefix and suffix. Derived from the
    /// two texts rather than tracked through the operations, so it cannot disagree with what actually
    /// changed however the operation got there.
    /// </summary>
    private static TextChange ChangeOf(string before, string after)
    {
        var max = Math.Min(before.Length, after.Length);

        var prefix = 0;
        while (prefix < max && before[prefix] == after[prefix]) prefix++;

        var suffix = 0;
        while (suffix < max - prefix
               && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix]) suffix++;

        return new TextChange(prefix, before.Length - suffix - prefix,
                              after[prefix..(after.Length - suffix)]);
    }

    // ── The operations ──────────────────────────────────────────────────────

    private static (string? Text, string? Error) Replace(string src, DeclarationAnchor a, string? text,
                                                         string indent, string newline, bool withTrivia,
                                                         string grammarId, List<string> notes)
    {
        if (text is null) return (null, "Replacement text is required.");

        // Keeping the old doc comment is right when the replacement has none — that is what "you needn't
        // supply one" means. When the replacement DOES open with one, keeping the old one too produces two,
        // which is never what was meant and compiles fine, so only reading the file catches it.
        var bringsOwnDoc = !withTrivia && a.TriviaStart < a.Start && OpensWithComment(grammarId, text);
        if (bringsOwnDoc)
            notes.Add("the replacement opens with a comment, so it replaced the existing doc comment rather "
                    + "than being added above it.");

        var replaceTrivia = withTrivia || bringsOwnDoc;
        var from = replaceTrivia ? LineStart(src, a.TriviaStart) : a.Start;
        return (Splice(src, from, a.End, Block(text, indent, newline, indentFirst: replaceTrivia)), null);
    }

    /// <summary>Whether a block of replacement text begins with a comment in this language.</summary>
    private static bool OpensWithComment(string grammarId, string text)
    {
        var first = SourceText.BlockOf(text).FirstOrDefault(l => l.Trim().Length > 0)?.Trim();
        if (first is not { Length: > 0 }) return false;

        // `#` is a comment in Python and Ruby and a preprocessor directive in C#, so the marker set has to
        // follow the language rather than be a union of all of them.
        return grammarId switch
        {
            "python" or "ruby"                   => first.StartsWith('#'),
            "xml" or "xaml" or "html" or "razor" => first.StartsWith("<!--", StringComparison.Ordinal),
            _ => first.StartsWith("//", StringComparison.Ordinal)
              || first.StartsWith("/*", StringComparison.Ordinal),
        };
    }

    private static (string? Text, string? Error) Delete(string src, DeclarationAnchor a)
    {
        // Whole lines, so no indentation is left stranded, and one adjacent blank line goes with it —
        // otherwise every deletion leaves a widening gap behind.
        //
        // The blank ABOVE is preferred, because that is the separator this declaration owns. Falling back to
        // the one below matters for the first member of a body, where there is nothing above but the opening
        // brace: taking neither left a blank line stranded at the top of the block.
        var from = LineStart(src, a.TriviaStart);
        var to   = LineEndInclusive(src, a.End);

        if (PrecedingBlankLine(src, from) is { } above) from = above;
        else if (BlankLine(src, to + 1, out var below)) to = below;

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
        // The splice starts at the brace, so the indentation in front of it is never consumed and must not be
        // written again — indenting the first line here put the opening brace one level in from its own member.
        return (Splice(src, start, end, Block(text, indent, newline, indentFirst: false)), null);
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
        if (a.BodyStart is not { } bodyStart)
            return (null, $"The parser gives this {a.NodeType} no body, so there is nothing to append into. "
                        + "Append targets a type; use insert-after for a free-standing declaration.");

        var inner = MemberIndent(src, a) ?? indent + shape.IndentUnit();

        // After the last thing actually in the body. Saying it that way rather than "before the closing
        // brace" is what makes this work for Python, whose body ends with its last statement and has no
        // brace to sit before — phrased the other way it inserted INTO the last method.
        if (a.BodyContentEnd is { } contentEnd)
        {
            var at     = Math.Min(LineEndInclusive(src, contentEnd) + 1, src.Length);
            var spacer = newline;
            return (src[..at] + spacer + Block(text, inner, newline) + newline + src[at..], null);
        }

        // An empty body: nothing to sit after, so sit just inside it — and with no blank line, because a
        // gap above the only member of an otherwise empty type reads as an accident.
        var opening = Math.Min(LineEndInclusive(src, bodyStart) + 1, src.Length);
        return (src[..opening] + Block(text, inner, newline) + newline + src[opening..], null);
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
    /// <param name="astPath">The declaration being edited, so a "not found" message can rule it out when
    /// suggesting where the text does live. Empty for a whole-file substitution.</param>
    private static (string? Text, string? Error) Substitute(string grammarId, string src, DeclarationAnchor a,
                                                            string? replacement, Options o, string newline,
                                                            string astPath, List<string> notes)
    {
        if (o.Find is not { Length: > 0 } find) return (null, "Text to find is required for a substitution.");
        if (replacement is null) return (null, "Replacement text is required (use an empty string to delete).");

        var body = src[a.Start..a.End];

        if (o.FindIsRegex)
        {
            Regex regex;
            try { regex = new Regex(find, RegexOptions.None, TimeSpan.FromSeconds(2)); }
            catch (ArgumentException ex) { return (null, $"'{find}' is not a valid regular expression: {ex.Message}"); }

            var matches = regex.Matches(body).Count;
            if (matches == 0) return (null, NotFound(grammarId, src, a, find, astPath));
            if (matches > 1 && !o.AllOccurrences) return (null, Ambiguous(find, matches));
            if (matches > 1) notes.Add($"replaced {matches} occurrences");

            var replaced = regex.Replace(body, m => Indented(body, m.Index, m.Result(replacement), newline),
                                         o.AllOccurrences ? int.MaxValue : 1);
            return (src[..a.Start] + replaced + src[a.End..], null);
        }

        // Exact first, so a caller who reproduced the text byte-for-byte gets the match it asked for, at
        // character granularity.
        var exact = Occurrences(body, find);
        if (exact > 1 && !o.AllOccurrences) return (null, Ambiguous(find, exact));
        if (exact > 0)
        {
            if (exact > 1) notes.Add($"replaced {exact} occurrences");
            return (src[..a.Start] + ReplaceIndented(body, find, replacement, o.AllOccurrences, newline)
                  + src[a.End..], null);
        }

        // Then ignoring indentation. Everywhere else this tool promises the caller does not handle
        // whitespace — text written flush-left lands indented — and then `find` demanded it byte-for-byte.
        // A fragment copied out of a listing has whatever indentation it had there, or none, and failing on
        // that is a papercut with no upside. Exact still wins, so nothing that used to work changes.
        var loose = LooseMatches(body, find);
        if (loose.Count > 1 && !o.AllOccurrences) return (null, Ambiguous(find, loose.Count));
        if (loose.Count == 0) return (null, NotFound(grammarId, src, a, find, astPath));

        notes.Add(loose.Count == 1
            ? "matched ignoring indentation"
            : $"replaced {loose.Count} occurrences, matched ignoring indentation");

        // Back to front, so an earlier match's offsets are still valid after a later one is replaced.
        var edited = body;
        foreach (var (start, end) in loose.AsEnumerable().Reverse())
        {
            var indent = SourceText.IndentOf(edited[start..]);
            edited = edited[..start] + Block(replacement, indent, newline) + edited[end..];
        }
        return (src[..a.Start] + edited + src[a.End..], null);
    }

    /// <summary>
    /// Replacement text indented for where it is going.
    /// <para>
    /// The tool's one promise about whitespace is that the caller does not handle it — text written
    /// flush-left lands correctly indented. A literal substitution used to be the exception, inserting the
    /// replacement byte-for-byte, so a multi-line fragment written flush-left produced flush-left
    /// continuation lines inside an indented body. That compiles, so nothing catches it except reading the
    /// file afterwards; an undocumented carve-out in the one guarantee everything else keeps is worse than
    /// no guarantee.
    /// </para>
    /// <para>
    /// The first line is indented only when the match began at column 0 — i.e. the search text swallowed the
    /// line's indentation, so the replacement has to supply it again. When the match starts after the
    /// indentation (or mid-line), the file already provides it and adding it would double it.
    /// </para>
    /// </summary>
    private static string Indented(string body, int at, string replacement, string newline)
    {
        var lineStart = LineStart(body, at);
        return Block(replacement, SourceText.IndentOf(body[lineStart..]), newline, indentFirst: at == lineStart);
    }

    /// <summary>Literal replacement, each occurrence indented for the line it lands on. Back to front, so an
    /// earlier match's offset is still valid after a later one is replaced.</summary>
    private static string ReplaceIndented(string body, string find, string replacement, bool all, string newline)
    {
        var at = new List<int>();
        for (var i = body.IndexOf(find, StringComparison.Ordinal); i >= 0;
             i = body.IndexOf(find, i + find.Length, StringComparison.Ordinal))
        {
            at.Add(i);
            if (!all) break;
        }

        var edited = body;
        foreach (var start in at.AsEnumerable().Reverse())
            edited = edited[..start] + Indented(edited, start, replacement, newline)
                   + edited[(start + find.Length)..];
        return edited;
    }

    /// <summary>
    /// Whole-line ranges within <paramref name="body"/> whose content matches <paramref name="find"/> once
    /// each line's own indentation is set aside. Line-granular by nature: what is being matched is lines,
    /// so what is replaced is lines.
    /// </summary>
    private static List<(int Start, int End)> LooseMatches(string body, string find)
    {
        var pattern = SourceText.BlockOf(find).Select(l => l.Trim()).ToList();
        var found   = new List<(int, int)>();
        if (pattern.Count == 0) return found;

        var lines = LineSpans(body);
        for (var i = 0; i + pattern.Count <= lines.Count; i++)
        {
            var matched = true;
            for (var k = 0; k < pattern.Count && matched; k++)
                matched = body[lines[i + k].Start..lines[i + k].End].Trim() == pattern[k];

            if (!matched) continue;
            found.Add((lines[i].Start, lines[i + pattern.Count - 1].End));
            i += pattern.Count - 1;                       // matches never overlap
        }
        return found;
    }

    /// <summary>Each line's [start, end) offsets, the end stopping before the newline.</summary>
    private static List<(int Start, int End)> LineSpans(string text)
    {
        var spans = new List<(int, int)>();
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i != text.Length && text[i] != '\n') continue;
            var end = i > start && text[i - 1] == '\r' ? i - 1 : i;
            spans.Add((start, end));
            start = i + 1;
        }
        return spans;
    }

    private static string Ambiguous(string find, int count) =>
        $"{Quote(find)} occurs {count} times in this declaration. Make the search text unique — extending it "
      + "with the line above or below is usually enough — or ask for all occurrences explicitly.";

    /// <summary>
    /// Why the search found nothing, and where to look instead. Naming the declaration that <i>does</i>
    /// contain the text turns "not found" from a dead end into the next call: the usual cause is having the
    /// right fragment and the wrong declaration.
    /// </summary>
    private static string NotFound(string grammarId, string src, DeclarationAnchor a, string find, string astPath)
    {
        var probe = SourceText.BlockOf(find).FirstOrDefault()?.Trim();
        if (probe is { Length: > 0 })
        {
            // The tightest declaration containing it, not the first: every member is also inside its type,
            // and being told the text is "in Widget" when it is in Widget.Reset is not an answer.
            //
            // Excluded by PATH, not by range. Range containment looked right and was not: a declaration's
            // line span starts at the beginning of its first line, while its anchor starts after that line's
            // indentation, so the declaration failed to contain itself and the message named the very node
            // the caller had just passed — telling them to do what they had just done.
            var elsewhere = Declarations(grammarId, src)
                .Where(d => d.Line > 0 && d.EndLine >= d.Line
                         && !string.Equals(d.AstPath, astPath, StringComparison.Ordinal))
                .Select(d => (Decl: d, From: OffsetOfLine(src, d.Line), To: OffsetOfLine(src, d.EndLine + 1)))
                .Where(x => x.To <= a.Start || x.From >= a.End)             // and nothing overlapping it
                .Where(x => src[x.From..x.To].Contains(probe, StringComparison.Ordinal))
                .OrderBy(x => x.To - x.From)
                .Select(x => x.Decl)
                .FirstOrDefault();

            if (elsewhere is not null)
                return $"{Quote(find)} does not occur in this declaration, so nothing was changed. It does "
                     + $"occur in '{elsewhere.Name}' ({elsewhere.AstPath}, line {elsewhere.Line}) — edit that "
                     + "one instead.";
        }

        return $"{Quote(find)} does not occur in this declaration, so nothing was changed. Indentation is "
             + "ignored when matching, so the difference is in the text itself — re-read the declaration.";
    }

    /// <summary>The offset the 1-based <paramref name="line"/> starts at, clamped to the end of the text.</summary>
    private static int OffsetOfLine(string src, int line)
    {
        var at = 0;
        for (var n = 1; n < line && at < src.Length; n++)
        {
            var next = src.IndexOf('\n', at);
            if (next < 0) return src.Length;
            at = next + 1;
        }
        return Math.Min(at, src.Length);
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
            // Counted, not resolved by path: deleting one of three overloads renumbers the survivors, so the
            // path that named the deleted one resolves again straight away and a correct delete looked
            // half-applied. What actually has to be true is that one fewer declaration carries the name.
            case Op.Delete when Named(extractor.Extract(grammar, updated), name)
                                >= Named(extractor.Extract(grammar, original), name):
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
                if (Part(grammar, updated, astPath, name, x => x.BodyStart, x => x.BodyEnd) is not { } newBody
                    || newBody != original[before.BodyStart!.Value..before.BodyEnd!.Value])
                    return "The body changed while replacing the signature, so the edit was not applied. The "
                         + "replacement should be the signature alone, with no body.";
                break;

            case Op.Body:
                if (Part(grammar, updated, astPath, name, x => x.Start, x => x.BodyStart) is not { } newHeader
                    || newHeader.TrimEnd() != original[before.Start..before.BodyStart!.Value].TrimEnd())
                    return "The signature changed while replacing the body, so the edit was not applied. The "
                         + "replacement should be the body alone, braces included.";
                break;
        }
        return null;
    }

    /// <summary>
    /// Re-finds the declaration in the edited text and returns one of its parts, for a before/after
    /// comparison.
    /// <para>
    /// The path is what identifies it, and the name is only the fallback for when the edit moved it. Asking by
    /// name first was wrong for the two cases where a name is not unique: a constructor carries its type's
    /// name, so the lookup returned the whole CLASS and its body never matched — <c>signature</c> and
    /// <c>body</c> each refused a correct edit to a constructor, with mirror-image messages. Overloads collide
    /// the same way, all of them answering to one name.
    /// </para>
    /// </summary>
    private static string? Part(string grammar, string updated, string astPath, string name,
                                Func<DeclarationAnchor, int?> from, Func<DeclarationAnchor, int?> to)
    {
        var extractor = new CodeStructureExtractor();
        var span      = extractor.ResolveSpan(grammar, updated, astPath)
                     ?? SpanOf(extractor.Extract(grammar, updated), name);
        if (span is not { } at) return null;

        var anchor = new DeclarationAnchors().Find(grammar, updated, name, at.Line, at.EndLine);
        if (anchor is null) return null;

        var a = from(anchor);
        var b = to(anchor);
        return a is null || b is null ? null : updated[a.Value..b.Value];
    }

    /// <summary>How many declarations carry <paramref name="name"/> — one per overload.</summary>
    private static int Named(CodeOutline outline, string name) =>
        outline.Types.Count(t => t.Name == name)
      + outline.Types.Sum(t => t.Members.Count(m => m.Name == name))
      + outline.TopLevel.Count(m => m.Name == name);

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
    /// How this type indents its members, taken from the first member it already has. Measuring beats
    /// inferring a file-wide indent unit and adding it: a nested type, a member inside a namespace block and
    /// a top-level one all sit at different depths, and the existing members are the only thing that knows.
    /// <para>
    /// It reads the first member's own line, not "the first non-blank line inside the braces" — those are
    /// the same thing until the body is empty, where the latter finds the closing <c>}</c> and reports an
    /// indent of nothing, which is how an appended member ended up flush-left.
    /// </para>
    /// Null when the body is empty, which is the one case with nothing to measure.
    /// </summary>
    private static string? MemberIndent(string src, DeclarationAnchor type) =>
        type.BodyContentStart is { } first
            ? src[LineStart(src, first)..Math.Clamp(first, 0, src.Length)] is { } lead
              && lead.Trim().Length == 0 ? lead : SourceText.IndentOf(src[LineStart(src, first)..])
            : null;

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

    /// <summary>The start offset of the line before <paramref name="at"/> when that line is blank.</summary>
    private static int? PrecedingBlankLine(string src, int at)
    {
        if (at <= 0) return null;
        var start = LineStart(src, at - 1);
        return src[start..(at - 1)].Trim().Length == 0 ? start : null;
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
        Op.Import  => "import",    _               => "insert after",
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
