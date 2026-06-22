using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;

namespace Nexaflow.Visuals.Text.Editor.Highlighting;

/// <summary>
/// Recolours AvalonEdit's built-in .xshd definitions to the app's Swatch palette so markup/config files
/// are readable on the dark theme (the shipped definitions are tuned for a light background). Maps each
/// named colour to a theme brush by name heuristic; applied once per definition.
/// </summary>
internal static class XshdTheming
{
    private static readonly HashSet<IHighlightingDefinition> Applied = [];

    public static void ApplyTheme(IHighlightingDefinition definition)
    {
        if (!Applied.Add(definition)) return; // idempotent; the definition is a shared singleton

        foreach (var color in definition.NamedHighlightingColors)
        {
            var key = TokenFor(color.Name);
            if (key is not null && Application.Current?.TryFindResource(key) is SolidColorBrush brush)
                color.Foreground = new SimpleHighlightingBrush(brush.Color);
        }
    }

    private static string? TokenFor(string name)
    {
        var n = name.ToLowerInvariant();
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
