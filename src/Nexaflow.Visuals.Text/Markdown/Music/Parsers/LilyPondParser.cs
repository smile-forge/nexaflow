using System;
using System.Collections.Generic;
using System.Text;
using Nexaflow.Visuals.Text.Markdown.Music.Model;

namespace Nexaflow.Visuals.Text.Markdown.Music.Parsers;

/// <summary>
/// Parses a practical subset of LilyPond into the shared <see cref="Score"/> IR. v1 targets a single
/// staff: <c>\relative</c>/absolute pitch entry, notes with accidentals/octave marks/durations,
/// <c>\clef</c>/<c>\key</c>/<c>\time</c>, bar lines and rests, plus variable definitions with
/// <c>\name</c> substitution (so <c>\global</c> settings flow into a voice). Everything it can't model —
/// Scheme <c>#(…)</c>, <c>\score</c>/<c>PianoStaff</c>, <c>\figuremode</c>, simultaneous <c>&lt;&lt; &gt;&gt;</c>,
/// tuplets, <c>\markup</c> beyond a title — is skipped and recorded in <see cref="Score.Warnings"/>.
/// When several voices are defined it renders the one with the most notes (e.g. the cantus firmus).
/// </summary>
public sealed class LilyPondParser
{
    // Default reference for an argument-less \relative — chosen so bass-clef content lands in the staff.
    private static readonly Pitch DefaultRelativeReference = new(0, 0, 3); // c (C3)

    public Score Parse(string source)
    {
        var score = new Score();
        try { ParseInternal(source, score); }
        catch { /* tolerant: return whatever parsed */ }
        return score;
    }

    private void ParseInternal(string source, Score score)
    {
        var toks = Tokenize(StripComments(source));

        var defs = new Dictionary<string, List<Tok>>(StringComparer.Ordinal);
        var topLevel = new List<List<Tok>>();
        var warnings = new HashSet<string>();

        // Split top level into definitions (name = expr), \markup titles, and bare music expressions.
        for (int i = 0; i < toks.Count;)
        {
            var t = toks[i];
            if (t.Kind == TokKind.Word && i + 1 < toks.Count && toks[i + 1].Kind == TokKind.Eq)
            {
                string name = t.Text;
                i += 2;
                defs[name] = ReadExpr(toks, ref i);
                continue;
            }
            if (t.Kind == TokKind.Command && t.Text == "markup")
            {
                i++;
                var block = ReadExpr(toks, ref i);
                string? title = FirstString(block);
                if (title is not null && score.Title is null) score.Title = title;
                continue;
            }
            if (t.Kind == TokKind.Command && (t.Text == "score" || t.Text == "book" || t.Text == "layout" || t.Text == "midi"))
            {
                i++;
                ReadExpr(toks, ref i);   // skip the whole block
                warnings.Add($"\\{t.Text} block not rendered");
                continue;
            }
            if (t.Kind is TokKind.LBrace or TokKind.SimStart || (t.Kind == TokKind.Command && IsMusicWrapper(t.Text)))
            {
                topLevel.Add(ReadExpr(toks, ref i));
                continue;
            }
            i++;
        }

        // Evaluate every candidate expression to a staff; keep the one with the most notes.
        Staff? best = null; int bestNotes = -1;
        foreach (var expr in EnumerateCandidates(defs, topLevel))
        {
            var staff = new Staff();
            var local = new HashSet<string>();
            int notes = EvaluateInto(expr, defs, staff, warnings, local);
            if (notes > bestNotes) { bestNotes = notes; best = staff; }
        }

        if (best is not null && best.Measures.Count > 0)
            score.Staves.Add(best);

        if (defs.Count > 1 || topLevel.Count > 0)
        {
            if (score.Staves.Count < CountVoices(defs))
                warnings.Add("only the primary voice is rendered");
        }
        foreach (var w in warnings) score.Warnings.Add(w);
    }

    private static int CountVoices(Dictionary<string, List<Tok>> defs)
    {
        int n = 0;
        foreach (var kv in defs)
            if (!ContainsCommand(kv.Value, "figuremode")) n++;
        return n;
    }

    private static IEnumerable<List<Tok>> EnumerateCandidates(
        Dictionary<string, List<Tok>> defs, List<List<Tok>> topLevel)
    {
        foreach (var e in topLevel) yield return e;
        foreach (var kv in defs) yield return kv.Value;
    }

