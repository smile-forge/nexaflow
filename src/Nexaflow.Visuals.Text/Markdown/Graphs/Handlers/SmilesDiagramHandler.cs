using Nexaflow.Markdown.Chemistry;
using Nexaflow.Visuals.Text.Markdown.Chemistry;
using System.Windows;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Handlers;

/// <summary>
/// Handles <c>smiles</c> fenced code blocks — chemical structures drawn from SMILES strings, the format described
/// at <see href="https://markdown.org/tools/diagrams/chemistry/"/>.
///
/// <para>
/// Registered beside the barcode and music handlers for the same reason they are here: a structure is not a
/// diagram, but it arrives as one — a fenced block whose info string names a language, rendered to an element in
/// place of its source — and registering it here is what puts it on both markdown surfaces at once.
/// </para>
/// <para>
/// A seam and nothing more: <see cref="SmilesParser"/> reads the block, <see cref="SmilesPipeline"/> works out what
/// it means, and <see cref="SmilesBuilder"/> draws it.
/// </para>
/// </summary>
public sealed class SmilesDiagramHandler : IDiagramHandler
{
    public bool CanHandle(string language) =>
        language.Equals("smiles", StringComparison.OrdinalIgnoreCase);

    public FrameworkElement Render(string source, MarkdownPalette palette, Func<string, bool>? onNavigate = null)
        => Render(source, DiagramRenderOptions.For(palette, onNavigate));

    public FrameworkElement Render(string source, DiagramRenderOptions options) => SmilesBuilder.Element(source, options);
}
