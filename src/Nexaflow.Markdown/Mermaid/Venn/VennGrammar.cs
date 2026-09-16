using System.Globalization;
using Nexaflow.Markdown.Ast;

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
/// item sits in — is not in the line's own characters, so it is the pipeline's (<see cref="VennPipeline"/>). This reads
/// what each line says, and holds what it cannot read as written with the reason.
/// </para>
/// </summary>
public sealed class VennGrammar : IMermaidGrammar
{
    public const string Title = "title";
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
        // A comment may close any line. What is read stops short of it, and it is kept inside the line it closes.
        var comment = Comment(text);
        var body = comment < 0 ? text : text[..comment];
        var written = body.TrimEnd();

        var read = Keyword(written) switch
        {
            Title => Titled(written),
            Set => Region(written, body, Set),
            Union => Region(written, body, Union),
            Text => Item(written, body),
            Style => Styled(written, body),
            _ => ContentNode.Shown(written,
                                   "A Venn diagram line is a set, a union, a text item, a style or a title: set A[\"Alpha\"]:20."),
        };

        return read is null || comment < 0 ? read : Commented(read, text, comment);
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
    /// A name or a label in quotes holds anything but a quote, which is written <c>#quot;</c>. A bare name holds a word —
    /// letters, digits, <c>_</c> and <c>-</c>, and a set's starts with a letter or an underscore — and a bare label anything
    /// but a quote, a bracket closing it or a comment; given anything else, either is put in quotes to hold it.
    /// </remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (part.Parent is not { } holder || part.Kind is not (VennKinds.Name or Kinds.Hole)) return null;

        if (holder.Children.Any(child => child.Role == Roles.Open && child.Text == "\""))
            return MermaidWriting.InQuotes(caret, text);

        var said = part.Kind == Kinds.Hole ? string.Empty : part.Text;
        var at = Math.Clamp(caret - part.Start, 0, said.Length);
        var (before, after) = (said[..at] + text, said[at..]);

        switch (holder.Kind)
        {
            case VennKinds.Id when !Bare(before + after, set: holder.Parent?.Kind == VennKinds.Set):
                return MermaidWriting.Quoting(part.Start, part.Start + said.Length, before, after);

            // Inside the brackets, space and all: a label in quotes is written hard against them.
            case VennKinds.Label when (before + after).Any(character => character is '"' or ']' or '%'):
                var open = holder.Children.First(child => child.Role == Roles.Open).End;
                var close = holder.Children.LastOrDefault(child => child.Role == Roles.Close)?.Start ?? holder.End;
                return MermaidWriting.Quoting(open, close, before, after);
        }