    // ── Comment / Scheme stripping ──────────────────────────────────────────

    private static string StripComments(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '%' && i + 1 < s.Length && s[i + 1] == '{')       // block comment %{ … %}
            {
                int end = s.IndexOf("%}", i + 2, StringComparison.Ordinal);
                i = end < 0 ? s.Length : end + 1;
                continue;
            }
            if (c == '%')                                              // line comment
            {
                int nl = s.IndexOf('\n', i);
                if (nl < 0) break;
                i = nl;
                sb.Append('\n');
                continue;
            }
            if (c == '#' && i + 1 < s.Length && s[i + 1] == '(')       // Scheme #( … ) balanced
            {
                int depth = 0, j = i + 1;
                for (; j < s.Length; j++)
                {
                    if (s[j] == '(') depth++;
                    else if (s[j] == ')') { depth--; if (depth == 0) { j++; break; } }
                }
                i = j - 1;
                continue;
            }
            if (c == '#')                                             // #f #t #24 #'sym — drop the token
            {
                int j = i + 1;
                while (j < s.Length && !char.IsWhiteSpace(s[j]) && s[j] != '{' && s[j] != '}') j++;
                i = j - 1;
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    // ── Tokenizer ───────────────────────────────────────────────────────────

    private enum TokKind { Command, Str, Word, LBrace, RBrace, SimStart, SimEnd, ChordStart, ChordEnd, Bar, Eq }
    private readonly record struct Tok(TokKind Kind, string Text);

    private static List<Tok> Tokenize(string s)
    {
        var toks = new List<Tok>();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            switch (c)
            {
                case '{': toks.Add(new(TokKind.LBrace, "{")); i++; continue;
                case '}': toks.Add(new(TokKind.RBrace, "}")); i++; continue;
                case '=': toks.Add(new(TokKind.Eq, "=")); i++; continue;
                case '|': toks.Add(new(TokKind.Bar, "|")); i++; continue;
                case '<':
                    if (i + 1 < s.Length && s[i + 1] == '<') { toks.Add(new(TokKind.SimStart, "<<")); i += 2; }
                    else { toks.Add(new(TokKind.ChordStart, "<")); i++; }
                    continue;
                case '>':
                    if (i + 1 < s.Length && s[i + 1] == '>') { toks.Add(new(TokKind.SimEnd, ">>")); i += 2; }
                    else { toks.Add(new(TokKind.ChordEnd, ">")); i++; }
                    continue;
                case '"':
                {
                    int j = i + 1; var sb = new StringBuilder();
                    while (j < s.Length && s[j] != '"') { sb.Append(s[j]); j++; }
                    toks.Add(new(TokKind.Str, sb.ToString()));
                    i = j + 1; continue;
                }
                case '\\':
                {
                    int j = i + 1;
                    while (j < s.Length && (char.IsLetter(s[j]))) j++;
                    if (j == i + 1) { i++; continue; }               // lone backslash (e.g. \\ voice sep)
                    toks.Add(new(TokKind.Command, s[(i + 1)..j]));
                    i = j; continue;
                }
                default:
                {
                    int j = i;
                    while (j < s.Length && !char.IsWhiteSpace(s[j]) &&
                           s[j] is not ('{' or '}' or '<' or '>' or '=' or '|' or '"' or '\\' or '%' or '#'))
                        j++;
                    if (j == i) { i++; continue; }
                    toks.Add(new(TokKind.Word, s[i..j]));
                    i = j; continue;
                }
            }
        }
        return toks;
    }

    // ── Expression capture ──────────────────────────────────────────────────

    /// <summary>Reads one music expression starting at <paramref name="i"/>: any leading wrapper commands
    /// plus the first balanced <c>{…}</c>/<c>&lt;&lt;…&gt;&gt;</c> group. Advances <paramref name="i"/> past it.</summary>
    private static List<Tok> ReadExpr(List<Tok> toks, ref int i)
    {
        int start = i;
        int depth = 0; bool opened = false;
        while (i < toks.Count)
        {
            var k = toks[i].Kind;
            if (k is TokKind.LBrace or TokKind.SimStart) { depth++; opened = true; }
            else if (k is TokKind.RBrace or TokKind.SimEnd) depth--;
            i++;
            if (opened && depth <= 0) break;
            // A bare reference (\name with no block) or a stray token: stop before the next definition.
            if (!opened && i < toks.Count && toks[i].Kind == TokKind.Eq) break;
            if (!opened && k == TokKind.Command && i < toks.Count &&
                toks[i].Kind is not (TokKind.LBrace or TokKind.SimStart or TokKind.Word or TokKind.Str))
                break;
        }
        return toks.GetRange(start, i - start);
    }

