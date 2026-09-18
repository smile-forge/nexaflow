using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// The lines that style what a diagram draws, written the same way wherever Mermaid has them: <c>classDef</c> naming the classes
/// a style stands for, <c>class</c> naming what takes one, and <c>style</c> naming what is styled on its own. A diagram says
/// what its ids and classes are read under and what to call the things it styles, and reads all three the one way.
///
/// <para>
/// What the lines add up to is the model's: a class is a <see cref="MermaidStyle"/>, and what something is drawn with is the
/// <see cref="Every"/> class, then the classes it is given, then the style written for it, each laid over the one before with
/// <see cref="MermaidStyle.Over"/>.
/// </para>
/// </summary>
/// <param name="bare">Whether a character carries a bare id or class name on.</param>
/// <param name="idRole">The role the ids these lines name are read under, which is the role a node's own id is read under.</param>
/// <param name="classRole">The role a class's name is read under.</param>
/// <param name="named">What the ids name, for the reason one is missing: <c>block</c>, <c>node</c>.</param>
public sealed class MermaidStyling(Func<char, bool> bare, string idRole, string classRole, string named)
{
    public const string ClassDefWord = "classDef";
    public const string ClassWord = "class";
    public const string StyleWord = "style";

    /// <summary>The class a <c>classDef</c> names to style everything the diagram draws at once.</summary>
    public const string Every = "default";

    /// <summary>The words these lines start with, which is what a grammar dispatches a line on.</summary>
    public static readonly string[] Words = [ClassDefWord, ClassWord, StyleWord];

    private const string DefinedShape = "A classDef names its class, then the style it is: classDef blue fill:#6e6ce6,stroke:#333.";

    /// <summary>The classes a style is given a name by, and what that style is: <c>classDef blue fill:#6e6ce6,stroke:#333;</c>.</summary>
    public ContentNode Defined(MermaidLine line, string kind)
    {
        line.Word(ClassDefWord, letter: bare);
        line.Room();

        if (!line.Names(Class, classRole, "class")) return line.Shown(DefinedShape);

        line.Room();
        if (!line.Done && !line.Properties(ends: ';')) return line.Shown(DefinedShape);

        return Closed(line, kind, DefinedShape);
    }

    /// <summary>What takes a class, and the class it takes: <c>class A,B blue</c>.</summary>
    public ContentNode Applied(MermaidLine line, string kind)
    {
        line.Word(ClassWord, letter: bare);
        line.Room();

        if (!line.Names(Id, idRole, named)) return line.Shown(AppliedShape);

        line.Room();
        if (!line.Done && !line.Name(classRole, bare)) return line.Shown(AppliedShape);

        return Closed(line, kind, AppliedShape);
    }

    /// <summary>What is styled on its own, and the style written for it: <c>style A fill:#969,stroke:#333</c>.</summary>
    public ContentNode Styled(MermaidLine line, string kind)
    {
        line.Word(StyleWord, letter: bare);
        line.Room();

        if (!line.Names(Id, idRole, named)) return line.Shown(StyledShape);

        line.Room();
        if (!line.Done && !line.Properties(ends: ';')) return line.Shown(StyledShape);

        return Closed(line, kind, StyledShape);
    }

    /// <summary>A styling line as far as it goes, with the semicolon that may close it.</summary>
    public static ContentNode Closed(MermaidLine line, string kind, string shape)
    {
        line.Space();
        line.Token(";");
        line.Space();

        return line.Done ? line.Read(kind) : line.Shown(shape);
    }

    /// <summary>The classes a <c>classDef</c> line names, which the style it writes belongs to every one of.</summary>
    public IReadOnlyList<string> Classes(ContentPart stated) => Said(stated, classRole);

    /// <summary>The ids a <c>class</c> or <c>style</c> line names.</summary>
    public IReadOnlyList<string> Ids(ContentPart stated) => Said(stated, idRole);

    /// <summary>The one class a <c>class</c> line gives what it names, or null where none is written yet.</summary>
    public string? Given(ContentPart stated) => Said(stated, classRole).FirstOrDefault();

