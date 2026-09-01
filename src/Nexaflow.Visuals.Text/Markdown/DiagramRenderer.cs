using Nexaflow.Visuals.Text.Markdown.Graphs.Handlers;
using Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Dispatches diagram fenced code blocks to the appropriate
/// <see cref="IDiagramHandler"/>.  Knows only which languages are diagram
/// languages and how to display errors.  All content parsing and rendering
/// decisions live inside the handlers.
///
/// To add a new diagram language:
///   • For graph-based languages: <c>new GraphDiagramHandler(new MyParser())</c>
///   • For other types: implement <see cref="IDiagramHandler"/> and add an instance below.
/// </summary>
public static class DiagramRenderer
{
    private static readonly IDiagramHandler[] Handlers =
    [
        new MermaidDiagramHandler(),                           // mermaid (pie, flowchart, …)
        new GraphDiagramHandler(new NomnomlParser()),          // nomnoml
        new QrDiagramHandler(),                                // qr
    ];

    // ── Public API ─────────────────────────────────────────────────────────

    public static bool IsDiagramLanguage(string? info) =>
        info is not null && Handlers.Any(h => h.CanHandle(info));

    public static FrameworkElement Render(string language, string source, MarkdownPalette? palette = null,
        Func<string, bool>? onNavigate = null)
        => Render(language, source, DiagramRenderOptions.For(palette ?? MarkdownPalette.FromTheme(), onNavigate));

    public static FrameworkElement Render(string language, string source, DiagramRenderOptions options)
    {
        try
        {
            var handler = Handlers.FirstOrDefault(h => h.CanHandle(language));
            if (handler is null)
                return ErrorElement($"No handler for diagram language '{language}'.", source);

            return handler.Render(source, options);
        }
        catch (Exception ex)
        {
            return ErrorElement($"Diagram error: {ex.Message}", source);
        }
    }

    // ── Error display ──────────────────────────────────────────────────────

    private static readonly Brush ErrorBg     = Frozen(Color.FromRgb(0x2A, 0x10, 0x10));
    private static readonly Brush ErrorBorder = Frozen(Color.FromRgb(0xC0, 0x40, 0x40));
    private static readonly Brush ErrorText   = Frozen(Color.FromRgb(0xE8, 0xA0, 0xA0));
    private static readonly Brush CodeBg      = Frozen(Color.FromRgb(0x08, 0x0C, 0x16));
    private static readonly Brush CodeText    = Frozen(Color.FromRgb(0x78, 0x80, 0xA0));
    private static readonly FontFamily MonoFont = new("Consolas, Courier New");
    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    /// <summary>
    /// The house style for "this block could not be rendered": the message, then the source that
    /// produced it. Internal rather than private so a handler can meet its own no-throw contract
    /// without inventing a second look for the same failure.
    /// </summary>
    internal static FrameworkElement ErrorElement(string message, string source)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };

        stack.Children.Add(new TextBlock
        {
            Text         = message,
            Foreground   = ErrorText,
            FontFamily   = new FontFamily("Segoe UI"),
            FontSize     = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 4),
        });

        stack.Children.Add(new Border
        {
            Background      = CodeBg,
            BorderBrush     = ErrorBorder,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding         = new Thickness(8, 6, 8, 6),
            Child = new TextBlock
            {
                Text         = source.TrimEnd(),
                FontFamily   = MonoFont,
                FontSize     = 11,
                Foreground   = CodeText,
                TextWrapping = TextWrapping.NoWrap,
            }
        });

        return new Border
        {
            Background      = ErrorBg,
            BorderBrush     = ErrorBorder,
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(10, 8, 10, 8),
            Margin          = new Thickness(0, 4, 0, 10),
            Child           = stack,
        };
    }
}