    private static bool IsMusicWrapper(string cmd) =>
        cmd is "relative" or "fixed" or "absolute" or "figuremode" or "chordmode" or "drummode"
            or "new" or "context" or "transpose" or "times" or "repeat" or "sequential" or "simultaneous";

    private static string? FirstString(List<Tok> toks)
    {
        foreach (var t in toks) if (t.Kind == TokKind.Str) return t.Text;
        return null;
    }

    private static bool ContainsCommand(List<Tok> toks, string cmd)
    {
        foreach (var t in toks) if (t.Kind == TokKind.Command && t.Text == cmd) return true;
        return false;
    }

    // ── Evaluation ──────────────────────────────────────────────────────────

    /// <summary>Evaluates <paramref name="expr"/> into <paramref name="staff"/> and returns the note count.
    /// Resolves <c>\name</c> references (e.g. <c>\global</c>) via <paramref name="defs"/>, guarding against
    /// cycles with <paramref name="active"/>.</summary>
    private int EvaluateInto(List<Tok> expr, Dictionary<string, List<Tok>> defs, Staff staff,
        HashSet<string> warnings, HashSet<string> active)
    {
        var items = new List<object>();
        var st = new EvalState { Reference = DefaultRelativeReference, LastDuration = Duration.Quarter };
        Walk(expr, 0, expr.Count, defs, staff, items, warnings, active, st);

        bool explicitBars = false;
        foreach (var it in items) if (it is BarToken or BreakMark) { explicitBars = true; break; }

        BuildMeasures(items, staff, staff.Time.QuarterLengthPerMeasure, explicitBars);

        int notes = 0;
        foreach (var m in staff.Measures) foreach (var e in m.Events) if (e is Note) notes++;
        return notes;
    }

    private sealed class EvalState
    {
        public bool Relative;
        public Pitch Reference;
        public Duration LastDuration = Duration.Quarter;
    }

    private sealed record BarToken(BarlineKind Kind);
    private sealed class BreakMark { public static readonly BreakMark Instance = new(); }

    private void Walk(List<Tok> toks, int from, int to, Dictionary<string, List<Tok>> defs, Staff staff,
        List<object> items, HashSet<string> warnings, HashSet<string> active, EvalState st)
    {
        int i = from;
        while (i < to)
        {
            var t = toks[i];
            switch (t.Kind)
            {
                case TokKind.Command:
                    HandleCommand(toks, ref i, to, defs, staff, items, warnings, active, st);
                    continue;
                case TokKind.LBrace:
                case TokKind.RBrace:
                    i++; continue;                                   // grouping only
                case TokKind.SimStart:
                {
                    int close = MatchClose(toks, i, to, TokKind.SimStart, TokKind.SimEnd);
                    warnings.Add("simultaneous << >> not rendered");
                    i = close + 1; continue;
                }
                case TokKind.ChordStart:
                {
                    int close = MatchClose(toks, i, to, TokKind.ChordStart, TokKind.ChordEnd);
                    var chord = ReadChord(toks, i + 1, close, st);
                    i = close + 1;
                    var dur = ReadTrailingDuration(toks, ref i, st);
                    if (chord is not null) { chord.Duration = dur; items.Add(chord); }
                    continue;
                }
                case TokKind.Bar:
                    items.Add(new BarToken(BarlineKind.Single)); i++; continue;
                case TokKind.Word:
                    if (TryReadNoteOrRest(toks, ref i, st, out var ev)) items.Add(ev!);
                    else i++;
                    continue;
                default:
                    i++; continue;
            }
        }
    }

