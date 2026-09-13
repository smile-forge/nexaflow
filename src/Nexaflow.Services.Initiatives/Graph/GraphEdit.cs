using System.Collections.Generic;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Syntax;
using System;
using System.Linq;

namespace Nexaflow.Services.Initiatives.Graph;

/// <summary>
/// Addresses a <see cref="StructuralEdit"/> by graph node: turns a node id into the file, the AST path and
/// the name the graph believes that declaration has, and hands those to the engine.
/// <para>
/// The editing itself lives in <see cref="StructuralEdit"/>, in the syntax layer, because it needs a parser
/// and nothing else — an editor changing the buffer in front of it wants the same eight operations and has
/// no graph to address them with. What this adds is the part that <i>is</i> about the graph: the graph is
/// built from a checkout that may not be the working tree, so its record of a declaration is a lead rather
/// than an authority, and the engine is asked to prove it against the file in hand before anything is
/// written.
/// </para>
/// </summary>
public static class GraphEdit
{
    /// <summary>Reads a repo-relative file's raw content — line endings and byte-order mark intact, because
    /// they are preserved. Supplied by the caller so the CLI can resolve against the working tree and the app
    /// against its own root.</summary>
    public delegate string? ReadText(string relativePath);

    /// <summary>A file's before and after. Nothing is written until the caller decides to.</summary>
    public sealed record FileChange(string RelativePath, string OriginalText, string NewText,
                                    StructuralEdit.Hunk Hunk, ChangeKind Kind = ChangeKind.Changed);

    public sealed record Result(bool Ok, string Message, IReadOnlyList<FileChange> Changes,
                                IReadOnlyList<string> Notes)
    {
        public static Result Fail(string message) => new(false, message, [], []);
    }

    /// <summary>
    /// Works out the edit and proves it, without writing anything. The caller writes
    /// <see cref="FileChange.NewText"/> back only when <see cref="Result.Ok"/>.
    /// </summary>
    public static Result Plan(KnowledgeGraph graph, string nodeId, StructuralEdit.Op op, string? text,
                              ReadText read, StructuralEdit.Options? options = null, string? renameTo = null)
    {
        // A node id carries everything an edit needs — `code:<relpath>#<astpath>` names the file and the
        // declaration outright — so an id the graph has not indexed is still perfectly editable: a file
        // created a moment ago, or one on a branch the graph was not built from. The graph is how you FIND
        // an id. It is not what makes one valid, and a rebuild takes long enough that requiring one here
        // would make the tool feel untrustworthy for no gain: the file is re-parsed and re-verified either
        // way.
        // An element path finds the element inside a file, so it pairs with the id that names only the file. With
        // a declaration's id it would be a second answer to "which one", and the two could disagree.
        if (options?.At is { Length: > 0 })
            return nodeId.StartsWith("file:", StringComparison.Ordinal)
                ? AtScoped(nodeId["file:".Length..], op, text, options, renameTo, read)
                : Result.Fail($"An element path (--at) finds the element itself, so it goes with a file: id - "
                            + $"'{nodeId}' already names a declaration. Drop --at, or use file:<path> --at <path>.");

        if (!GraphQuery.Index(graph).TryGetValue(nodeId, out var node)) return FromId(nodeId, op, text, read, options, renameTo);
        if (node.FilePath is not { Length: > 0 } rel)
            return Result.Fail($"'{nodeId}' is not a code node — it has no file.");

        // An import belongs to the file, not to any declaration in it, so it needs neither an AST path nor
        // a label — which also lets a `file:` node address it.
        if (op is StructuralEdit.Op.Import) return Import(rel, text, read);

        if (node.Metadata?.GetValueOrDefault("ast") is not { Length: > 0 } astPath)
        {
            // A `file:` node names a file and nothing inside it. Most ops need a declaration, but a
            // substitution over the whole file is exactly how you reach what is not in one — a namespace
            // statement, a file-level attribute — so let that through rather than making it a hand edit.
            if (op is StructuralEdit.Op.Substitute) return FileScoped(rel, text, options, read);

            return Result.Fail(
                $"'{nodeId}' names a file rather than a declaration in one. Use a code: node for {op}, or "
              + "graph search to find the declaration you mean. (substitute and import work on a file: node, and "
              + "in an XML file --at <path> names an element.)");
        }
        if (node.Label is not { Length: > 0 } name)
            return Result.Fail($"'{nodeId}' has no label, so there is nothing to verify the declaration against.");

        if (read(rel) is not { } original)
            return Result.Fail($"Could not read {rel}.");

        var grammar = TreeSitterLanguages.ForEdit(rel);
        if (grammar is not { Length: > 0 })
            return Result.Fail($"No tree-sitter grammar covers {rel}, so an edit there cannot be verified.");

        var result = StructuralEdit.Apply(grammar, original, astPath, name, op, text, options, renameTo);
        if (!result.Ok || result.NewText is null || result.Hunk is null)
            return Result.Fail($"{result.Message} ({rel})");

        return new Result(true, $"{result.Message} in {rel}",
                          [new FileChange(rel, original, result.NewText, result.Hunk)], result.Notes);
    }

