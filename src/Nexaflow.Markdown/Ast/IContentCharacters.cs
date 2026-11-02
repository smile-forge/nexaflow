namespace Nexaflow.Markdown.Ast;

/// <summary>
/// Characters held somewhere else, read a stretch at a time: a document's, or a body's own inside it. What a piece of a tree
/// standing for characters it does not copy reads them from (<see cref="ContentNode.Leaf(string, IContentCharacters, int, int, string, int?)"/>),
/// and what a reading is read from.
///
/// <para>
/// <strong>Nothing here hands over all of them.</strong> A stretch is copied only when it is asked for (<see cref="Slice"/>,
/// <see cref="CopyTo"/>) or read where it is held (<see cref="Chunks"/>), so whatever holds characters it may never print — what
/// a long document has not read yet — costs what it holds only if somebody prints it.
/// </para>
/// </summary>
public interface IContentCharacters
{
    /// <summary>How many characters there are.</summary>
    int Length { get; }

    /// <summary>The character at <paramref name="offset"/>.</summary>
    char this[int offset] { get; }

    /// <summary>The characters from <paramref name="start"/> over <paramref name="length"/>.</summary>
    string Slice(int start, int length);

    /// <summary>The characters from <paramref name="start"/> on, as many as <paramref name="into"/> holds.</summary>
    void CopyTo(int start, Span<char> into);

    /// <summary>The characters from <paramref name="start"/> to <paramref name="end"/> — to the last where it says nothing — as the stretches they are held in, in order.</summary>
    IEnumerable<ReadOnlyMemory<char>> Chunks(int start = 0, int? end = null);

    /// <summary>Which line, counting from nought, <paramref name="offset"/> is on — a line ending where a line feed does.</summary>
    int LineOf(int offset);

    /// <summary>Where the line <paramref name="line"/>, counting from nought, starts.</summary>
    int OffsetOf(int line);
}
