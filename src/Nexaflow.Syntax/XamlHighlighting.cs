using System.Collections.Generic;
using TreeSitter;

namespace Nexaflow.Syntax;

/// <summary>
/// The colouring inside a XAML attribute — the part the XML grammar cannot see.
///
/// <para>
/// To the <c>xml</c> grammar an attribute value is one opaque <c>AttValue</c> token, which is right: a markup
/// extension is XAML semantics, not XML syntax. But it means <c>Width="420"</c> and
/// <c>Style="{StaticResource PopupBorder}"</c> paint identically, when the second has real structure —
/// a delimiter, an extension name, arguments — that a reader uses to scan a view. Visual Studio colours those
/// separately and it is the main thing missing without this.
/// </para>
/// <para>
/// These spans are appended <em>after</em> the query's, and the colourizer applies spans in order, so the
/// finer ones win over the flat string underneath. Anything this scanner does not understand it simply leaves
/// alone, which falls back to that string colour rather than mis-painting.
/// </para>
/// </summary>
internal static class XamlHighlighting
{
    /// <summary>Namespace prefixes and markup-extension structure for every attribute under <paramref name="root"/>.</summary>
    public static void AddValueSpans(Node root, string text, int offset, List<HighlightSpan> spans)
    {
        foreach (var attribute in Attributes(root))
            foreach (var child in attribute.Children)
            {
                if (child.Type == "Name")
                    AddPrefix(child.StartIndex, child.EndIndex, text, offset, spans);
                else if (child.Type == "AttValue")
                    AddMarkupExtension(child.StartIndex, child.EndIndex, text, offset, spans);
            }
    }

    private static IEnumerable<Node> Attributes(Node n)
    {
        if (n.Type == "Attribute") { yield return n; yield break; }
        foreach (var c in n.Children)
            foreach (var a in Attributes(c))
                yield return a;
    }

    /// <summary>The <c>x:</c> in <c>x:Name</c> — a different thing from the name it qualifies, and read as one.</summary>
    private static void AddPrefix(int start, int end, string text, int offset, List<HighlightSpan> spans)
    {
        for (var i = start; i < end && i < text.Length; i++)
        {
            if (text[i] != ':') continue;
            spans.Add(new HighlightSpan(start + offset, i - start + 1, "type"));
            return;
        }
    }

    /// <summary>
    /// Paints a markup extension inside an attribute value. A value that is not one — a plain literal, or the
    /// <c>{}</c> escape that means "this really does start with a brace" — is left to the string colour.
    /// </summary>
    private static void AddMarkupExtension(int start, int end, string text, int offset, List<HighlightSpan> spans)
    {
        // AttValue spans the quotes too; the extension is what sits between them.
        var inner = start + 1;
        var innerEnd = end - 1;
        if (innerEnd - inner < 2 || inner >= text.Length) return;
        if (text[inner] != '{') return;
        if (text[inner + 1] == '}') return;   // "{}..." is the literal-brace escape

        Scan(text, inner, innerEnd, offset, spans, depth: 0);
    }

    private const int MaxDepth = 6;   // {Binding {RelativeSource {…}}} nests, but not without bound

    /// <summary>
    /// Scans one <c>{Extension arg, Name=value}</c> from <paramref name="i"/>, returning the index just past its
    /// closing brace. Recurses for a nested extension in an argument position.
    /// </summary>
    private static int Scan(string text, int i, int end, int offset, List<HighlightSpan> spans, int depth)
    {
        if (depth > MaxDepth || i >= end || text[i] != '{') return i;

        Emit(i, 1, "operator");                       // {
        i++;

        // The extension name: StaticResource, Binding, x:Type — the part that says what this value is.
        var nameStart = i;
        while (i < end && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '.' or ':')) i++;
        var isResourceLookup = false;
        if (i > nameStart)
        {
            AddPrefix(nameStart, i, text, offset, spans);   // the x: in x:Type
            var afterPrefix = PrefixEnd(text, nameStart, i);
            Emit(afterPrefix, i - afterPrefix, "keyword");
            isResourceLookup = text.AsSpan(afterPrefix, i - afterPrefix).EndsWith("Resource");
        }

        // A resource key is not a binding path: it names something declared elsewhere in the document (or a
        // merged dictionary), which reads differently and — when it resolves to a brush — is previewable.
        var argument = isResourceLookup ? "constant" : "variable";

        while (i < end)
        {
            while (i < end && char.IsWhiteSpace(text[i])) i++;
            if (i >= end) break;

            if (text[i] == '}') { Emit(i, 1, "operator"); return i + 1; }
            if (text[i] is ',' or '=') { Emit(i, 1, "operator"); i++; continue; }

            if (text[i] == '{') { i = Scan(text, i, end, offset, spans, depth + 1); continue; }

            // A bare token: either an argument name (a '=' follows) or a value.
            var tokenStart = i;
            while (i < end && text[i] is not (',' or '=' or '}' or '{') && !char.IsWhiteSpace(text[i])) i++;
            if (i == tokenStart) { i++; continue; }        // never stall on something unexpected

            var next = i;
            while (next < end && char.IsWhiteSpace(text[next])) next++;
            var isArgumentName = next < end && text[next] == '=';

            if (isArgumentName)
            {
                Emit(tokenStart, i - tokenStart, "attribute");
            }
            else
            {
                AddPrefix(tokenStart, i, text, offset, spans);   // the vmo: in vmo:PromptOverlay
                var afterPrefix = PrefixEnd(text, tokenStart, i);
                Emit(afterPrefix, i - afterPrefix, argument);
            }
        }
        return i;

        void Emit(int from, int length, string capture)
        {
            if (length > 0) spans.Add(new HighlightSpan(from + offset, length, capture));
        }
    }

    /// <summary>Where the local name starts — just past a <c>prefix:</c>, or the token's own start.</summary>
    private static int PrefixEnd(string text, int start, int end)
    {
        for (var i = start; i < end; i++)
            if (text[i] == ':') return i + 1;
        return start;
    }
}
