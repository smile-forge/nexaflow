using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.Markdown.Packaging;

/// <summary>A file a document shows or links to from beside itself: its path under the document's folder, and where its
/// address is written in the document.</summary>
public sealed record MarkdownAttachment(string Path, int At, int Length);

/// <summary>
/// The files a Markdown document shows or links to from beside itself — its pictures, a PDF it links to — which have to go
/// wherever the document goes for it to read the same there.
/// </summary>
public static class MarkdownAttachments
{
    /// <summary>
    /// Every address <paramref name="markdown"/> writes — in a link, a picture or a link's definition — that names a file
    /// beside it, as a path under the document's own folder, with where the address is written. A URL, a rooted path, one
    /// up out of the folder, or a place in the document itself names nothing beside it.
    /// </summary>
    public static IReadOnlyList<MarkdownAttachment> Find(string markdown)
    {
        var found = new List<MarkdownAttachment>();
        foreach (var (at, written) in Addresses(markdown))
            if (Beside(written) is { } path) found.Add(new MarkdownAttachment(path, at, written.Length));

        return found;
    }

    /// <summary>
    /// Every address <paramref name="markdown"/> writes — in a link, a picture or a link's definition — as written, angle brackets
    /// and all, and where it is written: a file, a URL, or a place in the document itself (<c>#getting-started</c>).
    /// </summary>
    public static IReadOnlyList<(int At, string Written)> Addresses(string markdown)
    {
        var found = new List<(int At, string Written)>();
        foreach (var node in MarkdownParser.Worked(markdown).SelfAndDescendants())
        {
            if (node.Kind is Kinds.Link or MarkdownKinds.Image)
            {
                foreach (var part in node.Children)
                {
                    if (part is not { Role: Roles.Destination, IsDerived: false, Offset: { } at } || part.Text is not { Length: > 0 } written) continue;
                    // Only an address standing in the document where it says: a diagram's own reading counts from its block.
                    if (at + written.Length > markdown.Length || string.CompareOrdinal(markdown, at, written, 0, written.Length) != 0) continue;
                    found.Add((at, written));
                }
            }
            else if (node.Kind == MarkdownKinds.Reference && Defined(node) is { } definition)
            {
                found.Add(definition);
            }
        }
        return found;
    }

    /// <summary>The files <paramref name="markdown"/> names beside itself, each once (<see cref="Find"/>).</summary>
    public static IReadOnlyList<string> Paths(string markdown)
        => [.. Find(markdown).Select(attachment => attachment.Path).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Where a link's definition — <c>[name]: address</c> — writes its address: the definition is read whole, so
    /// the address is found in its own characters.</summary>
    private static (int At, string Written)? Defined(ContentNode reference)
    {
        if (reference.Part(Roles.Body) is not { Offset: { } start, Text: { Length: > 0 } text }) return null;
        int i = text.IndexOf("]:", StringComparison.Ordinal);
        if (i < 0) return null;
        for (i += 2; i < text.Length && char.IsWhiteSpace(text[i]); i++) { }
        if (i >= text.Length) return null;
        int from = text[i] == '<' ? i + 1 : i;
        int to = text[i] == '<' ? text.IndexOf('>', from) : from;
        if (text[i] != '<') while (to < text.Length && !char.IsWhiteSpace(text[to])) to++;
        return to > from ? (start + from, text[from..to]) : null;
    }

    /// <summary>The path an address names under the document's folder, or null where it names nothing there.</summary>
    private static string? Beside(string written)
    {
        string address = written.Length > 1 && written[0] == '<' && written[^1] == '>' ? written[1..^1] : written;
        int cut = address.IndexOfAny(['#', '?']);
        if (cut >= 0) address = address[..cut];
        if (address.Length == 0 || Uri.TryCreate(address, UriKind.Absolute, out _) || System.IO.Path.IsPathRooted(address)) return null;
        string path = Uri.UnescapeDataString(address).Replace('/', System.IO.Path.DirectorySeparatorChar);
        return path.Split(System.IO.Path.DirectorySeparatorChar).Contains("..") ? null : path;
    }
}
