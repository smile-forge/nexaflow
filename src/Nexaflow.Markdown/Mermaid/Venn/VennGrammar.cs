using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Venn.Stages;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Venn;

/// <summary>
/// What a <c>venn-beta</c> block says beyond the lines every diagram shares: a <c>title</c>, the <c>set</c>s, the
/// <c>union</c>s where they overlap, the <c>text</c> items written inside either, and the <c>style</c>s of all three.
///
/// <para>
/// The rules are Mermaid's. A name is a word — letters, digits, <c>_</c> and <c>-</c> — or words in quotes; a label is
/// written in brackets after it, <c>["Alpha"]</c> or <c>[Alpha]</c>; a size after a colon. A union is the overlap of
/// the sets its names list, a comma between each. A <c>text</c> line indented under a set or a union is an item in it,
/// and one written at the start of a line names its region first: <c>text A,B AB1["OpenAPI"]</c>. A style sets
/// <c>fill</c>, <c>color</c>, <c>stroke</c>, <c>stroke-width</c> and <c>fill-opacity</c>.
/// </para>
/// <para>
/// Which of that a line means where it stands — whether a union's sets were written above it, which region an indented
/// item sits in — is not in the line's own characters, so it is the stages' (<see cref="Stages"/>). This reads what each
/// line says, through <see cref="MermaidLine"/>, and holds what it cannot read as written with the reason.
/// </para>
/// </summary>
public sealed class VennGrammar : IMermaidGrammar
{
    public const string Set = "set";
    public const string Union = "union";
    public const string Text = "text";
    public const string Style = "style";

    /// <summary>What a <c>style</c> line can set.</summary>
    public static readonly IReadOnlyList<string> Styles = ["fill", "color", "stroke", "stroke-width", "fill-opacity"];

    /// <inheritdoc/>
    /// <remarks>Nothing follows <c>venn-beta</c> on its line.</remarks>
    public ContentNode? Header(string arguments) =>
        ContentNode.Shown(arguments, "Nothing follows venn-beta on its line: a title is written on a line of its own.",
                          MermaidRoles.Arguments);

    /// <inheritdoc/>
    public ContentNode? Statement(string text)
    {
        var line = MermaidLine.Of(text);

        return MermaidLine.Keyword(line.Written, MermaidLine.TitleWord, Set, Union, Text, Style) switch
        {
            MermaidLine.TitleWord => line.Title(quotes: true),
            Set => Region(line, Set),
            Union => Region(line, Union),
            Text => Item(line),
            Style => Styled(line),
            _ => line.Shown("A Venn diagram line is a set, a union, a text item, a style or a title: set A[\"Alpha\"]:20."),
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Under a set or a union, an item in it, indented under it; under an item, another in the same region, written the
    /// way that one names it; anywhere else, another set. Each with its name still to write and the caret in its quotes.
    /// </remarks>
    public (string Text, int Caret)? Blank(ContentNode? above) => above?.Kind switch
    {
        VennKinds.Set or VennKinds.Union => ("  text \"\"", 8),
        VennKinds.Text when above.Part(VennRoles.Region) is { } region => ($"text {region.Print()} \"\"", 7 + region.Width),
        VennKinds.Text => ("text \"\"", 6),
        VennKinds.Style => null,
        _ => ("set \"\"", 5),
    };

    /// <inheritdoc/>
    /// <remarks>
    /// A bare name holds a word — letters, digits, <c>_</c> and <c>-</c>, and a set's starts with a letter or an underscore —
    /// and is put in quotes to hold anything else; see <see cref="MermaidWriting.Escape"/> for quotes and labels.
    /// </remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text) =>
        MermaidWriting.Escape(part, caret, text, (name, said) => Bare(said, set: name.Parent?.Kind == VennKinds.Set));

    /// <inheritdoc/>
    /// <remarks>
    /// A set is declared on its <c>set</c> line and used in the unions that overlap it, the items that name it as their
    /// region and the styles that style it; an item is declared on its <c>text</c> line and used by a style naming it alone.
    /// </remarks>
    public IReadOnlyList<MermaidName> Names(ContentPart block)
    {
        var lines = block.SelfAndDescendants()
            .Where(part => part.Kind is VennKinds.Set or VennKinds.Union or VennKinds.Text or VennKinds.Style)
            .ToList();

        var ofSets = lines.SelectMany(line => line.Kind switch
        {
            VennKinds.Union => line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Names).Named(),
            VennKinds.Text => line.Part(VennRoles.Region).Named(),
            VennKinds.Style => line.Part(VennRoles.Target).Named(),
            _ => [],
        }).ToList();

        var ofItems = lines.Where(line => line.Kind == VennKinds.Style)
            .Select(line => line.Part(VennRoles.Target).Named())
            .Where(targets => targets.Count == 1)
            .Select(targets => targets[0])
            .ToList();

        return
        [
            .. lines.Where(line => line.Kind is VennKinds.Set or VennKinds.Text)
                .Select(line => (Line: line, Name: line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name)))
                .Where(declared => declared.Name is not null)
                .Select(declared =>
                {
                    var name = Said(declared.Name!);
                    var uses = declared.Line.Kind == VennKinds.Set ? ofSets : ofItems;
                    return new MermaidName(name, declared.Name!, [.. uses.Where(use => Said(use) == name)]);
                }),
        ];

        static string Said(ContentPart name) => name.Words()?.Text ?? string.Empty;
    }

    /// <inheritdoc/>
    /// <remarks>Bare where it can be — a word starting with a letter or an underscore — and in quotes otherwise.</remarks>
    public string Naming(string name) => Bare(name, set: true) ? name : "\"" + name + "\"";

