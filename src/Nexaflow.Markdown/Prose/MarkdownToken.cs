namespace Nexaflow.Markdown.Prose;

/// <summary>What one stretch of a run of words is, before anything is matched with anything.</summary>
internal enum MarkdownTokenKind : byte
{
    /// <summary>Characters that are only themselves.</summary>
    Text,

    /// <summary>Space at the head or the end of a line, which is no part of the words.</summary>
    Space,

    /// <summary>A line ending between two lines of the words.</summary>
    Break,

    /// <summary>A line ending the writer asked to keep: two spaces before it, or a backslash.</summary>
    HardBreak,

    /// <summary>A backslash and the punctuation it takes the meaning off.</summary>
    Escape,

    Entity,

    /// <summary>A code span: a run of backticks, what it holds, and a run as long closing it.</summary>
    Code,

    /// <summary>An address in angle brackets.</summary>
    Autolink,

    /// <summary>An address written bare — a link only where nothing round it says otherwise, which matching settles.</summary>
    Bare,

    Html,

    /// <summary>A formula between dollars.</summary>
    Formula,

    /// <summary>A run of one of the characters emphasis is written with, and whether it can open or close.</summary>
    Run,

    /// <summary>A <c>[</c>.</summary>
    Open,

    /// <summary>A <c>![</c>.</summary>
    Picture,

    /// <summary>A <c>]</c>.</summary>
    Close,
}

/// <summary>
/// One stretch of a run of words, by where it stands in the characters it was read from — so a reading kept and moved along
/// holds tokens that are still true.
/// </summary>
/// <param name="Inner">Where what it holds starts: a code span's code, a formula's, an address in angle brackets.</param>
/// <param name="InnerEnd">Where what it holds ends.</param>
/// <param name="Mark">A run's character.</param>
/// <param name="Opens">Whether a run can open emphasis.</param>
/// <param name="Closes">Whether a run can close it.</param>
/// <param name="Plain">Whether it was read with no address written bare — asked for where one was found and turned out to be none.</param>
internal readonly record struct MarkdownToken(
    MarkdownTokenKind Kind, int Start, int End, int Inner = 0, int InnerEnd = 0, char Mark = '\0', bool Opens = false, bool Closes = false,
    bool Plain = false);

/// <summary>Where what makes a link written inline stands: past its closing parenthesis, its address, and its title where it has one.</summary>
internal readonly record struct MarkdownTail(int End, (int From, int To) Destination, (int From, int To)? Title);
