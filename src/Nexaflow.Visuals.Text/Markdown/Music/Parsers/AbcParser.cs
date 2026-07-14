using System;
using System.Collections.Generic;
using System.Globalization;
using Nexaflow.Visuals.Text.Markdown.Music.Model;

namespace Nexaflow.Visuals.Text.Markdown.Music.Parsers;

/// <summary>
/// Parses a practical subset of ABC notation (https://abcnotation.com/wiki/abc:standard:v2.1) into the
/// shared <see cref="Score"/> IR. v1 covers a single voice: the header fields (<c>X/T/C/M/L/K</c>),
/// notes with octave marks and explicit accidentals, note lengths, rests, whitespace beam grouping, and
/// bar lines including repeats. Unsupported constructs (chords, ties/slurs, decorations, multiple voices,
/// broken rhythm) are skipped and noted in <see cref="Score.Warnings"/> rather than failing the parse.
/// </summary>
public sealed class AbcParser
{
    private int _beamCounter;

    public Score Parse(string source)
    {
        var score = new Score();
        var staff = new Staff();
        score.Staves.Add(staff);

        // Header defaults; filled from the fields, then frozen once the body begins.
        TimeSignature meter = new(4, 4);
        bool meterSet = false;
        double unitLenQuarters = 0;    // 0 = "derive from meter once K: is seen"
        int fifths = 0;
        ClefKind clef = ClefKind.Treble;
        bool inBody = false;

        var events = new List<object>();   // MusicalEvent | BarToken
        var warnings = new HashSet<string>();

        foreach (var rawLine in source.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.TrimEnd();
            if (line.Length == 0) continue;

            // A field line: single letter + ':' (e.g. "K:G", "T:Title").
            if (line.Length >= 2 && char.IsLetter(line[0]) && line[1] == ':')
            {
                char field = char.ToUpperInvariant(line[0]);
                string value = line[2..].Trim();
                switch (field)
                {
                    case 'T': if (score.Title is null) score.Title = value; break;
                    case 'C': score.Composer = value; break;
                    case 'M': meter = ParseMeter(value, ref meterSet); break;
                    case 'L': unitLenQuarters = ParseUnitLength(value); break;
                    case 'K':
                        (fifths, var kClef) = ParseKeyField(value);
                        if (kClef is { } c) clef = c;
                        inBody = true;   // K: is the last header field — body follows.
                        break;
                    case 'X': case 'Q': break;               // metadata we don't render
                    default: break;                          // W/w/Z/N/O/… ignored in v1
                }
                continue;
            }

            // Stylesheet directive / comment lines.
            if (line.StartsWith("%%")) continue;
            if (line.StartsWith('%')) continue;
            if (!inBody) continue;   // music before K: is malformed; ignore.

            if (unitLenQuarters <= 0)
                unitLenQuarters = meter.QuarterLengthPerMeasure < 3 ? 0.25 : 0.5; // <3/4 → 1/16 else 1/8

            TokenizeMusicLine(line, unitLenQuarters, fifths, events, warnings);
            // A source line break is a suggested system break (ABC's default engraving convention),
            // unless the line ends with an explicit continuation.
            if (!line.EndsWith('\\')) events.Add(LineBreak.Instance);
        }

        staff.Time = meterSet ? meter : new TimeSignature(4, 4);
        staff.Key = KeySignature.FromFifths(fifths);
        staff.Clef = clef;

        BuildMeasures(events, staff);
        foreach (var w in warnings) score.Warnings.Add(w);
        return score;
    }

    // ── Header field parsing ────────────────────────────────────────────────

    private static TimeSignature ParseMeter(string v, ref bool set)
    {
        v = v.Trim();
        set = true;
        if (v is "C") return new TimeSignature(4, 4);
        if (v is "C|") return new TimeSignature(2, 2);
        int slash = v.IndexOf('/');
        if (slash > 0 &&
            int.TryParse(v[..slash], out int n) &&
            int.TryParse(v[(slash + 1)..], out int d) && d > 0)
            return new TimeSignature(n, d);
        set = false;
        return new TimeSignature(4, 4);
    }

    private static double ParseUnitLength(string v)
    {
        int slash = v.IndexOf('/');
        if (slash > 0 &&
            int.TryParse(v[..slash], out int n) &&
            int.TryParse(v[(slash + 1)..], out int d) && d > 0)
            return n * (4.0 / d);   // in quarter notes
        return 0;
    }