    private void HandleCommand(List<Tok> toks, ref int i, int to, Dictionary<string, List<Tok>> defs,
        Staff staff, List<object> items, HashSet<string> warnings, HashSet<string> active, EvalState st)
    {
        string cmd = toks[i].Text;
        i++;
        switch (cmd)
        {
            case "relative":
                st.Relative = true;
                if (i < to && toks[i].Kind == TokKind.Word && IsPitchWord(toks[i].Text))
                    st.Reference = ParseAbsolutePitch(toks[i++].Text);
                return;
            case "fixed":
                st.Relative = false;
                if (i < to && toks[i].Kind == TokKind.Word) i++;     // reference octave word
                return;
            case "absolute":
                st.Relative = false; return;
            case "clef":
                if (i < to && (toks[i].Kind is TokKind.Str or TokKind.Word)) staff.Clef = ParseClef(toks[i++].Text);
                return;
            case "key":
            {
                if (i < to && toks[i].Kind == TokKind.Word)
                {
                    string tonic = toks[i++].Text;
                    string mode = i < to && toks[i].Kind == TokKind.Command ? toks[i++].Text : "major";
                    staff.Key = ParseKey(tonic, mode);
                }
                return;
            }
            case "time":
                if (i < to && toks[i].Kind == TokKind.Word) staff.Time = ParseTime(toks[i++].Text);
                return;
            case "bar":
                if (i < to && toks[i].Kind == TokKind.Str) items.Add(new BarToken(ParseBar(toks[i++].Text)));
                return;
            case "break":
                items.Add(BreakMark.Instance); return;
            case "figuremode":
            case "lyricmode":
            case "chordmode":
            case "drummode":
                warnings.Add($"\\{cmd} not rendered");
                if (i < to && toks[i].Kind == TokKind.LBrace)
                    i = MatchClose(toks, i, to, TokKind.LBrace, TokKind.RBrace) + 1;
                return;
            case "numericTimeSignature":
            case "override":
            case "revert":
            case "set":
            case "unset":
            case "once":
            case "tweak":
            case "with":
                SkipCommandArgs(toks, ref i, to, cmd);
                return;
            case "new":
            case "context":
                if (i < to && toks[i].Kind == TokKind.Word) i++;      // context type
                if (i < to && toks[i].Kind == TokKind.Eq) i++;
                if (i < to && toks[i].Kind == TokKind.Str) i++;
                if (i < to && toks[i].Kind == TokKind.Command && toks[i].Text == "with")
                { i++; if (i < to && toks[i].Kind == TokKind.LBrace) i = MatchClose(toks, i, to, TokKind.LBrace, TokKind.RBrace) + 1; }
                return;                                              // the following music is evaluated normally
            case "repeat":
                warnings.Add("\\repeat not expanded");
                if (i < to && toks[i].Kind == TokKind.Word) i++;      // volta/unfold
                if (i < to && toks[i].Kind == TokKind.Word) i++;      // count
                return;
            case "times":
            case "tuplet":
                warnings.Add("tuplets rendered without ratio");
                if (i < to && toks[i].Kind == TokKind.Word) i++;      // ratio
                return;
            default:
                // A reference to a defined variable (e.g. \global) — substitute inline.
                if (defs.TryGetValue(cmd, out var body) && active.Add(cmd))
                {
                    Walk(body, 0, body.Count, defs, staff, items, warnings, active, st);
                    active.Remove(cmd);
                }
                return;
        }
    }

    private static void SkipCommandArgs(List<Tok> toks, ref int i, int to, string cmd)
    {
        // \override Ctx.Prop = value  /  \set Prop = value  /  \with { … }
        if (i < to && toks[i].Kind == TokKind.Command && toks[i].Text == "with") { i++; }
        if (i < to && toks[i].Kind == TokKind.LBrace) { i = MatchClose(toks, i, to, TokKind.LBrace, TokKind.RBrace) + 1; return; }
        if (i < to && toks[i].Kind == TokKind.Word) i++;
        if (i < to && toks[i].Kind == TokKind.Eq)
        {
            i++;
            if (i < to && toks[i].Kind == TokKind.Command) i++;
            else if (i < to && toks[i].Kind is TokKind.Word or TokKind.Str) i++;
        }
    }

    private static int MatchClose(List<Tok> toks, int open, int to, TokKind openKind, TokKind closeKind)
    {
        int depth = 0;
        for (int j = open; j < to; j++)
        {
            if (toks[j].Kind == openKind) depth++;
            else if (toks[j].Kind == closeKind) { depth--; if (depth == 0) return j; }
        }
        return to - 1;
    }

    // ── Notes / rests / chords ──────────────────────────────────────────────

