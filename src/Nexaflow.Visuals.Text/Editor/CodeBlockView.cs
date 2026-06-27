using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Nexaflow.Syntax;
using Nexaflow.Visuals.Text.Editor.Highlighting;

namespace Nexaflow.Visuals.Text.Editor;

/// <summary>
/// A lightweight, read-only, syntax-highlighted code display: set <see cref="Code"/> + <see cref="GrammarId"/>
/// and it renders the text as coloured runs (tree-sitter highlight, same role→theme mapping as the editor).
/// Auto-sizes to its content (a <see cref="TextBlock"/> of <see cref="Run"/>s), so it drops cleanly into a
/// stacked list — e.g. a notebook's code cells — and the surrounding surface owns the scrolling.
/// </summary>
public sealed class CodeBlockView : ContentControl
{
    private static readonly FontFamily MonoFont = new("Cascadia Code, Consolas, Menlo, monospace");

    private readonly TextBlock _text = new()
    {
        FontFamily   = MonoFont,
        FontSize     = 12.5,
        TextWrapping = TextWrapping.NoWrap,
    };

    public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(
        nameof(Code), typeof(string), typeof(CodeBlockView), new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty GrammarIdProperty = DependencyProperty.Register(
        nameof(GrammarId), typeof(string), typeof(CodeBlockView), new PropertyMetadata(null, OnChanged));

    public string Code
    {
        get => (string)GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    /// <summary>The tree-sitter grammar id for the code (e.g. "python"); null/unknown ⇒ plain (uncoloured) text.</summary>
    public string? GrammarId
    {
        get => (string?)GetValue(GrammarIdProperty);
        set => SetValue(GrammarIdProperty, value);
    }

    public CodeBlockView()
    {
        Content = _text;
        _text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");   // themed default text colour
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((CodeBlockView)d).Rebuild();

    private void Rebuild()
    {
        _text.Inlines.Clear();
        var code = Code ?? "";
        if (code.Length == 0) return;

        var brushes = ColorPerChar(code, GrammarId);

        // Coalesce consecutive same-brush characters into runs.
        int i = 0;
        while (i < code.Length)
        {
            var brush = brushes?[i];
            int j = i + 1;
            while (j < code.Length && ReferenceEquals(brushes?[j], brush)) j++;
            var run = new Run(code[i..j]);
            if (brush is not null) run.Foreground = brush;
            _text.Inlines.Add(run);
            i = j;
        }
    }

    /// <summary>A per-character foreground brush (or null) from the grammar's highlight spans. Later spans win
    /// for an overlapping character — the same last-wins rule the editor colourizer uses.</summary>
    private Brush?[]? ColorPerChar(string code, string? grammarId)
    {
        if (string.IsNullOrEmpty(grammarId)) return null;
        using var hl = CodeHighlighter.TryCreate(grammarId);
        if (hl is null) return null;

        var cache = new System.Collections.Generic.Dictionary<string, Brush?>();
        var brushes = new Brush?[code.Length];
        foreach (var span in hl.Highlight(code))
        {
            var brush = BrushFor(span.Capture, cache);
            if (brush is null) continue;
            int end = System.Math.Min(span.Start + span.Length, code.Length);
            for (int k = System.Math.Max(span.Start, 0); k < end; k++) brushes[k] = brush;
        }
        return brushes;
    }

    private static Brush? BrushFor(string capture, System.Collections.Generic.Dictionary<string, Brush?> cache)
    {
        if (cache.TryGetValue(capture, out var cached)) return cached;
        Brush? brush = SyntaxTokenMap.ResourceKey(capture) is { } key
            ? Application.Current?.TryFindResource(key) as Brush
            : null;
        cache[capture] = brush;
        return brush;
    }
}
