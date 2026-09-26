using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Architecture.Stages;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Architecture;

/// <summary>
/// What an <c>architecture-beta</c> block says beyond the lines every diagram shares: the <c>group</c>s, the
/// <c>service</c>s and <c>junction</c>s inside them, the edges between them, and the <c>align</c> lines saying which of
/// them share a row or a column.
///
/// <para>
/// The rules are Mermaid's. Everything is named by an id, and an id holds letters, digits, underscores and dashes; an icon
/// goes in round brackets after it and a title in square ones, either of them where it has one; and <c>in</c> puts it in a
/// group declared above it. An edge names the side each end leaves by — <c>db:R --> L:server</c> — and those sides are
/// what say where things sit: the right of one against the left of another puts the second to its right
/// (<c>ArchitectureBuilder</c>).
/// </para>
/// </summary>
public sealed class ArchitectureGrammar : IMermaidGrammar
{
    public const string GroupWord = "group";
    public const string ServiceWord = "service";
    public const string JunctionWord = "junction";
    public const string AlignWord = "align";
    public const string InWord = "in";
    public const string RowWord = "row";
    public const string ColumnWord = "column";

    /// <summary>The <c>{group}</c> written after an id to reach the group it is in rather than the service itself.</summary>
    public const string GroupMark = "{group}";

    /// <summary>The sides an edge may leave by and arrive at.</summary>
    public static readonly string[] Sides = ["L", "R", "T", "B"];

    private const string GroupShape = "A group is an id, with an icon and a title where it has them: group api(cloud)[API] in cloud.";
    private const string ServiceShape = "A service is an id, with an icon and a title where it has them: service db(database)[Database] in api.";
    private const string JunctionShape = "A junction is an id, and the group it is in where it is in one: junction split in api.";
    private const string EdgeShape = "An edge names the side each end leaves by: db:R --> L:server, or db:T -[reads]- B:server.";
    private const string AlignShape = "An align line shares a row or a column between two services or more: align row db1 db2 db3.";

    /// <inheritdoc/>
    public ContentNode? Statement(string text)
    {
        var line = MermaidLine.Of(text);

        return MermaidLine.Keyword(line.Written, MermaidLine.Letter, MermaidLine.TitleWord, GroupWord, ServiceWord, JunctionWord, AlignWord) switch
        {
            MermaidLine.TitleWord => line.Title(),
            GroupWord => Declared(line, GroupWord, ArchitectureKinds.Group, GroupShape, drawn: true),
            ServiceWord => Declared(line, ServiceWord, ArchitectureKinds.Service, ServiceShape, drawn: true),
            JunctionWord => Declared(line, JunctionWord, ArchitectureKinds.Junction, JunctionShape, drawn: false),
            AlignWord => Aligned(line),
            _ => Edge(line),
        };
    }

    /// <inheritdoc/>
    /// <remarks>Another service, with its id still to write — which is what most of a diagram's lines are.</remarks>
    public (string Text, int Caret)? Blank(ContentNode? above) => (ServiceWord + " ", ServiceWord.Length + 1);

    /// <inheritdoc/>
    /// <remarks>
    /// A title and an icon are in brackets, and are put in quotes to hold a quote, a bracket closing them or a comment. An id,
    /// the group something is in and a side are written bare and cannot be quoted at all, so what they cannot hold is dropped —
    /// and a hole standing where one of those goes holds what it will hold.
    /// </remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        var role = part.Kind == Kinds.Hole ? part.Parent?.Role : part.Role;
        return role is ArchitectureRoles.Id or ArchitectureRoles.In or ArchitectureRoles.Side
            ? MermaidWriting.Only(caret, text, MermaidLine.Letter)
            : null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A group, a service and a junction are declared where they are named, and used by every <c>in</c>, edge and
    /// <c>align</c> that names them afterwards — which is one namespace, since Mermaid refuses an id already in use.
    /// </remarks>
    public IReadOnlyList<MermaidName> Names(ContentPart block)
    {
        var said = new Dictionary<string, List<ContentPart>>(StringComparer.Ordinal);

        foreach (var name in block.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Name))
        {
            if (name.Words() is not { Length: > 0 } words) continue;
            if (words.Role is not (ArchitectureRoles.Id or ArchitectureRoles.In)) continue;

            if (!said.TryGetValue(words.Text, out var places)) said[words.Text] = places = [];
            places.Add(name);
        }

