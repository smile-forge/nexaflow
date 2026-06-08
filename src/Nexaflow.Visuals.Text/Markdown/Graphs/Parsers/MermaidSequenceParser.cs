using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Text.RegularExpressions;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses Mermaid <c>sequenceDiagram</c> diagrams into a <see cref="SequenceDiagram"/>.
///
/// Supported:
///   • <c>participant</c> / <c>actor</c> declarations, with <c>as &lt;label&gt;</c> and
///     <c>@{ "type": …, "alias": … }</c> metadata (type → glyph; an explicit <c>as</c> wins over
///     an inline <c>alias</c>).
///   • <c>create</c> / <c>destroy</c> participants.
///   • Messages <c>From&lt;arrow&gt;To: text</c> for arrows
///     <c>-&gt; --&gt; -&gt;&gt; --&gt;&gt; -) --) -x --x</c>, with <c>+</c>/<c>-</c> activation shorthand and
///     <c>()</c> central-connection markers stripped.
///   • <c>activate</c> / <c>deactivate</c>, <c>autonumber</c>, <c>Note left/right of</c> and
///     <c>Note over A,B</c>, and the fragment blocks <c>alt/else, opt, loop, par/and,
///     critical/option, break, rect</c> plus <c>box … end</c> grouping.
///   • <c>&lt;br&gt;</c> in labels → line breaks; <c>%%</c> comments.
/// </summary>
public sealed class MermaidSequenceParser
{
    public bool CanParse(string language) =>
        language.Equals("sequenceDiagram", StringComparison.OrdinalIgnoreCase);

    public SequenceDiagram Parse(string source)
    {
        var diagram = new SequenceDiagram();
        try { ParseInto(source, diagram); }
        catch { /* never throw; return partial diagram */ }
        return diagram;
    }

    // Arrow tokens, longest-first so prefixes (e.g. "->") don't shadow "->>" / "<<-->>".
    private static readonly string[] Arrows =
        ["<<-->>", "<<->>", "-->>", "--x", "--)", "-->", "->>", "-x", "-)", "->"];

    private static readonly Regex RxNote = new(
        @"^note\s+(?<place>right of|left of|over)\s+(?<who>.+?)\s*:\s*(?<text>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxBr = new(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxType  = new("\"?type\"?\\s*:\\s*\"?(?<v>[A-Za-z]+)\"?", RegexOptions.Compiled);
    private static readonly Regex RxAlias = new("\"?alias\"?\\s*:\\s*\"(?<v>[^\"]+)\"", RegexOptions.Compiled);