    /// <summary>
    /// Edits a node the graph does not hold, reading the file and the AST path straight out of the id. The
    /// graph's contribution when it DOES hold the node is one extra check — that the declaration is still
    /// called what the graph recorded — so its absence costs that check and nothing else.
    /// </summary>
    private static Result FromId(string nodeId, StructuralEdit.Op op, string? text, ReadText read,
                                 StructuralEdit.Options? options, string? renameTo)
    {
        if (nodeId.StartsWith("file:", StringComparison.Ordinal))
        {
            var path = nodeId["file:".Length..];
            return op switch
            {
                StructuralEdit.Op.Import     => Import(path, text, read),
                StructuralEdit.Op.Substitute => FileScoped(path, text, options, read),
                _ => Result.Fail($"'{nodeId}' names a file rather than a declaration in one. Use a code: id "
                               + $"for {op}. (substitute and import work on a file: id, and in an XML file --at <path> names an element.)"),
            };
        }

        if (!nodeId.StartsWith("code:", StringComparison.Ordinal) || !nodeId.Contains('#'))
            return Result.Fail(
                $"No graph node '{nodeId}', and it is not a code:<file>#<astpath> or file:<path> id that "
              + "could be read directly. Use graph search to find one.");

        var hash    = nodeId.IndexOf('#');
        var rel     = nodeId["code:".Length..hash];
        var astPath = nodeId[(hash + 1)..];

        if (read(rel) is not { } original) return Result.Fail($"Could not read {rel}.");
        if (TreeSitterLanguages.ForEdit(rel) is not { Length: > 0 } grammar)
            return Result.Fail($"No tree-sitter grammar covers {rel}, so an edit there cannot be verified.");

        if (op is StructuralEdit.Op.Import) return Import(rel, text, read);

        // The path-only overload takes the name to verify against from the path itself, which is the right
        // source when there is no graph record to cross-check it with.
        var result = StructuralEdit.Apply(grammar, original, astPath, op, text, options, renameTo);
        if (!result.Ok || result.NewText is null || result.Hunk is null)
            return Result.Fail($"{result.Message} ({rel})");

        return new Result(true, $"{result.Message} in {rel}",
                          [new FileChange(rel, original, result.NewText, result.Hunk)],
                          [.. result.Notes, $"'{nodeId}' is not in the graph; it was addressed from the id."]);
    }

    private static Result FileScoped(string rel, string? text, StructuralEdit.Options? options, ReadText read)
    {
        if (read(rel) is not { } original) return Result.Fail($"Could not read {rel}.");

        var o = options ?? new StructuralEdit.Options();

        // A grammar proves the file still parses afterwards. A text file with none can still be edited: it has no
        // shape a substitution could break, so the matching rules are the whole of the check. Something that is
        // not text at all — an image, a DLL — has no characters to match, and is refused.
        StructuralEdit.Result result;
        if (TreeSitterLanguages.ForEdit(rel) is { Length: > 0 } grammar)
            result = StructuralEdit.SubstituteInFile(grammar, original, text, o);
        else if (IsText(original))
            result = StructuralEdit.SubstituteInText(original, text, o);
        else
            return Result.Fail($"{rel} is not text, so there is nothing a substitution could safely change.");

        if (!result.Ok || result.NewText is null || result.Hunk is null)
            return Result.Fail($"{result.Message} ({rel})");

        return new Result(true, $"{result.Message} {rel}",
                          [new FileChange(rel, original, result.NewText, result.Hunk)], result.Notes);
    }