    private bool TryReadNoteOrRest(List<Tok> toks, ref int i, EvalState st, out MusicalEvent? ev)
    {
        ev = null;
        string w = toks[i].Text;
        if (w.Length == 0) { i++; return false; }
        char c0 = w[0];

        if (c0 is 'r' or 's' or 'R')                                 // rest / spacer / multi-measure rest
        {
            var d = ParseDurationSuffix(w, 1, st, out double scale);
            i++;
            ev = new Rest { Duration = d };
            st.LastDuration = d;
            return true;
        }
        if (c0 >= 'a' && c0 <= 'g')
        {
            var note = ParseNoteWord(w, st);
            i++;
            ev = note;
            return true;
        }
        i++;
        return false;
    }

    private Note ParseNoteWord(string w, EvalState st)
    {
        int p = 0;
        // Letter → diatonic step index (c=0,d=1,e=2,f=3,g=4,a=5,b=6).
        int step = "cdefgab".IndexOf(w[p]); p++;
        if (step < 0) step = 0;

        int alter = 0;
        while (p + 1 < w.Length && (w.Substring(p, 2) == "is" || w.Substring(p, 2) == "es"))
        {
            alter += w.Substring(p, 2) == "is" ? 1 : -1;
            p += 2;
        }

        int octMarks = 0;
        while (p < w.Length && (w[p] == '\'' || w[p] == ','))
        {
            octMarks += w[p] == '\'' ? 1 : -1;
            p++;
        }
        if (p < w.Length && (w[p] == '!' || w[p] == '?')) p++;      // forced/cautionary accidental marker

        var dur = ParseDurationSuffix(w, p, st, out _);

        Pitch pitch = st.Relative
            ? ResolveRelative(step, alter, octMarks, st)
            : new Pitch(step, alter, 3 + octMarks);

        st.Reference = pitch;
        var acc = alter switch
        {
            2 => AccidentalKind.DoubleSharp, 1 => AccidentalKind.Sharp,
            -1 => AccidentalKind.Flat, -2 => AccidentalKind.DoubleFlat, _ => AccidentalKind.None
        };
        return new Note { Pitch = pitch, Accidental = acc, Duration = dur };
    }

    private Pitch ResolveRelative(int step, int alter, int octMarks, EvalState st)
    {
        int prev = st.Reference.DiatonicIndex;
        int candidate = st.Reference.Octave * 7 + step;
        while (candidate - prev > 3) candidate -= 7;
        while (prev - candidate > 3) candidate += 7;
        candidate += 7 * octMarks;
        int octave = (candidate - step) / 7;
        return new Pitch(step, alter, octave);
    }

    private Chord? ReadChord(List<Tok> toks, int from, int to, EvalState st)
    {
        Note? lowest = null;
        for (int j = from; j < to; j++)
        {
            if (toks[j].Kind != TokKind.Word) continue;
            char c0 = toks[j].Text.Length > 0 ? toks[j].Text[0] : ' ';
            if (c0 < 'a' || c0 > 'g') continue;
            var n = ParseNoteWord(toks[j].Text, st);
            if (lowest is null || n.Pitch.DiatonicIndex < lowest.Pitch.DiatonicIndex) lowest = n;
        }
        if (lowest is null) return null;
        var chord = new Chord();
        chord.Notes.Add(lowest);
        return chord;
    }

    private Duration ReadTrailingDuration(List<Tok> toks, ref int i, EvalState st)
    {
        if (i < toks.Count && toks[i].Kind == TokKind.Word && char.IsDigit(toks[i].Text[0]))
        {
            var d = ParseDurationSuffix(toks[i].Text, 0, st, out _);
            i++;
            return d;
        }
        return st.LastDuration;
    }

    /// <summary>Parses a LilyPond duration suffix (<c>4</c>, <c>8.</c>, <c>1</c>, <c>16</c>, <c>*n/m</c>) from
    /// <paramref name="pos"/>. With no digits, inherits the previous duration (LilyPond's rule).</summary>
    private Duration ParseDurationSuffix(string w, int pos, EvalState st, out double scale)
    {
        scale = 1.0;
        int p = pos;
        int num = 0; bool sawDigit = false;
        while (p < w.Length && char.IsDigit(w[p])) { num = num * 10 + (w[p] - '0'); sawDigit = true; p++; }
        int dots = 0;
        while (p < w.Length && w[p] == '.') { dots++; p++; }
        // Duration scale *n or *n/m
        if (p < w.Length && w[p] == '*')
        {
            p++;
            int a = 0; while (p < w.Length && char.IsDigit(w[p])) { a = a * 10 + (w[p] - '0'); p++; }
            int b = 1;
            if (p < w.Length && w[p] == '/') { p++; b = 0; while (p < w.Length && char.IsDigit(w[p])) { b = b * 10 + (w[p] - '0'); p++; } if (b == 0) b = 1; }
            if (a > 0) scale = (double)a / b;
        }

        Duration dur = sawDigit && num > 0
            ? new Duration { Base = num, Dots = dots }
            : new Duration { Base = st.LastDuration.Base, Dots = dots };
        st.LastDuration = dur;
        return dur;
    }

