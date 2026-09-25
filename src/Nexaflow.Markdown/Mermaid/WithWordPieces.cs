using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// What each run of a diagram's words is made of, where it is more than its own characters — an entity code standing for the
/// character it names, a line break written in it, a binding standing for its value, a whole other content written in its
/// place, which is the characters it is written as wherever nothing draws that content — found once and held on the diagram
/// (<see cref="MermaidWords"/>).
///
/// <para>
/// A stage, so whatever draws a label is handed the pieces it is made of rather than looking at its characters to find them:
/// a line of a label is the pieces between two breaks, and it stands for those pieces. Run last, once every binding has
/// been worked out, so a binding's piece says its value.
/// </para>
/// <para>
/// The runs are left as they were read — every model reads a run's own characters — and a run that is only those
/// characters, which is nearly every one, has nothing held for it.
/// </para>
/// </summary>
public sealed class WithWordPieces : IAstStage
{
    /// <summary>What breaks a line in a diagram's words: a <c>&lt;br&gt;</c> however it is written, and a <c>\n</c>.</summary>
    private static readonly string[] Breaks = ["<br/>", "<br />", "<br>", @"\n"];

    public string Name => "mermaid:word-pieces";

    public ContentNode Run(ContentNode tree)
    {
        Dictionary<ContentNode, IReadOnlyList<WordPiece>>? found = null;

        foreach (var node in tree.SelfAndDescendants())
            if (node.Kind is MermaidKinds.Words or Kinds.Nested && !node.IsDerived && Pieces(node) is { } pieces)
                (found ??= new(ReferenceEqualityComparer.Instance))[node] = pieces;

        return found is null ? tree : tree.Holding(MermaidKinds.WordPieces, Roles.Derived, new MermaidWords(found));
    }

    /// <summary>The pieces a run is made of, or null where it is only its own characters.</summary>
    private static List<WordPiece>? Pieces(ContentNode words)
    {
        if (words.IsLeaf) return Plain(words.Text) ? null : Split(words.Text, 0);

        // A run with bindings in it, or a whole other content: each binding is one piece, saying its value where one was worked
        // out, and everything else the pieces its characters are.
        var pieces = new List<WordPiece>();
        var at = 0;

        foreach (var child in words.Children)
        {
            if (child.IsDerived) continue;

            var written = child.Print();
            if (child.Kind == Kinds.Bound)
            {
                var value = child.Said(ContentWords.Value);
                pieces.Add(new WordPiece(at, written, value ?? written, Breaks: false, AsWritten: value is null));
            }
            else pieces.AddRange(Split(written, at));

            at += child.Width;
        }

        return pieces;
    }

    /// <summary>Whether characters hold nothing that could be a line break or an entity code.</summary>
    private static bool Plain(string text) => text.AsSpan().IndexOfAny("<\\#&") < 0;

    /// <summary>Characters written <paramref name="offset"/> into a run, as the pieces they are.</summary>
    private static List<WordPiece> Split(string text, int offset)
    {
        var pieces = new List<WordPiece>();
        if (text.Length == 0) return pieces;

        var codes = Plain(text) ? [] : MermaidText.Codes(text).ToList();
        var (from, next) = (0, 0);

        for (var at = 0; at < text.Length;)
        {
            while (next < codes.Count && codes[next].At < at) next++;

            if (Break(text, at) is > 0 and var mark)
            {
                Own(at);
                pieces.Add(new WordPiece(offset + at, text.Substring(at, mark), string.Empty, Breaks: true, AsWritten: false));
                from = at += mark;
            }
            else if (next < codes.Count && codes[next].At == at)
            {
                var (_, length, says) = codes[next++];
                Own(at);
                pieces.Add(new WordPiece(offset + at, text.Substring(at, length), says, Breaks: false, AsWritten: false));
                from = at += length;
            }
            else at++;
        }

        Own(text.Length);
        return pieces;

        // The stretch since the last piece, which is only its own characters.
        void Own(int to)
        {
            if (to > from) pieces.Add(new WordPiece(offset + from, text[from..to], text[from..to], Breaks: false, AsWritten: true));
        }
    }

    /// <summary>How long the line break written at <paramref name="at"/> is — nought where none is.</summary>
    private static int Break(string text, int at)
    {
        foreach (var mark in Breaks)
            if (string.Compare(text, at, mark, 0, mark.Length, StringComparison.OrdinalIgnoreCase) == 0 && at + mark.Length <= text.Length)
                return mark.Length;

        return 0;
    }
}
