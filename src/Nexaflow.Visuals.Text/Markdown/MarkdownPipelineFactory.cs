using Markdig;
using Nexaflow.Visuals.Text.Markdown.Music;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Shared Markdig pipeline configuration used by every markdown renderer in the
/// app — the read-only <see cref="MarkdownView"/> control and the block-based
/// editor in <c>Nexaflow.Features.Markdown</c>.  Centralised so both stay in
/// sync (otherwise the editor would parse, say, a math fence and the read-only
/// view would not, producing divergent output).
/// </summary>
public static class MarkdownPipelineFactory
{
    /// <summary>Lazily-built pipeline with the full extension set.</summary>
    public static MarkdownPipeline Default { get; } =
        new MarkdownPipelineBuilder()
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
            .UseMusicNotation()   // #%abc … #% / #%lilypond … #% → sheet music
            .Build();
}