    /// <summary>Parses an ABC <c>K:</c> field into a fifths count and (optional) clef.</summary>
    internal static (int fifths, ClefKind? clef) ParseKeyField(string field)
    {
        field = field.Trim();
        ClefKind? clef = null;
        string lower = field.ToLowerInvariant();
        if (lower.Contains("clef=bass") || lower.Contains("bass")) clef = ClefKind.Bass;
        else if (lower.Contains("alto")) clef = ClefKind.Alto;
        else if (lower.Contains("tenor")) clef = ClefKind.Tenor;

        if (field.Length == 0 || lower.StartsWith("none") || lower.StartsWith("hp"))
            return (0, clef);

        char letter = char.ToUpperInvariant(field[0]);
        if (letter < 'A' || letter > 'G') return (0, clef);
        int step = Array.IndexOf(Pitch.StepLetters, letter);

        int idx = 1, alter = 0;
        if (idx < field.Length && (field[idx] == '#' || field[idx] == 'b'))
        {
            alter = field[idx] == '#' ? 1 : -1;
            idx++;
        }
        string mode = idx < field.Length ? field[idx..] : "";
        // Strip a leading space + any clef=… tail from the mode token.
        int sp = mode.IndexOf(' ');
        if (sp >= 0) mode = mode[..sp];

        return (KeyTable.FromTonic(step, alter, mode).Fifths, clef);
    }

    // ── Music-line tokenizer ────────────────────────────────────────────────

    private void TokenizeMusicLine(string s, double unitLen, int fifths,
        List<object> events, HashSet<string> warnings)
    {
        var beamRun = new List<Note>();
        void FlushBeam()
        {
            if (beamRun.Count >= 2)
            {
                int id = ++_beamCounter;
                foreach (var n in beamRun) n.BeamId = id;
            }
            beamRun.Clear();
        }

        int i = 0;
        int pendingAlter = 0; bool hasAlter = false;
        while (i < s.Length)
        {
            char c = s[i];

            if (c == '%') break;                       // trailing comment
            if (char.IsWhiteSpace(c)) { FlushBeam(); i++; continue; }

            // Bar lines / repeats.
            if (c is '|' or ':' or '[' or ']')
            {
                if (TryScanBarline(s, ref i, out var bar, out bool consumedOnly))
                {
                    FlushBeam();
                    if (!consumedOnly) events.Add(bar);
                    continue;
                }
                // '[' that isn't a bar line → chord / inline field.
                if (c == '[')
                {
                    if (SkipChordOrField(s, ref i, events, unitLen, fifths, warnings)) continue;
                }
                i++; continue;
            }

            switch (c)
            {
                case '^': pendingAlter += (i + 1 < s.Length && s[i + 1] == '^') ? 2 : 1; hasAlter = true;
                          i += (i + 1 < s.Length && s[i + 1] == '^') ? 2 : 1; continue;
                case '_': pendingAlter -= (i + 1 < s.Length && s[i + 1] == '_') ? 2 : 1; hasAlter = true;
                          i += (i + 1 < s.Length && s[i + 1] == '_') ? 2 : 1; continue;
                case '=': pendingAlter = 0; hasAlter = true; i++; continue;

                case '"': SkipDelimited(s, ref i, '"'); continue;                 // chord symbol/annotation
                case '!': SkipDelimited(s, ref i, '!'); continue;                 // decoration
                case '{': SkipBraced(s, ref i); warnings.Add("grace notes not rendered"); continue;
                case '.': i++; continue;                                          // staccato
                case '(': i++; continue;                                          // slur start / tuplet '(' handled loosely
                case ')': i++; continue;
                case '-': if (LastNote(events) is { } tied) tied.TieStart = true; i++; continue;
                case '>': case '<': warnings.Add("broken rhythm not rendered"); i++; continue;
                case '\\': i++; continue;                                          // line continuation
            }

            // Rest.
            if (c is 'z' or 'x' or 'Z' or 'X')
            {
                i++;
                var dur = ScanLength(s, ref i, unitLen);
                if (c is 'Z' or 'X') { events.Add(new Rest { Duration = Duration.Whole }); }
                else events.Add(new Rest { Duration = dur });
                FlushBeam();   // rests break beams in v1
                hasAlter = false; pendingAlter = 0;
                continue;
            }

            // Note.
            if ((c >= 'A' && c <= 'G') || (c >= 'a' && c <= 'g'))
            {
                var note = ScanNote(s, ref i, unitLen, fifths, hasAlter, pendingAlter);
                hasAlter = false; pendingAlter = 0;
                events.Add(note);
                if (note.Duration.IsBeamable) beamRun.Add(note);
                else FlushBeam();
                continue;
            }

            // Anything else — skip one char.
            i++;
        }
        FlushBeam();
    }