        return null;
    }

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
            VennKinds.Union => Ids(line.Children.FirstOrDefault(child => child.Kind == VennKinds.Sets)),
            VennKinds.Text => Ids(line.Part(VennRoles.Region)),
            VennKinds.Style => Ids(line.Part(VennRoles.Target)),
            _ => [],
        }).ToList();

        var ofItems = lines.Where(line => line.Kind == VennKinds.Style)
            .Select(line => Ids(line.Part(VennRoles.Target)))
            .Where(targets => targets.Count == 1)
            .Select(targets => targets[0])
            .ToList();

        return
        [
            .. lines.Where(line => line.Kind is VennKinds.Set or VennKinds.Text)
                .Select(line => (Line: line, Id: line.Children.FirstOrDefault(child => child.Kind == VennKinds.Id)))
                .Where(declared => declared.Id is not null)
                .Select(declared =>
                {
                    var name = Said(declared.Id!);
                    var uses = declared.Line.Kind == VennKinds.Set ? ofSets : ofItems;
                    return new MermaidName(name, declared.Id!, [.. uses.Where(use => Said(use) == name)]);
                }),
        ];

        static List<ContentPart> Ids(ContentPart? names) =>
            names?.Children.Where(child => child.Kind == VennKinds.Id).ToList() ?? [];

        static string Said(ContentPart id) =>
            id.Children.FirstOrDefault(child => child.Kind == VennKinds.Name)?.Text ?? string.Empty;
    }

    /// <inheritdoc/>
    /// <remarks>Bare where it can be — a word starting with a letter or an underscore — and in quotes otherwise.</remarks>
    public string Naming(string name) => Bare(name, set: true) ? name : "\"" + name + "\"";

    /// <summary>Whether a name can be written without quotes — as a set's name, where <paramref name="set"/> says it is one.</summary>
    private static bool Bare(string name, bool set) =>
        name.Length > 0
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
        && (!set || char.IsAsciiLetter(name[0]) || name[0] == '_');

    // ── Lines ───────────────────────────────────────────────────────────────

    /// <summary>A <c>title …</c>, in quotes or not. Null where the word is there with nothing after it.</summary>
    private static ContentNode? Titled(string written)
    {
        var pieces = new List<ContentNode> { Key(written[..Title.Length]) };
        var at = Space(written, Title.Length, pieces);
        if (at >= written.Length) return null;

        var rest = written[at..];
        pieces.Add(rest.Length >= 2 && rest[0] == '"' && rest[^1] == '"'
                       ? Quoted(rest, VennRoles.Label)
                       : ContentNode.Leaf(VennKinds.Name, rest, VennRoles.Label));

        return ContentNode.Branch(VennKinds.Title, pieces);
    }

    /// <summary>
    /// A <c>set</c> — <c>set A["Alpha"]:20</c> — or a <c>union</c> — <c>union A,B["AB"]:3</c>. A size not yet written stands
    /// after whatever space was left for it after the colon, which is where typing it puts it.
    /// </summary>
    private static ContentNode Region(string written, string body, string keyword)
    {
        var pieces = new List<ContentNode> { Key(written[..keyword.Length]) };
        var at = Space(written, keyword.Length, pieces);

        if (at >= written.Length)
            return ContentNode.Shown(written, keyword == Set
                                         ? "A set is named: set A, or set A[\"Alpha\"]:20."
                                         : "A union names the sets it is the overlap of: union A,B[\"AB\"]:3.");

        string? reason;
        if (keyword == Set)
        {
            if (!Identifier(written, ref at, pieces, strict: true, out reason)) return ContentNode.Shown(written, reason);
        }
        else if (Listed(written, body, ref at, Roles.Element, out reason) is { } sets) pieces.Add(sets);
        else return ContentNode.Shown(written, reason);

        at = Space(written, at, pieces);

        if (at < written.Length && written[at] == '[')
        {
            if (!Labelled(written, ref at, pieces, out reason)) return ContentNode.Shown(written, reason);
            at = Space(written, at, pieces);
        }

        if (at < written.Length && written[at] == ':')
        {
            pieces.Add(ContentNode.Leaf(Kinds.Token, ":", Roles.Separator));

            // Nothing after the colon: the size is still to come, and goes after whatever space was left for it.
            at = at + 1 < written.Length ? Space(written, at + 1, pieces) : Space(body, at + 1, pieces);
            pieces.Add(Weight(written.Length > at ? written[at..] : string.Empty));
            at = Math.Max(at, written.Length);
        }

        if (at < written.Length)
            return ContentNode.Shown(written, keyword == Set
                                         ? "A set is its name, then its label and its size where they are written: set A[\"Alpha\"]:20."
                                         : "A union is its sets, then its label and its size where they are written: union A,B[\"AB\"]:3.");

        return ContentNode.Branch(keyword == Set ? VennKinds.Set : VennKinds.Union, pieces);
    }

    /// <summary>
    /// A <c>text</c> item: <c>text A1["React"]</c>, sitting in the set or union it is indented under — or, written at the
    /// start of a line, <c>text A,B AB1["OpenAPI"]</c>, naming its region first.
    /// </summary>
    private static ContentNode Item(string written, string body)
    {
        const string Shape = "A text item is its name and its label: text A1[\"React\"] — or, naming its region first, text A,B AB1[\"OpenAPI\"].";

        var pieces = new List<ContentNode> { Key(written[..Text.Length]) };
        var at = Space(written, Text.Length, pieces);
        if (at >= written.Length) return ContentNode.Shown(written, Shape);

        if (Listed(written, body, ref at, VennRoles.Region, out var reason) is not { } first)
            return ContentNode.Shown(written, reason);

        var next = at;
        while (next < written.Length && char.IsWhiteSpace(written[next])) next++;

        if (next > at && next < written.Length && written[next] != '[')
        {
            // A second name: the first was the region.
            pieces.Add(first);
            pieces.Add(ContentNode.Leaf(Kinds.Space, written[at..next], Roles.Trivia));
            at = next;

            if (!Identifier(written, ref at, pieces, strict: false, out reason)) return ContentNode.Shown(written, reason);
        }
        else if (first.Children.Count == 1)
        {
            pieces.Add(first.Children[0]);
        }
        else
        {
            return ContentNode.Shown(written, Shape);
        }

        at = Space(written, at, pieces);

        if (at < written.Length && written[at] == '[')
        {
            if (!Labelled(written, ref at, pieces, out reason)) return ContentNode.Shown(written, reason);
        }

        return at < written.Length ? ContentNode.Shown(written, Shape) : ContentNode.Branch(VennKinds.Text, pieces);
    }

    /// <summary>A <c>style</c>: what it styles, and <c>name:value</c> properties with a comma between each.</summary>
    private static ContentNode Styled(string written, string body)
    {
        const string Shape = "A style names what it styles and what it sets, a comma between each: style A fill:#ff6b6b, stroke:#333.";

        var pieces = new List<ContentNode> { Key(written[..Style.Length]) };
        var at = Space(written, Style.Length, pieces);
        if (at >= written.Length) return ContentNode.Shown(written, Shape);

        if (Listed(written, body, ref at, VennRoles.Target, out var reason) is not { } targets)
            return ContentNode.Shown(written, reason);

        pieces.Add(targets);

        var next = Space(written, at, pieces);
        if (next == at || next >= written.Length) return ContentNode.Shown(written, Shape);
        at = next;

        var properties = new List<ContentNode>();
        while (true)
        {
            var colon = at;
            while (colon < written.Length && written[colon] is not (':' or ',')) colon++;
            if (colon >= written.Length || written[colon] != ':') return ContentNode.Shown(written, Shape);

            var name = written[at..colon].TrimEnd();
            if (name.Length == 0) return ContentNode.Shown(written, Shape);

            var property = new List<ContentNode>
            {
                ContentNode.Leaf(MermaidKinds.Key, name, Roles.Name,
                                 Styles.Contains(name, StringComparer.OrdinalIgnoreCase)
                                     ? null
                                     : $"A style sets fill, color, stroke, stroke-width or fill-opacity, not '{name}'."),
            };
            if (colon > at + name.Length) property.Add(ContentNode.Leaf(Kinds.Space, written[(at + name.Length)..colon], Roles.Trivia));
            property.Add(ContentNode.Leaf(Kinds.Token, ":", Roles.Separator));

            at = Space(written, colon + 1, property);
            var end = ValueEnd(written, at);
            var value = written[at..end].TrimEnd();

            property.Add(ContentNode.Leaf(VennKinds.Setting, value, VennRoles.Value, Setting(name, value)));
            if (end > at + value.Length) property.Add(ContentNode.Leaf(Kinds.Space, written[(at + value.Length)..end], Roles.Trivia));

            properties.Add(ContentNode.Branch(VennKinds.Property, property));
            at = end;

            if (at >= written.Length) break;

            properties.Add(ContentNode.Leaf(Kinds.Token, ",", Roles.Separator));
            at = Space(written, at + 1, properties);
            if (at >= written.Length) return ContentNode.Shown(written, Shape);
        }

        pieces.Add(ContentNode.Branch(VennKinds.Properties, properties));
        return ContentNode.Branch(VennKinds.Style, pieces);
    }

    // ── Parts ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A name, in quotes or bare, as an <see cref="VennKinds.Id"/>. Only a set's own name is held to Mermaid's rule that a
    /// bare one starts with a letter or an underscore: an item may be called by a number.
    /// </summary>
    private static bool Identifier(string written, ref int at, List<ContentNode> pieces, bool strict, out string? reason)
    {
        reason = null;

        if (written[at] == '"')
        {
            var close = written.IndexOf('"', at + 1);
            if (close < 0)
            {
                reason = "This name is never closed with a quote.";
                return false;
            }

            pieces.Add(ContentNode.Branch(VennKinds.Id, Quoted(written[at..(close + 1)], VennRoles.Id).Children));
            at = close + 1;
            return true;
        }

        var start = at;
        while (at < written.Length && (char.IsLetterOrDigit(written[at]) || written[at] is '_' or '-' or '.' or '+')) at++;

        if (at == start)
        {
            reason = "A name is a word, or words in quotes: A, Set_1, \"Foo Bar\".";
            return false;
        }

        var name = written[start..at];
        var trouble = strict && !(char.IsLetter(name[0]) || name[0] == '_')
            ? "A set's name starts with a letter or an underscore, or is written in quotes."
            : null;

        pieces.Add(ContentNode.Branch(VennKinds.Id, [ContentNode.Leaf(VennKinds.Name, name, VennRoles.Id, trouble)]));
        return true;
    }

    /// <summary>
    /// Names with a comma between each, as <see cref="VennKinds.Sets"/>. A comma with nothing written after it yet is
    /// followed by a name still to write, standing after the space left for it.
    /// </summary>
    private static ContentNode? Listed(string written, string body, ref int at, string role, out string? reason)
    {
        var names = new List<ContentNode>();

        while (true)
        {
            if (!Identifier(written, ref at, names, strict: false, out reason)) return null;

            var next = at;
            while (next < written.Length && char.IsWhiteSpace(written[next])) next++;
            if (next >= written.Length || written[next] != ',') break;

            if (next > at) names.Add(ContentNode.Leaf(Kinds.Space, written[at..next], Roles.Trivia));
            names.Add(ContentNode.Leaf(Kinds.Token, ",", Roles.Separator));

            at = next + 1 < written.Length ? Space(written, next + 1, names) : Space(body, next + 1, names);
            if (at >= written.Length)
            {
                names.Add(ContentNode.Branch(VennKinds.Id, [ContentNode.Leaf(VennKinds.Name, string.Empty, VennRoles.Id)]));
                break;
            }
        }

        return ContentNode.Branch(VennKinds.Sets, names, role);
    }

    /// <summary>A label in its brackets: <c>["Alpha"]</c>, quotes and all, or <c>[Alpha]</c>, space round it the label's own.</summary>
    private static bool Labelled(string written, ref int at, List<ContentNode> pieces, out string? reason)
    {
        reason = null;
        var label = new List<ContentNode> { ContentNode.Leaf(Kinds.Token, "[", Roles.Open) };

        if (at + 1 < written.Length && written[at + 1] == '"')
        {
            var close = written.IndexOf("\"]", at + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                reason = "This label is never closed with \"].";
                return false;
            }

            label.Add(Quoted(written[(at + 1)..(close + 1)], VennRoles.Label));
            at = close + 2;
        }
        else
        {
            var close = written.IndexOf(']', at + 1);
            if (close < 0)
            {
                reason = "This label is never closed with ].";
                return false;
            }

            var inner = written[(at + 1)..close];
            if (inner.Contains('"'))
            {
                reason = "A label with a quote in it is written in quotes: [\"Alpha\"].";
                return false;
            }

            var text = inner.Trim();
            var lead = inner.Length - inner.TrimStart().Length;
            if (lead > 0) label.Add(ContentNode.Leaf(Kinds.Space, inner[..lead], Roles.Trivia));
            label.Add(ContentNode.Leaf(VennKinds.Name, text, VennRoles.Label,
                                       text.Length == 0 ? "A label in brackets has something in it: [Alpha], or [\"Alpha\"]." : null));
            if (inner.Length > lead + text.Length) label.Add(ContentNode.Leaf(Kinds.Space, inner[(lead + text.Length)..], Roles.Trivia));
            at = close + 1;
        }

        label.Add(ContentNode.Leaf(Kinds.Token, "]", Roles.Close));
        pieces.Add(ContentNode.Branch(VennKinds.Label, label));
        return true;
    }

    /// <summary>Text in quotes, the quotes machinery either side of what it says.</summary>
    private static ContentNode Quoted(string quoted, string role) =>
        ContentNode.Branch(VennKinds.Quoted,
        [
            ContentNode.Leaf(Kinds.Token, "\"", Roles.Open),
            ContentNode.Leaf(VennKinds.Name, quoted[1..^1], role),
            ContentNode.Leaf(Kinds.Token, "\"", Roles.Close),
        ]);

    /// <summary>A size, in the place it is written — an empty one included, so there is somewhere to write it.</summary>
    private static ContentNode Weight(string number) =>
        ContentNode.Branch(VennKinds.Weight,
                           [ContentNode.Leaf(VennKinds.Size, number, VennRoles.Size, number.Length == 0 ? null : Sized(number))]);

    /// <summary>What is wrong with a size, where anything is.</summary>
    private static string? Sized(string text) =>
        !double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var size)
            ? $"'{text}' is not a number."
            : size > 0 ? null : "A size is a number greater than nought.";

    /// <summary>What is wrong with what a property is set to, where anything is. A colour is the builder's to make sense of.</summary>
    private static string? Setting(string name, string value)
    {
        if (value.Length == 0) return $"{name} is set to nothing: {name}:{(name.StartsWith("fill-", StringComparison.OrdinalIgnoreCase) ? "0.5" : "#ff6b6b")}.";

        if (name.Equals("fill-opacity", StringComparison.OrdinalIgnoreCase))
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity) && opacity is >= 0 and <= 1
                ? null
                : "fill-opacity is a number from 0 to 1.";

        if (name.Equals("stroke-width", StringComparison.OrdinalIgnoreCase))
            return Pixels(value) is >= 0 ? null : "stroke-width is a number of pixels.";

        return null;
    }

    /// <summary>A number of pixels, written with <c>px</c> after it or without, or null where it is not one.</summary>
    public static double? Pixels(string? value)
    {
        if (value is null) return null;

        var digits = value.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? value[..^2].TrimEnd() : value;
        return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    // ── Characters ──────────────────────────────────────────────────────────

    /// <summary>Which of the diagram's words a line starts with, ignoring case — or null where it starts with none of them.</summary>
    private static string? Keyword(string written)
    {
        var end = 0;
        while (end < written.Length && char.IsAsciiLetter(written[end])) end++;
        if (end < written.Length && (char.IsAsciiLetterOrDigit(written[end]) || written[end] is '_' or '-')) return null;

        return written[..end].ToLowerInvariant() switch
        {
            Title => Title,
            Set => Set,
            Union => Union,
            Text => Text,
            Style => Style,
            _ => null,
        };
    }

    private static ContentNode Key(string word) => ContentNode.Leaf(MermaidKinds.Key, word, Roles.Name);

    /// <summary>Where a <c>%%</c> comment starts on a line, outside anything in quotes — or -1.</summary>
    private static int Comment(string text)
    {
        var quoted = false;
        for (var at = 0; at + 1 < text.Length; at++)
        {
            if (text[at] == '"') quoted = !quoted;
            else if (!quoted && text[at] == '%' && text[at + 1] == '%') return at;
        }

        return -1;
    }

    /// <summary>A line read, with the comment that closes it — the space before it, and the comment itself.</summary>
    private static ContentNode Commented(ContentNode read, string text, int comment)
    {
        var pieces = new List<ContentNode>();
        if (comment > read.Width) pieces.Add(ContentNode.Leaf(Kinds.Space, text[read.Width..comment], Roles.Trivia));
        pieces.Add(ContentNode.Leaf(Kinds.Comment, text[comment..].TrimEnd(), Roles.Trivia));

        return read.IsLeaf
            ? ContentNode.Branch(Kinds.Sequence, [read, .. pieces])
            : read.With([.. read.Children, .. pieces]);
    }

    /// <summary>Where a property's value ends: at the next comma outside quotes and brackets, or the end of the line.</summary>
    private static int ValueEnd(string written, int at)
    {
        var depth = 0;
        var quoted = false;

        for (; at < written.Length; at++)
        {
            switch (written[at])
            {
                case '"': quoted = !quoted; break;
                case '(' when !quoted: depth++; break;
                case ')' when !quoted: depth = Math.Max(0, depth - 1); break;
                case ',' when !quoted && depth == 0: return at;
            }
        }

        return at;
    }

    /// <summary>Takes the space at <paramref name="at"/> as trivia, and says where what follows it starts.</summary>
    private static int Space(string text, int at, List<ContentNode> pieces)
    {
        var past = at;
        while (past < text.Length && char.IsWhiteSpace(text[past])) past++;

        if (past > at) pieces.Add(ContentNode.Leaf(Kinds.Space, text[at..past], Roles.Trivia));
        return past;
    }
}
