using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using Nexaflow.Features.Common;
using Nexaflow.Syntax;
using Nexaflow.Visuals.Text.Editor.Highlighting;

namespace Nexaflow.Visuals.Text.Editor;

/// <summary>
/// The shared read-write editor control. Thin: it binds the AvalonEdit document to the view-model, themes
/// the line-number gutter, and exposes <see cref="OnEditorReady"/> so a structured/code editor subclass can
/// attach colorizers/renderers without the base knowing about them.
/// </summary>
public partial class FileTextEditorView : UserControl, IPageView
{
    private readonly FileTextEditorViewModel _vm;

    IPageViewModel? IPageView.ViewModel => _vm;

    public FileTextEditorView(FileTextEditorViewModel vm)
    {
        InitializeComponent();

        _vm = vm;
        DataContext = vm;

        Editor.Document = vm.Document;
        vm.EditorAccess = () => Editor.TextArea; // lets commands read the live selection
        if (TryFindResource("TextMutedBrush") is Brush gutter)
            Editor.LineNumbersForeground = gutter;

        ApplyHighlighting(vm.FileName);
        OnEditorReady(Editor);

        vm.ScrollToLineRequested += ScrollEditorToLine;

        Loaded   += async (_, _) => await _vm.LoadAsync();
        Unloaded += (_, _) => { _treeSitter?.Dispose(); _vm.ScrollToLineRequested -= ScrollEditorToLine; _vm.Dispose(); };
    }

    /// <summary>Moves the caret to <paramref name="line"/> (1-based) and scrolls it into view. Driven by the
    /// view-model's <see cref="FileTextEditorViewModel.ScrollToLineRequested"/> (e.g. the As Code structure panel).</summary>
    private void ScrollEditorToLine(int line)
    {
        if (Editor.Document is null || line < 1 || line > Editor.Document.LineCount) return;
        Editor.CaretOffset = Editor.Document.GetLineByNumber(line).Offset;
        Dispatcher.InvokeAsync(() =>
        {
            Editor.ScrollToLine(line);
            Editor.TextArea.Caret.BringCaretToView();
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    private TreeSitterController? _treeSitter;

    /// <summary>Override to attach background renderers / line transformers (syntax highlighting,
    /// spell-check) to the editor. Called once after the document is wired.</summary>
    protected virtual void OnEditorReady(TextEditor editor) { }

    private void ApplyHighlighting(string fileName)
    {
        var resolution = HighlightingRegistry.Resolve(fileName);
        switch (resolution.Mode)
        {
            case HighlightMode.Xshd:
                XshdTheming.ApplyTheme(resolution.Definition!); // retint built-in colours to the Swatch palette
                Editor.SyntaxHighlighting = resolution.Definition;
                break;
            case HighlightMode.TreeSitter:
                var highlighter = CodeHighlighter.TryCreate(resolution.TreeSitterLanguage!);
                if (highlighter is not null)
                    _treeSitter = new TreeSitterController(Editor, highlighter);
                else
                    Editor.SyntaxHighlighting = null; // native/query unavailable — degrade to plain text
                break;
            default:
                Editor.SyntaxHighlighting = null;
                break;
        }
    }

    // Opens a command group's dropdown below its toolbar button.
    private void CommandGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }
}
