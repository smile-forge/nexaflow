using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax;

namespace Nexaflow.Visuals.Text.Markdown.Music;

/// <summary>
/// Parses a <c>#% … #%</c> musical-notation block. The opening fence is <c>#%</c> optionally followed by
/// a dialect tag (<c>#%abc</c>, <c>#%lilypond</c>); the closing fence is a line whose only content is
/// <c>#%</c>. Everything between is captured verbatim onto the <see cref="MusicBlock"/>. This is the
/// repo's first custom Markdig block parser — modelled on the fenced-code lifecycle
/// (open → <see cref="BlockState.ContinueDiscard"/>, content → <see cref="BlockState.Continue"/>,
/// close → <see cref="BlockState.BreakDiscard"/>) so it treats blank lines as content, not terminators.
/// </summary>
public sealed class MusicBlockParser : BlockParser
{
    public MusicBlockParser()
    {
        // A '#' start is a candidate; TryOpen confirms the following '%'.
        OpeningCharacters = ['#'];
    }

    public override BlockState TryOpen(BlockProcessor processor)
    {
        // A 4-space indent is a code block, not a fence.
        if (processor.IsCodeIndent) return BlockState.None;

        var line = processor.Line;
        if (line.CurrentChar != '#' || line.PeekChar(1) != '%') return BlockState.None;

        // Everything after "#%" on the opening line is the (optional) dialect tag.
        string opening = line.ToString();
        string tag = opening.Length > 2 ? opening[2..].Trim() : string.Empty;

        processor.NewBlocks.Push(new MusicBlock(this)
        {
            Column     = processor.Column,
            Span       = new SourceSpan(processor.Line.Start, processor.Line.End),
            DialectTag = tag.Length == 0 ? null : tag,
        });

        // Consume the opening fence line without adding it to the block content.
        return BlockState.ContinueDiscard;
    }

    public override BlockState TryContinue(BlockProcessor processor, Block block)
    {
        var music = (MusicBlock)block;

        // A line whose only content is "#%" closes the block (fence discarded).
        if (processor.Line.ToString().Trim() == "#%")
        {
            block.UpdateSpanEnd(processor.Line.End);
            return BlockState.BreakDiscard;
        }

        music.AppendSourceLine(processor.Line.ToString());
        block.UpdateSpanEnd(processor.Line.End);
        return BlockState.Continue;
    }
}
