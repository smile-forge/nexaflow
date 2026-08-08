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
    /// <summary>
    /// Retints <paramref name="definition"/> to the theme that is active right now.
    /// <para>
    /// Applied on every resolve rather than once per definition. It reads as wasted work — the
    /// definitions are shared singletons, so the colours would already be set — but a theme switch
    /// rebuilds the window inside the <i>same process</i>, and both the definition and any
    /// "already done" bookkeeping would outlive it. Guarding this left code coloured for whichever
    /// theme happened to be loaded first. It is a couple of dozen property assignments.
    /// </para>
    /// </summary>
    public static void ApplyTheme(IHighlightingDefinition definition)
    {
        foreach (var color in definition.NamedHighlightingColors)
        {
            var key = SyntaxTokenMap.XshdResourceKey(color.Name);
            if (key is not null && Application.Current?.TryFindResource(key) is SolidColorBrush brush)
                color.Foreground = new SimpleHighlightingBrush(brush.Color);
        }
    }
}
