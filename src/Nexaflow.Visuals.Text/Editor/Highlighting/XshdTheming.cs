using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;

namespace Nexaflow.Visuals.Text.Editor.Highlighting;

/// <summary>
/// Recolours AvalonEdit's built-in .xshd definitions to the app's Swatch palette so markup/config files
/// are readable on the dark theme (the shipped definitions are tuned for a light background). Each named
/// colour is mapped to a role by <see cref="SyntaxTokenMap.XshdResourceKey"/> — the same role palette
/// tree-sitter colouring uses — and applied once per definition.
/// </summary>
internal static class XshdTheming
{
    private static readonly HashSet<IHighlightingDefinition> Applied = [];

    public static void ApplyTheme(IHighlightingDefinition definition)
    {
        if (!Applied.Add(definition)) return; // idempotent; the definition is a shared singleton

        foreach (var color in definition.NamedHighlightingColors)
        {
            var key = SyntaxTokenMap.XshdResourceKey(color.Name);
            if (key is not null && Application.Current?.TryFindResource(key) is SolidColorBrush brush)
                color.Foreground = new SimpleHighlightingBrush(brush.Color);
        }
    }
}
