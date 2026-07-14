using Markdig;
using Markdig.Parsers;
using Markdig.Renderers;

namespace Nexaflow.Visuals.Text.Markdown.Music;

/// <summary>
/// Registers <see cref="MusicBlockParser"/> so <c>#% … #%</c> blocks parse into <see cref="MusicBlock"/>
/// nodes. Inserted <em>before</em> the ATX <see cref="HeadingBlockParser"/> — belt-and-braces, since
/// <c>#%</c> is not a valid ATX heading anyway (no space after <c>#</c>), matching how Markdig orders
/// its own <c>MathBlock</c> ahead of the fenced-code parser.
///
/// Only the parser half is wired: the app walks the Markdig AST itself in
/// <see cref="BlockRenderer"/>/<see cref="MarkdownFlowDocument"/>, so no <see cref="IMarkdownRenderer"/>
/// is needed.
/// </summary>
public sealed class MusicBlockExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.BlockParsers.Contains<MusicBlockParser>())
            pipeline.BlockParsers.InsertBefore<HeadingBlockParser>(new MusicBlockParser());
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer) { }
}

/// <summary>Fluent registration helper, matching Markdig's own <c>Use…()</c> convention.</summary>
public static class MusicNotationExtensions
{
    public static MarkdownPipelineBuilder UseMusicNotation(this MarkdownPipelineBuilder pipeline)
    {
        pipeline.Extensions.AddIfNotAlready<MusicBlockExtension>();
        return pipeline;
    }
}
