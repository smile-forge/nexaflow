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
        if (!GraphQuery.Index(graph).TryGetValue(nodeId, out var node))
            return Result.Fail($"No graph node '{nodeId}'. Use graph search to find one.");
        if (node.FilePath is not { Length: > 0 } rel)
            return Result.Fail($"'{nodeId}' is not a code node — it has no file.");
        if (node.Metadata?.GetValueOrDefault("ast") is not { Length: > 0 } astPath)
            return Result.Fail($"'{nodeId}' records no AST path, so its declaration cannot be located exactly.");
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
}
