using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.C4.Stages;
using Nexaflow.Markdown.Mermaid.Sequence;

using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.C4;

/// <summary>
/// What a C4 block says. The header is Mermaid's; the body is C4-PlantUML's macro set, which is one shape throughout — a
/// name, its arguments between brackets, and the brace that may open a block after them:
///
/// <code>
/// Person(customer, "Banking Customer", "A customer of the bank", $tags="v1")
/// System_Boundary(c1, "Internet Banking", "System") {
/// Rel(customer, spa, "Submits credentials", "HTTPS", $index=Index())
/// }
/// </code>
///
/// <para>
/// <strong>This says only what a line is.</strong> A macro, a boundary opening one, the line closing one, a line a pasted
/// diagram brought with it — and which of the arguments name an element, which is what lets a rename carry. What a macro
/// <em>means</em> is the model's: <see cref="C4Structure"/> for a diagram laid out as a graph, <see cref="C4Sequence"/> for
/// one laid out as a timeline.
/// </para>
///
/// <para>
/// <strong>A C4 diagram may be written in two languages at once.</strong> A <c>C4Sequence</c> takes <c>alt</c>,
/// <c>note over</c> and <c>activate</c> among its macros, so <see cref="C4SequenceGrammar"/> names the sequence diagram's
/// own grammar as the one every line that is not a macro belongs to. A structural diagram names none, and a line that is
/// not a macro is a line nobody meant to write.
/// </para>
/// </summary>
public class C4Grammar : IMermaidGrammar
{
    /// <summary>The macros that open a boundary round everything written until the line closing it.</summary>
    public static readonly string[] Boundaries =
        ["Boundary", "Enterprise_Boundary", "System_Boundary", "Container_Boundary", "Deployment_Node",
         "Node", "Node_L", "Node_R"];

    /// <summary>And the macro that closes one, which <c>}</c> does as well.</summary>
    public const string Shutter = "Boundary_End";

    /// <summary>What a relationship's two ends are called where they are given by name rather than by position.</summary>
    public static readonly string[] Ends = ["from", "to"];

    /// <summary>And what an element's own name is called.</summary>
    public const string Named = "alias";

    private const string MacroShape = "A macro is written Person(alias, \"Label\"), with its arguments between brackets.";
    private const string ShutShape = "A boundary is closed by } or by Boundary_End().";

    /// <summary>
    /// The language every line that is not a macro belongs to, or null where a C4 diagram is written in C4's words alone.
    /// </summary>
    protected virtual IMermaidGrammar? Within => null;

    /// <inheritdoc/>
    /// <remarks>Nothing follows the keyword: everything a C4 diagram says, it says on a line of its own.</remarks>
    public ContentNode? Header(string arguments)
    {
        var line = MermaidLine.Of(arguments);
        if (line.Done) return null;

        return ContentNode.Shown(arguments, "Nothing follows a C4 header — it is written on a line of its own.",
                                 MermaidRoles.Arguments);
    }

    /// <inheritdoc/>
    public ContentNode? Statement(string text)
    {
        var line = MermaidLine.Of(text, comments: false);

        // PlantUML's own comment, which an apostrophe opens; a %% comment is every diagram's, and MermaidLine takes it.
        if (line.Written.TrimStart().StartsWith('\'')) return Aside(line, Kinds.Comment, Roles.Trivia);

        // The wrapper lines a diagram pasted from PlantUML brings with it.
        if (line.Written.TrimStart() is ['@', ..] or ['!', ..]) return Aside(line, C4Kinds.Aside, C4Roles.Aside);

        var read = MermaidLine.Of(text);

        if (MermaidLine.Keyword(read.Written, MermaidLine.TitleWord) is not null) return read.Title();
        if (read.Written.Trim() is "}" or "})") return Shutting(read);
        if (Heads(read.Written) is { } name) return Called(read, name);

        if (this.Within is { } within) return within.Statement(text);

        return read.Done ? null : read.Shown(MacroShape);
    }

    /// <inheritdoc/>
    /// <remarks>Nothing: a macro is written on one line, and a boundary is closed by a line of its own.</remarks>
    public IEnumerable<MermaidStretch> Stretches => [];

    /// <inheritdoc/>
    /// <remarks>Under a macro, a relationship — which is what most of a C4 diagram is.</remarks>
    public (string Text, int Caret)? Blank(ContentNode? above) => above?.Kind switch
    {
        C4Kinds.Macro or C4Kinds.Boundary or C4Kinds.Aside => Relationship,
        _ => this.Within is { } within ? within.Blank(above) : Relationship,
    };

    private static (string Text, int Caret) Relationship => ("Rel(, , \"\")", 4);

