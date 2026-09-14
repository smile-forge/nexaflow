using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nexaflow.Syntax;

/// <summary>
/// The parts of <see cref="StructuralEdit"/> a move is made of. A move is two edits, and only the first is new: lift the
/// declaration out of its file, then place it with an ordinary <see cref="StructuralEdit.Op.Append"/> or
/// <see cref="StructuralEdit.Op.InsertAfter"/> — or as the body of a new file. Keeping it to those pieces is what gives
/// a move the same guarantees every other edit has, since each half is resolved against the parse and re-verified.
/// </summary>
public static partial class StructuralEdit
{
    /// <summary>A declaration lifted out of its file.</summary>
    /// <param name="Remaining">The file without it, exactly as <see cref="Op.Delete"/> would leave it.</param>
    /// <param name="Declaration">The declaration as it stood — doc comment and attributes included, at its original
    /// indentation, which every placing operation sets aside and replaces with its destination's.</param>
    /// <param name="AstPath">The path it was found at, which differs from the one asked for when it had moved.</param>
    /// <param name="IsType">Whether it is a type rather than a member — what decides where it can go.</param>
    public sealed record Taken(string Remaining, string Declaration, string AstPath, bool IsType,
                               IReadOnlyList<string> Notes);

    /// <summary>
    /// Lifts the declaration <paramref name="astPath"/> names out of <paramref name="source"/>. Returns why not when it
    /// cannot be found, or when this kind of file has no declarations to move.
    /// </summary>
    public static (Taken? Taken, string? Error) Take(string grammarId, string source, string astPath, string expectedName)
    {
        if (string.IsNullOrEmpty(grammarId))
            return (null, "No tree-sitter grammar covers this file, so nothing in it can be lifted out and verified.");
        if (TreeSitterLanguages.IsXml(grammarId))
            return (null, "Moving an XML element is not supported: insert it where it belongs, then delete it here.");

        var notes     = new List<string>();
        var extractor = new CodeStructureExtractor();
        if (Resolve(grammarId, source, ref astPath, expectedName, extractor, notes, out var span) is { } unresolved)
            return (null, unresolved);

        var anchor = new DeclarationAnchors().Find(grammarId, source, expectedName, span.Line, span.EndLine);
        if (anchor is null)
            return (null, $"The parser found no declaration named '{expectedName}' at line {span.Line}. Refusing to "
                        + "move it on position alone.");

        var removed = Apply(grammarId, source, astPath, expectedName, Op.Delete, null);
        if (!removed.Ok || removed.NewText is null) return (null, removed.Message);

        var from        = LineStart(source, anchor.TriviaStart);
        var to          = Math.Min(LineEndInclusive(source, anchor.End) + 1, source.Length);
        var declaration = source[from..to].TrimEnd('\r', '\n');
        var isType      = astPath.Split('/')[^1].StartsWith("T:", StringComparison.Ordinal);

        return (new Taken(removed.NewText, declaration, astPath, isType, [.. notes, .. removed.Notes]), null);
    }

    /// <summary>
    /// The text of a new file for <paramref name="declaration"/>, moved out of <paramref name="source"/>: the source
    /// file's imports and, in C#, its namespace, so the declaration arrives with what it was written against rather
    /// than as a file that fails to compile for want of a <c>using</c>.
    /// </summary>
    public static string NewFileFor(string grammarId, string source, string declaration, string newline)
    {
        var parts = new List<string>();

        if (new DeclarationAnchors().ImportRegion(grammarId, source).LastImportEnd is { } end)
            parts.Add(string.Join(newline, SourceText.Of(source[..(LineEndInclusive(source, end) + 1)]).Lines).TrimEnd());

        if (NamespaceOf(grammarId, source) is { } ns) parts.Add($"namespace {ns};");

        parts.Add(Block(declaration, "", newline));
        return string.Join(newline + newline, parts) + newline;
    }

    /// <summary>The namespace a C# file declares, file-scoped or block — null for another language, or for none.</summary>
    public static string? NamespaceOf(string grammarId, string source)
    {
        if (grammarId != "c-sharp") return null;

        var declared = Regex.Match(source, @"^[ \t]*namespace[ \t]+([\w.@]+)[ \t]*(;|\{|\r?$)", RegexOptions.Multiline);
        return declared.Success ? declared.Groups[1].Value : null;
    }

    /// <summary>
    /// Whether a C# file holds nothing but imports, a file-scoped namespace and comments — what a file is reduced to
    /// once the only declaration in it has moved out, and so a file with nothing left worth keeping. Anything else at
    /// all (a block namespace, an assembly attribute, a directive) counts as content, so a file is only ever judged
    /// empty when it plainly is.
    /// </summary>
    public static bool IsEmptyShell(string grammarId, string text)
    {
        if (grammarId != "c-sharp") return false;

        foreach (var line in SourceText.Of(text).Lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
            if (Regex.IsMatch(trimmed, @"^(global\s+)?using\s[^;]*;$")) continue;
            if (Regex.IsMatch(trimmed, @"^namespace\s+[\w.@]+\s*;$")) continue;
            return false;
        }
        return true;
    }

    /// <summary>The declarations at the top of a file — the ones not inside a type — in document order.</summary>
    public static IReadOnlyList<Declaration> TopLevelDeclarations(string grammarId, string source) =>
        [.. Declarations(grammarId, source).Where(d => !d.AstPath.Contains('/', StringComparison.Ordinal))];
}
