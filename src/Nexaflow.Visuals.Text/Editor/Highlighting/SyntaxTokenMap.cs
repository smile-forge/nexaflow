using System.Collections.Generic;

namespace Nexaflow.Visuals.Text.Editor.Highlighting;

/// <summary>
/// The role palette: the single source of truth mapping a syntax role onto its <c>TextSwatch.*</c> theme
/// resource key, for <em>both</em> highlighting engines — tree-sitter capture names
/// (<see cref="ResourceKey"/>, used by <see cref="TreeSitterColorizer"/> and the read-only
/// <c>CodeBlockView</c>) and AvalonEdit's .xshd named colours (<see cref="XshdResourceKey"/>, used by
/// <see cref="XshdTheming"/>). Roles resolve to brushes at paint/build time so a theme switch retints code;
/// an unrecognised role maps to null (no colour → default text brush).
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
    };

    /// <summary>The <c>TextSwatch.*</c> resource key for a capture, or null if the capture has no colour role.</summary>
    public static string? ResourceKey(string capture) =>
        CaptureToToken.TryGetValue(capture, out var key) ? key : null;

    /// <summary>
    /// The <c>TextSwatch.*</c> resource key for an AvalonEdit .xshd named colour, or null when the name maps
    /// to no role. The shipped definitions each name their colours differently ("Comment", "XmlString",
    /// "DigitNumber", "AttributeName"…), so this matches on substrings rather than an exact table — a
    /// heuristic by necessity, which is exactly why it is worth pinning down in tests.
    /// </summary>
    public static string? XshdResourceKey(string colorName)
    {
        var n = colorName.ToLowerInvariant();
        if (n.Contains("comment")) return "TextSwatch.Comment";
        if (n.Contains("string") || n.Contains("char")) return "TextSwatch.String";
        if (n.Contains("keyword")) return "TextSwatch.Keyword";
        if (n.Contains("digit") || n.Contains("number")) return "TextSwatch.Number";
        if (n.Contains("type") || n.Contains("class")) return "TextSwatch.Type";
        if (n.Contains("attribute")) return "TextSwatch.Attribute";
        if (n.Contains("tag") || n.Contains("element")) return "TextSwatch.Tag";
        if (n.Contains("value")) return "TextSwatch.String";
        if (n.Contains("punctuation") || n.Contains("operator")) return "TextSwatch.Operator";
        return null;
    }
}
