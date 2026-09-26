using System.Text;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.Class;

/// <summary>
/// One member of a class as the stages leave it (<see cref="Stages.ResolveMembers"/>): what it draws once its type parameters
/// are angled and its classifiers taken off, and what those classifiers said. It prints as the member written.
/// </summary>
internal sealed class MemberNode : ContentNode
{
    private MemberNode(ContentNode written, string says, bool method) : base(written)
    {
        this.Says = says;
        this.Method = method;
    }

    /// <summary>What the token ending a member says it leads to, and the member without it.</summary>
    public const string Link = " @@";

    /// <summary>What is drawn: <c>~T~</c> as <c>&lt;T&gt;</c>, without the <c>*</c> or <c>$</c> ending it.</summary>
    public string Says { get; }

    /// <summary>Whether it is a method, which Mermaid decides by the brackets after its name.</summary>
    public bool Method { get; }

    /// <summary>Whether a <c>$</c> ended it, which draws it underlined.</summary>
    public bool Fixed { get; private init; }

    /// <summary>Whether a <c>*</c> ended it, which draws it in italics.</summary>
    public bool Abstract { get; private init; }

    /// <summary>
    /// Where pressing it leads, from the <c>@@</c> ending it — which nothing in Mermaid writes, and the Code feature writes to
    /// point each member at the line it is declared on.
    /// </summary>
    public string? Href { get; private init; }

    /// <summary>Whether what is drawn is the characters written, which is what lets a caret stand in it.</summary>
    public bool Written => Href is null && Says == Print().Trim();

    protected override ContentNode Reshaped(ContentNode shape) =>
        new MemberNode(shape, this.Says, this.Method) { Fixed = this.Fixed, Abstract = this.Abstract, Href = this.Href };

    /// <summary>One member, read from what was written for it.</summary>
    public static MemberNode Of(ContentNode written)
    {
        var text = written.Print();
        var (said, href) = text.IndexOf(Link, StringComparison.Ordinal) is var at and >= 0
            ? (text[..at], text[(at + Link.Length)..].Trim())
            : (text, null);

        said = said.Trim();

        // Mermaid writes a classifier after the brackets or after what the method gives back, so both are taken off here.
        var (kept, quick, loose) = Classified(said);
        var method = kept.Contains('(', StringComparison.Ordinal);

        return new MemberNode(written, Angled(method ? Returned(kept) : kept), method)
        {
            Fixed = quick,
            Abstract = loose,
            Href = string.IsNullOrEmpty(href) ? null : href,
        };
    }

    /// <summary>
    /// A member with the <c>$</c> that says the class holds it and the <c>*</c> that says nothing here does taken off —
    /// written at the end of it, or hard against the brackets of a method that gives something back.
    /// </summary>
    private static (string Said, bool Fixed, bool Abstract) Classified(string said)
    {
        if (said.EndsWith('$')) return (said[..^1].TrimEnd(), true, false);
        if (said.EndsWith('*')) return (said[..^1].TrimEnd(), false, true);

        var close = said.LastIndexOf(')');
        if (close < 0 || close + 1 >= said.Length) return (said, false, false);

        var mark = said[close + 1];
        if (mark is not ('$' or '*')) return (said, false, false);

        return (said[..(close + 1)] + said[(close + 2)..], mark == '$', mark == '*');
    }

    /// <summary>
    /// What a method gives back, which Mermaid draws after a colon: <c>getId() int</c> is drawn <c>getId() : int</c>, and a
    /// method giving nothing back is drawn as it was written.
    /// </summary>
    private static string Returned(string said)
    {
        var close = said.LastIndexOf(')');
        if (close < 0 || close == said.Length - 1) return said;

        var gives = said[(close + 1)..].Trim();

        return gives.Length == 0 ? said : said[..(close + 1)] + " : " + gives;
    }

    /// <summary>
    /// Type parameters written between tildes, drawn between angle brackets as Mermaid draws them — nested ones and all, so
    /// <c>List~List~int~~</c> comes out <c>List&lt;List&lt;int&gt;&gt;</c>. Text whose tildes do not pair off is left as
    /// written, since a lone one is a member's package visibility rather than a type.
    /// </summary>
    public static string Angled(string text)
    {
        var tildes = text.Count(character => character == '~');
        if (tildes == 0 || tildes % 2 != 0) return text;

        var built = new StringBuilder(text.Length);
        var depth = 0;

        for (var at = 0; at < text.Length; at++)
        {
            if (text[at] != '~')
            {
                built.Append(text[at]);
                continue;
            }

            // A tilde closes what is open where nothing more of the type follows it — the end, another tilde, or a space.
            var next = at + 1 < text.Length ? text[at + 1] : '\0';
            var closes = depth > 0 && next is '\0' or '~' or ',' or ')' or ' ' or '\t';

            built.Append(closes ? '>' : '<');
            depth += closes ? -1 : 1;
        }

        return depth == 0 ? built.ToString() : text;
    }
}
