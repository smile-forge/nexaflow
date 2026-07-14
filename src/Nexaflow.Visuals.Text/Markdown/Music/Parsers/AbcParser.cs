using System;
using System.Collections.Generic;
using Nexaflow.Visuals.Text.Markdown.Music.Model;

namespace Nexaflow.Visuals.Text.Markdown.Music.Parsers;

/// <summary>
/// Parses ABC notation (https://abcnotation.com/wiki/abc:standard:v2.1) into the shared <see cref="Score"/> IR.
///
/// The tune is read in two passes. The first walks the source line by line, splitting header fields from body
/// music and routing each body line through <see cref="TokenizeMusicLine"/>, which emits a flat token stream
/// per voice: events (<see cref="Note"/>/<see cref="Rest"/>/<see cref="Chord"/>) interleaved with the things
/// that live <em>between</em> events — bar lines, repeat brackets, key/meter changes, section headings and
/// source-line breaks. The second pass (<see cref="BuildMeasures"/>) folds that stream into measures, which is
/// where a bar line stops being a token and becomes a property of the bars either side of it.
///
/// Anything that decorates a note rather than being one (an accidental, a chord symbol, a decoration, a grace
/// group, an opening slur, a tuplet, a broken-rhythm marker) is accumulated in <see cref="Pending"/> as the
/// scanner passes it and attached to the next event created — which is exactly the order ABC writes them in.
/// Constructs the engraver cannot draw are recorded in <see cref="Score.Warnings"/> rather than failing the parse.
/// </summary>
public sealed class AbcParser
{
    private int _beamCounter;
    private int _tupletCounter;

    public Score Parse(string source)
    {
        var score = new Score();
        var warnings = new HashSet<string>();

        var meter = new TimeSignature(4, 4);
        bool meterSet = false;
        double unitLen = 0;                          // quarter-lengths; 0 = "derive from the meter at K:"
        var key = KeySignature.CMajor;
        var clef = ClefKind.Treble;
        bool inBody = false;

        // What the staff opens with — frozen when K: ends the header, so a mid-tune change can't rewrite it.
        var headKey = KeySignature.CMajor;
        var headClef = ClefKind.Treble;
        var headMeter = new TimeSignature(4, 4);
        bool headMeterSet = false;

        var voices = new List<Voice>();
        var byId = new Dictionary<string, Voice>(StringComparer.OrdinalIgnoreCase);
        Voice current = GetVoice("1");

        Voice GetVoice(string id)
        {
            if (byId.TryGetValue(id, out var v)) return v;
            v = new Voice(id);
            byId[id] = v;
            voices.Add(v);
            return v;
        }

        foreach (var rawLine in source.Replace("\r\n", "\n").Split('\n'))
        {
            string line = StripComment(rawLine);
            if (line.Length == 0) continue;

            if (IsField(line, out char field, out string value))
            {
                switch (field)
                {
                    case 'T':
                        if (!inBody)
                        {
                            if (score.Title is null) score.Title = value;
                            else score.Subtitles.Add(value);
                        }
                        else current.Tokens.Add(new SectionToken(value));
                        break;
                    case 'C': score.Composer = value; break;
                    case 'O': score.Origin = value; break;
                    case 'R': score.Rhythm = value; break;
                    case 'S': score.Source = value; break;
                    case 'Z': score.Transcription = value; break;
                    case 'N': score.Notes.Add(value); break;
                    case 'W': score.Verses.Add(value); break;

                    case 'M':
                        meter = ParseMeter(value, ref meterSet);
                        if (inBody) current.Tokens.Add(new TimeToken(meter));
                        break;

                    case 'L':
                        unitLen = ParseUnitLength(value);
                        break;

                    case 'K':
                        (key, var kClef) = ParseKeyField(value);
                        if (kClef is { } kc) clef = kc;
                        if (!inBody)
                        {
                            inBody = true;
                            (headKey, headClef, headMeter, headMeterSet) = (key, clef, meter, meterSet);
                        }
                        else current.Tokens.Add(new KeyToken(key, kClef));
                        break;

                    case 'V':
                        {
                            var (id, name) = ParseVoiceField(value);
                            var v = GetVoice(id);
                            if (name is not null) v.Name = name;
                            if (inBody) current = v;
                            break;
                        }

                    case 'w':
                        AlignLyrics(current, value);
                        break;

                    default: break;   // X/Q/P/I/H/B/D/F/G — carried by the source, not engraved
                }
                continue;
            }

            if (!inBody) continue;   // stray text before K: is malformed

            if (unitLen <= 0)
                unitLen = meter.QuarterLengthPerMeasure < 3 ? 0.25 : 0.5;   // ABC's default L: rule

            // A body line may open with an inline voice selector: "[V: P2] B B c/ …".
            string body = line;
            if (TryLeadingVoice(ref body, out string vid)) current = GetVoice(vid);

            int lineStart = current.Tokens.Count;
            TokenizeMusicLine(body, unitLen, meter, key, current, warnings);
            current.LyricAnchor = lineStart;
            current.VerseIndex = 0;

            // A source-line break is ABC's suggested system break, unless the line asks to continue.
            if (!body.TrimEnd().EndsWith('\\')) current.Tokens.Add(LineBreak.Instance);
        }

        foreach (var v in voices)
        {
            if (v.Tokens.Count == 0) continue;
            var staff = new Staff
            {
                Clef = headClef,
                Key  = headKey,
                Time = headMeterSet ? headMeter : new TimeSignature(4, 4),
                ShowTime = headMeterSet,
                Name = v.Name,
            };
            // The staff opens with the *header* key/meter; mid-tune changes ride on their measures. The header
            // key was captured after the whole file was read, so rewind it to the first KeyToken if there is one.
            RewindOpeningSignatures(v, staff);
            BuildMeasures(v.Tokens, staff);
            if (staff.Measures.Count > 0) score.Staves.Add(staff);
        }

        if (score.Staves.Count > 1) warnings.Add($"{score.Staves.Count} voices drawn as separate staves (no bracketed system yet)");
        foreach (var w in warnings) score.Warnings.Add(w);
        return score;
    }

