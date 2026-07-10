using System.IO;
using ICSharpCode.AvalonEdit.Highlighting;
using Nexaflow.Syntax;

namespace Nexaflow.Visuals.Text.Editor.Highlighting;

/// <summary>How the editor should colour a document.</summary>
public enum HighlightMode { PlainText, Xshd, TreeSitter }

/// <summary>The resolved highlighting strategy for a file. Plain text ⇒ no colouring (and spell-check is
/// eligible); <see cref="HighlightMode.Xshd"/> carries an AvalonEdit definition; <see cref="HighlightMode.TreeSitter"/>
/// carries a grammar id the tree-sitter colourizer understands.</summary>
public sealed record HighlightResolution(
    HighlightMode Mode,
    IHighlightingDefinition? Definition = null,
    string? TreeSitterLanguage = null)
{
    public static readonly HighlightResolution Plain = new(HighlightMode.PlainText);
}

/// <summary>
/// Picks a highlighting strategy by file extension. Real code languages route to tree-sitter (which also
/// yields a parse tree for AI/graphify); simpler/markup formats use AvalonEdit's built-in .xshd; anything
/// unrecognised stays plain text (so the Windows spell-checker can attach instead).
/// </summary>
public static class HighlightingRegistry
{
    /// <summary>Registers a tree-sitter grammar for a set of extensions (called during tree-sitter setup).</summary>
    /// <remarks>The extension→grammar map itself lives in <see cref="TreeSitterLanguages"/> (Nexaflow.Syntax)
    /// so headless callers can resolve a grammar without WPF; this stays as the editor-side entry point.</remarks>
    public static void RegisterTreeSitter(string grammarId, params string[] extensions)
        => TreeSitterLanguages.Register(grammarId, extensions);

    public static HighlightResolution Resolve(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext)) return HighlightResolution.Plain;

        if (TreeSitterLanguages.ForFile(fileName) is { } grammar)
            return new HighlightResolution(HighlightMode.TreeSitter, TreeSitterLanguage: grammar);

        var def = HighlightingManager.Instance.GetDefinitionByExtension(ext);
        return def is not null
            ? new HighlightResolution(HighlightMode.Xshd, def)
            : HighlightResolution.Plain;
    }

    /// <summary>True when the file has a known syntax (structured mode); false ⇒ plain text (spell-check mode).</summary>
    public static bool IsStructured(string fileName) => Resolve(fileName).Mode != HighlightMode.PlainText;
}