    /// <summary>
    /// An edit to the element an XPath names (<see cref="StructuralEdit.Options.At"/>) in an XML-family file —
    /// a view, a project, a props file. The file's own id is the address; the path picks the element in it.
    /// </summary>
    private static Result AtScoped(string rel, StructuralEdit.Op op, string? text, StructuralEdit.Options options,
                                   string? renameTo, ReadText read)
    {
        var grammar = TreeSitterLanguages.ForEdit(rel);
        if (!TreeSitterLanguages.IsXml(grammar))
            return Result.Fail($"An element path (--at) needs an XML-family file, and {rel} "
                             + (grammar is null ? "has no grammar." : $"parses as {grammar}."));
        if (read(rel) is not { } original) return Result.Fail($"Could not read {rel}.");

        var result = StructuralEdit.ApplyAt(grammar!, original, op, text, options, renameTo);
        if (!result.Ok || result.NewText is null || result.Hunk is null)
            return Result.Fail($"{result.Message} ({rel})");

        return new Result(true, $"{result.Message} in {rel}",
                          [new FileChange(rel, original, result.NewText, result.Hunk)], result.Notes);
    }

    /// <summary>
    /// Whether what was read is text. A NUL is the one character no text format writes and every binary one
    /// does, which is the same test git applies before calling a file binary.
    /// </summary>
    private static bool IsText(string content) => !content.Contains('\0');

    private static Result Import(string rel, string? text, ReadText read)
    {
        if (read(rel) is not { } original) return Result.Fail($"Could not read {rel}.");

        var grammar = TreeSitterLanguages.ForFile(rel);
        if (grammar is not { Length: > 0 })
            return Result.Fail($"No tree-sitter grammar covers {rel}, so an import cannot be placed.");

        var result = StructuralEdit.AddImport(grammar, original, text ?? "");
        if (!result.Ok || result.NewText is null || result.Hunk is null)
            return Result.Fail($"{result.Message} ({rel})");

        return new Result(true, $"{result.Message} in {rel}",
                          [new FileChange(rel, original, result.NewText, result.Hunk)], result.Notes);
    }

    /// <summary>
    /// A new file. Refused when one is already there, and — for a file a grammar covers — when the content does not
    /// parse, because the next tool to read it sees a root ERROR node and every declaration in it vanishes from the graph.
    /// A new file has no line endings of its own, so it takes its neighbours' (<paramref name="newlineFor"/>) rather
    /// than the machine's.
    /// </summary>
    public static Result Create(string rel, string? text, ReadText read, Func<string, string> newlineFor)
    {
        if (text is null) return Result.Fail($"Creating {rel} needs its content.");
        if (read(rel) is not null) return Result.Fail($"{rel} already exists — use an edit op to change it.");

        var grammar = TreeSitterLanguages.ForEdit(rel);
        if (grammar is { Length: > 0 } && !new DeclarationAnchors().ParsesCleanly(grammar, text))
            return Result.Fail($"That content does not parse as {grammar}, so {rel} was not created.");

        var newline = newlineFor(rel);
        var body    = string.Join(newline, SourceText.BlockOf(text)) + newline;
        var lines   = SourceText.Of(body).Lines;

        return new Result(true, $"created {rel} ({lines.Count} lines)",
                          [new FileChange(rel, "", body, new StructuralEdit.Hunk(1, [], [.. lines]), ChangeKind.Created)], []);
    }

