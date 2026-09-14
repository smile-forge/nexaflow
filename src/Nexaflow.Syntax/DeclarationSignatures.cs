using System.Text;
using TreeSitter;

namespace Nexaflow.Syntax;

/// <summary>One declaration as it reads from the outside.</summary>
/// <param name="Line">1-based line the declaration starts on, attributes included where the grammar nests them.</param>
/// <param name="EndLine">1-based line it ends on.</param>
/// <param name="Name">The name the grammar gives it; for an XML element, its tag name.</param>
/// <param name="Text">Everything before its body, on one line — for an XML element, its start tag.</param>
/// <param name="NameLine">1-based line its name is on, which is where some grammars say a declaration starts.</param>
/// <param name="XmlPath">For an XML element, the <c>--at</c> path that addresses it.</param>
public sealed record DeclarationSignature(int Line, int EndLine, string Name, string Text, int NameLine = 0, string? XmlPath = null);

/// <summary>
/// What each declaration in a file looks like from the outside — <c>public void Press(PaletteKey key)</c> rather than
/// <c>M:Press</c> — so a listing answers "what does this offer" without the bodies being read.
/// <para>
/// A name and a line say where something is. They do not say what calling it takes or what it gives back, and that
/// is most of why a listing used to be followed by reading the file it had just listed. The body is left out on
/// purpose: from outside it is the part nobody asked for, and it is nearly all of a file.
/// </para>
/// </summary>
public static class DeclarationSignatures
{
    /// <summary>Past this a signature is cut: a line too long to take in at a glance defeats the listing it is in.</summary>
    public const int Longest = 200;

    /// <summary>Every declaration in <paramref name="text"/>, outermost first — for XML, every element.</summary>
    public static IReadOnlyList<DeclarationSignature> Of(string grammarId, string text)
    {
        if (string.IsNullOrEmpty(grammarId) || string.IsNullOrEmpty(text)) return [];
        if (TreeSitterLanguages.IsXml(grammarId)) return XmlAnchors.Signatures(grammarId, text);

        using var highlighter = CodeHighlighter.TryCreate(grammarId);
        if (highlighter is null) return [];
        try
        {
            return highlighter.WithParseTree(text, root =>
            {
                var found = new List<DeclarationSignature>();
                Collect(root, text, found);
                return (IReadOnlyList<DeclarationSignature>)found;
            }) ?? [];
        }
        catch { return []; }
    }

    /// <summary>
    /// The declaration called <paramref name="name"/> that starts where it was recorded as starting — on its first line
    /// or on its name's — preferring one that also ends where it was recorded as ending. A null name matches on position
    /// alone, which is how an XML element is found, since its name is a tag rather than its identity.
    /// </summary>
    public static DeclarationSignature? Find(IReadOnlyList<DeclarationSignature> all, string? name, int line, int endLine = 0)
    {
        bool Named(DeclarationSignature s) => name is not { Length: > 0 } || s.Name == name;
        bool Starts(DeclarationSignature s) => s.Line == line || s.NameLine == line;

        return all.FirstOrDefault(s => Named(s) && Starts(s) && (endLine <= 0 || s.EndLine == endLine))
            ?? all.FirstOrDefault(s => Named(s) && Starts(s))
            ?? (endLine > 0 ? all.FirstOrDefault(s => Named(s) && s.EndLine == endLine) : null);
    }

    /// <summary>The deepest declaration whose lines hold <paramref name="line"/> — what a diagnostic on that line is in.</summary>
    public static DeclarationSignature? Innermost(IReadOnlyList<DeclarationSignature> all, int line) =>
        all.Where(s => s.Line <= line && line <= s.EndLine)
           .OrderByDescending(s => s.Line).ThenBy(s => s.EndLine)
           .FirstOrDefault();

    private static void Collect(Node node, string text, List<DeclarationSignature> into)
    {
        foreach (var child in node.NamedChildren)
        {
            if (IsDeclaration(child.Type) && child.GetChildForField("name") is { } name)
            {
                var span = DeclarationAnchors.WholeDeclaration(child);
                into.Add(new DeclarationSignature(span.StartPosition.Row + 1, span.EndPosition.Row + 1, name.Text,
                                                  Header(child, span, text), name.StartPosition.Row + 1));
            }
            Collect(child, text, into);
        }
    }

    /// <summary>Node types that declare something, across the grammars: a parameter or a member access has a name too,
    /// and is not one.</summary>
    private static bool IsDeclaration(string type) =>
        (type.Contains("declaration", StringComparison.Ordinal)
         || type.Contains("declarator", StringComparison.Ordinal)
         || type.Contains("definition", StringComparison.Ordinal)
         || type.EndsWith("_item", StringComparison.Ordinal)
         || type == "local_function_statement")
        && !type.Contains("parameter", StringComparison.Ordinal);

    /// <summary>
    /// The declaration from its first word to where its body begins: past the attributes C# nests inside it, and short
    /// of a block, an accessor list or an arrow body. Something with no body at all — a field, a constant — keeps its
    /// first line, so a short initialiser is read and a table of them is not.
    /// </summary>
    private static string Header(Node declaration, Node span, string text)
    {
        var start = span.StartIndex;
        foreach (var child in span.NamedChildren)
        {
            if (!child.Type.Contains("attribute", StringComparison.Ordinal)
                && !child.Type.Contains("comment", StringComparison.Ordinal)) break;
            start = child.EndIndex;
        }
        while (start < span.EndIndex && char.IsWhiteSpace(text[start])) start++;

        var end = span.EndIndex;
        var cut = false;
        if (DeclarationAnchors.BodyOf(declaration, declaration.GetChildForField("parameters")) is { } body)
            end = body.StartIndex;
        else if (declaration.NamedChildren.FirstOrDefault(c => c.Type == "arrow_expression_clause") is { } arrow)
            end = arrow.StartIndex;
        else if (text.IndexOf('\n', start, end - start) is var lineEnd and >= 0)
        {
            end = lineEnd;
            cut = true;
        }

        return OneLine(text[start..Math.Max(start, end)], cut);
    }

    /// <summary>Whitespace runs as one space, a trailing brace or semicolon dropped, and cut at <see cref="Longest"/>.</summary>
    internal static string OneLine(string text, bool cut = false)
    {
        var sb = new StringBuilder(Math.Min(text.Length, Longest + 1));
        var space = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch)) { space = sb.Length > 0; continue; }
            if (space) sb.Append(' ');
            sb.Append(ch);
            space = false;
        }

        // A parameter list broken across lines collapses to "( int a" without this.
        var line = sb.ToString().Replace("( ", "(", StringComparison.Ordinal).Replace(" )", ")", StringComparison.Ordinal)
                     .TrimEnd('{', ';', ' ');
        if (line.Length > Longest) return line[..(Longest - 1)] + "…";
        return cut ? line + " …" : line;
    }
}
