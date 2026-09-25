using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// One piece of what a diagram's words are made of: a stretch that is only its own characters, an entity code standing for
/// the character it names, a line break, or a binding standing for its value.
/// </summary>
/// <param name="From">Where in the words it is written, counted from their first character.</param>
/// <param name="Written">The characters it is written as.</param>
/// <param name="Says">What it says: the characters themselves, the character a code names, a binding's value — nothing, for a line break.</param>
/// <param name="Breaks">Whether it is a line break: <c>&lt;br&gt;</c> however it is written, or <c>\n</c>.</param>
/// <param name="AsWritten">Whether what it says is exactly the characters it is written as, so each is where it was typed.</param>
public sealed record WordPiece(int From, string Written, string Says, bool Breaks, bool AsWritten)
{
    /// <summary>Where it stands, inside the <paramref name="words"/> it is a piece of.</summary>
    public ISourcePart In(ISourcePart words) => new PartSlice(words, this.From, this.Written.Length);
}

/// <summary>
/// What each run of a diagram's words that is more than its own characters is made of, found by <see cref="WithWordPieces"/>
/// and held on the diagram. A run found in here is not — it says exactly what it is written as.
/// </summary>
public sealed class MermaidWords(IReadOnlyDictionary<ContentNode, IReadOnlyList<WordPiece>> pieces)
{
    /// <summary>A diagram whose every run of words is only its own characters.</summary>
    public static readonly MermaidWords None = new(new Dictionary<ContentNode, IReadOnlyList<WordPiece>>());

    /// <summary>The pieces a run of <see cref="MermaidKinds.Words"/> is made of, or null where it is only its own characters.</summary>
    public IReadOnlyList<WordPiece>? Of(ContentNode words) => pieces.TryGetValue(words, out var found) ? found : null;

    /// <summary>What the diagram <paramref name="root"/> holds about its words — <see cref="None"/> where nothing was found.</summary>
    public static MermaidWords Held(ContentNode root)
    {
        foreach (var child in root.Children)
            if (child.IsDerived && child.Kind == MermaidKinds.WordPieces)
                foreach (var held in child.Children)
                    if (held.Held is MermaidWords words) return words;

        return None;
    }
}
