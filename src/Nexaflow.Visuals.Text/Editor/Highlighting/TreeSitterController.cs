using System;
using System.Linq;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Folding;
using Nexaflow.Syntax;

namespace Nexaflow.Visuals.Text.Editor.Highlighting;

/// <summary>
/// Owns tree-sitter features for one editor: syntax colouring and structural code folding. Reparses the
/// document on edit (debounced) and tears everything down on dispose. Reparse is a full parse — fast for
/// editable-size files; the binding doesn't expose incremental edits in its managed surface.
/// </summary>
internal sealed class TreeSitterController : IDisposable
{
    private readonly TextEditor _editor;
    private readonly CodeHighlighter _highlighter;
    private readonly TreeSitterColorizer _colorizer = new();
    private readonly ColorSwatchRenderer _swatches = new();
    private readonly FoldingManager _foldingManager;
    private readonly DispatcherTimer _debounce;
    private bool _disposed;

    public TreeSitterController(TextEditor editor, CodeHighlighter highlighter)
    {
        _editor = editor;
        _highlighter = highlighter;

        _editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        _editor.TextArea.TextView.BackgroundRenderers.Add(_swatches);
        _foldingManager = FoldingManager.Install(_editor.TextArea); // adds the fold margin + [-]/[+] markers
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Reparse(); };
        _editor.Document.TextChanged += OnTextChanged;

        Reparse(); // colour/fold whatever is loaded now; reload fills it in via TextChanged
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void Reparse()
    {
        if (_disposed) return;

        var text = _editor.Document.Text; // single snapshot keeps colour + fold offsets consistent
        var spans = _highlighter.Highlight(text);
        _colorizer.SetSpans(spans);
        _swatches.SetSpans(text, _highlighter.GrammarId, spans);   // same spans, so they can never disagree

        var foldings = _highlighter.GetFolds(text)
            .Distinct()
            .Where(f => f.Start >= 0 && f.End <= text.Length && f.Start < f.End)
            .OrderBy(f => f.Start).ThenByDescending(f => f.End)
            .Select(f => new NewFolding(f.Start, f.End))
            .ToList();
        _foldingManager.UpdateFoldings(foldings, -1);

        _editor.TextArea.TextView.Redraw();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _editor.Document.TextChanged -= OnTextChanged;
        _debounce.Stop();
        _editor.TextArea.TextView.LineTransformers.Remove(_colorizer);
        _editor.TextArea.TextView.BackgroundRenderers.Remove(_swatches);
        FoldingManager.Uninstall(_foldingManager);
        _highlighter.Dispose();
    }
}