    /// <summary>
    /// Moves a declaration out of its file and into a type (a <c>code:</c> destination) or a file (<c>file:</c>, created
    /// when it does not exist). Both halves are ordinary edits, each resolved and verified against the parse, and the
    /// result is one change per file — so a move either lands whole or is refused whole.
    /// <para>
    /// Moving to another file carries the imports the destination lacks, and a C# file left holding nothing but imports
    /// and its namespace is removed rather than left behind as an empty shell.
    /// </para>
    /// </summary>
    public static Result Move(KnowledgeGraph graph, string nodeId, string destination, ReadText read,
                              Func<string, string> newlineFor)
    {
        if (!TryAddress(graph, nodeId, read, out var rel, out var astPath, out var name, out var why))
            return Result.Fail(why);
        if (read(rel) is not { } source) return Result.Fail($"Could not read {rel}.");
        if (TreeSitterLanguages.ForEdit(rel) is not { Length: > 0 } grammar)
            return Result.Fail($"No tree-sitter grammar covers {rel}, so a move out of it cannot be verified.");

        var intoFile = destination.StartsWith("file:", StringComparison.Ordinal);
        string destRel, destAst = "";
        if (intoFile) destRel = destination["file:".Length..];
        else if (!TryAddress(graph, destination, read, out destRel, out destAst, out _, out var whereNot))
            return Result.Fail($"Move {name} where? {whereNot} A move goes --to code:<file>#<type path> (into a type) or "
                             + "--to file:<path> (a file, created if it is not there).");

        if (intoFile && destRel == rel) return Result.Fail($"{name} is already in {rel}.");
        if (!intoFile && destRel == rel && (destAst == astPath || destAst.StartsWith(astPath + "/", StringComparison.Ordinal)))
            return Result.Fail($"{name} cannot be moved into itself.");

        var (taken, error) = StructuralEdit.Take(grammar, source, astPath, name);
        if (taken is null) return Result.Fail($"{error} ({rel})");

        var notes = new List<string>(taken.Notes);
        string? Seen(string r) => r == rel ? taken.Remaining : read(r);
        var destBefore = Seen(destRel);

        // A C# member at the top of a file is not a member of anything, so it has to be given a type to go into.
        if (intoFile && !taken.IsType && grammar == "c-sharp")
            return Result.Fail($"{name} is a member, and a C# member needs a type to live in — move it "
                             + "--to code:<file>#<type path>.");

        Result placed;
        if (!intoFile)
            placed = Plan(graph, destination, StructuralEdit.Op.Append, taken.Declaration, Seen);
        else if (destBefore is not null)
        {
            var last = StructuralEdit.TopLevelDeclarations(TreeSitterLanguages.ForEdit(destRel) ?? "", destBefore);
            placed = last.Count == 0
                ? Result.Fail($"{destRel} declares nothing for {name} to sit beside. Move it into a type in it "
                            + "(--to code:<file>#<type path>), or to a new file.")
                : Plan(graph, $"code:{destRel}#{last[^1].AstPath}", StructuralEdit.Op.InsertAfter, taken.Declaration, Seen);
        }
        else if (TreeSitterLanguages.ForEdit(destRel) != grammar)
            return Result.Fail($"{destRel} would not parse as {grammar}, which is what {name} is written in.");
        else
            placed = Create(destRel, StructuralEdit.NewFileFor(grammar, source, taken.Declaration, newlineFor(destRel)),
                            Seen, newlineFor);

        if (!placed.Ok || placed.Changes.Count == 0) return Result.Fail(placed.Message);
        notes.AddRange(placed.Notes.Where(n => !n.Contains("is not in the graph", StringComparison.Ordinal)));

        var destAfter = placed.Changes[0].NewText;
        if (destRel != rel && destBefore is not null)
        {
            destAfter = WithImportsFrom(grammar, source, destRel, destAfter, notes);

            if (StructuralEdit.NamespaceOf(grammar, source) is { } from
                && StructuralEdit.NamespaceOf(grammar, destBefore) is { } to && from != to)
                notes.Add($"{name} moved from namespace {from} to {to} — code that names it may now need a using {to};");
        }

        var changes = new List<FileChange>();
        if (destRel == rel)
            changes.Add(new FileChange(rel, source, destAfter, StructuralEdit.HunkOf(source, destAfter)));
        else
        {
            if (StructuralEdit.IsEmptyShell(grammar, taken.Remaining))
            {
                changes.Add(new FileChange(rel, source, "", new StructuralEdit.Hunk(1, [.. SourceText.Of(source).Lines], []),
                                           ChangeKind.Deleted));
                notes.Add($"{rel} held nothing but {name}, so it was removed.");
            }
            else
                changes.Add(new FileChange(rel, source, taken.Remaining, StructuralEdit.HunkOf(source, taken.Remaining)));

            changes.Add(destBefore is null
                ? placed.Changes[0]
                : new FileChange(destRel, destBefore, destAfter, StructuralEdit.HunkOf(destBefore, destAfter)));

            if (taken.IsType)
                notes.Add($"a snaplink naming {name} in {rel} now points at the wrong file — nfi remap {rel} {destRel} "
                        + $"--class {name} moves it.");
        }

        return new Result(true, $"move {name} from {rel} to {(intoFile ? destRel : destination)}", changes, notes);
    }

