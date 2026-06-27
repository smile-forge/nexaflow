using System.Collections.Generic;

namespace Nexaflow.Visuals.Text.Editor.Highlighting;

/// <summary>
/// Maps a tree-sitter highlight capture name to its <c>TextSwatch.*</c> theme resource key — the single
/// source of truth shared by the editor colourizer (<see cref="TreeSitterColorizer"/>) and any read-only
/// code display (<c>CodeBlockView</c>). Roles resolve to brushes at paint/build time so a theme switch
/// retints code; an unknown capture maps to null (no colour → default text brush).
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
    };

    /// <summary>The <c>TextSwatch.*</c> resource key for a capture, or null if the capture has no colour role.</summary>
    public static string? ResourceKey(string capture) =>
        CaptureToToken.TryGetValue(capture, out var key) ? key : null;
}