    // ── Small parsers ───────────────────────────────────────────────────────

    private static bool IsPitchWord(string w) =>
        w.Length > 0 && w[0] >= 'a' && w[0] <= 'g';

    private static Pitch ParseAbsolutePitch(string w)
    {
        int step = "cdefgab".IndexOf(w[0]);
        if (step < 0) step = 0;
        int p = 1, alter = 0;
        while (p + 1 < w.Length && (w.Substring(p, 2) == "is" || w.Substring(p, 2) == "es"))
        { alter += w.Substring(p, 2) == "is" ? 1 : -1; p += 2; }
        int marks = 0;
        while (p < w.Length && (w[p] == '\'' || w[p] == ',')) { marks += w[p] == '\'' ? 1 : -1; p++; }
        return new Pitch(step, alter, 3 + marks);
    }

    private static ClefKind ParseClef(string name)
    {
        name = name.Trim().ToLowerInvariant();
        if (name.Contains("bass") || name == "f") return ClefKind.Bass;
        if (name.Contains("alto") || name == "c") return ClefKind.Alto;
        if (name.Contains("tenor")) return ClefKind.Tenor;
        return ClefKind.Treble;
    }

    private static KeySignature ParseKey(string tonic, string mode)
    {
        int step = "cdefgab".IndexOf(char.ToLowerInvariant(tonic[0]));
        if (step < 0) return KeySignature.CMajor;
        int p = 1, alter = 0;
        while (p + 1 < tonic.Length && (tonic.Substring(p, 2) == "is" || tonic.Substring(p, 2) == "es"))
        { alter += tonic.Substring(p, 2) == "is" ? 1 : -1; p += 2; }
        return KeyTable.FromTonic(step, alter, mode);
    }

    private static TimeSignature ParseTime(string w)
    {
        int slash = w.IndexOf('/');
        if (slash > 0 && int.TryParse(w[..slash], out int n) && int.TryParse(w[(slash + 1)..], out int d) && d > 0)
            return new TimeSignature(n, d);
        return new TimeSignature(4, 4);
    }

    private static BarlineKind ParseBar(string s) => s switch
    {
        "||"                 => BarlineKind.Double,
        "|." or "|.|"        => BarlineKind.Final,
        ".|:" or "|:"        => BarlineKind.RepeatStart,
        ":|." or ":|"        => BarlineKind.RepeatEnd,
        ":|.|:" or ":..:"    => BarlineKind.RepeatBoth,
        _                     => BarlineKind.Single,
    };

    // ── Measure assembly (auto-bar by time when no explicit bars) ────────────

    private static void BuildMeasures(List<object> items, Staff staff, double measureLen, bool explicitBars)
    {
        var current = new Measure();
        double acc = 0;
        bool pendingBreak = false;

        void Close(BarlineKind end)
        {
            if (current.Events.Count > 0)
            {
                current.EndBarline = end;
                if (pendingBreak) { current.SystemBreak = true; pendingBreak = false; }
                staff.Measures.Add(current);
            }
            current = new Measure();
            acc = 0;
        }

        foreach (var it in items)
        {
            switch (it)
            {
                case MusicalEvent ev:
                    current.Events.Add(ev);
                    acc += ev.Duration.QuarterLength;
                    if (!explicitBars && measureLen > 0 && acc >= measureLen - 1e-6) Close(BarlineKind.Single);
                    break;
                case BarToken bt:
                    Close(bt.Kind is BarlineKind.Single ? BarlineKind.Single : bt.Kind);
                    break;
                case BreakMark:
                    if (current.Events.Count == 0 && staff.Measures.Count > 0) staff.Measures[^1].SystemBreak = true;
                    else pendingBreak = true;
                    break;
            }
        }
        Close(BarlineKind.Single);
    }
}