    /// <inheritdoc/>
    /// <remarks>
    /// An argument in quotes holds anything but a quote; a bare one holds anything that does not close it; and a name holds
    /// what a name holds, since the same name is written in a native <c>note over</c> where it cannot be quoted at all.
    /// </remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        // In quotes anything but a quote goes in as it is, and the quote has already been written as its entity code.
        if (part.Parent?.Children.Any(child => child.Role == Roles.Open && child.Text == "\"") ?? false) return null;

        var role = part.Kind == Kinds.Hole ? part.Parent?.Role ?? part.Role : part.Role;

        if (role is C4Roles.Value) return MermaidWriting.Only(caret, text, Argued);
        if (role is C4Roles.Key or C4Roles.Macro) return MermaidWriting.Only(caret, text, MermaidLine.Letter);
        if (role is C4Roles.Aside) return null;

        // A name is written bare in a macro's argument list, so what neither that nor a native line can hold is dropped —
        // the same name is used in both, and it has to read in each.
        if (role is C4Roles.Alias or SequenceRoles.Id) return MermaidWriting.Only(caret, text, Bare);

        return this.Within?.Escaping(part, caret, text);
    }

    /// <inheritdoc/>
    /// <remarks>An element is named by the macro declaring it and used by every relationship and boundary naming it.</remarks>
    public IReadOnlyList<MermaidName> Names(ContentPart block)
    {
        var said = new Dictionary<string, List<ContentPart>>(StringComparer.Ordinal);

        foreach (var name in block.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Name))
        {
            if (name.Words() is not { Role: SequenceRoles.Id, Length: > 0 } words) continue;

            if (!said.TryGetValue(words.Text, out var places)) said[words.Text] = places = [];
            places.Add(name);
        }

        return [.. said.Select(name => new MermaidName(name.Key, name.Value[0], [.. name.Value.Skip(1)]))];
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A name is written bare in a macro's argument list and bare again in a native line, so what neither can hold is
    /// dropped rather than quoted.
    /// </remarks>
    public string Naming(string name) => new([.. name.Where(Bare)]);

    /// <inheritdoc/>
    /// <remarks>Which boundary each line is written inside, which is a fact about the whole block rather than about a line.</remarks>
    public virtual IEnumerable<IAstStage> Stages(MermaidBlock block, bool writing) => [new ResolveBoundaries()];

    /// <inheritdoc/>
    /// <remarks>Between an argument's quotes, and wherever a line of the other language has one.</remarks>
    public bool Holds(ContentNode? holder, ContentNode node) =>
        node.Kind == MermaidKinds.Quoted || (this.Within?.Holds(holder, node) ?? false);

    /// <summary>The name of the macro a line calls, or null where it calls none.</summary>
    public static string? Heads(string written)
    {
        var end = 0;
        while (end < written.Length && (char.IsLetterOrDigit(written[end]) || written[end] == '_')) end++;
        if (end == 0) return null;

        var past = end;
        while (past < written.Length && written[past] is ' ' or '\t') past++;

        return past < written.Length && written[past] == '(' ? written[..end] : null;
    }

    /// <summary>
    /// Where the argument written at <paramref name="at"/> ends: the comma or the bracket closing it, outside any quotes and
    /// any brackets of its own — which is what lets <c>"C#, ASP.NET Core"</c> and <c>Index()</c> be one argument each.
    /// </summary>
    public static int Ending(string written, int at)
    {
        var depth = 0;
        var quoted = false;

        for (var index = at; index < written.Length; index++)
        {
            var character = written[index];

            if (character == '"') quoted = !quoted;
            else if (quoted) continue;
            else if (character == '(') depth++;
            else if (character == ')' && depth == 0) return index;
            else if (character == ')') depth--;
            else if (character == ',' && depth == 0) return index;
        }

        return written.Length;
    }

    /// <summary>Whether a character carries an element's name on: the letters an identifier is written with.</summary>
    public static bool Bare(char character) =>
        char.IsLetterOrDigit(character) || character is '_' or '-' or '.' || character > '';

    /// <summary>And whether one carries a bare argument on, which is anything that does not close it.</summary>
    private static bool Argued(char character) => character is not ('"' or ',' or '(' or ')') && !char.IsControl(character);

    // ── The lines ───────────────────────────────────────────────────────────

    /// <summary>A line read and drawn as nothing: a PlantUML comment, or a wrapper a pasted diagram brought with it.</summary>
    private static ContentNode Aside(MermaidLine line, string kind, string role)
    {
        line.Room();
        line.Words(role);

        return line.Read(kind);
    }

    /// <summary>The <c>}</c> that closes a boundary, which a <c>)</c> may follow.</summary>
    private static ContentNode Shutting(MermaidLine line)
    {
        line.Room();
        line.Token("}", Roles.Close);
        line.Token(")", Roles.Close);

        return line.Closed(C4Kinds.Ends, ShutShape);
    }

    /// <summary>One macro call: its name, its arguments, and the brace that may open a block after them.</summary>
    private static ContentNode Called(MermaidLine line, string name)
    {
        var kind = Boundaries.Contains(name, StringComparer.OrdinalIgnoreCase) ? C4Kinds.Boundary
                 : string.Equals(name, Shutter, StringComparison.OrdinalIgnoreCase) ? C4Kinds.Ends
                 : C4Kinds.Macro;

        var aliases = Aliased(name);

        line.Room();
        line.Word(name, MermaidKinds.Key, C4Roles.Macro, Bare);
        line.Space();

        if (!line.Token("(", Roles.Open)) return line.Shown(MacroShape);

        line.Room();
        line.Open();

        var at = 0;
        while (!line.Done && line.Next != ')')
        {
            if (Argued(line, aliases, at)) at++;

            line.Room();
            if (line.Next != ',') break;

            line.Token(",");
            line.Room();
        }

        line.Close(MermaidKinds.Properties);

        if (!line.Token(")", Roles.Close)) return line.Shown(MacroShape);

        line.Space();
        line.Token("{", Roles.Open);

        return line.Closed(kind, MacroShape);
    }

    /// <summary>One argument: given by name where a <c>$</c> opens it, and by position otherwise.</summary>
    private static bool Argued(MermaidLine line, Aliases aliases, int at)
    {
        line.Open();

        if (line.Next != '$')
        {
            Valued(line, aliases.At.Contains(at));
            line.Close(MermaidKinds.Property);

            return true;
        }

        line.Token("$");

        var mark = line.At;
        if (!line.Name(C4Roles.Key, MermaidLine.Letter))
        {
            line.Close(MermaidKinds.Property);
            return false;
        }

        var key = line.Written[mark..line.At];

        line.Space();
        line.Token("=");
        line.Room();
        Valued(line, aliases.Keys.Contains(key, StringComparer.OrdinalIgnoreCase));
        line.Close(MermaidKinds.Property);

        return false;
    }

    /// <summary>What an argument is: the name of an element, or a value in quotes or bare.</summary>
    private static void Valued(MermaidLine line, bool alias)
    {
        if (alias)
        {
            if (line.Next == '"')
            {
                line.Name(SequenceRoles.Id, Bare);
                return;
            }

            // A name with nothing written in it yet is one still to be written, so it is read as an empty one.
            line.Open();
            if (line.At <= line.Written.Length) line.Words(SequenceRoles.Id, Ending(line.Written, line.At));
            line.Close(MermaidKinds.Name, SequenceRoles.Id);

            return;
        }

        if (line.Next == '"')
        {
            line.Quoted(C4Roles.Value, what: "value");
            return;
        }

        line.Words(C4Roles.Value, Ending(line.Written, line.At));
    }

    /// <summary>Which of a macro's arguments name an element — by position, and by the name they may be given under.</summary>
    private static Aliases Aliased(string name)
    {
        var word = name.ToLowerInvariant();

        if (word.StartsWith("relindex", StringComparison.Ordinal)) return new([1, 2], Ends);
        if (word.StartsWith("rel", StringComparison.Ordinal) || word.StartsWith("birel", StringComparison.Ordinal))
            return new([0, 1], Ends);

        if (word is "updaterelstyle") return new([0, 1], Ends);
        if (Boundaries.Contains(name, StringComparer.OrdinalIgnoreCase)) return new([], []);

        return Elemental(word) ? new([0], [Named]) : new([], []);
    }

    /// <summary>
    /// Whether a macro declares an element. The names are systematic —
    /// <c>{Person|System|Container|Component}{Db|Queue}?{_Ext}?</c> — so this reads them apart rather than listing every one.
    /// </summary>
    public static bool Elemental(string name)
    {
        var word = name.ToLowerInvariant();
        if (word.EndsWith("_ext", StringComparison.Ordinal)) word = word[..^4];

        foreach (var kind in new[] { "person", "system", "container", "component" })
        {
            if (!word.StartsWith(kind, StringComparison.Ordinal)) continue;

            return word[kind.Length..] is "" or "db" or "queue";
        }

        return false;
    }

    /// <summary>Which arguments of one macro name an element.</summary>
    private readonly record struct Aliases(IReadOnlyList<int> At, IReadOnlyList<string> Keys);
}
