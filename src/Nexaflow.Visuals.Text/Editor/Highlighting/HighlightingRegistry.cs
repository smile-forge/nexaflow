using System.IO;

using Nexaflow.Syntax;

namespace Nexaflow.Visuals.Text.Editor.Highlighting;

/// <summary>How the editor should colour a document.</summary>
public enum HighlightMode { PlainText, TreeSitter }

/// <summary>The resolved highlighting for a file. Plain text ⇒ no colouring (and spell-check is eligible);
/// <see cref="HighlightMode.TreeSitter"/> carries the grammar id that colours it.</summary>
public sealed record HighlightResolution(HighlightMode Mode, string? TreeSitterLanguage = null)
{
    public static readonly HighlightResolution Plain = new(HighlightMode.PlainText);
}

/// <summary>
/// Picks how a file is coloured by its name: the tree-sitter grammar that reads it
/// (<see cref="TreeSitterLanguages.ForEdit"/>) — code, markup, scripts, the XML family, project files — or plain
/// text where none does, so the Windows spell-checker can attach instead.
/// </summary>
public static class HighlightingRegistry
{
    /// <summary>Registers a tree-sitter grammar for a set of extensions (called during tree-sitter setup).</summary>
    /// <remarks>The extension→grammar map itself lives in <see cref="TreeSitterLanguages"/> (Nexaflow.Syntax)
    /// so headless callers can resolve a grammar without WPF; this stays as the editor-side entry point.</remarks>
    public static void RegisterTreeSitter(string grammarId, params string[] extensions)
        => TreeSitterLanguages.Register(grammarId, extensions);

    /// <summary>How to colour <paramref name="fileName"/>.</summary>
    public static HighlightResolution Resolve(string fileName) =>
        TreeSitterLanguages.ForEdit(fileName) is { } grammar
            ? new HighlightResolution(HighlightMode.TreeSitter, grammar)
            : HighlightResolution.Plain;

    /// <summary>True when the file has a known syntax (structured mode); false ⇒ plain text (spell-check mode).</summary>
    public static bool IsStructured(string fileName) => Resolve(fileName).Mode != HighlightMode.PlainText;
}