    private static readonly HashSet<string> ColorWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "transparent","aqua","purple","red","green","blue","yellow","orange","pink",
        "gray","grey","lightblue","lightgreen","lightyellow","white","black","cyan","magenta",
    };

    private static void ParseInto(string source, SequenceDiagram diagram)
    {
        bool autonumber = false;
        int  counter    = 0;
        int  fragDepth  = 0;
        SequenceBox? box = null;

        foreach (var rawLine in source.Split('\n'))
        {
            var line = StripComment(rawLine).Trim().TrimEnd(';');
            if (line.Length == 0) continue;

            var first = line.Split(' ', '\t')[0].ToLowerInvariant();
            string rest = WordRest(line);

            switch (first)
            {
                case "sequencediagram": continue;
                case "title": diagram.Title = line[5..].TrimStart(':', ' ').Trim(); continue;

                case "participant":
                case "actor":
                    ParseParticipant(rest, first == "actor" ? ParticipantKind.Actor : ParticipantKind.Participant,
                                     diagram, box);
                    continue;

                case "create": ParseCreate(rest, diagram); continue;
                case "destroy":
                {
                    var id = CleanRef(rest);
                    if (id.Length > 0) { diagram.GetOrAdd(id).Destroyed = true; diagram.Items.Add(new SequenceDestroy { ParticipantId = id }); }
                    continue;
                }

                case "activate":
                case "deactivate":
                {
                    var id = CleanRef(rest);
                    if (id.Length > 0) { diagram.GetOrAdd(id); diagram.Items.Add(new SequenceActivation { ParticipantId = id, Activate = first == "activate" }); }
                    continue;
                }

                case "autonumber": autonumber = true; continue;

                case "note" when RxNote.IsMatch(line): ParseNote(line, diagram); continue;

                case "box": box = OpenBox(rest, diagram); continue;

                case "alt":      OpenFragment(FragmentKind.Alt,      rest, diagram, ref fragDepth); continue;
                case "opt":      OpenFragment(FragmentKind.Opt,      rest, diagram, ref fragDepth); continue;
                case "loop":     OpenFragment(FragmentKind.Loop,     rest, diagram, ref fragDepth); continue;
                case "par":      OpenFragment(FragmentKind.Par,      rest, diagram, ref fragDepth); continue;
                case "critical": OpenFragment(FragmentKind.Critical, rest, diagram, ref fragDepth); continue;
                case "break":    OpenFragment(FragmentKind.Break,    rest, diagram, ref fragDepth); continue;
                case "rect":     OpenRect(rest, diagram, ref fragDepth); continue;

                case "else":
                case "and":
                case "option":
                    if (fragDepth > 0)
                        diagram.Items.Add(new SequenceFragment { Boundary = FragmentBoundary.Section, Label = Br(rest) });
                    continue;

                case "end":
                    if (fragDepth > 0) { diagram.Items.Add(new SequenceFragment { Boundary = FragmentBoundary.End }); fragDepth--; }
                    else if (box is not null) box = null;
                    continue;

                case "link":
                case "links": continue;
            }

            // Otherwise: a message line, or an unknown line we ignore.
            if (TryParseMessage(line, diagram, autonumber, ref counter)) continue;
        }
    }

    // ── Participants ─────────────────────────────────────────────────────────

    private static void ParseParticipant(string rest, ParticipantKind kind, SequenceDiagram diagram, SequenceBox? box)
    {
        var (id, label, metaKind) = ParseDecl(rest);
        if (id.Length == 0) return;

        var p = diagram.GetOrAdd(id, label);
        p.Kind = metaKind ?? kind;
        box?.ParticipantIds.Add(id);
    }

    private static void ParseCreate(string rest, SequenceDiagram diagram)
    {
        var kind = ParticipantKind.Participant;
        var w0 = rest.Split(' ', '\t')[0].ToLowerInvariant();
        if (w0 is "participant" or "actor")
        {
            kind = w0 == "actor" ? ParticipantKind.Actor : ParticipantKind.Participant;
            rest = WordRest(rest);
        }
        var (id, label, metaKind) = ParseDecl(rest);
        if (id.Length == 0) return;

        var p = diagram.GetOrAdd(id, label);
        p.Kind    = metaKind ?? kind;
        p.Created = true;
    }

    /// <summary>Parses a declaration tail: <c>Id[@{ … }] [as Label]</c> → (id, label, kind?).</summary>
    private static (string id, string label, ParticipantKind? kind) ParseDecl(string rest)
    {
        ParticipantKind? kind = null;
        string? inlineAlias = null;

        // Pull out @{ … } metadata first.
        int at = rest.IndexOf("@{", StringComparison.Ordinal);
        if (at >= 0)
        {
            int close = rest.IndexOf('}', at + 2);
            string meta = close > at ? rest[(at + 2)..close] : rest[(at + 2)..];
            var tm = RxType.Match(meta);
            if (tm.Success) kind = MapKind(tm.Groups["v"].Value);
            var am = RxAlias.Match(meta);
            if (am.Success) inlineAlias = am.Groups["v"].Value.Trim();
            rest = (rest[..at] + (close > at ? rest[(close + 1)..] : "")).Trim();
        }

        // 'as Label' (an explicit external name wins over an inline alias).
        string id;
        string? asLabel = null;
        int asIdx = IndexOfWord(rest, "as");
        if (asIdx >= 0) { id = rest[..asIdx].Trim(); asLabel = rest[(asIdx + 2)..].Trim(); }
        else            { id = rest.Trim(); }

        id = CleanRef(id);
        string label = Br(asLabel ?? inlineAlias ?? id);
        return (id, label, kind);
    }

    private static ParticipantKind MapKind(string t) => t.ToLowerInvariant() switch
    {
        "actor"       => ParticipantKind.Actor,
        "boundary"    => ParticipantKind.Boundary,
        "control"     => ParticipantKind.Control,
        "entity"      => ParticipantKind.Entity,
        "database"    => ParticipantKind.Database,
        "collections" => ParticipantKind.Collections,
        "queue"       => ParticipantKind.Queue,
        _             => ParticipantKind.Participant,
    };

    // ── Notes / boxes / fragments ────────────────────────────────────────────

    private static void ParseNote(string line, SequenceDiagram diagram)
    {
        var m = RxNote.Match(line);
        if (!m.Success) return;

        var note = new SequenceNote
        {
            Placement = m.Groups["place"].Value.ToLowerInvariant() switch
            {
                "left of"  => NotePlacement.LeftOf,
                "right of" => NotePlacement.RightOf,
                _          => NotePlacement.Over,
            },
            Text = Br(m.Groups["text"].Value),
        };
        foreach (var who in m.Groups["who"].Value.Split(','))
        {
            var id = CleanRef(who);
            if (id.Length > 0) { diagram.GetOrAdd(id); note.ParticipantIds.Add(id); }
        }
        if (note.ParticipantIds.Count > 0) diagram.Items.Add(note);
    }

    private static SequenceBox OpenBox(string rest, SequenceDiagram diagram)
    {
        var (color, label) = ParseColorPrefix(rest);
        var box = new SequenceBox { Color = color, Label = Br(label) };
        diagram.Boxes.Add(box);
        return box;
    }

    private static void OpenFragment(FragmentKind kind, string rest, SequenceDiagram diagram, ref int depth)
    {
        diagram.Items.Add(new SequenceFragment { Boundary = FragmentBoundary.Begin, Kind = kind, Label = Br(rest) });
        depth++;
    }

    private static void OpenRect(string rest, SequenceDiagram diagram, ref int depth)
    {
        var (color, _) = ParseColorPrefix(rest);
        diagram.Items.Add(new SequenceFragment { Boundary = FragmentBoundary.Begin, Kind = FragmentKind.Rect, Color = color ?? rest.Trim() });
        depth++;
    }

    /// <summary>Splits a leading colour token (name, <c>rgb(…)</c> or <c>#hex</c>) off the rest.</summary>
    private static (string? color, string label) ParseColorPrefix(string rest)
    {
        rest = rest.Trim();
        if (rest.StartsWith("rgb", StringComparison.OrdinalIgnoreCase) && rest.Contains('('))
        {
            int close = rest.IndexOf(')');
            if (close > 0) return (rest[..(close + 1)], rest[(close + 1)..].Trim());
        }
        var first = rest.Split(' ', '\t')[0];
        if (first.StartsWith('#') || ColorWords.Contains(first))
            return (first, rest[first.Length..].Trim());
        return (null, rest);
    }

    // ── Messages ─────────────────────────────────────────────────────────────

    private static bool TryParseMessage(string line, SequenceDiagram diagram, bool autonumber, ref int counter)
    {
        var (idx, arrow) = FindArrow(line);
        if (idx < 0 || arrow is null) return false;

        string fromRaw = line[..idx];
        string rest    = line[(idx + arrow.Length)..];

        int colon = rest.IndexOf(':');
        string toRaw = colon >= 0 ? rest[..colon] : rest;
        string text  = colon >= 0 ? rest[(colon + 1)..].Trim() : string.Empty;

        bool dotSource = fromRaw.Contains("()", StringComparison.Ordinal);
        bool dotTarget = toRaw.Contains("()", StringComparison.Ordinal);

        string from = CleanRef(fromRaw);
        string to   = CleanEndpoint(toRaw, out bool activate, out bool deactivate);
        if (from.Length == 0 || to.Length == 0) return false;

        diagram.GetOrAdd(from);
        diagram.GetOrAdd(to);

        diagram.Items.Add(new SequenceMessage
        {
            FromId           = from,
            ToId             = to,
            Text             = Br(text),
            Line             = arrow.Contains("--") ? SequenceLineStyle.Dashed : SequenceLineStyle.Solid,
            Head             = HeadOf(arrow),
            Bidirectional    = arrow.StartsWith("<<", StringComparison.Ordinal),
            DotSource        = dotSource,
            DotTarget        = dotTarget,
            ActivateTarget   = activate,
            DeactivateSource = deactivate,
            Number           = autonumber ? ++counter : null,
        });
        return true;
    }

    private static SequenceArrowHead HeadOf(string arrow) =>
        arrow.EndsWith(">>", StringComparison.Ordinal) ? SequenceArrowHead.Filled
      : arrow.EndsWith(")",  StringComparison.Ordinal) ? SequenceArrowHead.Open
      : arrow.EndsWith("x",  StringComparison.Ordinal) ? SequenceArrowHead.Cross
      : SequenceArrowHead.None;

    private static (int idx, string? arrow) FindArrow(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] != '-' && line[i] != '<') continue;   // tokens start with '-' or '<<'
            foreach (var a in Arrows)
                if (i + a.Length <= line.Length && line.AsSpan(i, a.Length).SequenceEqual(a))
                    return (i, a);
        }
        return (-1, null);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string StripComment(string line)
    {
        int idx = line.IndexOf("%%", StringComparison.Ordinal);
        return idx >= 0 ? line[..idx] : line;
    }

    /// <summary>Everything after the first whitespace-delimited word.</summary>
    private static string WordRest(string line)
    {
        int sp = line.IndexOfAny([' ', '\t']);
        return sp < 0 ? string.Empty : line[(sp + 1)..].Trim();
    }

    /// <summary>Strips central-connection <c>()</c> markers and surrounding whitespace from a participant ref.</summary>
    private static string CleanRef(string s) => s.Replace("()", "").Trim();

    /// <summary>Like <see cref="CleanRef"/> but also peels a leading <c>+</c>/<c>-</c> activation marker.</summary>
    private static string CleanEndpoint(string s, out bool activate, out bool deactivate)
    {
        s = s.Trim();
        activate = deactivate = false;
        if (s.StartsWith('+')) { activate = true; s = s[1..]; }
        else if (s.StartsWith('-')) { deactivate = true; s = s[1..]; }
        return CleanRef(s);
    }

    private static string Br(string raw) => RxBr.Replace(raw.Trim(), "\n");

    /// <summary>Index of the standalone word <paramref name="word"/> in <paramref name="s"/>, or -1.</summary>
    private static int IndexOfWord(string s, string word)
    {
        int from = 0;
        while (true)
        {
            int i = s.IndexOf(word, from, StringComparison.Ordinal);
            if (i < 0) return -1;
            bool preOk  = i == 0 || char.IsWhiteSpace(s[i - 1]);
            bool postOk = i + word.Length >= s.Length || char.IsWhiteSpace(s[i + word.Length]);
            if (preOk && postOk) return i;
            from = i + 1;
        }
    }
}