    /// <summary>
    /// What each thing the diagram draws is styled with: the <see cref="Every"/> class it starts from, then the classes it is
    /// given, then the style written for it — each laid over the one before, so the nearest thing to it wins.
    /// </summary>
    public static IReadOnlyDictionary<string, MermaidStyle> Styles(
        IEnumerable<string> drawn, IReadOnlyDictionary<string, MermaidStyle> classes,
        IReadOnlyList<(IReadOnlyList<string> Ids, string Class)> taken,
        IReadOnlyList<(IReadOnlyList<string> Ids, MermaidStyle Style)> written)
    {
        var every = classes.TryGetValue(Every, out var start) ? start : MermaidStyle.None;
        var styles = new Dictionary<string, MermaidStyle>(StringComparer.Ordinal);
        foreach (var id in drawn) styles[id] = every;

        foreach (var (ids, name) in taken)
            if (classes.TryGetValue(name, out var style))
                foreach (var id in ids)
                    styles[id] = style.Over(styles.GetValueOrDefault(id, every));

        foreach (var (ids, style) in written)
            foreach (var id in ids)
                styles[id] = style.Over(styles.GetValueOrDefault(id, every));

        return styles;
    }

    /// <summary>
    /// Says where a <c>class</c> or a <c>style</c> line names something nothing writes — something the diagram draws nowhere, or a
    /// class no <c>classDef</c> declares. Mermaid lets the styling be written above what it styles or below it, so whether what it
    /// names exists is a fact about the whole block rather than about any one line; a diagram runs this from a stage of its own.
    /// </summary>
    /// <param name="drawn">The kinds of line that write the things these lines style.</param>
    /// <param name="missing">What is wrong with an id nothing writes, given the id.</param>
    public ContentNode Resolve(ContentNode tree, IReadOnlyList<string> drawn, string classDef, string classKind,
                               string styleKind, Func<string, string> missing)
    {
        var written = Names(tree, drawn, idRole);
        var classes = Names(tree, [classDef], classRole);
        var wrong = new Dictionary<ContentNode, string>();

        foreach (var line in tree.SelfAndDescendants().Where(node => node.Kind == classKind || node.Kind == styleKind))
        {
            foreach (var name in Said(line, idRole))
                if (Text(name) is { Length: > 0 } id && !written.Contains(id))
                    wrong[name] = missing(id);

            if (line.Kind != classKind) continue;

            foreach (var name in Said(line, classRole))
                if (Text(name) is { Length: > 0 } taken && !classes.Contains(taken))
                    wrong[name] = $"No classDef {taken} is written.";
        }

        if (wrong.Count == 0) return tree;

        return AstRewrite.Each(tree, node => wrong.TryGetValue(node, out var reason) ? node.Saying(reason) : node);
    }

    /// <summary>Everything named in a role by the lines of those kinds.</summary>
    private static HashSet<string> Names(ContentNode tree, IReadOnlyList<string> kinds, string role) =>
        [.. tree.SelfAndDescendants()
              .Where(node => kinds.Contains(node.Kind))
              .SelectMany(node => Said(node, role))
              .Select(Text)
              .OfType<string>()
              .Where(name => name.Length > 0)];

    /// <summary>The names a line writes in a role.</summary>
    private static IEnumerable<ContentNode> Said(ContentNode line, string role) =>
        line.SelfAndDescendants()
            .Where(node => node.Kind == MermaidKinds.Name
                           && node.Children.Any(child => child.Kind == MermaidKinds.Words && child.Role == role));

    private static string? Text(ContentNode name) =>
        name.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Words)?.Text;

    private string AppliedShape => $"A class line names the {named}s taking a class, then the class: class A,B blue.";

    private string StyledShape => $"A style line names the {named}s it styles, then the style: style A fill:#969,stroke:#333.";

    /// <summary>Everything a line names in a role, in the order it is written.</summary>
    private static IReadOnlyList<string> Said(ContentPart stated, string role) =>
        [.. stated.SelfAndDescendants()
              .Where(part => part.Kind == MermaidKinds.Words && part.Role == role)
              .Select(part => part.Text)
              .Where(said => said.Length > 0)];

    private bool Id(MermaidLine line) => line.Name(idRole, bare);

    private bool Class(MermaidLine line) => line.Name(classRole, bare);
}