    private static Note? LastNote(List<object> events)
    {
        for (int k = events.Count - 1; k >= 0; k--)
            if (events[k] is Note n) return n;
        return null;
    }

    private Note ScanNote(string s, ref int i, double unitLen, int fifths, bool hasAlter, int pendingAlter)
    {
        char letter = s[i++];
        bool upper = letter <= 'Z';
        int step = Array.IndexOf(Pitch.StepLetters, char.ToUpperInvariant(letter));
        int octave = upper ? 4 : 5;

        // Octave marks.
        while (i < s.Length && (s[i] == ',' || s[i] == '\''))
        {
            octave += s[i] == '\'' ? 1 : -1;
            i++;
        }

        var dur = ScanLength(s, ref i, unitLen);

        int alter = hasAlter ? pendingAlter : KeyTable.KeyAlterFor(step, fifths);
        var acc = hasAlter
            ? pendingAlter switch { 2 => AccidentalKind.DoubleSharp, 1 => AccidentalKind.Sharp,
                                    -1 => AccidentalKind.Flat, -2 => AccidentalKind.DoubleFlat,
                                    _ => AccidentalKind.Natural }
            : AccidentalKind.None;

        return new Note
        {
            Pitch = new Pitch(step, alter, octave),
            Accidental = acc,
            Duration = dur,
        };
    }

    /// <summary>Reads an ABC length suffix (<c>2</c>, <c>/2</c>, <c>3/2</c>, <c>/</c>) relative to the
    /// unit note length and snaps it to a drawable duration.</summary>
    private static Duration ScanLength(string s, ref int i, double unitLen)
    {
        int num = 0, den = 0; bool sawNum = false, sawSlash = false, sawDen = false;
        while (i < s.Length && char.IsDigit(s[i])) { num = num * 10 + (s[i] - '0'); sawNum = true; i++; }
        while (i < s.Length && s[i] == '/') { sawSlash = true; den = den == 0 ? 2 : den * 2; i++; }
        if (sawSlash)
        {
            int d2 = 0; bool sd = false;
            while (i < s.Length && char.IsDigit(s[i])) { d2 = d2 * 10 + (s[i] - '0'); sd = true; i++; }
            if (sd) { den = d2; sawDen = true; }
        }
        double factor = 1.0;
        if (sawNum) factor *= num;
        if (sawSlash) factor /= (sawDen ? den : (den == 0 ? 2 : den));
        return Duration.FromQuarterLength(unitLen * factor);
    }

    // ── Bar lines ───────────────────────────────────────────────────────────

    private sealed record BarToken(BarlineKind Kind);

    /// <summary>Sentinel marking an ABC source-line boundary (a suggested system break).</summary>
    private sealed class LineBreak { public static readonly LineBreak Instance = new(); }

    /// <summary>Scans a bar-line token at <paramref name="i"/>. Sets <paramref name="consumedOnly"/> when
    /// the characters were a volta marker that carries no drawable bar line.</summary>
    private static bool TryScanBarline(string s, ref int i, out BarToken bar, out bool consumedOnly)
    {
        bar = new BarToken(BarlineKind.Single);
        consumedOnly = false;
        char c = s[i];
        char n1 = i + 1 < s.Length ? s[i + 1] : '\0';
        char n2 = i + 2 < s.Length ? s[i + 2] : '\0';

        if (c == ':' && n1 == '|')
        {
            i += 2;
            if (i < s.Length && s[i] == ':') { i++; bar = new BarToken(BarlineKind.RepeatBoth); }
            else bar = new BarToken(BarlineKind.RepeatEnd);
            return true;
        }
        if (c == ':' && n1 == ':') { i += 2; bar = new BarToken(BarlineKind.RepeatBoth); return true; }
        if (c == ':') return false;   // lone ':' — not a bar line here

        if (c == '|')
        {
            if (n1 == ':') { i += 2; bar = new BarToken(BarlineKind.RepeatStart); return true; }
            if (n1 == ']') { i += 2; bar = new BarToken(BarlineKind.Final); return true; }
            if (n1 == '|') { i += 2; bar = new BarToken(BarlineKind.Double); return true; }
            // Volta: |1 |2 — a plain bar line; skip the digit.
            if (char.IsDigit(n1)) { i += 2; bar = new BarToken(BarlineKind.Single); return true; }
            i += 1; bar = new BarToken(BarlineKind.Single); return true;
        }
        if (c == '[')
        {
            if (n1 == '|') { i += 2; bar = new BarToken(BarlineKind.Double); return true; }   // [|
            if (char.IsDigit(n1)) { i += 2; consumedOnly = true; return true; }               // [1/[2 volta
            return false;   // chord/inline field
        }
        if (c == ']') { i += 1; consumedOnly = true; return true; }
        return false;
    }

