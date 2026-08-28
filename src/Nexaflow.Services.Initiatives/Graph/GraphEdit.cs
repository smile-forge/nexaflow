using System.Collections.Generic;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Syntax;

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
                                    StructuralEdit.Hunk Hunk);

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
              + "graph search to find the declaration you mean. (substitute and import work on a file: node.)");
        }
        if (node.Label is not { Length: > 0 } name)
            return Result.Fail($"'{nodeId}' has no label, so there is nothing to verify the declaration against.");

        if (read(rel) is not { } original)
            return Result.Fail($"Could not read {rel}.");

        var grammar = TreeSitterLanguages.ForFile(rel);
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
                               + $"for {op}. (substitute and import work on a file: id.)"),
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
        if (TreeSitterLanguages.ForFile(rel) is not { Length: > 0 } grammar)
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

        var grammar = TreeSitterLanguages.ForFile(rel);
        if (grammar is not { Length: > 0 })
            return Result.Fail($"No tree-sitter grammar covers {rel}, so an edit there cannot be verified.");

        var result = StructuralEdit.SubstituteInFile(grammar, original, text, options ?? new StructuralEdit.Options());
        if (!result.Ok || result.NewText is null || result.Hunk is null)
            return Result.Fail($"{result.Message} ({rel})");

        return new Result(true, $"{result.Message} {rel}",
                          [new FileChange(rel, original, result.NewText, result.Hunk)], result.Notes);
    }

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
}
