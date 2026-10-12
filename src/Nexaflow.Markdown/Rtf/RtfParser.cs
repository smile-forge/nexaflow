using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Rtf;

/// <summary>
/// Reads an RTF document into the tree a document is drawn from — the same paragraphs, tables, links and pictures
/// markdown is read into, so the same builder sets it.
///
/// <para>
/// <strong>The parser only copies.</strong> Every character of the source is a leaf at the offset it was read from, so the
/// tree prints back as exactly what was written. What RTF spells as formatting — a <c>\b</c>, a <c>\fs28</c>, a colour
/// table — is machinery, kept and never drawn; what it comes to is hung underneath the words it applies to
/// (<see cref="Prose.TextLook"/>, <see cref="Prose.ParagraphLook"/>), the way any language hangs what it worked out.
/// </para>
/// <para>
/// <strong>Formatting is a state, not a structure.</strong> RTF switches bold on and off as it goes rather than putting
/// it round anything, so the tree is not shaped by the source's groups: each paragraph holds its words in the order they
/// were written, every run of words set one way gathered under one look.
/// </para>
/// <para>
/// Nothing is refused. RTF readers are told to skip what they do not know, so a destination this does not draw is read
/// past, and a group left open at the end is closed there.
/// </para>
/// </summary>
public static class RtfParser
{
    /// <summary>The word naming what a document is written in — what its root says it is (<see cref="BlockNode"/>).</summary>
    public const string Language = "rtf";

    /// <summary>
    /// A parse for one document, which hands back what it read last time while the document is the same — so laying it out
    /// again, as a page does whenever its room changes, reads none of it again and keeps every picture it already decoded.
    /// </summary>
    public static Func<string, ContentParse> Parsing()
    {
        string? last = null;
        ContentParse? read = null;

        return source =>
        {
            if (read is not null && string.Equals(source, last, StringComparison.Ordinal)) return read;

            last = source;

            return read = ContentParse.Of(Parse(source));
        };
    }

    /// <summary><paramref name="source"/> — the document's bytes, one character to each — as the blocks it is drawn as.</summary>
    public static ContentNode Parse(string? source) => new RtfReading(source ?? string.Empty).Read();
}

/// <summary>What a piece of RTF source is that markdown has no word for: the machinery round the words.</summary>
public static class RtfKinds
{
    /// <summary><c>\b</c>, <c>\par</c> — a control word.</summary>
    public const string Control = "rtf-control";

    /// <summary><c>\~</c>, <c>\*</c> — a control symbol.</summary>
    public const string Symbol = "rtf-symbol";

    /// <summary><c>\'e9</c> — a byte spelled in hex.</summary>
    public const string Hex = "rtf-hex";

    /// <summary>Characters in a destination nothing draws: a font's name, a picture's bytes.</summary>
    public const string Data = "rtf-data";

    /// <summary>Bytes a <c>\bin</c> said follow it.</summary>
    public const string Binary = "rtf-binary";

    /// <summary>
    /// The control words between two blocks, or before the first and after the last — a group's braces, a font table, a
    /// row's definition — held together as the trivia they are.
    /// </summary>
    public const string Between = "rtf-between";

    /// <summary>A cell another one was merged over: written, and drawn as part of the one covering it.</summary>
    public const string Merged = "rtf-merged";
}
