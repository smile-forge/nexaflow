using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// The size an image asks to be drawn at, written the way Obsidian writes it: after a bar at the end of its words.
/// <c>![a cat|300](cat.png)</c> is three hundred wide and as tall as the picture's own proportions make it;
/// <c>![a cat|300x200](cat.png)</c> is three hundred by two hundred.
///
/// <para>
/// The bar and the numbers are characters the writer typed, so they stay exactly where they were typed — as a part of the
/// image of their own (<see cref="MarkdownKinds.Size"/>), between its words and the bracket closing them. The words are
/// then only the words: what is drawn where the picture cannot be found, and what is read aloud. What the numbers say
/// is hung on the image, read once here, so nothing that draws it reads a character.
/// </para>
/// <para>
/// A bar somebody escaped (<c>\|</c>) is a bar, and words that end in anything but a number after the bar say no size.
/// </para>
/// </summary>
/// <param name="Width">How wide, in device-independent pixels.</param>
/// <param name="Height">How tall, or null to keep the picture's own proportions.</param>
public sealed partial record MarkdownPictureSize(double Width, double? Height)
{
    /// <summary>The size an image says it is drawn at, or null where it says none.</summary>
    public static MarkdownPictureSize? Of(ContentNode image)
    {
        foreach (var child in image.Children)
            foreach (var held in child.Children)
                if (held.Role == MarkdownRoles.Size && held.Held is MarkdownPictureSize size)
                    return size;

        return null;
    }

    /// <summary>
    /// Whether <paramref name="words"/> end the way a size is written — so words that are to stay words, an image's
    /// description converted from somewhere else, can have their bar escaped before anybody reads it as one.
    /// </summary>
    public static bool EndsLikeOne(string words) => Written().IsMatch(words);

    /// <summary>How a size is written: an unescaped bar, a width, and optionally <c>x</c> and a height, with space allowed round them.</summary>
    [GeneratedRegex(@"(?<!\\)\|\s*(?<wide>\d+(?:\.\d+)?)\s*(?:[xX]\s*(?<tall>\d+(?:\.\d+)?)\s*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Written();

    /// <summary>
    /// The size written at the end of an image's words, taken off them: <paramref name="words"/> loses the characters
    /// spelling it, and the piece holding them comes back with what they say — or null, the words untouched, where they
    /// end in no size.
    /// </summary>
    internal static (ContentNode Written, MarkdownPictureSize Size)? Taken(List<ContentNode> words)
    {
        // The plain characters the words end in — a size is plain text, so nothing else can spell one.
        var tail = new StringBuilder();
        var first = words.Count;

        while (first > 0 && words[first - 1] is { IsLeaf: true } leaf && Plain(leaf)) first--;

        if (first == words.Count) return null;

        for (var at = first; at < words.Count; at++) tail.Append(words[at].Text);

        var text = tail.ToString();
        var found = Written().Match(text);
        if (!found.Success) return null;

        var wide = double.Parse(found.Groups["wide"].Value, CultureInfo.InvariantCulture);
        double? tall = found.Groups["tall"].Success ? double.Parse(found.Groups["tall"].Value, CultureInfo.InvariantCulture) : null;
        if (wide <= 0 || tall <= 0) return null;

        // Which piece the bar stands in, and how far along it.
        var into = found.Index;
        var piece = first;
        while (into >= words[piece].Text.Length)
        {
            into -= words[piece].Text.Length;
            piece++;
        }

        var cut = words[piece];
        var stands = cut.Offset is { } offset ? offset + into : (int?)null;
        var spelled = ContentNode.Leaf(MarkdownKinds.Size, text[found.Index..], MarkdownRoles.Size, offset: stands);

        words.RemoveRange(piece, words.Count - piece);
        if (into > 0) words.Add(ContentNode.Leaf(cut.Kind, cut.Text[..into], cut.Role, cut.Trouble, cut.Offset));

        return (spelled, new MarkdownPictureSize(wide, tall));
    }

    /// <summary>A piece that is just the characters it was written as: a word, or the space or marks between two.</summary>
    private static bool Plain(ContentNode leaf) => leaf.Kind is MarkdownKinds.Word or Kinds.Space or Kinds.Token;
}