    /// <inheritdoc/>
    /// <remarks>
    /// A set or a union is gathered with the items indented under it into one region (<see cref="GroupRegions"/>), so the tree
    /// holds what the diagram is made of rather than only the lines it was written on. Which region each part stands for — the
    /// sets a union overlaps, the region an item written on its own sits in, what a style styles — is a fact about the lines
    /// above it rather than its own, so it is worked out and hung underneath (<see cref="ResolveRegions"/>), and so is what each is
    /// styled with (<see cref="ResolveStyles"/>).
    /// </remarks>
    public IEnumerable<IAstStage> Stages(MermaidBlock block, bool writing) => [new GroupRegions(), new ResolveRegions(), new ResolveStyles(), new WithConfig<VennConfig>(VennConfig.Read(block.Config))];

    /// <inheritdoc/>
    /// <remarks>
    /// Between a name's quotes or after the comma before it, and between a label's brackets or its quotes. A size gets none,
    /// being drawn as how much room a region takes rather than as anything to write in.
    /// </remarks>
    public bool Holds(ContentNode? holder, ContentNode node) =>
        node.Kind is MermaidKinds.Name or MermaidKinds.Label
        || (node.Kind == MermaidKinds.Quoted && holder?.Kind == MermaidKinds.Label);

    /// <summary>Whether a name can be written without quotes — as a set's name, where <paramref name="set"/> says it is one.</summary>
    private static bool Bare(string name, bool set) =>
        name.Length > 0
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
        && (!set || char.IsAsciiLetter(name[0]) || name[0] == '_');

    // ── Lines ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A <c>set</c> — <c>set A["Alpha"]:20</c> — or a <c>union</c> — <c>union A,B["AB"]:3</c>. A size not yet written stands
    /// after whatever space was left for it after the colon, which is where typing it puts it.
    /// </summary>
    private static ContentNode Region(MermaidLine line, string keyword)
    {
        var shape = keyword == Set
            ? "A set is its name, then its label and its size where they are written: set A[\"Alpha\"]:20."
            : "A union is its sets, then its label and its size where they are written: union A,B[\"AB\"]:3.";

        line.Word(keyword);
        line.Space();

        if (line.Done)
            return line.Shown(keyword == Set
                                  ? "A set is named: set A, or set A[\"Alpha\"]:20."
                                  : "A union names the sets it is the overlap of: union A,B[\"AB\"]:3.");

        var named = keyword == Set
            ? line.Name(VennRoles.Id, Letter, name => char.IsLetter(name[0]) || name[0] == '_'
                                                  ? null
                                                  : "A set's name starts with a letter or an underscore, or is written in quotes.")
            : line.Names(Id, Roles.Element, VennRoles.Id);
        if (!named) return line.Shown(shape);

        line.Space();
        if (line.Next == '[')
        {
            if (!line.Label("[", "]", VennRoles.Label)) return line.Shown(shape);
            line.Space();
        }

        if (line.Token(":"))
        {
            // Nothing after the colon: the size is still to come, and goes after whatever space was left for it.
            line.Room();
            line.Amount(VennRoles.Size, MermaidNumber.Positive("A size is a number greater than nought."));
        }

        return line.Done ? line.Read(keyword == Set ? VennKinds.Set : VennKinds.Union) : line.Shown(shape);
    }

    /// <summary>
    /// A <c>text</c> item: <c>text A1["React"]</c>, sitting in the set or union it is indented under — or, written at the
    /// start of a line, <c>text A,B AB1["OpenAPI"]</c>, naming its region first.
    /// </summary>
    private static ContentNode Item(MermaidLine line)
    {
        const string Shape = "A text item is its name and its label: text A1[\"React\"] — or, naming its region first, text A,B AB1[\"OpenAPI\"].";

        line.Word(Text);
        line.Space();
        if (line.Done) return line.Shown(Shape);

        var start = line.Save();
        if (!line.Names(Id, VennRoles.Region, VennRoles.Id)) return line.Shown(Shape);

        if (line.Spaced && line.Past is not ('\0' or '['))
        {
            // A second name: the first was the region.
            line.Space();
            if (!Id(line)) return line.Shown(Shape);
        }
        else if (line.Since(start) is [{ Children: [var only] }])
        {
            // One name and nothing more: the item's own.
            line.Restore(start);
            line.Add(only);
        }
        else
        {
            return line.Shown(Shape);
        }

        line.Space();
        if (line.Next == '[' && !line.Label("[", "]", VennRoles.Label)) return line.Shown(Shape);

        return line.Done ? line.Read(VennKinds.Text) : line.Shown(Shape);
    }

    /// <summary>A <c>style</c>: what it styles, and <c>name:value</c> properties with a comma between each.</summary>
    private static ContentNode Styled(MermaidLine line)
    {
        const string Shape = "A style names what it styles and what it sets, a comma between each: style A fill:#ff6b6b, stroke:#333.";

        line.Word(Style);
        line.Space();
        if (line.Done || !line.Names(Id, VennRoles.Target, VennRoles.Id)) return line.Shown(Shape);

        var at = line.At;
        line.Space();
        if (line.At == at || line.Done || !line.Properties(Styles)) return line.Shown(Shape);

        return line.Read(VennKinds.Style);
    }

    /// <summary>
    /// A name, in quotes or bare. Only a set's own name is held to Mermaid's rule that a bare one starts with a letter or an
    /// underscore: an item may be called by a number.
    /// </summary>
    private static bool Id(MermaidLine line) => line.Name(VennRoles.Id, Letter);

    /// <summary>What a bare name is made of.</summary>
    private static bool Letter(char character) => char.IsLetterOrDigit(character) || character is '_' or '-' or '.' or '+';
}