    /// <summary>
    /// Adds to <paramref name="destination"/> the imports <paramref name="source"/> has and it lacks. All of them rather
    /// than a guess at which the moved code uses: an unused import costs a line, and a missing one costs a build.
    /// </summary>
    private static string WithImportsFrom(string grammar, string source, string destRel, string destination,
                                          List<string> notes)
    {
        var destGrammar = TreeSitterLanguages.ForEdit(destRel) ?? "";
        var anchors     = new DeclarationAnchors();
        var have        = anchors.Imports(destGrammar, destination).ToHashSet(StringComparer.Ordinal);
        var added       = new List<string>();

        foreach (var import in anchors.Imports(grammar, source))
        {
            if (have.Contains(import)) continue;
            var result = StructuralEdit.AddImport(destGrammar, destination, import);
            if (!result.Ok || result.NewText is null) continue;

            destination = result.NewText;
            added.Add(import);
        }

        if (added.Count > 0)
            notes.Add($"{destRel} gained the import(s) it lacked from where the code came from: {string.Join(" ", added)}");
        return destination;
    }

    /// <summary>
    /// The file, AST path and name a <c>code:</c> id names — from the graph's record when it holds one, and from the id
    /// and the file itself when it does not, which is the same rule every other edit follows.
    /// </summary>
    private static bool TryAddress(KnowledgeGraph graph, string nodeId, ReadText read, out string rel, out string astPath,
                                   out string name, out string error)
    {
        rel = astPath = name = error = "";

        if (GraphQuery.Index(graph).TryGetValue(nodeId, out var node)
            && node.FilePath is { Length: > 0 } path
            && node.Metadata?.GetValueOrDefault("ast") is { Length: > 0 } ast
            && node.Label is { Length: > 0 } label)
        {
            (rel, astPath, name) = (path, ast, label);
            return true;
        }

        if (!nodeId.StartsWith("code:", StringComparison.Ordinal) || !nodeId.Contains('#'))
        {
            error = $"'{nodeId}' does not name a declaration.";
            return false;
        }

        var hash = nodeId.IndexOf('#');
        rel     = nodeId["code:".Length..hash];
        astPath = nodeId[(hash + 1)..];

        var inPath = astPath;
        name = (read(rel) is { } text && TreeSitterLanguages.ForEdit(rel) is { Length: > 0 } grammar
                    ? StructuralEdit.Declarations(grammar, text).FirstOrDefault(d => d.AstPath == inPath)?.Name
                    : null)
            ?? StructuralEdit.NameInPath(astPath) ?? "";

        if (name.Length > 0) return true;
        error = $"'{nodeId}' does not end in a declaration's name.";
        return false;
    }

    /// <summary>What a <see cref="FileChange"/> does to its file.</summary>
    public enum ChangeKind
    {
        /// <summary>The file is rewritten with <see cref="FileChange.NewText"/>.</summary>
        Changed,
        /// <summary>The file does not exist yet and is written with <see cref="FileChange.NewText"/>.</summary>
        Created,
        /// <summary>The file is removed.</summary>
        Deleted,
    }

    /// <summary>
    /// The file, AST path and name a <c>code:</c> id names — what a caller needs to ask anything else about the
    /// declaration, such as where a compiler would find its symbol.
    /// </summary>
    public static (string RelativePath, string AstPath, string Name)? Address(KnowledgeGraph graph, string nodeId, ReadText read,
                                                                           out string error)
    {
        return TryAddress(graph, nodeId, read, out var rel, out var astPath, out var name, out error)
            ? (rel, astPath, name)
            : null;
    }

    /// <summary>
    /// A file's whole text replaced by a caller that worked the new text out itself — a rename carried to every reference a
    /// compiler found. Refused unless the file is still exactly the text the caller worked from, and, for a file a grammar
    /// covers, unless the result still parses.
    /// </summary>
    public static Result Rewrite(string rel, string before, string after, string description, ReadText read)
    {
        if (read(rel) is not { } current) return Result.Fail($"Could not read {rel}.");
        if (!string.Equals(current, before, StringComparison.Ordinal))
            return Result.Fail($"{rel} is not the text this change was worked out from — an earlier step changed it. Put this "
                             + "step first.");

        var anchors = new DeclarationAnchors();
        if (TreeSitterLanguages.ForEdit(rel) is { Length: > 0 } grammar
            && anchors.ParsesCleanly(grammar, before) && !anchors.ParsesCleanly(grammar, after))
            return Result.Fail($"{description} would leave {rel} unparseable, so it was not applied.");

        return new Result(true, $"{description} in {rel}", [new FileChange(rel, before, after, StructuralEdit.HunkOf(before, after))], []);
    }
}
