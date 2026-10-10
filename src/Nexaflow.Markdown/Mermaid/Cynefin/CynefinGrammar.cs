using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Cynefin.Stages;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Cynefin;

/// <summary>
/// What a <c>cynefin-beta</c> block says beyond the lines every diagram shares: a <c>title</c>, the five sense-making
/// domains, the items placed in each, and the transitions between them.
///
/// <para>
/// A domain is opened by its one word on a line of its own — <c>clear</c>, <c>complicated</c>, <c>complex</c>,
/// <c>chaotic</c>, <c>confusion</c> — and every line under it is an item sitting in it, written in quotes or bare to the
/// end of its line. A line starting with a domain's word and saying more than it is an item too, so <c>clear enough</c>
/// says what it says; only <c>--&gt;</c> after the word makes the line a transition: <c>complex --&gt; complicated :
/// "Pattern found"</c>, its label optional.
/// </para>
/// <para>
/// An item sits in the domain opened above it, which is the order the lines are written in; one written before any domain is
/// opened sits in none, which is worked out over the block (<see cref="ResolveDomains"/>).
/// </para>
/// </summary>
public sealed class CynefinGrammar : IMermaidGrammar
{
    /// <summary>What goes between the two domains of a transition.</summary>
    public const string Arrow = "-->";

    public const string Clear = "clear";
    public const string Complicated = "complicated";
    public const string Complex = "complex";
    public const string Chaotic = "chaotic";
    public const string Confusion = "confusion";

    /// <summary>The word opening each domain — the only five there are.</summary>
    public static readonly IReadOnlyList<string> Domains = [Clear, Complicated, Complex, Chaotic, Confusion];

    private const string Moving = "A transition goes from one domain to another: complex --> complicated : \"Pattern found\".";
    private const string Saying = "An item is what it says, in quotes: \"Investigate root cause\".";

    /// <inheritdoc/>
    public ContentNode? Statement(string text, int at)
    {
        var line = MermaidLine.Of(text, at);

        if (MermaidLine.Keyword(line.Written, MermaidLine.TitleWord) is not null) return line.Title();
        if (MermaidLine.Keyword(line.Written, Letter, [.. Domains]) is { } word && Opens(line, word) is { } read) return read;

        // Anything else on a line is what it says, in the domain opened above it — the reading starts again, having taken nothing.
        return Item(MermaidLine.Of(text, at));
    }

    /// <inheritdoc/>
    /// <remarks>Where an item has no domain above it (<see cref="ResolveDomains"/>), and what the front matter asks for.</remarks>
    public IEnumerable<IAstStage> Stages(MermaidBlock block, bool writing) =>
        [new ResolveDomains(), new WithConfig<CynefinConfig>(CynefinConfig.Read(block.Config))];

    /// <inheritdoc/>
    /// <remarks>Where what an item or a transition says is still to write, between its quotes.</remarks>
    public bool Holds(ContentNode? holder, ContentNode node) => node.Kind == CynefinKinds.Text;

    // ── Lines ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A domain's line, where its word is all that is written, or a transition from that domain — and null where the line
    /// says something else, which makes it an item.
    /// </summary>
    private static ContentNode? Opens(MermaidLine line, string word)
    {
        line.Word(word, letter: Letter);
        line.Space();
        if (line.Done) return line.Read(CynefinKinds.Domain);
        if (!line.Sees(Arrow)) return null;

        line.Token(Arrow);
        line.Room();

        // The domain it goes to is still to write.
        if (line.Done) return line.Read(CynefinKinds.Move);

        if (MermaidLine.Keyword(line.Rest, Letter, [.. Domains]) is not { } to) return line.Shown(Moving);

        line.Word(to, letter: Letter);
        line.Space();
        if (line.Done) return line.Read(CynefinKinds.Move);

        if (!line.Token(":")) return line.Shown(Moving);

        line.Room();
        if (!line.Done && !Text(line, CynefinRoles.Label)) return line.Shown(Moving);

        return line.Done ? line.Read(CynefinKinds.Move) : line.Shown(Moving);
    }

    /// <summary>What carries a domain's word on: no domain's word holds a hyphen, so an arrow hard against it closes it.</summary>
    private static bool Letter(char character) => char.IsLetterOrDigit(character) || character == '_';

    /// <summary>An item: what it says, in quotes or bare to the end of its line.</summary>
    private static ContentNode Item(MermaidLine line)
    {
        if (!Text(line, CynefinRoles.Says)) return line.Shown(Saying);

        line.Space();
        return line.Done ? line.Read(CynefinKinds.Item) : line.Shown(Saying);
    }

    /// <summary>Text as a piece of its own: in quotes, or bare to where the line ends — false where its quote is never closed.</summary>
    private static bool Text(MermaidLine line, string role)
    {
        line.Open();

        if (line.Next == '"')
        {
            if (!line.Quoted(role, kind: null)) return false;
        }
        else
        {
            line.Words(role);
        }

        line.Close(CynefinKinds.Text, role);
        return true;
    }
}
