namespace Nexaflow.Markdown.Ast;

/// <summary>
/// A piece of content, read: the parse tree with every part's position and parent worked out, and the
/// lookups an editor needs to go from a stretch of source to the part written there.
/// </summary>
public sealed class ContentReading
{
    private ContentReading(string source, ContentPart root)
    {
        this.Source = source;
        this.Root = root;
    }

    /// <summary>
    /// A reading of a tree that has already been read and worked over — what the stages of an
    /// <see cref="Pipeline.AstPipeline"/> hand back. The source it reports is the source the tree prints
    /// as, which is the same source it came from, because that is the one rule every stage keeps.
    /// </summary>
    public static ContentReading Of(ContentNode tree) => new(tree.Print(), ContentPart.Of(tree));

    /// <summary>The source this was read from.</summary>
    public string Source { get; }

    /// <summary>The whole of it.</summary>
    public ContentPart Root { get; }

    /// <summary>
    /// Every part written exactly at <paramref name="start"/> for <paramref name="length"/> characters,
    /// outermost first.
    /// <para>
    /// Several, because a part and the thing holding it can stand for the very same characters — a tune
    /// that is one bar, a group holding one note — and which of them a question is about depends on the
    /// question.
    /// </para>
    /// </summary>
    public IEnumerable<ContentPart> Naming(int start, int length)
    {
        foreach (var part in this.Root.SelfAndDescendants())
        {
            if (part.Start > start) break;
            if (part.Start == start && part.Length == length) yield return part;
        }
    }

    /// <summary>
    /// What stands for exactly this stretch of source: the parts written there, outermost first — or,
    /// when nothing is written exactly there, the wrapper whose contents these are.
    ///
    /// <para>
    /// The seam between the two readings of a piece of content, and the fallback is the whole of it. A
    /// builder drops brackets as soon as it has understood them, so its box for the numerator of
    /// <c>\frac{a+b}{c}</c> covers <c>a+b</c> — three things here, and no single part. The group is what
    /// that box is a picture of.
    /// </para>
    /// <para>
    /// Only when nothing matches exactly, which is what keeps the two apart where both could answer: in
    /// <c>\frac{a}{b}</c> the same stretch is both the letter a and the contents of the numerator, and
    /// asking what is written there has to answer the letter.
    /// </para>
    /// </summary>
    public IReadOnlyList<ContentPart> Standing(int start, int length)
    {
        var exact = new List<ContentPart>();
        ContentPart? wrapping = null;

        foreach (var part in this.Root.SelfAndDescendants())
        {
            if (part.Start == start && part.Length == length) { exact.Add(part); continue; }

            if (wrapping is null
                && part.IsWrapper
                && (part.Contents == (start, length) || part.Written == (start, length)))
                wrapping = part;
        }

        if (exact.Count > 0) return exact;

        return wrapping is null ? [] : [wrapping];
    }

    /// <summary>
    /// The innermost part covering <paramref name="offset"/> that stands for characters — what a caret at
    /// that offset is inside of.
    /// </summary>
    public ContentPart? Innermost(int offset)
    {
        ContentPart? best = null;

        foreach (var part in this.Root.SelfAndDescendants())
        {
            if (part.Derived || part.Length == 0) continue;
            if (offset < part.Start || offset > part.End) continue;
            if (best is null || part.Length <= best.Length) best = part;
        }

        return best;
    }
}
