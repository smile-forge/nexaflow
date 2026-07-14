using System.Text;
using Markdig.Parsers;
using Markdig.Syntax;

namespace Nexaflow.Visuals.Text.Markdown.Music;

/// <summary>
/// The AST node produced by <see cref="MusicBlockParser"/> for a <c>#% … #%</c> musical-notation block.
/// A leaf block that carries its raw source verbatim (the parser accumulates content lines itself rather
/// than relying on Markdig's line capture, so the exact notation text reaches the renderer unmodified).
/// </summary>
public sealed class MusicBlock : LeafBlock
{
    private readonly StringBuilder _source = new();

    public MusicBlock(BlockParser? parser) : base(parser)
    {
        // No inline parsing — the body is notation source, not markdown.
        ProcessInlines = false;
    }

    /// <summary>The explicit dialect tag on the opening fence (<c>abc</c>/<c>lilypond</c>/…), or null.</summary>
    public string? DialectTag { get; set; }

    /// <summary>Appends one raw content line (newline-terminated) to the block body.</summary>
    public void AppendSourceLine(string line)
    {
        _source.Append(line);
        _source.Append('\n');
    }

    /// <summary>The verbatim notation source between the fences.</summary>
    public string Source => _source.ToString();

    /// <summary>The dialect for this block: the tag if valid, otherwise auto-detected from the source.</summary>
    public MusicDialect Dialect =>
        MusicDialectExtensions.FromTag(DialectTag) ?? MusicDialectExtensions.Detect(Source);
}