        return [.. said.Select(name => new MermaidName(name.Key, name.Value[0], [.. name.Value.Skip(1)]))];
    }

    /// <inheritdoc/>
    /// <remarks>An id is written bare, so what an id cannot hold is dropped.</remarks>
    public string Naming(string name) => new([.. name.Where(MermaidLine.Letter)]);

    /// <inheritdoc/>
    /// <remarks>Whether what a line names is declared, and what it is (<see cref="ResolveArchitecture"/>).</remarks>
    public IEnumerable<IAstStage> Stages(MermaidBlock block, bool writing) => [new ResolveArchitecture(), new WithConfig<ArchitectureConfig>(ArchitectureConfig.Read(block.Config))];

    /// <inheritdoc/>
    /// <remarks>Between a title's or an icon's brackets, and where an id is still to be written.</remarks>
    public bool Holds(ContentNode? holder, ContentNode node) =>
        node.Kind == MermaidKinds.Quoted || (node.Kind == MermaidKinds.Name && node.Width == 0);

    // ── The lines ───────────────────────────────────────────────────────────

    /// <summary>
    /// A group, a service or a junction: its id, the icon and title a drawn one may carry, and the group it is put in.
    /// </summary>
    private static ContentNode Declared(MermaidLine line, string word, string kind, string shape, bool drawn)
    {
        line.Word(word);
        line.Room();
        Id(line, ArchitectureRoles.Id);

        if (drawn)
        {
            line.Space();
            if (line.Sees("("))
            {
                line.Open();
                var named = line.Label("(", ")", ArchitectureRoles.Icon);
                line.Close(MermaidKinds.Icon);
                if (!named) return line.Shown(shape);
            }

            line.Space();
            if (line.Sees("[") && !line.Label("[", "]", ArchitectureRoles.Title)) return line.Shown(shape);
        }

        line.Space();

        if (line.Word(InWord))
        {
            line.Room();
            Id(line, ArchitectureRoles.In);
            line.Space();
        }

        return line.Done ? line.Read(kind) : line.Shown(shape);
    }

    /// <summary>An edge: each end, the side it leaves by, the heads it draws and what is written on it.</summary>
    private static ContentNode Edge(MermaidLine line)
    {
        Id(line, ArchitectureRoles.Id);
        line.Token(GroupMark, ArchitectureRoles.Group);

        if (!line.Token(":")) return line.Shown(EdgeShape);
        if (!Side(line)) return line.Shown(EdgeShape);

        line.Space();
        line.Token("<", ArchitectureRoles.Head);
        if (!Joined(line)) return line.Shown(EdgeShape);

        line.Token(">", ArchitectureRoles.Head);
        line.Space();

        if (!Side(line)) return line.Shown(EdgeShape);
        if (!line.Token(":")) return line.Shown(EdgeShape);

        Id(line, ArchitectureRoles.Id);
        line.Token(GroupMark, ArchitectureRoles.Group);
        line.Space();

        return line.Done ? line.Read(ArchitectureKinds.Edge) : line.Shown(EdgeShape);
    }

    /// <summary>The services sharing a row or a column: <c>align row db1 db2 db3</c>.</summary>
    private static ContentNode Aligned(MermaidLine line)
    {
        line.Word(AlignWord);
        line.Room();

        if (!line.Word(RowWord, MermaidKinds.Key, ArchitectureRoles.Axis) && !line.Word(ColumnWord, MermaidKinds.Key, ArchitectureRoles.Axis))
            return line.Shown(AlignShape);

        line.Room();
        line.Open();

        while (!line.Done)
        {
            if (!MermaidLine.Letter(line.Next)) break;

            Id(line, ArchitectureRoles.Id);
            line.Room();
        }

        line.Close(MermaidKinds.Names, ArchitectureRoles.Id);
        return line.Done ? line.Read(ArchitectureKinds.Align) : line.Shown(AlignShape);
    }

    // ── What the lines are made of ──────────────────────────────────────────

    /// <summary>
    /// An id, as far as it is written: letters, digits, underscores and dashes, which is what Mermaid's ids hold. One still
    /// to be written is where a hole stands, so a line being typed reads as the line it is becoming.
    /// </summary>
    private static void Id(MermaidLine line, string role)
    {
        line.Open();
        line.Words(role, until: Stops);
        line.Close(MermaidKinds.Name, role);
    }

    /// <summary>The side an edge leaves by or arrives at.</summary>
    private static bool Side(MermaidLine line)
    {
        foreach (var side in Sides)
            if (line.Word(side, MermaidKinds.Key, ArchitectureRoles.Side, char.IsLetterOrDigit))
                return true;

        return line.Fail("An edge leaves and arrives on a side of what it joins: L, R, T or B.");
    }

    /// <summary>What joins an edge's two ends: <c>--</c>, or <c>-[reads]-</c> with what is written on it between.</summary>
    private static bool Joined(MermaidLine line)
    {
        if (line.Token("--")) return true;
        if (!line.Token("-", Roles.Open)) return line.Fail("An edge joins its two ends with -- , or with -[reads]- to say what it is.");

        return line.Label("[", "]", ArchitectureRoles.Title) && line.Token("-", Roles.Close);
    }

    /// <summary>
    /// The characters an id ends at: every printable character Mermaid's ids exclude, which is all of them but a letter, a
    /// digit, an underscore and a dash.
    /// </summary>
    private static readonly string Stops =
        new([.. Enumerable.Range(' ', '~' - ' ' + 1).Select(at => (char)at).Where(character => !MermaidLine.Letter(character))]);
}
