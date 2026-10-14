using Markdig;
using Markdig.Extensions.AutoIdentifiers;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// How the hosts that write markdown out through Markdig read it — copying a block as HTML and as plain text
/// (<see cref="MarkdownClipboard"/>), and the help's contents and search text — with everything a document here can hold, and
/// every heading given the GitHub-style id the page's own reading names it by (<see cref="MarkdownAnchors"/>).
///
/// <para>
/// The page itself is read by the app's own reader (<c>MarkdownParser</c>); this is only what turns markdown into another
/// format, which Markdig's renderers do.
/// </para>
/// </summary>
public static class MarkdigReading
{
    /// <summary>The pipeline: every extension the app's reading enables, and the ids.</summary>
    public static MarkdownPipeline Pipeline { get; } = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .UsePipeTables()
        .UseGridTables()
        .UseTaskLists()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .UseDefinitionLists()
        .UseListExtras()
        .UseAbbreviations()
        .UseAlertBlocks()
        .UseFigures()
        .UseFooters()
        .UseCitations()
        .UseMathematics()
        .UseDiagrams()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .Build();
}