    // ── Lines and fields ────────────────────────────────────────────────────

    /// <summary>Strips an ABC comment (<c>%</c> to end of line), keeping <c>%%</c> directives out entirely and
    /// preserving an escaped <c>\%</c>.</summary>
    private static string StripComment(string line)
    {
        if (line.StartsWith("%%", StringComparison.Ordinal)) return "";
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] != '%') continue;
            if (i > 0 && line[i - 1] == '\\') continue;
            return line[..i].TrimEnd();
        }
        return line.TrimEnd();
    }

    /// <summary>True for an ABC information field — a single letter, a colon, then the value.</summary>
    private static bool IsField(string line, out char field, out string value)
    {
        field = '\0';
        value = "";
        if (line.Length < 2 || line[1] != ':' || !char.IsLetter(line[0])) return false;
        // 'w' (aligned lyrics) and 'W' (block verses) are different fields — case is significant for those two.
        field = line[0] is 'w' or 'W' ? line[0] : char.ToUpperInvariant(line[0]);
        value = line[2..].Trim();
        return true;
    }

    /// <summary>Consumes a leading inline voice selector (<c>[V: P1] …</c>) from a body line.</summary>
    private static bool TryLeadingVoice(ref string line, out string id)
    {
        id = "";
        string t = line.TrimStart();
        if (!t.StartsWith("[V:", StringComparison.OrdinalIgnoreCase)) return false;
        int close = t.IndexOf(']');
        if (close < 0) return false;
        id = ParseVoiceField(t[3..close]).id;
        line = t[(close + 1)..];
        return true;
    }

    /// <summary>Splits a <c>V:</c> value into its id and optional display name.</summary>
    private static (string id, string? name) ParseVoiceField(string value)
    {
        value = value.Trim();
        int sp = value.IndexOfAny([' ', '\t']);
        string id = sp < 0 ? value : value[..sp];
        string? name = null;
        int n = value.IndexOf("name=", StringComparison.OrdinalIgnoreCase);
        if (n >= 0)
        {
            string rest = value[(n + 5)..].Trim();
            if (rest.StartsWith('"'))
            {
                int end = rest.IndexOf('"', 1);
                if (end > 0) name = rest[1..end];
            }
            else
            {
                int end = rest.IndexOfAny([' ', '\t']);
                name = end < 0 ? rest : rest[..end];
            }
        }
        return (id.Length == 0 ? "1" : id, name);
    }

    private static TimeSignature ParseMeter(string v, ref bool set)
    {
        v = v.Trim();
        set = true;
        if (v is "C") return TimeSignature.Common;      // the ¢/C symbols are what the writer asked for —
        if (v is "C|") return TimeSignature.Cut;         // printing "4/4" instead would lose that

        int slash = v.IndexOf('/');
        if (slash > 0 &&
            int.TryParse(v[..slash], out int n) &&
            int.TryParse(v[(slash + 1)..], out int d) && d > 0)
            return new TimeSignature(n, d);
        set = false;                                  // "M:" / "M:none" — free meter, print nothing
        return new TimeSignature(4, 4);
    }

    private static double ParseUnitLength(string v)
    {
        int slash = v.IndexOf('/');
        if (slash > 0 &&
            int.TryParse(v[..slash], out int n) &&
            int.TryParse(v[(slash + 1)..], out int d) && d > 0)
            return n * (4.0 / d);
        return 0;
    }

    /// <summary>Words a <c>K:</c> value can carry that are modifiers rather than the mode.</summary>
    private static bool IsKeyModifier(string w) =>
        w.StartsWith("clef=", StringComparison.OrdinalIgnoreCase) ||
        w.StartsWith("middle=", StringComparison.OrdinalIgnoreCase) ||
        w.StartsWith("transpose=", StringComparison.OrdinalIgnoreCase) ||
        w.StartsWith("octave=", StringComparison.OrdinalIgnoreCase) ||
        w.StartsWith("stafflines=", StringComparison.OrdinalIgnoreCase) ||
        w.Equals("treble", StringComparison.OrdinalIgnoreCase) ||
        w.Equals("bass", StringComparison.OrdinalIgnoreCase) ||
        w.Equals("alto", StringComparison.OrdinalIgnoreCase) ||
        w.Equals("tenor", StringComparison.OrdinalIgnoreCase) ||
        w.Equals("perc", StringComparison.OrdinalIgnoreCase) ||
        w.Equals("exp", StringComparison.OrdinalIgnoreCase);

    /// <summary>Parses an ABC <c>K:</c> field into a key signature and (optional) clef. Accepts the tonic glued
    /// to its mode (<c>Cmajor</c>, <c>Cm</c>, <c>CMAJOR</c>) or separated from it (<c>C major</c>,
    /// <c>C Lydian</c>), in any case, with clef words in any position.</summary>
    internal static (KeySignature key, ClefKind? clef) ParseKeyField(string field)
    {
        field = field.Trim();
        var words = field.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

        ClefKind? clef = null;
        foreach (var w in words)
        {
            string c = w.StartsWith("clef=", StringComparison.OrdinalIgnoreCase) ? w[5..] : w;
            if (c.StartsWith("bass", StringComparison.OrdinalIgnoreCase)) clef = ClefKind.Bass;
            else if (c.StartsWith("alto", StringComparison.OrdinalIgnoreCase)) clef = ClefKind.Alto;
            else if (c.StartsWith("tenor", StringComparison.OrdinalIgnoreCase)) clef = ClefKind.Tenor;
            else if (c.StartsWith("treble", StringComparison.OrdinalIgnoreCase)) clef = ClefKind.Treble;
        }

        if (words.Length == 0) return (KeySignature.CMajor, clef);

        string head = words[0];
        if (head.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            head.StartsWith("HP", StringComparison.OrdinalIgnoreCase) ||
            IsKeyModifier(head))
            return (KeySignature.CMajor, clef);

        char letter = char.ToUpperInvariant(head[0]);
        if (letter < 'A' || letter > 'G') return (KeySignature.CMajor, clef);
        int step = Array.IndexOf(Pitch.StepLetters, letter);

        int idx = 1, alter = 0;
        if (idx < head.Length && (head[idx] == '#' || head[idx] == 'b'))
        {
            alter = head[idx] == '#' ? 1 : -1;
            idx++;
        }

        // The mode is whatever follows the tonic — glued to it, or the next word if that word isn't a modifier.
        string mode = head[idx..];
        if (mode.Length == 0 && words.Length > 1 && !IsKeyModifier(words[1])) mode = words[1];

        return (KeyTable.FromTonic(step, alter, mode), clef);
    }

    // ── Voice buffers ───────────────────────────────────────────────────────

    /// <summary>One ABC voice: its token stream plus the scanner state that must survive a source-line break
    /// (an open slur, a pending tie) and the anchor a following <c>w:</c> line aligns its syllables to.</summary>
    private sealed class Voice(string id)
    {
        public string Id { get; } = id;
        public string? Name { get; set; }
        public List<object> Tokens { get; } = [];
        public Pending Pending { get; } = new();
        public int LyricAnchor { get; set; } = -1;
        public int VerseIndex { get; set; }
    }

    /// <summary>Everything the scanner has read but not yet attached to an event.</summary>
    private sealed class Pending
    {
        public int Alter;
        public bool HasAlter;
        public string? ChordSymbol;
        public string? Annotation;
        public AnnotationPlacement AnnotationAt;
        public List<ArticulationKind> Articulations { get; } = [];
        public List<Note> Graces { get; } = [];
        public bool GraceSlashed;
        public int SlurOpen;
        public int Broken;                    // >0 = '>' (lengthen the previous), <0 = '<'
        public int TupletId, TupletNumber, TupletTime, TupletLeft;
    }

    // ── Token stream ────────────────────────────────────────────────────────

    private sealed record BarToken(BarlineKind Kind);
    private sealed record VoltaToken(string Label);
    private sealed record KeyToken(KeySignature Key, ClefKind? Clef);
    private sealed record TimeToken(TimeSignature Time);
    private sealed record SectionToken(string Label);

    /// <summary>Sentinel marking an ABC source-line boundary (a suggested system break).</summary>
    private sealed class LineBreak { public static readonly LineBreak Instance = new(); }

    // ── Music-line scanner ──────────────────────────────────────────────────

    private void TokenizeMusicLine(string s, double unitLen, TimeSignature meter, KeySignature key,
        Voice voice, HashSet<string> warnings)
    {
        var p = voice.Pending;
        var tokens = voice.Tokens;
        var beam = new List<MusicalEvent>();
        int fifths = key.Fifths;

        void FlushBeam()
        {
            if (beam.Count >= 2)
            {
                int id = ++_beamCounter;
                foreach (var e in beam) e.BeamId = id;
            }
            beam.Clear();
        }

        MusicalEvent? Last()
        {
            for (int k = tokens.Count - 1; k >= 0; k--)
                if (tokens[k] is MusicalEvent e) return e;
            return null;
        }

        void Emit(MusicalEvent ev)
        {
            // A broken-rhythm marker steals time from one side of the pair and gives it to the other.
            if (p.Broken != 0 && Last() is { } prev)
            {
                int n = Math.Min(Math.Abs(p.Broken), 3);
                if (p.Broken > 0) { prev.Duration = prev.Duration.Dotted(n); ev.Duration = ev.Duration.Halved(n); }
                else              { prev.Duration = prev.Duration.Halved(n); ev.Duration = ev.Duration.Dotted(n); }
            }
            p.Broken = 0;

            ev.ChordSymbol = p.ChordSymbol;
            ev.Annotation = p.Annotation;
            ev.AnnotationPlacement = p.AnnotationAt;
            ev.SlurOpen = p.SlurOpen;
            ev.GraceSlashed = p.GraceSlashed;
            ev.Articulations.AddRange(p.Articulations);
            ev.Graces.AddRange(p.Graces);
            if (p.TupletLeft > 0)
            {
                ev.TupletId = p.TupletId;
                ev.TupletNumber = p.TupletNumber;
                ev.TupletTime = p.TupletTime;
                p.TupletLeft--;
            }

            p.ChordSymbol = p.Annotation = null;
            p.AnnotationAt = AnnotationPlacement.Above;
            p.SlurOpen = 0;
            p.GraceSlashed = false;
            p.Articulations.Clear();
            p.Graces.Clear();
            p.HasAlter = false;
            p.Alter = 0;

            tokens.Add(ev);
            if (ev.Duration.IsBeamable && ev is not Rest) beam.Add(ev);
            else FlushBeam();
        }

        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];

            if (char.IsWhiteSpace(c)) { FlushBeam(); i++; continue; }

            // ── Bar lines, repeats and repeat brackets ──
            if (c is '|' or ':' or '[' or ']')
            {
                int before = i;
                if (TryScanBarline(s, ref i, out var bar, out string? volta))
                {
                    FlushBeam();
                    if (bar is { } b) tokens.Add(new BarToken(b));
                    if (volta is not null) tokens.Add(new VoltaToken(volta));
                    continue;
                }
                if (c == '[' && TryScanBracketed(s, ref i, unitLen, meter, fifths, tokens, p, Emit, warnings)) continue;
                if (i == before) i++;
                continue;
            }

            switch (c)
            {
                case '^':
                    { int n = Run(s, ref i, '^'); p.Alter += n; p.HasAlter = true; continue; }
                case '_':
                    { int n = Run(s, ref i, '_'); p.Alter -= n; p.HasAlter = true; continue; }
                case '=':
                    p.Alter = 0; p.HasAlter = true; i++; continue;

                case '"':
                    ScanQuoted(s, ref i, p); continue;
                case '!':
                    ScanBangDecoration(s, ref i, p); continue;
                case '{':
                    ScanGraceGroup(s, ref i, unitLen, fifths, p); continue;

                case '(':
                    if (i + 1 < s.Length && char.IsDigit(s[i + 1])) { ScanTuplet(s, ref i, meter, p, ++_tupletCounter); continue; }
                    p.SlurOpen++; i++; continue;
                case ')':
                    if (Last() is { } sl) sl.SlurClose++;
                    i++; continue;

                case '-':
                    if (Last() is Note tn) tn.TieStart = true;
                    else if (Last() is Chord tc) foreach (var cn in tc.Notes) cn.TieStart = true;
                    i++; continue;

                case '>':
                    p.Broken = Run(s, ref i, '>'); continue;
                case '<':
                    p.Broken = -Run(s, ref i, '<'); continue;

                case '&':
                    warnings.Add("voice overlays (&) not engraved"); i++; continue;
                case 'y':
                    i++; ScanFactor(s, ref i); continue;      // typesetting spacer — no time, no ink
                case '\\':
                    i++; continue;
                case '$': case '*':
                    i++; continue;
            }

            // ── Shorthand decorations (not note letters) ──
            if (Shorthand(c) is { } art) { p.Articulations.Add(art); i++; continue; }

            // ── Rests ──
            if (c is 'z' or 'x' or 'Z')
            {
                i++;
                double f = ScanFactor(s, ref i);
                var rest = c switch
                {
                    'Z' => new Rest { Duration = Duration.FromQuarterLength(meter.QuarterLengthPerMeasure), IsWholeMeasure = true },
                    'x' => new Rest { Duration = Duration.FromQuarterLength(unitLen * f), IsInvisible = true },
                    _   => new Rest { Duration = Duration.FromQuarterLength(unitLen * f) },
                };
                Emit(rest);
                continue;
            }

            // ── Notes ──
            if (IsNoteLetter(c))
            {
                Emit(ScanNote(s, ref i, unitLen, fifths, p));
                continue;
            }

            i++;   // unknown character — skip it rather than fail the tune
        }

        FlushBeam();
    }

    private static bool IsNoteLetter(char c) => (c >= 'A' && c <= 'G') || (c >= 'a' && c <= 'g');

    /// <summary>Consumes a run of one repeated character and returns its length.</summary>
    private static int Run(string s, ref int i, char c)
    {
        int n = 0;
        while (i < s.Length && s[i] == c) { n++; i++; }
        return n;
    }

    /// <summary>ABC's single-character decorations. None of these collide with a note letter (A-G/a-g).</summary>
    private static ArticulationKind? Shorthand(char c) => c switch
    {
        '.' => ArticulationKind.Staccato,
        '~' => ArticulationKind.Roll,
        'H' => ArticulationKind.Fermata,
        'L' => ArticulationKind.Accent,
        'M' => ArticulationKind.LowerMordent,
        'O' => ArticulationKind.Coda,
        'P' => ArticulationKind.Mordent,
        'S' => ArticulationKind.Segno,
        'T' => ArticulationKind.Trill,
        'u' => ArticulationKind.UpBow,
        'v' => ArticulationKind.DownBow,
        _   => null,
    };

    private static void ScanBangDecoration(string s, ref int i, Pending p)
    {
        int close = s.IndexOf('!', i + 1);
        if (close < 0) { i++; return; }
        string name = s[(i + 1)..close].Trim().ToLowerInvariant();
        i = close + 1;
        ArticulationKind? k = name switch
        {
            "staccato" => ArticulationKind.Staccato,
            "tenuto" => ArticulationKind.Tenuto,
            "accent" or "emphasis" or ">" => ArticulationKind.Accent,
            "marcato" or "^" => ArticulationKind.Marcato,
            "upbow" => ArticulationKind.UpBow,
            "downbow" => ArticulationKind.DownBow,
            "fermata" => ArticulationKind.Fermata,
            "trill" => ArticulationKind.Trill,
            "roll" => ArticulationKind.Roll,
            "turn" => ArticulationKind.Turn,
            "uppermordent" or "pralltriller" => ArticulationKind.Mordent,
            "lowermordent" or "mordent" => ArticulationKind.LowerMordent,
            "segno" => ArticulationKind.Segno,
            "coda" => ArticulationKind.Coda,
            _ => null,
        };
        if (k is { } a) p.Articulations.Add(a);
    }

    /// <summary>A double-quoted run: a bare one is a chord symbol, one led by <c>^ _ &lt; &gt; @</c> is a
    /// placed text annotation.</summary>
    private static void ScanQuoted(string s, ref int i, Pending p)
    {
        int close = s.IndexOf('"', i + 1);
        if (close < 0) { i++; return; }
        string text = s[(i + 1)..close];
        i = close + 1;
        if (text.Length == 0) return;

        switch (text[0])
        {
            case '^': p.Annotation = text[1..]; p.AnnotationAt = AnnotationPlacement.Above; break;
            case '_': p.Annotation = text[1..]; p.AnnotationAt = AnnotationPlacement.Below; break;
            case '<': p.Annotation = text[1..]; p.AnnotationAt = AnnotationPlacement.Left; break;
            case '>': p.Annotation = text[1..]; p.AnnotationAt = AnnotationPlacement.Right; break;
            case '@': p.Annotation = text[1..]; p.AnnotationAt = AnnotationPlacement.Above; break;
            default:  p.ChordSymbol = text; break;
        }
    }

    /// <summary>Grace notes: <c>{gAGAG}</c>, or <c>{/g}</c> for a slashed acciaccatura.</summary>
    private void ScanGraceGroup(string s, ref int i, double unitLen, int fifths, Pending p)
    {
        int close = s.IndexOf('}', i);
        if (close < 0) { i++; return; }
        string inner = s[(i + 1)..close];
        i = close + 1;

        if (inner.StartsWith('/')) { p.GraceSlashed = true; inner = inner[1..]; }

        var scratch = new Pending();
        int j = 0;
        while (j < inner.Length)
        {
            char c = inner[j];
            if (c == '^') { scratch.Alter += Run(inner, ref j, '^'); scratch.HasAlter = true; continue; }
            if (c == '_') { scratch.Alter -= Run(inner, ref j, '_'); scratch.HasAlter = true; continue; }
            if (c == '=') { scratch.Alter = 0; scratch.HasAlter = true; j++; continue; }
            if (IsNoteLetter(c))
            {
                var n = ScanNote(inner, ref j, unitLen, fifths, scratch);
                scratch.HasAlter = false;
                scratch.Alter = 0;
                p.Graces.Add(n);
                continue;
            }
            j++;
        }
        if (p.Graces.Count == 1) p.GraceSlashed = true;   // a lone grace note is conventionally slashed
    }

    /// <summary>A tuplet marker: <c>(p</c>, <c>(p:q</c> or <c>(p:q:r</c> — p notes in the time of q, over the
    /// next r notes. The defaults for q follow the ABC standard, where the odd-numbered tuplets mean one thing
    /// in a simple meter and another in a compound one.</summary>
    private static void ScanTuplet(string s, ref int i, TimeSignature meter, Pending p, int id)
    {
        i++;                                              // '('
        int pv = (int)ScanInt(s, ref i);
        if (pv <= 0) return;

        int q = 0, r = 0;
        if (i < s.Length && s[i] == ':')
        {
            i++;
            q = (int)ScanInt(s, ref i);
            if (i < s.Length && s[i] == ':')
            {
                i++;
                r = (int)ScanInt(s, ref i);
            }
        }

        bool compound = meter.Denominator == 8 && meter.Numerator % 3 == 0;
        int n = compound ? 3 : 2;
        if (q <= 0)
            q = pv switch { 2 => 3, 3 => 2, 4 => 3, 6 => 2, 8 => 3, _ => n };
        if (r <= 0) r = pv;

        p.TupletId = id;
        p.TupletNumber = pv;
        p.TupletTime = q;
        p.TupletLeft = r;
    }

    private static double ScanInt(string s, ref int i)
    {
        double v = 0;
        bool any = false;
        while (i < s.Length && char.IsDigit(s[i])) { v = v * 10 + (s[i] - '0'); any = true; i++; }
        return any ? v : 0;
    }

    private Note ScanNote(string s, ref int i, double unitLen, int fifths, Pending p)
    {
        char letter = s[i++];
        bool upper = letter <= 'Z';
        int step = Array.IndexOf(Pitch.StepLetters, char.ToUpperInvariant(letter));
        int octave = upper ? 4 : 5;

        while (i < s.Length && (s[i] == ',' || s[i] == '\''))
        {
            octave += s[i] == '\'' ? 1 : -1;
            i++;
        }

        double f = ScanFactor(s, ref i);

        int alter = p.HasAlter ? p.Alter : KeyTable.KeyAlterFor(step, fifths);
        var acc = p.HasAlter
            ? p.Alter switch
              {
                  >= 2 => AccidentalKind.DoubleSharp,
                  1    => AccidentalKind.Sharp,
                  -1   => AccidentalKind.Flat,
                  <= -2 => AccidentalKind.DoubleFlat,
                  _    => AccidentalKind.Natural,
              }
            : AccidentalKind.None;

        return new Note
        {
            Pitch = new Pitch(step, Math.Clamp(alter, -2, 2), octave),
            Accidental = acc,
            Duration = Duration.FromQuarterLength(unitLen * f),
        };
    }

    /// <summary>Reads an ABC length suffix (<c>2</c>, <c>/2</c>, <c>3/2</c>, <c>/</c>, <c>//</c>) as a multiplier
    /// of the unit note length. No suffix is 1.</summary>
    private static double ScanFactor(string s, ref int i)
    {
        double num = ScanInt(s, ref i);
        double factor = num > 0 ? num : 1.0;

        int slashes = Run(s, ref i, '/');
        if (slashes == 0) return factor;

        double den = ScanInt(s, ref i);
        if (den > 0) return factor / den;

        // Bare slashes halve once each: "A/" = half, "A//" = quarter.
        for (int k = 0; k < slashes; k++) factor /= 2.0;
        return factor;
    }

    // ── Bar lines ───────────────────────────────────────────────────────────

    /// <summary>Scans a bar line and/or a repeat-bracket number at <paramref name="i"/>. Either output may be
    /// null on its own: <c>[1</c> is a bracket with no line, and <c>|</c> is a line with no bracket.</summary>
    private static bool TryScanBarline(string s, ref int i, out BarlineKind? bar, out string? volta)
    {
        bar = null;
        volta = null;
        int start = i;
        char c = s[i];
        char n1 = i + 1 < s.Length ? s[i + 1] : '\0';

        if (c == ':')
        {
            if (n1 == '|')
            {
                i += 2;
                Run(s, ref i, '|');                                   // ':||' is still one end-repeat
                if (i < s.Length && s[i] == ':') { i++; bar = BarlineKind.RepeatBoth; }
                else bar = BarlineKind.RepeatEnd;
            }
            else if (n1 == ':') { i += 2; bar = BarlineKind.RepeatBoth; }
            else return false;                                        // a lone ':' is not a bar line
        }
        else if (c == '|')
        {
            if (n1 == ':') { i += 2; Run(s, ref i, ':'); bar = BarlineKind.RepeatStart; }
            else if (n1 == ']') { i += 2; bar = BarlineKind.Final; }
            else if (n1 == '|') { i += 2; bar = BarlineKind.Double; }
            else { i++; bar = BarlineKind.Single; }
        }
        else if (c == '[')
        {
            if (n1 == '|') { i += 2; bar = BarlineKind.HeavyLight; }
            else if (char.IsDigit(n1)) { i++; }                       // '[1' — bracket only
            else return false;                                        // a chord or an inline field
        }
        else if (c == ']')
        {
            return false;                                             // a stray ']' — nothing to draw
        }

        volta = ScanVolta(s, ref i);
        return i > start;
    }

    /// <summary>Reads a repeat-bracket label immediately after a bar line: <c>1</c>, <c>2</c>, <c>1,2</c>,
    /// <c>1-3</c>.</summary>
    private static string? ScanVolta(string s, ref int i)
    {
        if (i >= s.Length || !char.IsDigit(s[i])) return null;
        int start = i;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == ',' || s[i] == '-')) i++;
        return s[start..i];
    }

    // ── Bracketed constructs: chords and inline fields ──────────────────────

    /// <summary>Handles the two things a <c>[</c> can open once it isn't a bar line: an inline field
    /// (<c>[K:G]</c>) or a chord (<c>[CEG]2</c>).</summary>
    private bool TryScanBracketed(string s, ref int i, double unitLen, TimeSignature meter, int fifths,
        List<object> tokens, Pending p, Action<MusicalEvent> emit, HashSet<string> warnings)
    {
        int close = s.IndexOf(']', i);
        if (close <= i) return false;

        // Inline field: [K:...], [M:...], [L:...].
        if (i + 2 < s.Length && char.IsLetter(s[i + 1]) && s[i + 2] == ':')
        {
            char f = char.ToUpperInvariant(s[i + 1]);
            string val = s[(i + 3)..close].Trim();
            i = close + 1;
            switch (f)
            {
                case 'K':
                    var (k, kc) = ParseKeyField(val);
                    tokens.Add(new KeyToken(k, kc));
                    break;
                case 'M':
                    bool set = false;
                    tokens.Add(new TimeToken(ParseMeter(val, ref set)));
                    break;
                default:
                    warnings.Add($"inline field [{f}:] not applied");
                    break;
            }
            return true;
        }

        // Chord: the members' pitches, the first member's length, times any suffix after the bracket.
        string inner = s[(i + 1)..close];
        i = close + 1;
        double suffix = ScanFactor(s, ref i);

        var chord = new Chord();
        var scratch = new Pending();
        int j = 0;
        Duration? first = null;
        while (j < inner.Length)
        {
            char c = inner[j];
            if (c == '^') { scratch.Alter += Run(inner, ref j, '^'); scratch.HasAlter = true; continue; }
            if (c == '_') { scratch.Alter -= Run(inner, ref j, '_'); scratch.HasAlter = true; continue; }
            if (c == '=') { scratch.Alter = 0; scratch.HasAlter = true; j++; continue; }
            if (IsNoteLetter(c))
            {
                var n = ScanNote(inner, ref j, unitLen, fifths, scratch);
                scratch.HasAlter = false;
                scratch.Alter = 0;
                first ??= n.Duration;
                chord.Notes.Add(n);
                continue;
            }
            j++;
        }
        if (chord.Notes.Count == 0) return true;

        chord.Duration = Duration.FromQuarterLength((first ?? Duration.Quarter).QuarterLength * suffix);
        chord.Notes.Sort((a, b) => a.Pitch.DiatonicIndex.CompareTo(b.Pitch.DiatonicIndex));
        emit(chord);
        return true;
    }

    // ── Lyrics ──────────────────────────────────────────────────────────────

    /// <summary>Aligns one <c>w:</c> line's syllables to the notes of the music line it follows. ABC's rules:
    /// a space or a hyphen ends a syllable (a hyphen also prints one), <c>_</c> holds the previous syllable over
    /// another note, <c>*</c> skips a note, <c>|</c> jumps to the next bar, <c>~</c> glues two words onto one
    /// note, and <c>\-</c> is a literal hyphen. Rests take no syllable.</summary>
    private static void AlignLyrics(Voice voice, string line)
    {
        if (voice.LyricAnchor < 0) return;
        int verse = voice.VerseIndex++;

        // The notes (and bar lines) of the anchored music line, in reading order.
        var slots = new List<object>();
        for (int k = voice.LyricAnchor; k < voice.Tokens.Count; k++)
        {
            var t = voice.Tokens[k];
            if (t is Rest) continue;
            if (t is MusicalEvent or BarToken) slots.Add(t);
        }

        int at = 0;
        void PadTo(int v, MusicalEvent e)
        {
            while (e.Lyrics.Count < v) e.Lyrics.Add(new LyricSyllable("", false, false));
        }
        MusicalEvent? Next()
        {
            while (at < slots.Count && slots[at] is not MusicalEvent) at++;
            return at < slots.Count ? (MusicalEvent)slots[at++] : null;
        }

        foreach (var tok in SplitLyricLine(line))
        {
            if (tok == "|")
            {
                while (at < slots.Count && slots[at] is not BarToken) at++;
                if (at < slots.Count) at++;   // step over the bar itself
                continue;
            }
            if (tok == "*") { Next(); continue; }
            if (tok == "_")
            {
                if (Next() is { } held) { PadTo(verse, held); held.Lyrics.Add(new LyricSyllable("", false, true)); }
                continue;
            }

            bool hyphen = tok.EndsWith('-');
            string text = (hyphen ? tok[..^1] : tok).Replace("\\-", "-").Replace('~', ' ');
            if (text.Length == 0 && !hyphen) continue;
            if (Next() is not { } ev) break;
            PadTo(verse, ev);
            ev.Lyrics.Add(new LyricSyllable(text, hyphen, false));
        }
    }

    /// <summary>Splits a <c>w:</c> line into syllable tokens, keeping a trailing hyphen on the syllable it
    /// belongs to and emitting <c>| * _</c> as tokens of their own.</summary>
    private static IEnumerable<string> SplitLyricLine(string line)
    {
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c is '|' or '*') { i++; yield return c.ToString(); continue; }
            if (c == '_') { i++; yield return "_"; continue; }

            int start = i;
            while (i < line.Length && !char.IsWhiteSpace(line[i]) && line[i] is not ('|' or '*' or '_'))
            {
                if (line[i] == '\\' && i + 1 < line.Length) { i += 2; continue; }   // \- is a literal hyphen
                if (line[i] == '-') { i++; break; }                                 // a hyphen ends the syllable
                i++;
            }
            yield return line[start..i];
        }
    }

    // ── Measure assembly ────────────────────────────────────────────────────

    /// <summary>The staff header shows the key and meter the tune <em>opens</em> with. Both were captured from
    /// the last field seen, so rewind them to the values in force before the first mid-tune change.</summary>
    private static void RewindOpeningSignatures(Voice v, Staff staff)
    {
        foreach (var tok in v.Tokens)
        {
            if (tok is MusicalEvent) break;
            if (tok is KeyToken kt) { staff.Key = kt.Key; if (kt.Clef is { } c) staff.Clef = c; }
            if (tok is TimeToken tt) staff.Time = tt.Time;
        }
    }

    private static void BuildMeasures(List<object> tokens, Staff staff)
    {
        var current = new Measure();
        bool pendingBreak = false;
        KeySignature? pendingKey = null;
        TimeSignature? pendingTime = null;
        string? pendingSection = null;
        string? pendingVolta = null;
        var openBar = BarlineKind.Single;

        void Close(BarlineKind end)
        {
            if (current.Events.Count == 0)
            {
                // Nothing to close (a leading '|', or two bar lines in a row) — but an opening repeat still
                // has to land on the bar that follows it.
                if (openBar != BarlineKind.Single) { current.StartBarline = openBar; openBar = BarlineKind.Single; }
                return;
            }
            current.EndBarline = end;
            if (pendingBreak) { current.SystemBreak = true; pendingBreak = false; }
            staff.Measures.Add(current);
            current = new Measure { StartBarline = openBar };
            openBar = BarlineKind.Single;
        }

        foreach (var tok in tokens)
        {
            switch (tok)
            {
                case MusicalEvent ev:
                    if (current.Events.Count == 0)
                    {
                        current.KeyChange = pendingKey;
                        current.TimeChange = pendingTime;
                        current.SectionLabel = pendingSection;
                        current.Volta ??= pendingVolta;
                        if (pendingSection is not null && staff.Measures.Count > 0)
                            staff.Measures[^1].SystemBreak = true;
                        pendingKey = null;
                        pendingTime = null;
                        pendingSection = null;
                        pendingVolta = null;
                    }
                    current.Events.Add(ev);
                    break;

                case BarToken bt:
                    switch (bt.Kind)
                    {
                        case BarlineKind.RepeatStart or BarlineKind.HeavyLight:
                            if (current.Events.Count == 0) current.StartBarline = bt.Kind;
                            else { openBar = bt.Kind; Close(BarlineKind.Single); }
                            break;
                        case BarlineKind.RepeatBoth:
                            openBar = BarlineKind.RepeatStart;
                            Close(BarlineKind.RepeatEnd);
                            break;
                        default:
                            Close(bt.Kind);
                            break;
                    }
                    break;

                case VoltaToken vt:
                    if (current.Events.Count == 0) pendingVolta = vt.Label;
                    else current.Volta = vt.Label;
                    break;

                case KeyToken kt:
                    pendingKey = kt.Key;
                    break;

                case TimeToken tt:
                    pendingTime = tt.Time;
                    break;

                case SectionToken st:
                    pendingSection = st.Label;
                    break;

                case LineBreak:
                    if (current.Events.Count == 0 && staff.Measures.Count > 0) staff.Measures[^1].SystemBreak = true;
                    else pendingBreak = true;
                    break;
            }
        }

        Close(BarlineKind.Single);
    }
}
