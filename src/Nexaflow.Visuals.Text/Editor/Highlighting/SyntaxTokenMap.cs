using System.Collections.Generic;

namespace Nexaflow.Visuals.Text.Editor.Highlighting;

/// <summary>
/// The role palette: the single source of truth mapping a syntax role onto its <c>TextSwatch.*</c> theme
/// resource key, by tree-sitter capture name (<see cref="ResourceKey"/>), for every surface that colours code.
/// Roles resolve to brushes at paint/build time so a theme switch retints code; an unrecognised role maps to null
/// (no colour → default text brush).
/// </summary>
internal static class SyntaxTokenMap
{
    private static readonly Dictionary<string, string> CaptureToToken = new()
    {
        ["comment"]   = "TextSwatch.Comment",
        ["string"]    = "TextSwatch.String",
        ["number"]    = "TextSwatch.Number",
        ["keyword"]   = "TextSwatch.Keyword",
        ["type"]      = "TextSwatch.Type",
        ["constant"]  = "TextSwatch.Constant",
        ["function"]  = "TextSwatch.Function",
        ["parameter"] = "TextSwatch.Parameter",
        ["variable"]  = "TextSwatch.Parameter",
        ["tag"]       = "TextSwatch.Tag",         // html/markup element names
        ["attribute"] = "TextSwatch.Attribute",   // html attributes, css properties
        ["operator"]  = "TextSwatch.Operator",    // markup delimiters (< > /> =), operators
        ["whitespace"] = "TextSwatch.Whitespace", // the faint marks shown over spaces, tabs and line ends
        ["inserted"]  = "TextSwatch.Inserted",    // the lines a diff adds
        ["deleted"]   = "TextSwatch.Deleted",     // the lines a diff takes out
    };

    /// <summary>The <c>TextSwatch.*</c> resource key for a capture, or null if the capture has no colour role.</summary>
    public static string? ResourceKey(string capture) =>
        CaptureToToken.TryGetValue(capture, out var key) ? key : null;
}
