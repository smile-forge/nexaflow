using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Ast;

/// <summary>
/// A run of words, read into the tree that holds it — and what that run says once it is read.
///
/// <para>
/// Everything a run can be besides its own characters is settled here, while it is being read: a binding standing in
/// for a value somebody else holds, a whole other content written in place of the lot. Each becomes a node of its
/// own, carrying the characters exactly as they were written, so the run prints back as what it came from.
/// </para>
/// <para>
/// Which is the point of doing it here. Past the parse nothing looks at a character to find out what it is holding:
/// a builder asks the tree what the parts of a run <em>are</em> and is told, and the one place that decides is the
/// one place that read the source in the first place.
/// </para>
/// </summary>
public static class ContentWords
{
    /// <summary>
    /// The role a binding's value is hung under, once something has worked out what it stands for — see
    /// <c>Pipeline.Stages.WithBindings</c>. A binding nothing has resolved carries none, and says the characters it
    /// was written as.
    /// </summary>
    public const string Value = "value";

    /// <summary>
    /// A run of <paramref name="text"/> as a node: a leaf where it is only itself, and otherwise the parts it turned
    /// out to be made of.
    /// </summary>
    /// <param name="kind">What this language calls a run of words — the kind its own readers look for.</param>
    /// <param name="role">What the run is to whatever holds it.</param>
    /// <param name="trouble">What is wrong with it, where anything is; a run with something wrong is left whole.</param>
    public static ContentNode Of(string text, string kind, string role, string? trouble = null)
    {
        if (trouble is not null) return ContentNode.Leaf(kind, text, role, trouble);
        if (ContentLink.Opens(text)) return Block(text, kind, role);
        if (BoundText.Binds(text)) return Bound(text, kind, role);

        return ContentNode.Leaf(kind, text, role);
    }

    /// <summary>
    /// What a run says: its characters, with every binding in it replaced by what it was worked out to stand for and
    /// every literal stretch unescaped by <paramref name="decode"/>.
    ///
    /// <para>
    /// Told apart by what each part is, never by what it looks like, and nothing is worked out here — a binding says
    /// what a stage already hung under it. With nothing hung there, it says the characters it was written as, because
    /// a document shown alongside no data is still a document.
    /// </para>
    /// </summary>
    /// <param name="decode">How this language writes a character its text cannot hold as itself, or null where it has no such thing.</param>
    public static string Says(ContentPart part, Func<string, string>? decode = null)
    {
        if (part.Node.IsLeaf) return Read(part.Text, decode);
        if (part.Kind == Kinds.Bound) return Stands(part);

        var said = new StringBuilder();
        foreach (var child in part.Children)
            said.Append(child.Kind == Kinds.Bound ? Stands(child) : Says(child, decode));

        return said.ToString();
    }

    /// <summary>
    /// A run as it was written: its own characters where it is one stretch, and the whole of it built back up where
    /// it turned out to be several. What <see cref="Says"/> is compared against to find out whether a reader is
    /// looking at what they typed.
    /// </summary>
    public static string Wrote(this ContentPart part) => part.Node.IsLeaf ? part.Text : part.Print();

    private static string Read(string text, Func<string, string>? decode) => decode is null ? text : decode(text);

    private static string Stands(ContentPart bound) => bound.Node.Said(Value) ?? bound.Print();

    /// <summary>
    /// A run that is a whole other content, split into the parts that say which: the fence, what it names itself, and
    /// that language's own source. A fence naming nothing, or naming something with nothing after it, is not a block
    /// of anything — it is the characters somebody typed, and stays them.
    /// </summary>
    private static ContentNode Block(string text, string kind, string role)
    {
        var named = ContentLink.Fence.Length;
        while (named < text.Length && !char.IsWhiteSpace(text[named])) named++;

        var at = named;
        while (at < text.Length && char.IsWhiteSpace(text[at])) at++;

        if (named == ContentLink.Fence.Length || at >= text.Length) return ContentNode.Leaf(kind, text, role);

        List<ContentNode> parts =
        [
            ContentNode.Leaf(Kinds.Token, text[..ContentLink.Fence.Length], Roles.Trivia),
            ContentNode.Leaf(Kinds.Token, text[ContentLink.Fence.Length..named], Roles.Name),
        ];

        if (at > named) parts.Add(ContentNode.Leaf(Kinds.Space, text[named..at], Roles.Trivia));

        // Held as written and nothing read out of it: what is in there is a different language, and reading it is
        // its own parser's business — which is why it is not this language's kind of words.
        parts.Add(ContentNode.Leaf(Kinds.Verbatim, text[at..], Roles.Body));

        return ContentNested.Naming(ContentNode.Branch(Kinds.Nested, parts, role), text[ContentLink.Fence.Length..named]);
    }

    /// <summary>
    /// A run with bindings in it, split into the stretches that are themselves and the ones that stand for a value.
    /// A pair of braces naming nothing stands for nothing, so it stays the characters it is.
    /// </summary>
    private static ContentNode Bound(string text, string kind, string role)
    {
        List<ContentNode> parts = [];
        var from = 0;
        var at = 0;

        while (at < text.Length)
        {
            var opens = text.IndexOf(BoundText.Opens, at, StringComparison.Ordinal);
            if (opens < 0) break;

            var shuts = text.IndexOf(BoundText.Shuts, opens + BoundText.Opens.Length, StringComparison.Ordinal);
            if (shuts < 0) break;

            at = shuts + BoundText.Shuts.Length;

            var inner = text[(opens + BoundText.Opens.Length)..shuts];
            if (inner.Trim().Length == 0) continue;

            if (opens > from) parts.Add(ContentNode.Leaf(kind, text[from..opens], Roles.Element));
            parts.Add(Binding(inner, kind));
            from = at;
        }

        if (parts.Count == 0) return ContentNode.Leaf(kind, text, role);
        if (from < text.Length) parts.Add(ContentNode.Leaf(kind, text[from..], Roles.Element));

        return ContentNode.Branch(kind, parts, role);
    }

    /// <summary>One binding: the braces, the path it names, and whatever space the writer left round it.</summary>
    private static ContentNode Binding(string inner, string kind)
    {
        var path = inner.Trim();
        var lead = inner.Length - inner.TrimStart().Length;

        List<ContentNode> parts = [ContentNode.Leaf(Kinds.Token, BoundText.Opens, Roles.Trivia)];

        if (lead > 0) parts.Add(ContentNode.Leaf(Kinds.Space, inner[..lead], Roles.Trivia));
        parts.Add(ContentNode.Leaf(kind, path, Roles.Name));
        if (inner.Length > lead + path.Length) parts.Add(ContentNode.Leaf(Kinds.Space, inner[(lead + path.Length)..], Roles.Trivia));

        parts.Add(ContentNode.Leaf(Kinds.Token, BoundText.Shuts, Roles.Trivia));

        return ContentNode.Branch(Kinds.Bound, parts, Roles.Element);
    }
}