    // ── Chord / inline-field / delimited skips ──────────────────────────────

    private bool SkipChordOrField(string s, ref int i, List<object> events, double unitLen, int fifths,
        HashSet<string> warnings)
    {
        // Inline field: [K:...], [M:...] etc.
        if (i + 2 < s.Length && char.IsLetter(s[i + 1]) && s[i + 2] == ':')
        {
            int end = s.IndexOf(']', i);
            i = end < 0 ? s.Length : end + 1;
            warnings.Add("inline fields not applied");
            return true;
        }
        // Chord [CEG]: render the lowest note only in v1.
        int close = s.IndexOf(']', i);
        if (close > i)
        {
            string inner = s[(i + 1)..close];
            i = close + 1;
            var dur = ScanLength(s, ref i, unitLen);
            Note? lowest = null;
            int j = 0; int pa = 0; bool ha = false;
            while (j < inner.Length)
            {
                char cc = inner[j];
                if (cc == '^') { pa += 1; ha = true; j++; continue; }
                if (cc == '_') { pa -= 1; ha = true; j++; continue; }
                if (cc == '=') { pa = 0; ha = true; j++; continue; }
                if ((cc >= 'A' && cc <= 'G') || (cc >= 'a' && cc <= 'g'))
                {
                    int jj = j;
                    var note = ScanNote(inner, ref jj, unitLen, fifths, ha, pa);
                    j = jj; ha = false; pa = 0;
                    if (lowest is null || note.Pitch.DiatonicIndex < lowest.Pitch.DiatonicIndex) lowest = note;
                    continue;
                }
                j++;
            }
            if (lowest is not null)
            {
                lowest.Duration = dur;
                events.Add(lowest);
                warnings.Add("chords rendered as lowest note");
            }
            return true;
        }
        return false;
    }

    private static void SkipDelimited(string s, ref int i, char delim)
    {
        i++;   // opening delim
        while (i < s.Length && s[i] != delim) i++;
        if (i < s.Length) i++;   // closing delim
    }

    private static void SkipBraced(string s, ref int i)
    {
        i++;
        while (i < s.Length && s[i] != '}') i++;
        if (i < s.Length) i++;
    }

    // ── Measure assembly ────────────────────────────────────────────────────

    private static void BuildMeasures(List<object> tokens, Staff staff)
    {
        var current = new Measure();
        bool pendingBreak = false;
        void Close()
        {
            if (current.Events.Count > 0)
            {
                if (pendingBreak) { current.SystemBreak = true; pendingBreak = false; }
                staff.Measures.Add(current);
            }
            current = new Measure();
        }

        foreach (var tok in tokens)
        {
            if (tok is MusicalEvent ev) { current.Events.Add(ev); continue; }
            if (tok is LineBreak)
            {
                // Boundary: flag the just-closed measure, or defer to the next close if mid-measure.
                if (current.Events.Count == 0 && staff.Measures.Count > 0) staff.Measures[^1].SystemBreak = true;
                else pendingBreak = true;
                continue;
            }
            if (tok is BarToken bt)
            {
                switch (bt.Kind)
                {
                    case BarlineKind.RepeatStart:
                        if (current.Events.Count == 0) current.StartBarline = BarlineKind.RepeatStart;
                        else { Close(); current.StartBarline = BarlineKind.RepeatStart; }
                        break;
                    case BarlineKind.RepeatEnd:
                        current.EndBarline = BarlineKind.RepeatEnd; Close();
                        break;
                    case BarlineKind.RepeatBoth:
                        current.EndBarline = BarlineKind.RepeatEnd; Close();
                        current.StartBarline = BarlineKind.RepeatStart;
                        break;
                    default:
                        current.EndBarline = bt.Kind; Close();
                        break;
                }
            }
        }
        Close();
    }
}
