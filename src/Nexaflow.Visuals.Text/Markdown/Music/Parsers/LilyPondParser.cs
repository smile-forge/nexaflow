using System;
using System.Collections.Generic;
using System.Text;
using Nexaflow.Visuals.Text.Markdown.Music.Model;

namespace Nexaflow.Visuals.Text.Markdown.Music.Parsers;

/// <summary>
/// Parses LilyPond (https://lilypond.org/doc/v2.26/Documentation/notation/index) into the shared
/// <see cref="Score"/> IR — the same IR, and the same engraver, as <see cref="AbcParser"/>.
///
/// Where ABC is a line format, LilyPond is a tree, so the front of the parse differs: the source is lexed once
/// (<see cref="Lex"/>), then <see cref="Structure"/> walks the <c>\score</c>/<c>\new</c>/<c>&lt;&lt; &gt;&gt;</c>
/// scaffolding to find out <em>what the staves are</em>, and <see cref="Music"/> evaluates each staff's body into
/// the same flat token stream the ABC parser builds — events interleaved with bar lines, repeat brackets and
/// key/meter changes — which <see cref="BuildMeasures"/> folds into measures. From there the two dialects are
/// indistinguishable to the engraver.
///
/// Three things LilyPond does that ABC doesn't, and which therefore have no counterpart in the other parser:
/// <list type="bullet">
/// <item>Bars are <em>implied by the meter</em> — <c>|</c> is a check, not a bar line — so measures are closed by
/// accumulated time (with <c>\partial</c> shortening the first one), and an explicit <c>\bar "…"</c> may arrive
/// <em>after</em> the bar it belongs to has already closed.</item>
/// <item>Beams are <em>implied by the meter</em> too, so <see cref="AutoBeam"/> groups them after the fact
/// (a manual <c>[ ]</c> beam wins where one is written).</item>
/// <item>A note name carries its own accidental (<c>fis</c> is F sharp whatever the key), so whether one is
/// <em>printed</em> is an engraving decision, not a parsing one — <see cref="ResolveAccidentals"/> prints one only
/// where the note departs from what is already in force in that bar.</item>
/// </list>
///
/// Constructs the engraver cannot draw (dynamics, figured bass, polyphony within one staff, Scheme) are skipped
/// and recorded in <see cref="Score.Warnings"/> rather than failing the parse.
/// </summary>
public sealed class LilyPondParser
{
    private Score _score = new();
    private string _src = "";
    private List<Tok> _t = [];

    private readonly Dictionary<string, (int From, int To)> _defs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _warn = new(StringComparer.Ordinal);
    private readonly List<Vox> _voices = [];

    /// <summary>Lyric blocks found while walking the structure, resolved to a voice once every staff exists.</summary>
    private readonly List<(int From, int To, string? Target, int Near)> _lyrics = [];

    /// <summary>Chord-name blocks (a <c>\chords</c> / <c>\chordmode</c> context), attached to the staff they run against.</summary>
    private readonly List<(int From, int To, int Near)> _chords = [];

    private int _beam, _tuplet;

    public Score Parse(string source)
    {
        _score = new Score();
        _src = "";
        _t = [];
        _defs.Clear();
        _warn.Clear();
        _voices.Clear();
        _lyrics.Clear();
        _chords.Clear();

        try { Run(source); }
        catch { /* tolerant: engrave whatever parsed */ }

        foreach (var w in _warn) _score.Warnings.Add(w);
        return _score;
    }

    private void Run(string source)
    {
        _src = Blank(source);
        _t = Lex(_src);

        var music = ReadTopLevel();

        if (music.Count == 0)
        {
            // No \score and no top-level music: the file is definitions only (a fragment meant to be \include-d).
            // Engrave the richest one, which is what a reader of such a fragment is looking at.
            BuildBestDefinition();
        }
        else
        {
            foreach (var (from, to) in music)
            {
                int before = _voices.Count;
                Structure(from, to, []);
                if (_voices.Count == before && HasEvents(from, to))
                {
                    // Music with no \new Staff around it — the expression is itself the staff.
                    var v = BuildVoice(from, to, null, null, []);
                    Contexts(from, to, _voices.IndexOf(v), []);
                }
            }
        }

        AttachChordSymbols();
        AttachLyrics();

        foreach (var v in _voices)
        {
            if (v.Tokens.Count == 0) continue;
            // LilyPond's default meter is 4/4, drawn as the C symbol — \numericTimeSignature is what takes that back.
            // A leading \time overrides this in RewindOpeningSignatures; this is only what a voice with none gets.
            var staff = new Staff { Name = v.Name, Time = v.Numeric ? new TimeSignature(4, 4) : TimeSignature.Common };
            RewindOpeningSignatures(v, staff);
            AutoBeam(v, staff);
            BuildMeasures(v, staff);
            ResolveAccidentals(staff);
            if (staff.Measures.Count > 0) _score.Staves.Add(staff);
        }
    }

    // ── Top level ───────────────────────────────────────────────────────────

    /// <summary>Splits the file into definitions (<c>name = …</c>), header fields, and the music expressions worth
    /// engraving (<c>\score</c> bodies, or bare top-level music).</summary>
    private List<(int From, int To)> ReadTopLevel()
    {
        var music = new List<(int, int)>();
        int n = _t.Count;

        for (int i = 0; i < n;)
        {
            var t = _t[i];

            if (t.Kind == K.Word && i + 1 < n && _t[i + 1].Kind == K.Eq)
            {
                int body = i + 2;
                int end = ExprEnd(body, n);
                _defs[t.Text] = (body, end);
                i = end;
                continue;
            }

            if (t.Kind == K.Command)
            {
                switch (t.Text)
                {
                    case "header":
                    {
                        int end = ExprEnd(i + 1, n);
                        ReadHeader(i + 1, end);
                        i = end;
                        continue;
                    }
                    case "score":
                    case "book":
                    case "bookpart":
                    {
                        int end = ExprEnd(i + 1, n);
                        music.Add((i + 1, end));
                        i = end;
                        continue;
                    }
                    case "markup":
                    case "markuplist":
                    {
                        int end = ExprEnd(i + 1, n);
                        _score.Title ??= FirstString(i + 1, end);
                        i = end;
                        continue;
                    }
                    case "paper":
                    case "layout":
                    case "midi":
                        i = ExprEnd(i + 1, n);
                        continue;
                    case "version":
                    case "include":
                        i = Math.Min(i + 2, n);
                        continue;
                    case "language":
                    {
                        string lang = i + 1 < n ? _t[i + 1].Text : "";
                        if (lang.Length > 0 && lang is not ("nederlands" or "dutch"))
                            _warn.Add($"\\language \"{lang}\" — note names are read as Dutch (c cis ces)");
                        i = Math.Min(i + 2, n);
                        continue;
                    }
                    default:
                        if (IsWrapper(t.Text) || _defs.ContainsKey(t.Text))
                        {
                            int end = ExprEnd(i, n);
                            music.Add((i, end));
                            i = end;
                            continue;
                        }
                        i++;
                        continue;
                }
            }

            if (t.Kind is K.LBrace or K.SimStart)
            {
                int end = ExprEnd(i, n);
                music.Add((i, end));
                i = end;
                continue;
            }

            i++;
        }

        return music;
    }

    /// <summary>A file of definitions with no <c>\score</c> — engrave the one with the most notes (the melody, not
    /// the <c>\global</c> settings block).</summary>
    private void BuildBestDefinition()
    {
        string? best = null;
        int bestNotes = 0;
        foreach (var (name, range) in _defs)
        {
            int notes = CountEvents(range.From, range.To);
            if (notes > bestNotes) { bestNotes = notes; best = name; }
        }
        if (best is null) return;

        var (f, to) = _defs[best];
        var v = BuildVoice(f, to, null, null, [best]);
        Contexts(f, to, _voices.IndexOf(v), [best]);
        if (_defs.Count > 1) _warn.Add("no \\score block — engraving the definition with the most notes");
    }

    /// <summary>Maps a <c>\header</c> block onto the front matter, by <em>where LilyPond prints each field</em>:
    /// the title block centred, poet/meter at the top left, composer (and opus) at the top right.</summary>
    private void ReadHeader(int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            if (_t[i].Kind != K.Word || i + 1 >= to || _t[i + 1].Kind != K.Eq) continue;

            string field = _t[i].Text.ToLowerInvariant();
            int v = i + 2;
            string? value = v < to && _t[v].Kind == K.Str ? _t[v].Text
                          : v < to && _t[v].Kind == K.Command && _t[v].Text is "markup" or "markuplist"
                              ? FirstString(v + 1, ExprEnd(v + 1, to))
                              : v < to && _t[v].Kind == K.Word ? _t[v].Text
                              : null;
            i = v;
            if (string.IsNullOrWhiteSpace(value)) continue;

            switch (field)
            {
                case "title": _score.Title ??= value; break;
                case "subtitle":
                case "subsubtitle":
                case "dedication":
                case "instrument": _score.Subtitles.Add(value); break;
                case "composer": _score.Composer ??= value; break;
                case "opus":
                case "arranger": _score.Origin ??= value; break;
                case "poet":
                case "meter": _score.Rhythm ??= value; break;
                case "source": _score.Source ??= value; break;
                case "copyright": _score.Notes.Add(value); break;
                case "tagline": break;                       // LilyPond's own footer boilerplate
                default: break;
            }
        }
    }

    // ── Structure: which staves are there? ──────────────────────────────────

    /// <summary>Walks the container scaffolding — <c>\score</c>, staff groups, <c>&lt;&lt; &gt;&gt;</c>, braces and
    /// variable references — building a <see cref="Vox"/> for every <c>\new Staff</c>/<c>\new Voice</c> it finds.
    /// It does <em>not</em> descend into a staff's body; that is <see cref="Music"/>'s job.</summary>
    private void Structure(int from, int to, HashSet<string> active)
    {
        int i = from;
        while (i < to)
        {
            var t = _t[i];

            if (t.Kind is K.LBrace or K.RBrace or K.SimStart or K.SimEnd) { i++; continue; }

            if (t.Kind != K.Command) { i++; continue; }

            switch (t.Text)
            {
                case "new":
                case "context":
                {
                    var (ctx, id, name, body) = ReadContextHead(i, to);
                    int end = ExprEnd(body, to);
                    switch (Kindly(ctx))
                    {
                        case Ctx.Staff:
                        {
                            var v = BuildVoice(body, end, name, id, active);
                            Contexts(body, end, _voices.IndexOf(v), active);
                            break;
                        }
                        case Ctx.Group:
                            Structure(body, end, active);
                            break;
                        case Ctx.Lyrics:
                            _lyrics.Add((body, end, LyricTarget(i, body), _voices.Count - 1));
                            break;
                        case Ctx.Chords:
                            _chords.Add((body, end, _voices.Count - 1));
                            break;
                        default:
                            _warn.Add($"\\new {ctx} not engraved");
                            break;
                    }
                    i = end;
                    continue;
                }

                case "score":
                case "book":
                case "bookpart":
                    Structure(i + 1, ExprEnd(i + 1, to), active);
                    i = ExprEnd(i + 1, to);
                    continue;

                case "header":
                    ReadHeader(i + 1, ExprEnd(i + 1, to));
                    i = ExprEnd(i + 1, to);
                    continue;

                case "layout":
                case "midi":
                case "paper":
                case "markup":
                    i = ExprEnd(i + 1, to);
                    continue;

                case "addlyrics":
                    _lyrics.Add((i + 1, ExprEnd(i + 1, to), null, _voices.Count - 1));
                    i = ExprEnd(i + 1, to);
                    continue;

                case "chords":
                case "chordmode":
                    _chords.Add((i + 1, ExprEnd(i + 1, to), _voices.Count - 1));
                    i = ExprEnd(i + 1, to);
                    continue;

                default:
                    // A reference to a definition that is itself scaffolding (music = { \new Staff … }).
                    if (_defs.TryGetValue(t.Text, out var def) && active.Add(t.Text))
                    {
                        Structure(def.From, def.To, active);
                        active.Remove(t.Text);
                        i++;
                        continue;
                    }
                    // Anything else at this level is music, and belongs to a staff we haven't been told about.
                    i = ExprEnd(i, to);
                    continue;
            }
        }
    }

    /// <summary>The lyric and chord-name contexts written <em>inside</em> a staff's body (the
    /// <c>\new Staff { \new Voice = "x" { … } \addlyrics { … } }</c> idiom), attached to that staff.</summary>
    private void Contexts(int from, int to, int voice, HashSet<string> active)
    {
        int i = from;
        while (i < to)
        {
            var t = _t[i];
            if (t.Kind != K.Command) { i++; continue; }

            switch (t.Text)
            {
                case "addlyrics":
                    _lyrics.Add((i + 1, ExprEnd(i + 1, to), null, voice));
                    i = ExprEnd(i + 1, to);
                    continue;
                case "new":
                case "context":
                {
                    var (ctx, _, _, body) = ReadContextHead(i, to);
                    int end = ExprEnd(body, to);
                    if (Kindly(ctx) == Ctx.Lyrics) _lyrics.Add((body, end, LyricTarget(i, body), voice));
                    else if (Kindly(ctx) == Ctx.Chords) _chords.Add((body, end, voice));
                    i = end;
                    continue;
                }
                default:
                    if (_defs.TryGetValue(t.Text, out var def) && active.Add(t.Text))
                    {
                        Contexts(def.From, def.To, voice, active);
                        active.Remove(t.Text);
                    }
                    i++;
                    continue;
            }
        }
    }

    private enum Ctx { Staff, Group, Lyrics, Chords, Other }

    private static Ctx Kindly(string ctx) => ctx switch
    {
        "Staff" or "RhythmicStaff" or "DrumStaff" or "TabStaff" or "Voice" or "NullVoice" or "VaticanaStaff" => Ctx.Staff,
        "StaffGroup" or "ChoirStaff" or "PianoStaff" or "GrandStaff" or "Score" or "ChoirStaffGroup" => Ctx.Group,
        "Lyrics" or "LyricsVoice" => Ctx.Lyrics,
        "ChordNames" => Ctx.Chords,
        _ => Ctx.Other,
    };

    /// <summary>Reads <c>\new Ctx = "id" \with { instrumentName = "…" }</c>, returning where the music starts.</summary>
    private (string Ctx, string? Id, string? Name, int Body) ReadContextHead(int i, int to)
    {
        int j = i + 1;                                                   // past \new
        string ctx = j < to && _t[j].Kind is K.Word or K.Str ? _t[j++].Text : "";

        string? id = null;
        if (j < to && _t[j].Kind == K.Eq)
        {
            j++;
            if (j < to && _t[j].Kind is K.Word or K.Str) id = _t[j++].Text;
        }

        string? name = null;
        if (j < to && _t[j].Kind == K.Command && _t[j].Text == "with")
        {
            int end = ExprEnd(j + 1, to);
            name = FieldValue(j + 1, end, "instrumentName");
            j = end;
        }

        return (ctx, id, name, j);
    }

    /// <summary>The voice a <c>\new Lyrics \lyricsto "melody" …</c> block sings to.</summary>
    private string? LyricTarget(int head, int body)
    {
        for (int k = head; k < body; k++)
            if (_t[k].Kind == K.Command && _t[k].Text == "lyricsto" && k + 1 < body && _t[k + 1].Kind is K.Str or K.Word)
                return _t[k + 1].Text;
        return null;
    }

    /// <summary>The string assigned to <paramref name="field"/> inside a <c>\with</c>/<c>\set</c> block.</summary>
    private string? FieldValue(int from, int to, string field)
    {
        for (int i = from; i + 2 < to; i++)
        {
            if (_t[i].Kind != K.Word || !_t[i].Text.EndsWith(field, StringComparison.Ordinal)) continue;
            if (_t[i + 1].Kind != K.Eq) continue;
            if (_t[i + 2].Kind == K.Str) return _t[i + 2].Text;
            if (_t[i + 2].Kind == K.Command && _t[i + 2].Text is "markup" or "markuplist")
                return FirstString(i + 3, ExprEnd(i + 3, to));
        }
        return null;
    }

    // ── The staff body ──────────────────────────────────────────────────────

    /// <summary>One LilyPond staff under construction: the same flat token stream <see cref="AbcParser"/> builds,
    /// plus the state that has to survive the whole voice (the <c>\relative</c> reference lives in
    /// <see cref="St"/>).</summary>
    private sealed class Vox
    {
        public string? Name;
        public List<string> Ids { get; } = [];
        public List<object> Tokens { get; } = [];
        public MusicalEvent? Last;

        /// <summary>Quarter-lengths of a <c>\partial</c> pickup, so the first bar closes early.</summary>
        public double Partial;

        /// <summary>Set by <c>\numericTimeSignature</c> — 4/4 prints as figures rather than the C symbol.</summary>
        public bool Numeric;

        public bool AutoBeam = true;
        public int Verses;
    }

    /// <summary>Scanner state for one voice. Unlike ABC, the only thing that <em>precedes</em> its event is the
    /// grace group — every other mark is written after the note it belongs to, and lands on <see cref="Vox.Last"/>.</summary>
    private sealed class St
    {
        public bool Relative;
        public Pitch Reference = new(0, 0, 3);
        public int AbsoluteBase = 3;                 // \fixed c' → 4; plain absolute → c is C3
        public Duration Last = Duration.Quarter;
        public int Fifths;
        public int TupletId, TupletNumber, TupletTime;
        public int ManualBeam;
        public List<Note> Graces { get; } = [];
        public bool GraceSlashed;
    }

    private sealed record BarToken(BarlineKind Kind);
    private sealed record VoltaToken(string Label);
    private sealed record KeyToken(KeySignature Key);
    private sealed class TimeToken(TimeSignature time) { public TimeSignature Time { get; set; } = time; }
    private sealed record ClefToken(ClefKind Clef);
    private sealed record SectionToken(string Label);
    private sealed record CadenzaToken(bool On);
    private sealed class LineBreak { public static readonly LineBreak Instance = new(); }

    /// <summary>Restyles the time signature currently being set up — the last one written with no note after it yet.
    /// <paramref name="symbol"/> null restores the default for its fraction (C for 4/4, ¢ for 2/2, figures else).</summary>
    private static void Restyle(Vox v, TimeSymbol? symbol)
    {
        for (int i = v.Tokens.Count - 1; i >= 0; i--)
        {
            if (v.Tokens[i] is MusicalEvent) return;
            if (v.Tokens[i] is not TimeToken tt) continue;
            tt.Time = tt.Time with { Symbol = symbol ?? DefaultSymbol(tt.Time.Numerator, tt.Time.Denominator) };
            return;
        }
    }

    private static TimeSymbol DefaultSymbol(int n, int d) =>
        (n, d) switch { (4, 4) => TimeSymbol.Common, (2, 2) => TimeSymbol.Cut, _ => TimeSymbol.Numeric };

    private Vox BuildVoice(int from, int to, string? name, string? id, HashSet<string> active)
    {
        var v = new Vox { Name = name };
        if (id is not null) v.Ids.Add(id);
        _voices.Add(v);
        Music(from, to, v, new St(), active);
        return v;
    }

    private void Music(int from, int to, Vox v, St st, HashSet<string> active)
    {
        int i = from;
        while (i < to)
        {
            var t = _t[i];
            switch (t.Kind)
            {
                case K.LBrace:
                case K.RBrace:
                    i++;
                    break;

                case K.SimStart:
                {
                    int close = MatchClose(i, to, K.SimStart, K.SimEnd);
                    Simultaneous(i + 1, close, v, st, active);
                    i = close + 1;
                    break;
                }

                case K.ChordStart:
                {
                    int close = MatchClose(i, to, K.ChordStart, K.ChordEnd);
                    var chord = ReadChord(i + 1, close, st);
                    i = close + 1;
                    if (chord is not null)
                    {
                        chord.Duration = ReadDuration(ref i, to, st);
                        Emit(v, chord, st);
                    }
                    break;
                }

                case K.Bar:
                    v.Tokens.Add(new BarToken(BarlineKind.Single));
                    i++;
                    break;

                case K.Tie:
                    Tie(v.Last);
                    i++;
                    break;

                case K.SlurOpen:
                    if (v.Last is { } so) so.SlurOpen++;
                    i++;
                    break;

                case K.SlurClose:
                    if (v.Last is { } sc) sc.SlurClose++;
                    i++;
                    break;

                case K.BeamOpen:
                    st.ManualBeam = ++_beam;
                    if (v.Last is { } bo) bo.BeamId = st.ManualBeam;
                    i++;
                    break;

                case K.BeamClose:
                    st.ManualBeam = 0;
                    i++;
                    break;

                case K.Artic:
                    if (Shorthand(t.Text[0]) is { } sh) v.Last?.Articulations.Add(sh);
                    i++;
                    break;

                case K.Dir:
                    i++;
                    ReadDirected(ref i, to, v, t.Text);
                    break;

                case K.Command:
                    Command(ref i, to, v, st, active);
                    break;

                case K.Word:
                {
                    var evs = new List<MusicalEvent>();
                    if (TryEvent(ref i, to, st, evs))
                        foreach (var ev in evs)
                            Emit(v, ev, st);
                    else i++;
                    break;
                }

                default:
                    i++;
                    break;
            }
        }
    }

    private void Emit(Vox v, MusicalEvent ev, St st)
    {
        if (st.Graces.Count > 0)
        {
            ev.Graces.AddRange(st.Graces);
            ev.GraceSlashed = st.GraceSlashed;
            st.Graces.Clear();
            st.GraceSlashed = false;
        }
        if (st.TupletNumber > 1)
        {
            ev.TupletId = st.TupletId;
            ev.TupletNumber = st.TupletNumber;
            ev.TupletTime = st.TupletTime;
        }
        if (st.ManualBeam != 0 && ev.Duration.IsBeamable && ev is not Rest) ev.BeamId = st.ManualBeam;

        v.Tokens.Add(ev);
        v.Last = ev;
    }

    private static void Tie(MusicalEvent? ev)
    {
        switch (ev)
        {
            case Note n: n.TieStart = true; break;
            case Chord c: foreach (var cn in c.Notes) cn.TieStart = true; break;
        }
    }

    /// <summary>A <c>^</c>/<c>_</c>/<c>-</c> prefix: either placed text (<c>c4^"Fine"</c>) or a named articulation
    /// with an explicit side (<c>c4_\fermata</c>). The side only matters for text — the engraver places a mark.</summary>
    private void ReadDirected(ref int i, int to, Vox v, string dir)
    {
        if (i >= to) return;

        var t = _t[i];
        if (t.Kind == K.Str)
        {
            Annotate(v.Last, t.Text, dir);
            i++;
            return;
        }
        if (t.Kind == K.Command && t.Text is "markup" or "markuplist")
        {
            int end = ExprEnd(i + 1, to);
            if (FirstString(i + 1, end) is { } text) Annotate(v.Last, text, dir);
            i = end;
            return;
        }
        if (t.Kind == K.Command)
        {
            if (Named(t.Text) is { } a) v.Last?.Articulations.Add(a);
            else _warn.Add($"\\{t.Text} not engraved");
            i++;
        }
    }

    private static void Annotate(MusicalEvent? ev, string text, string dir)
    {
        if (ev is null || text.Length == 0) return;
        ev.Annotation = text;
        ev.AnnotationPlacement = dir == "_" ? AnnotationPlacement.Below : AnnotationPlacement.Above;
    }

    /// <summary>Inside a staff, <c>&lt;&lt; … &gt;&gt;</c> is polyphony. The engraver draws one voice per staff, so
    /// the first strand is engraved and the rest are reported — an honest single line beats two lines of notes
    /// sharing one set of stems.</summary>
    private void Simultaneous(int from, int to, Vox v, St st, HashSet<string> active)
    {
        var strands = new List<(int From, int To)>();

        int i = from;
        while (i < to)
        {
            var t = _t[i];
            if (t.Kind == K.VoiceSep) { i++; continue; }

            if (t.Kind == K.Command && t.Text is "new" or "context")
            {
                var (ctx, id, name, body) = ReadContextHead(i, to);
                int end = ExprEnd(body, to);
                switch (Kindly(ctx))
                {
                    case Ctx.Staff:
                        if (id is not null) v.Ids.Add(id);
                        v.Name ??= name;
                        strands.Add((body, end));
                        break;
                    case Ctx.Lyrics:
                        _lyrics.Add((body, end, LyricTarget(i, body), _voices.IndexOf(v)));
                        break;
                    case Ctx.Chords:
                        _chords.Add((body, end, _voices.IndexOf(v)));
                        break;
                    default:
                        _warn.Add($"\\new {ctx} not engraved");
                        break;
                }
                i = end;
                continue;
            }

            int expr = ExprEnd(i, to);
            if (expr <= i) break;
            strands.Add((i, expr));
            i = expr;
        }

        if (strands.Count == 0) return;
        if (strands.Count > 1) _warn.Add("polyphony within one staff — only the first voice is engraved");
        Music(strands[0].From, strands[0].To, v, st, active);
    }

    // ── Commands ────────────────────────────────────────────────────────────

    private void Command(ref int i, int to, Vox v, St st, HashSet<string> active)
    {
        string cmd = _t[i].Text;
        i++;

        switch (cmd)
        {
            case "relative":
                st.Relative = true;
                if (i < to && _t[i].Kind == K.Word && IsPitchWord(_t[i].Text)) st.Reference = Absolute(_t[i++].Text, 3);
                return;

            case "fixed":
                st.Relative = false;
                if (i < to && _t[i].Kind == K.Word && IsPitchWord(_t[i].Text)) st.AbsoluteBase = 3 + OctaveMarks(_t[i++].Text);
                return;

            case "absolute":
                st.Relative = false;
                st.AbsoluteBase = 3;
                return;

            case "clef":
                if (i < to && _t[i].Kind is K.Str or K.Word) v.Tokens.Add(new ClefToken(ParseClef(_t[i++].Text)));
                return;

            case "key":
            {
                if (i < to && _t[i].Kind == K.Word)
                {
                    string tonic = _t[i++].Text;
                    string mode = i < to && _t[i].Kind == K.Command ? _t[i++].Text : "major";
                    var key = ParseKey(tonic, mode);
                    st.Fifths = key.Fifths;
                    v.Tokens.Add(new KeyToken(key));
                }
                return;
            }

            case "time":
                if (i < to && _t[i].Kind == K.Word)
                {
                    var ts = ParseTime(_t[i++].Text);
                    if (v.Numeric) ts = ts with { Symbol = TimeSymbol.Numeric };
                    v.Tokens.Add(new TimeToken(ts));
                }
                return;

            // Written *after* the \time it applies to, as often as before it — so it reaches back to the signature
            // still being set up (one with no note after it yet), and forward to every later one.
            case "numericTimeSignature":
                v.Numeric = true;
                Restyle(v, TimeSymbol.Numeric);
                return;
            case "defaultTimeSignature":
                v.Numeric = false;
                Restyle(v, null);
                return;

            case "cadenzaOn":
                v.Tokens.Add(new CadenzaToken(true));
                return;
            case "cadenzaOff":
                v.Tokens.Add(new CadenzaToken(false));
                return;

            case "partial":
                if (i < to && _t[i].Kind == K.Word) v.Partial = ReadWordDuration(_t[i++].Text, st).QuarterLength;
                return;

            case "bar":
                if (i < to && _t[i].Kind is K.Str or K.Word) v.Tokens.Add(new BarToken(ParseBar(_t[i++].Text)));
                return;

            case "break":
                v.Tokens.Add(LineBreak.Instance);
                return;

            case "sectionLabel":
            {
                if (i < to && _t[i].Kind == K.Str) v.Tokens.Add(new SectionToken(_t[i++].Text));
                else if (i < to && _t[i].Kind == K.Command && _t[i].Text is "markup")
                {
                    int end = ExprEnd(i + 1, to);
                    if (FirstString(i + 1, end) is { } lbl) v.Tokens.Add(new SectionToken(lbl));
                    i = end;
                }
                return;
            }

            case "repeat":
                Repeat(ref i, to, v, st, active);
                return;

            case "alternative":
                Alternative(ref i, to, v, st, active);
                return;

            case "tuplet":
            case "times":
            {
                int num = 3, time = 2;
                if (i < to && _t[i].Kind == K.Word && Ratio(_t[i].Text) is var (a, b) && b > 0)
                {
                    // \tuplet 3/2 is "3 in the time of 2"; \times 2/3 says the same thing the other way round.
                    (num, time) = cmd == "tuplet" ? (a, b) : (b, a);
                    i++;
                }
                if (i < to && _t[i].Kind == K.Word && char.IsDigit(_t[i].Text[0])) i++;   // optional bracket duration

                int end = ExprEnd(i, to);
                var (oid, onum, otime) = (st.TupletId, st.TupletNumber, st.TupletTime);
                (st.TupletId, st.TupletNumber, st.TupletTime) = (++_tuplet, num, time);
                Music(i, end, v, st, active);
                (st.TupletId, st.TupletNumber, st.TupletTime) = (oid, onum, otime);
                i = end;
                return;
            }

            case "grace":
            case "acciaccatura":
            case "appoggiatura":
            case "slashedGrace":
            {
                int end = ExprEnd(i, to);
                var scratch = new Vox();
                Music(i, end, scratch, st, active);                      // the graces advance \relative, as they should
                foreach (var tok in scratch.Tokens)
                    if (tok is Note g) st.Graces.Add(g);
                st.GraceSlashed = cmd is "acciaccatura" or "slashedGrace";
                i = end;
                return;
            }

            case "afterGrace":
                _warn.Add("\\afterGrace not engraved");
                i = ExprEnd(i, to);
                i = ExprEnd(i, to);
                return;

            case "addlyrics":
            case "lyricmode":
            case "lyricsto":
            case "chordmode":
            case "chords":
            case "figuremode":
            case "figures":
            case "drummode":
            case "drums":
                if (cmd is "figuremode" or "figures") _warn.Add("figured bass not engraved");
                if (cmd is "drummode" or "drums") _warn.Add("drum notation not engraved");
                if (cmd == "lyricsto" && i < to && _t[i].Kind is K.Str or K.Word) i++;
                i = ExprEnd(i, to);                                      // registered by Structure/Contexts
                return;

            case "new":
            case "context":
            {
                var (ctx, id, name, body) = ReadContextHead(i - 1, to);
                int end = ExprEnd(body, to);
                if (Kindly(ctx) == Ctx.Staff)
                {
                    if (id is not null) v.Ids.Add(id);
                    v.Name ??= name;
                    Music(body, end, v, st, active);                     // \new Voice inside a staff — same staff
                }
                else if (Kindly(ctx) is Ctx.Other)
                {
                    _warn.Add($"\\new {ctx} not engraved");
                }
                i = end;
                return;
            }

            case "with":
                if (FieldValue(i, ExprEnd(i, to), "instrumentName") is { } wn) v.Name ??= wn;
                i = ExprEnd(i, to);
                return;

            case "set":
            case "override":
            {
                int end = SkipAssignment(i, to);
                if (cmd == "set" && FieldValue(i, end, "instrumentName") is { } sn) v.Name ??= sn;
                i = end;
                return;
            }

            case "unset":
            case "revert":
            case "omit":
            case "hide":
            case "once":
            case "tweak":
            case "shape":
                if (i < to && _t[i].Kind is K.Word or K.Str) i++;
                return;

            case "autoBeamOff": v.AutoBeam = false; return;
            case "autoBeamOn": v.AutoBeam = true; return;

            case "markup":
            case "markuplist":
                i = ExprEnd(i, to);
                return;

            case "tempo":
                while (i < to && _t[i].Kind is K.Str or K.Word) i++;
                if (i < to && _t[i].Kind == K.Eq) i++;
                while (i < to && _t[i].Kind == K.Word) i++;
                return;

            case "mark":
                if (i < to && _t[i].Kind is K.Str or K.Word) i++;
                else if (i < to && _t[i].Kind == K.Command) i = ExprEnd(i + 1, to);
                return;

            case "breve":
            case "longa":
            case "maxima":
                // A duration with no note in front of it — LilyPond writes it after the pitch, so it was already
                // consumed there. Reaching it here means a stray; ignore.
                return;

            default:
                if (Named(cmd) is { } art) { v.Last?.Articulations.Add(art); return; }
                if (IsDynamic(cmd)) { _warn.Add("dynamics not engraved"); return; }
                if (IsIgnorable(cmd)) return;

                if (_defs.TryGetValue(cmd, out var def) && active.Add(cmd))
                {
                    Music(def.From, def.To, v, st, active);
                    active.Remove(cmd);
                    return;
                }
                _warn.Add($"\\{cmd} not engraved");
                return;
        }
    }

    /// <summary><c>\repeat volta 2 { … }</c> — the repeat bar lines the ABC writer would have typed by hand.
    /// <c>\repeat unfold n</c> genuinely repeats the music, so it is written out.</summary>
    private void Repeat(ref int i, int to, Vox v, St st, HashSet<string> active)
    {
        string kind = i < to && _t[i].Kind == K.Word ? _t[i++].Text : "volta";
        int count = 2;
        if (i < to && _t[i].Kind == K.Word && int.TryParse(_t[i].Text, out int c)) { count = c; i++; }

        int end = ExprEnd(i, to);

        if (kind == "unfold")
        {
            for (int k = 0; k < Math.Clamp(count, 1, 8); k++) Music(i, end, v, st, active);
            if (count > 8) _warn.Add($"\\repeat unfold {count} — written out {8} times");
            i = end;
            return;
        }

        if (kind is "percent" or "tremolo")
        {
            _warn.Add($"\\repeat {kind} not engraved");
            Music(i, end, v, st, active);
            i = end;
            return;
        }

        v.Tokens.Add(new BarToken(BarlineKind.RepeatStart));
        int mark = v.Tokens.Count;
        Music(i, end, v, st, active);
        i = end;

        // \alternative may sit inside the repeat body (2.24) or follow it — either way it has already emitted its
        // voltas, and the end-repeat bar line belongs to the first alternative, not to the body.
        bool voltas = false;
        for (int k = mark; k < v.Tokens.Count; k++)
            if (v.Tokens[k] is VoltaToken) { voltas = true; break; }

        if (!voltas && i < to && _t[i].Kind == K.Command && _t[i].Text == "alternative")
        {
            i++;
            Alternative(ref i, to, v, st, active);
            return;
        }

        if (!voltas) v.Tokens.Add(new BarToken(BarlineKind.RepeatEnd));
    }

    private void Alternative(ref int i, int to, Vox v, St st, HashSet<string> active)
    {
        int end = ExprEnd(i, to);
        var groups = BraceGroups(i, end);
        if (groups.Count == 0) { i = end; return; }

        for (int k = 0; k < groups.Count; k++)
        {
            v.Tokens.Add(new BarToken(BarlineKind.Single));               // close the bar the bracket opens after
            v.Tokens.Add(new VoltaToken((k + 1).ToString()));
            Music(groups[k].From, groups[k].To, v, st, active);
            if (k < groups.Count - 1) v.Tokens.Add(new BarToken(BarlineKind.RepeatEnd));
        }
        i = end;
    }

    /// <summary>The top-level <c>{ … }</c> groups inside an <c>\alternative</c>'s braces.</summary>
    private List<(int From, int To)> BraceGroups(int from, int to)
    {
        var groups = new List<(int, int)>();
        int open = -1;
        for (int i = from; i < to; i++)
            if (_t[i].Kind == K.LBrace) { open = i; break; }
        if (open < 0) return groups;

        int close = MatchClose(open, to, K.LBrace, K.RBrace);
        for (int i = open + 1; i < close;)
        {
            if (_t[i].Kind != K.LBrace) { i++; continue; }
            int g = MatchClose(i, close, K.LBrace, K.RBrace);
            groups.Add((i + 1, g));
            i = g + 1;
        }
        // \alternative { music } with no inner braces — one alternative.
        if (groups.Count == 0) groups.Add((open + 1, close));
        return groups;
    }

    /// <summary>Steps over <c>\set Ctx.prop = value</c> / <c>\override Grob.prop = value</c>. The value may be a
    /// string, a Scheme atom, a markup or a block — but never a note, so this must not fall through to one.</summary>
    private int SkipAssignment(int i, int to)
    {
        while (i < to && _t[i].Kind is K.Word or K.Str or K.Scheme) i++;
        if (i < to && _t[i].Kind == K.Eq)
        {
            i++;
            if (i < to && _t[i].Kind == K.LBrace) return MatchClose(i, to, K.LBrace, K.RBrace) + 1;
            if (i < to && _t[i].Kind == K.Command) return ExprEnd(i + 1, to);
            if (i < to && _t[i].Kind is K.Str or K.Scheme) i++;
        }
        return i;
    }

    // ── Notes, rests, chords ────────────────────────────────────────────────

    /// <summary>Reads the event a word spells. Usually one — but <c>R1*3</c> is three bars of rest, and writing them
    /// out is what lets each of them close its own measure.</summary>
    private bool TryEvent(ref int i, int to, St st, List<MusicalEvent> events)
    {
        string w = _t[i].Text;
        if (w.Length == 0) { i++; return false; }

        char c0 = w[0];

        if (c0 is 'r' or 'R' or 's')
        {
            int p = 1;
            var dur = ReadDuration(w, ref p, st, out double scale, ref i, to);
            i++;

            bool whole = scale >= 2 && Math.Abs(scale - Math.Round(scale)) < 1e-6;
            int bars = whole && c0 is 'R' or 's' ? (int)Math.Round(scale) : 1;
            var length = bars > 1 ? dur : Duration.FromQuarterLength(dur.QuarterLength * scale);

            for (int k = 0; k < bars; k++)
                events.Add(new Rest
                {
                    Duration = length,
                    IsWholeMeasure = c0 == 'R',
                    IsInvisible = c0 == 's',
                });
            return true;
        }

        if (IsNoteWord(w))
        {
            int p = 0;
            events.Add(ReadNote(w, ref p, st, ref i, to));
            i++;
            return true;
        }

        return false;
    }

    private Note ReadNote(string w, ref int p, St st, ref int i, int to)
    {
        int step = "cdefgab".IndexOf(w[p]);
        p++;

        int alter = ReadAlteration(w, ref p, step);
        int marks = 0;
        while (p < w.Length && (w[p] == '\'' || w[p] == ',')) { marks += w[p] == '\'' ? 1 : -1; p++; }
        while (p < w.Length && (w[p] == '!' || w[p] == '?')) p++;        // forced / cautionary — always printed anyway

        var dur = ReadDuration(w, ref p, st, out double scale, ref i, to);
        if (scale != 1.0) dur = Duration.FromQuarterLength(dur.QuarterLength * scale);

        var pitch = st.Relative ? Relative(step, alter, marks, st) : new Pitch(step, alter, st.AbsoluteBase + marks);
        st.Reference = pitch;

        return new Note { Pitch = pitch, Duration = dur };                // the accidental is decided at engrave time
    }

    /// <summary>Dutch note names: <c>is</c> raises, <c>es</c> lowers, and <c>a</c>/<c>e</c> may contract
    /// (<c>as</c> = a flat, <c>es</c> = e flat).</summary>
    private static int ReadAlteration(string w, ref int p, int step)
    {
        int alter = 0;
        while (p + 1 < w.Length)
        {
            string two = w.Substring(p, 2);
            if (two == "is") { alter++; p += 2; continue; }
            if (two == "es") { alter--; p += 2; continue; }
            break;
        }
        // The contracted forms: aes/ees are written as/es.
        if (p < w.Length && w[p] == 's' && step is 5 or 2 && alter == 0) { alter--; p++; }
        return alter;
    }

    private static Pitch Relative(int step, int alter, int marks, St st)
    {
        int prev = st.Reference.DiatonicIndex;
        int candidate = st.Reference.Octave * 7 + step;
        while (candidate - prev > 3) candidate -= 7;
        while (prev - candidate > 3) candidate += 7;
        candidate += 7 * marks;
        return new Pitch(step, alter, (candidate - step) / 7);
    }

    private Chord? ReadChord(int from, int to, St st)
    {
        var chord = new Chord();
        var save = st.Reference;
        Pitch? first = null;

        for (int j = from; j < to; j++)
        {
            if (_t[j].Kind != K.Word || !IsNoteWord(_t[j].Text)) continue;
            int p = 0;
            int dummy = j;
            var n = ReadNote(_t[j].Text, ref p, st, ref dummy, to);
            first ??= n.Pitch;
            chord.Notes.Add(n);
        }

        if (chord.Notes.Count == 0) { st.Reference = save; return null; }

        // The next event is relative to the chord's *first* note, not its last.
        st.Reference = first!.Value;
        chord.Notes.Sort((a, b) => a.Pitch.DiatonicIndex.CompareTo(b.Pitch.DiatonicIndex));
        return chord;
    }

    /// <summary>The duration written after a chord's <c>&gt;</c>, or inherited from the previous event.</summary>
    private Duration ReadDuration(ref int i, int to, St st)
    {
        if (i < to && _t[i].Kind == K.Word && (char.IsDigit(_t[i].Text[0]) || _t[i].Text[0] == '.'))
        {
            int p = 0;
            var d = ReadDuration(_t[i].Text, ref p, st, out double scale, ref i, to);
            i++;
            return scale == 1.0 ? d : Duration.FromQuarterLength(d.QuarterLength * scale);
        }
        if (i < to && _t[i].Kind == K.Command && _t[i].Text is "breve" or "longa" or "maxima")
        {
            i++;
            st.Last = Duration.Breve;
            return Duration.Breve;
        }
        return st.Last;
    }

    /// <summary>A LilyPond duration suffix: <c>4</c>, <c>8.</c>, <c>1</c>, <c>16..</c>, <c>2*3</c>, <c>4*2/3</c>.
    /// With none written, the previous duration carries over — LilyPond's rule, and the reason a tune can be typed
    /// as <c>c4 d e f</c>. A <c>\breve</c> is a command, not a digit, so it is peeked at the token after.</summary>
    private Duration ReadDuration(string w, ref int p, St st, out double scale, ref int i, int to)
    {
        scale = 1.0;

        int num = 0;
        bool digits = false;
        while (p < w.Length && char.IsDigit(w[p])) { num = num * 10 + (w[p] - '0'); digits = true; p++; }

        int dots = 0;
        while (p < w.Length && w[p] == '.') { dots++; p++; }

        if (p < w.Length && w[p] == '*')
        {
            p++;
            double a = 0;
            while (p < w.Length && char.IsDigit(w[p])) { a = a * 10 + (w[p] - '0'); p++; }
            double b = 1;
            if (p < w.Length && w[p] == '/')
            {
                p++;
                b = 0;
                while (p < w.Length && char.IsDigit(w[p])) { b = b * 10 + (w[p] - '0'); p++; }
                if (b == 0) b = 1;
            }
            if (a > 0) scale = a / b;
        }

        Duration dur;
        if (digits && num > 0)
            dur = new Duration { Base = num, Dots = dots };
        else if (!digits && i + 1 < to && _t[i + 1].Kind == K.Command && _t[i + 1].Text is "breve" or "longa" or "maxima")
        {
            if (_t[i + 1].Text != "breve") _warn.Add($"\\{_t[i + 1].Text} engraved as a breve");
            i++;
            dur = new Duration { Base = 0, Dots = dots };
        }
        else
            dur = new Duration { Base = st.Last.Base, Dots = dots };

        st.Last = dur;
        return dur;
    }

    private Duration ReadWordDuration(string w, St st)
    {
        int p = 0;
        int i = 0, to = 0;
        var d = ReadDuration(w, ref p, st, out double scale, ref i, to);
        return scale == 1.0 ? d : Duration.FromQuarterLength(d.QuarterLength * scale);
    }

    private static bool IsNoteWord(string w)
    {
        if (w.Length == 0) return false;
        int step = "cdefgab".IndexOf(w[0]);
        if (step < 0) return false;
        if (w.Length == 1) return true;
        // 'r'/'s' aren't note letters, but 'e'/'a' start both a note and nothing else — a note word continues with
        // an accidental, an octave mark, a duration or an articulation-free end.
        char c = w[1];
        return c is 'i' or 'e' or 's' or '\'' or ',' or '!' or '?' or '*' or '.' || char.IsDigit(c);
    }

    private static bool IsPitchWord(string w) => w.Length > 0 && "cdefgab".IndexOf(w[0]) >= 0;

    private static int OctaveMarks(string w)
    {
        int marks = 0;
        foreach (char c in w)
        {
            if (c == '\'') marks++;
            else if (c == ',') marks--;
        }
        return marks;
    }

    private static Pitch Absolute(string w, int baseOctave)
    {
        int p = 0;
        int step = "cdefgab".IndexOf(w[p]);
        if (step < 0) step = 0;
        p++;
        int alter = ReadAlteration(w, ref p, step);
        return new Pitch(step, alter, baseOctave + OctaveMarks(w));
    }

    // ── Marks ───────────────────────────────────────────────────────────────

    /// <summary>LilyPond's post-note shorthands: <c>-.</c> <c>-&gt;</c> <c>--</c> <c>-_</c> <c>-!</c> <c>-^</c>.</summary>
    private static ArticulationKind? Shorthand(char c) => c switch
    {
        '.' => ArticulationKind.Staccato,
        '>' => ArticulationKind.Accent,
        '-' => ArticulationKind.Tenuto,
        '_' => ArticulationKind.Tenuto,
        '!' => ArticulationKind.Staccato,
        '^' => ArticulationKind.Marcato,
        '+' => ArticulationKind.Mordent,
        _ => null,
    };

    private static ArticulationKind? Named(string cmd) => cmd switch
    {
        "staccato" or "staccatissimo" => ArticulationKind.Staccato,
        "tenuto" or "portato" => ArticulationKind.Tenuto,
        "accent" => ArticulationKind.Accent,
        "marcato" => ArticulationKind.Marcato,
        "upbow" => ArticulationKind.UpBow,
        "downbow" => ArticulationKind.DownBow,
        "fermata" or "shortfermata" or "longfermata" or "verylongfermata" => ArticulationKind.Fermata,
        "trill" => ArticulationKind.Trill,
        "turn" or "reverseturn" => ArticulationKind.Turn,
        "prall" or "prallprall" or "upprall" or "downprall" => ArticulationKind.Mordent,
        "mordent" or "lineprall" => ArticulationKind.LowerMordent,
        "segno" => ArticulationKind.Segno,
        "coda" or "varcoda" => ArticulationKind.Coda,
        _ => null,
    };

    private static bool IsDynamic(string cmd) => cmd switch
    {
        "ppppp" or "pppp" or "ppp" or "pp" or "p" or "mp" or "mf" or "f" or "ff" or "fff" or "ffff" or "fffff"
            or "fp" or "sf" or "sff" or "sfz" or "sp" or "spp" or "rfz"
            or "cresc" or "decresc" or "dim" or "crescHairpin" or "dimHairpin"
            or "<" or ">" or "!" => true,
        _ => false,
    };

    private static bool IsIgnorable(string cmd) => cmd switch
    {
        "voiceOne" or "voiceTwo" or "voiceThree" or "voiceFour" or "oneVoice"
            or "stemUp" or "stemDown" or "stemNeutral"
            or "slurUp" or "slurDown" or "slurNeutral"
            or "tieUp" or "tieDown" or "tieNeutral"
            or "dynamicUp" or "dynamicDown" or "dynamicNeutral"
            or "noBreak" or "pageBreak" or "noPageBreak" or "allowBreak"
            or "melisma" or "melismaEnd" or "default" or "bookOutputName" => true,
        _ => false,
    };

    // ── Chord symbols ───────────────────────────────────────────────────────

    /// <summary>A <c>\chordmode</c> stream runs in parallel with the melody, so its symbols are placed by
    /// <em>time</em>: each entry is matched to the event that starts where it does.</summary>
    private void AttachChordSymbols()
    {
        foreach (var (from, to, near) in _chords)
        {
            int idx = near >= 0 && near < _voices.Count ? near : 0;
            if (idx >= _voices.Count) continue;
            var v = _voices[idx];

            var entries = ReadChordMode(from, to);
            if (entries.Count == 0) continue;

            int at = 0;
            double pos = 0;
            foreach (var tok in v.Tokens)
            {
                if (tok is not MusicalEvent ev) continue;
                while (at < entries.Count && entries[at].At < pos - 1e-6) at++;
                if (at < entries.Count && Math.Abs(entries[at].At - pos) < 1e-6)
                {
                    ev.ChordSymbol = entries[at].Text;
                    at++;
                }
                pos += EventTime(ev);
            }
        }
    }

    private List<(double At, string Text)> ReadChordMode(int from, int to)
    {
        var list = new List<(double, string)>();
        var st = new St { Last = Duration.Whole };
        double pos = 0;

        for (int i = from; i < to; i++)
        {
            if (_t[i].Kind != K.Word) continue;
            string w = _t[i].Text;
            if (w.Length == 0) continue;

            if (w[0] is 'r' or 's' && !IsNoteWord(w))
            {
                int rp = 1;
                int di = i;
                var rd = ReadDuration(w, ref rp, st, out double rs, ref di, to);
                pos += rd.QuarterLength * rs;
                continue;
            }
            if (!IsPitchWord(w)) continue;

            // root[accidental][octave][duration][:modifiers][/bass]
            int slash = w.IndexOf('/');
            string bass = slash > 0 ? w[(slash + 1)..] : "";
            string head = slash > 0 ? w[..slash] : w;

            int colon = head.IndexOf(':');
            string mods = colon > 0 ? head[(colon + 1)..] : "";
            string root = colon > 0 ? head[..colon] : head;

            int p = 0;
            int step = "cdefgab".IndexOf(root[p]);
            if (step < 0) continue;
            p++;
            int alter = ReadAlteration(root, ref p, step);
            while (p < root.Length && (root[p] == '\'' || root[p] == ',')) p++;

            int dp = p;
            int ti = i;
            var dur = ReadDuration(root, ref dp, st, out double scale, ref ti, to);

            var sb = new StringBuilder();
            sb.Append(Pitch.StepLetters[step]);
            sb.Append(alter switch { 2 => "##", 1 => "#", -1 => "b", -2 => "bb", _ => "" });
            sb.Append(Quality(mods));
            if (bass.Length > 0)
            {
                int bp = 0;
                int bstep = "cdefgab".IndexOf(bass[bp]);
                if (bstep >= 0)
                {
                    bp++;
                    int balter = ReadAlteration(bass, ref bp, bstep);
                    sb.Append('/').Append(Pitch.StepLetters[bstep])
                      .Append(balter switch { 1 => "#", -1 => "b", _ => "" });
                }
            }

            list.Add((pos, sb.ToString()));
            pos += dur.QuarterLength * scale;
        }

        return list;
    }

    /// <summary>LilyPond's chord modifiers are already how a lead sheet spells them — <c>:m7</c> reads "m7". Only
    /// the two that are pure LilyPond spelling get rewritten.</summary>
    private static string Quality(string mods)
    {
        mods = mods.Trim();
        if (mods.Length == 0) return "";
        if (mods == "5") return "5";
        if (mods.StartsWith("maj", StringComparison.Ordinal) && mods.Length == 3) return "maj7";
        return mods.Replace("^", "no").Replace(".", "");
    }

    // ── Lyrics ──────────────────────────────────────────────────────────────

    private void AttachLyrics()
    {
        foreach (var (from, to, target, near) in _lyrics)
        {
            var v = Resolve(target, near);
            if (v is null) continue;

            string raw = LyricSource(from, to, []);
            if (raw.Length == 0) continue;
            Align(v, raw, v.Verses++);
        }
    }

    private Vox? Resolve(string? target, int near)
    {
        if (target is not null)
            foreach (var v in _voices)
                if (v.Ids.Contains(target))
                    return v;
        if (near >= 0 && near < _voices.Count) return _voices[near];
        return _voices.Count > 0 ? _voices[0] : null;
    }

    /// <summary>The raw text between a lyric block's braces. Lyrics are the one place a music lexer gets in the
    /// way — <c>--</c> is a hyphen there, not a tenuto — so the source is re-read rather than re-tokenised.</summary>
    private string LyricSource(int from, int to, HashSet<string> active)
    {
        for (int i = from; i < to; i++)
        {
            if (_t[i].Kind == K.LBrace)
            {
                int close = MatchClose(i, to, K.LBrace, K.RBrace);
                return _src[_t[i].End.._t[close].Pos];
            }
            if (_t[i].Kind == K.Command && _defs.TryGetValue(_t[i].Text, out var def) && active.Add(_t[i].Text))
            {
                string inner = LyricSource(def.From, def.To, active);
                active.Remove(_t[i].Text);
                if (inner.Length > 0) return inner;
            }
        }
        return "";
    }

    /// <summary>Lines one verse's syllables up under the notes. LilyPond's rules: whitespace ends a syllable,
    /// <c>--</c> between two of them prints a hyphen and keeps them one word, <c>__</c> holds the previous syllable
    /// over the next note (an extender), a lone <c>_</c> is a note with no syllable, and a quoted run may contain
    /// spaces. Rests take no syllable.</summary>
    private static void Align(Vox v, string raw, int verse)
    {
        var events = new List<MusicalEvent>();
        foreach (var tok in v.Tokens)
            if (tok is MusicalEvent e and not Rest)
                events.Add(e);

        int at = 0;
        MusicalEvent? Next() => at < events.Count ? events[at++] : null;
        static void PadTo(int n, MusicalEvent e)
        {
            while (e.Lyrics.Count < n) e.Lyrics.Add(new LyricSyllable("", false, false));
        }

        foreach (string tok in SplitLyrics(raw))
        {
            switch (tok)
            {
                case "--":
                    // The hyphen belongs to the syllable just placed.
                    if (at > 0 && events[at - 1].Lyrics.Count > verse)
                    {
                        var last = events[at - 1].Lyrics[verse];
                        events[at - 1].Lyrics[verse] = last with { Hyphen = true };
                    }
                    continue;

                case "__":
                    if (Next() is { } held)
                    {
                        PadTo(verse, held);
                        held.Lyrics.Add(new LyricSyllable("", false, true));
                    }
                    continue;

                case "_":
                    if (Next() is { } blank)
                    {
                        PadTo(verse, blank);
                        blank.Lyrics.Add(new LyricSyllable("", false, false));
                    }
                    continue;

                default:
                    if (Next() is not { } ev) return;
                    PadTo(verse, ev);
                    ev.Lyrics.Add(new LyricSyllable(tok, false, false));
                    continue;
            }
        }
    }

    private static IEnumerable<string> SplitLyrics(string raw)
    {
        int i = 0;
        while (i < raw.Length)
        {
            char c = raw[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '"')
            {
                int close = raw.IndexOf('"', i + 1);
                if (close < 0) yield break;
                yield return raw[(i + 1)..close];
                i = close + 1;
                continue;
            }

            if (c == '\\')                                   // \skip, \set stanza = "1." — not a syllable
            {
                int j = i + 1;
                while (j < raw.Length && char.IsLetter(raw[j])) j++;
                string cmd = raw[(i + 1)..j];
                i = j;
                if (cmd == "skip")
                {
                    while (i < raw.Length && char.IsWhiteSpace(raw[i])) i++;
                    while (i < raw.Length && !char.IsWhiteSpace(raw[i])) i++;
                    yield return "_";
                }
                else
                {
                    // Skip the rest of the assignment, if any.
                    while (i < raw.Length && raw[i] != '\n' && raw[i] != '"') i++;
                    if (i < raw.Length && raw[i] == '"') { int q = raw.IndexOf('"', i + 1); i = q < 0 ? raw.Length : q + 1; }
                }
                continue;
            }

            int start = i;
            while (i < raw.Length && !char.IsWhiteSpace(raw[i])) i++;
            string tok = raw[start..i];

            if (tok is "--" or "__" or "_") { yield return tok; continue; }

            // Strip a trailing duration, which LilyPond allows a syllable to carry ("Ly4 -- rics4."). A duration is
            // digits, optionally dotted — so the dots only count as one when digits precede them. Otherwise the run
            // is punctuation, and "sky." is a word that ends a sentence.
            int p = tok.Length;
            while (p > 0 && tok[p - 1] == '.') p--;
            int digits = p;
            while (p > 0 && char.IsDigit(tok[p - 1])) p--;
            if (p < digits && p > 0) tok = tok[..p];

            if (tok.Length > 0) yield return tok;
        }
    }

    // ── Beaming ─────────────────────────────────────────────────────────────

    /// <summary>LilyPond beams by the meter rather than by how the source is spaced, so the beams are worked out
    /// after the fact. Eighths group by the half-bar in common time and by the dotted quarter in a compound one;
    /// anything shorter groups by the beat. A tuplet always beams as one group, and a manual <c>[ ]</c> beam wins.</summary>
    private void AutoBeam(Vox v, Staff staff)
    {
        if (!v.AutoBeam) return;

        var time = staff.Time;
        double meter = time.QuarterLengthPerMeasure;
        double cap = staff.ShowTime ? meter : 0;
        double pos = v.Partial > 0 ? Math.Max(0, cap - v.Partial) : 0;

        var run = new List<MusicalEvent>();
        double runStart = 0;

        void Flush()
        {
            if (run.Count >= 2)
            {
                int id = ++_beam;
                foreach (var e in run) e.BeamId = id;
            }
            run.Clear();
        }

        foreach (var tok in v.Tokens)
        {
            switch (tok)
            {
                case TimeToken tt:
                    Flush();
                    time = tt.Time;
                    meter = time.QuarterLengthPerMeasure;
                    cap = meter;
                    pos = 0;
                    break;

                case CadenzaToken ct:
                    Flush();
                    cap = ct.On ? 0 : meter;
                    pos = 0;
                    break;

                case BarToken:
                    Flush();
                    pos = 0;
                    break;

                case MusicalEvent ev:
                {
                    bool beamable = ev is not Rest && ev.Duration.IsBeamable && ev.BeamId == 0;
                    if (!beamable) Flush();
                    else
                    {
                        bool sameTuplet = run.Count > 0 && ev.TupletId != 0 && run[^1].TupletId == ev.TupletId;
                        double unit = BeamUnit(time, ev.Duration.Base);
                        if (run.Count > 0 && !sameTuplet &&
                            Math.Floor(runStart / unit + 1e-9) != Math.Floor(pos / unit + 1e-9))
                            Flush();
                        if (run.Count == 0) runStart = pos;
                        run.Add(ev);
                    }

                    pos += EventTime(ev);
                    if (cap > 0 && pos >= cap - 1e-6) { Flush(); pos = 0; }
                    break;
                }
            }
        }

        Flush();
    }

    private static double BeamUnit(TimeSignature t, int baseValue)
    {
        if (t.Denominator == 8 && t.Numerator % 3 == 0) return 1.5;              // 6/8, 9/8, 12/8 — dotted quarters
        double beat = 4.0 / t.Denominator;
        if (baseValue >= 16) return Math.Min(beat, 1.0);                          // shorter values group by the beat
        if (t.QuarterLengthPerMeasure >= 4 && t.Numerator % 2 == 0) return 2.0;   // 4/4, 2/2 — eighths in fours
        return beat;
    }

    // ── Measures ────────────────────────────────────────────────────────────

    /// <summary>The time an event actually occupies — a tuplet's members are written full-length and sound short.</summary>
    private static double EventTime(MusicalEvent ev)
    {
        double ql = ev.Duration.QuarterLength;
        if (ev.TupletNumber > 1 && ev.TupletTime > 0) ql *= (double)ev.TupletTime / ev.TupletNumber;
        return ql;
    }

    /// <summary>The clef/key/meter written before the first note <em>is</em> the staff header — so it is lifted out
    /// of the stream rather than left in it, where it would also print as a mid-tune change at bar 1.</summary>
    private static void RewindOpeningSignatures(Vox v, Staff staff)
    {
        for (int i = 0; i < v.Tokens.Count; i++)
        {
            switch (v.Tokens[i])
            {
                case MusicalEvent:
                    return;
                case KeyToken kt:
                    staff.Key = kt.Key;
                    v.Tokens.RemoveAt(i--);
                    break;
                case TimeToken tt:
                    staff.Time = tt.Time;
                    v.Tokens.RemoveAt(i--);
                    break;
                case ClefToken ct:
                    staff.Clef = ct.Clef;
                    v.Tokens.RemoveAt(i--);
                    break;
                case CadenzaToken { On: true }:
                    staff.ShowTime = false;             // free meter — print no signature, as ABC's M:none does
                    break;
            }
        }
    }

    private void BuildMeasures(Vox v, Staff staff)
    {
        var current = new Measure();
        double meter = staff.Time.QuarterLengthPerMeasure;
        double cap = staff.ShowTime ? meter : 0;              // cadenza: no meter, so nothing closes a bar but a \bar
        double acc = v.Partial > 0 ? Math.Max(0, cap - v.Partial) : 0;

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
                if (openBar != BarlineKind.Single) { current.StartBarline = openBar; openBar = BarlineKind.Single; }
                return;
            }
            current.EndBarline = end;
            if (pendingBreak) { current.SystemBreak = true; pendingBreak = false; }
            staff.Measures.Add(current);
            current = new Measure { StartBarline = openBar };
            openBar = BarlineKind.Single;
            acc = 0;
        }

        foreach (var tok in v.Tokens)
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
                    acc += EventTime(ev);
                    if (cap > 0 && acc >= cap - 1e-6) Close(BarlineKind.Single);
                    break;

                case BarToken bt:
                    switch (bt.Kind)
                    {
                        case BarlineKind.RepeatStart or BarlineKind.HeavyLight:
                            if (current.Events.Count == 0) current.StartBarline = bt.Kind;
                            else { openBar = bt.Kind; Close(BarlineKind.Single); }
                            break;
                        case BarlineKind.RepeatBoth:
                            // Back-to-back repeat. If the meter already closed the bar before it, the end-repeat has
                            // to reach back to that bar while the start-repeat still opens the next one.
                            if (current.Events.Count == 0 && staff.Measures.Count > 0)
                            {
                                staff.Measures[^1].EndBarline = BarlineKind.RepeatEnd;
                                current.StartBarline = BarlineKind.RepeatStart;
                            }
                            else
                            {
                                openBar = BarlineKind.RepeatStart;
                                Close(BarlineKind.RepeatEnd);
                            }
                            break;
                        default:
                            // A \bar may likewise arrive after the meter has already closed the bar it belongs to.
                            if (current.Events.Count == 0 && staff.Measures.Count > 0 && bt.Kind != BarlineKind.Single)
                                staff.Measures[^1].EndBarline = bt.Kind;
                            else Close(bt.Kind);
                            break;
                    }
                    break;

                case CadenzaToken ct:
                    cap = ct.On ? 0 : meter;
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
                    meter = tt.Time.QuarterLengthPerMeasure;
                    cap = meter;
                    break;

                case ClefToken:
                    _warn.Add("mid-staff clef changes are not engraved");
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

    /// <summary>
    /// Which accidentals to print. A LilyPond note name carries its own alteration — <c>fis</c> is F sharp whatever
    /// the key — so unlike ABC, the source never says "print a sharp here". That is an engraving decision, and it
    /// follows the ordinary rule: print one only where the note departs from what is already in force in the bar,
    /// which starts as the key signature and is then overridden, for that pitch, by each accidental printed.
    /// </summary>
    private static void ResolveAccidentals(Staff staff)
    {
        int fifths = staff.Key.Fifths;

        foreach (var m in staff.Measures)
        {
            if (m.KeyChange is { } kc) fifths = kc.Fifths;
            var inForce = new Dictionary<int, int>();

            foreach (var ev in m.Events)
            {
                foreach (var g in ev.Graces) Decide(g, fifths, inForce);
                switch (ev)
                {
                    case Note n: Decide(n, fifths, inForce); break;
                    case Chord c: foreach (var cn in c.Notes) Decide(cn, fifths, inForce); break;
                }
            }
        }
    }

    private static void Decide(Note n, int fifths, Dictionary<int, int> inForce)
    {
        int at = n.Pitch.DiatonicIndex;
        int current = inForce.TryGetValue(at, out int a) ? a : KeyTable.KeyAlterFor(n.Pitch.Step, fifths);

        if (n.Pitch.Alter == current)
        {
            n.Accidental = AccidentalKind.None;
            return;
        }

        inForce[at] = n.Pitch.Alter;
        n.Accidental = n.Pitch.Alter switch
        {
            >= 2 => AccidentalKind.DoubleSharp,
            1 => AccidentalKind.Sharp,
            -1 => AccidentalKind.Flat,
            <= -2 => AccidentalKind.DoubleFlat,
            _ => AccidentalKind.Natural,
        };
    }

    // ── Small parsers ───────────────────────────────────────────────────────

    private static ClefKind ParseClef(string name)
    {
        name = name.Trim().ToLowerInvariant();
        if (name.Contains("bass") || name == "f") return ClefKind.Bass;
        if (name.Contains("tenor")) return ClefKind.Tenor;
        if (name.Contains("alto") || name == "c") return ClefKind.Alto;
        return ClefKind.Treble;
    }

    private static KeySignature ParseKey(string tonic, string mode)
    {
        int p = 0;
        int step = "cdefgab".IndexOf(char.ToLowerInvariant(tonic[0]));
        if (step < 0) return KeySignature.CMajor;
        p++;
        int alter = ReadAlteration(tonic, ref p, step);
        return KeyTable.FromTonic(step, alter, mode);
    }

    /// <summary>LilyPond prints 4/4 and 2/2 with the C and ¢ symbols unless the source asks for figures, so that is
    /// what the source <em>said</em> — a <c>\numericTimeSignature</c> later in the voice takes it back.</summary>
    private static TimeSignature ParseTime(string w)
    {
        if (Ratio(w) is var (n, d) && d > 0)
        {
            if (n == 4 && d == 4) return TimeSignature.Common;
            if (n == 2 && d == 2) return TimeSignature.Cut;
            return new TimeSignature(n, d);
        }
        return new TimeSignature(4, 4);
    }

    private static (int, int) Ratio(string w)
    {
        int slash = w.IndexOf('/');
        if (slash > 0 &&
            int.TryParse(w[..slash], out int a) &&
            int.TryParse(w[(slash + 1)..], out int b))
            return (a, b);
        return (0, 0);
    }

    private static BarlineKind ParseBar(string s) => s switch
    {
        "||" => BarlineKind.Double,
        "|." or "|.|" => BarlineKind.Final,
        ".|" or ".|:" or "|:" => BarlineKind.RepeatStart,
        ":|." or ":|" or ":|]" => BarlineKind.RepeatEnd,
        ":|.|:" or ":..:" or ":|][|:" => BarlineKind.RepeatBoth,
        "[|" or "!" => BarlineKind.HeavyLight,
        _ => BarlineKind.Single,
    };

    // ── Ranges ──────────────────────────────────────────────────────────────

    /// <summary>The extent of one music expression from <paramref name="i"/>: any leading wrapper commands and
    /// their arguments, then the balanced <c>{ … }</c> / <c>&lt;&lt; … &gt;&gt;</c> group — or, for a bare
    /// <c>\name</c> reference, just the reference.</summary>
    private int ExprEnd(int i, int to)
    {
        while (i < to)
        {
            var t = _t[i];
            if (t.Kind == K.LBrace) return MatchClose(i, to, K.LBrace, K.RBrace) + 1;
            if (t.Kind == K.SimStart) return MatchClose(i, to, K.SimStart, K.SimEnd) + 1;
            if (t.Kind == K.ChordStart) return MatchClose(i, to, K.ChordStart, K.ChordEnd) + 1;

            if (t.Kind == K.Command)
            {
                if (!IsWrapper(t.Text)) return i + 1;
                i = SkipWrapperArgs(i, to);
                continue;
            }

            if (t.Kind is K.Word or K.Str) return i + 1;
            return i + 1;
        }
        return to;
    }

    private static bool IsWrapper(string cmd) => cmd switch
    {
        "relative" or "fixed" or "absolute" or "transpose" or "sequential" or "simultaneous"
            or "new" or "context" or "with" or "repeat" or "alternative" or "tuplet" or "times"
            or "grace" or "acciaccatura" or "appoggiatura" or "slashedGrace" or "afterGrace"
            or "chordmode" or "chords" or "lyricmode" or "lyrics" or "lyricsto" or "addlyrics"
            or "drummode" or "drums" or "figuremode" or "figures" or "markup" or "markuplist"
            or "score" or "book" or "bookpart" or "header" or "layout" or "midi" or "paper" => true,
        _ => false,
    };

    private int SkipWrapperArgs(int i, int to)
    {
        string cmd = _t[i].Text;
        i++;
        switch (cmd)
        {
            case "relative":
            case "fixed":
                if (i < to && _t[i].Kind == K.Word && IsPitchWord(_t[i].Text)) i++;
                break;
            case "transpose":
                for (int k = 0; k < 2 && i < to && _t[i].Kind == K.Word; k++) i++;
                break;
            case "tuplet":
            case "times":
                if (i < to && _t[i].Kind == K.Word) i++;
                if (i < to && _t[i].Kind == K.Word && char.IsDigit(_t[i].Text[0])) i++;
                break;
            case "repeat":
                for (int k = 0; k < 2 && i < to && _t[i].Kind == K.Word; k++) i++;
                break;
            case "lyricsto":
                if (i < to && _t[i].Kind is K.Str or K.Word) i++;
                break;
            case "new":
            case "context":
                if (i < to && _t[i].Kind is K.Word or K.Str) i++;
                if (i < to && _t[i].Kind == K.Eq)
                {
                    i++;
                    if (i < to && _t[i].Kind is K.Word or K.Str) i++;
                }
                if (i < to && _t[i].Kind == K.Command && _t[i].Text == "with") i = ExprEnd(i + 1, to);
                break;
        }
        return i;
    }

    private int MatchClose(int open, int to, K openKind, K closeKind)
    {
        int depth = 0;
        for (int j = open; j < to; j++)
        {
            if (_t[j].Kind == openKind) depth++;
            else if (_t[j].Kind == closeKind && --depth == 0) return j;
        }
        return to - 1;
    }

    private string? FirstString(int from, int to)
    {
        for (int i = from; i < to && i < _t.Count; i++)
            if (_t[i].Kind == K.Str)
                return _t[i].Text;
        return null;
    }

    private bool HasEvents(int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            if (_t[i].Kind == K.ChordStart) return true;
            if (_t[i].Kind != K.Word) continue;
            string w = _t[i].Text;
            if (IsNoteWord(w) || (w.Length > 0 && w[0] is 'r' or 'R' or 's')) return true;
        }
        return false;
    }

    private int CountEvents(int from, int to)
    {
        int n = 0;
        for (int i = from; i < to; i++)
            if (_t[i].Kind == K.Word && IsNoteWord(_t[i].Text))
                n++;
        return n;
    }

    // ── Lexer ───────────────────────────────────────────────────────────────

    private enum K
    {
        Command, Str, Word, Scheme, LBrace, RBrace, SimStart, SimEnd, ChordStart, ChordEnd,
        Bar, Eq, SlurOpen, SlurClose, BeamOpen, BeamClose, Tie, Artic, Dir, VoiceSep,
    }

    private readonly record struct Tok(K Kind, string Text, int Pos, int End);

    /// <summary>Blanks comments and Scheme <em>in place</em> — the lexer's positions have to keep pointing at the
    /// original source, because lyric blocks are re-read from it.</summary>
    private static string Blank(string source)
    {
        var sb = new StringBuilder(source);
        int n = sb.Length;

        for (int i = 0; i < n; i++)
        {
            char c = sb[i];

            if (c == '"')
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (sb[j] == '\\') { j++; continue; }
                    if (sb[j] == '"') { i = j; break; }
                    if (j == n - 1) i = j;
                }
                continue;
            }

            int stop = -1;
            if (c == '%' && i + 1 < n && sb[i + 1] == '{')
            {
                stop = source.IndexOf("%}", i + 2, StringComparison.Ordinal);
                stop = stop < 0 ? n : stop + 2;
            }
            else if (c == '%')
            {
                stop = source.IndexOf('\n', i);
                if (stop < 0) stop = n;
            }
            else if (c == '#' && i + 1 < n && sb[i + 1] == '(')
            {
                // A balanced Scheme expression is genuinely unreadable to us, so it goes. A bare #-atom (#red, #f,
                // #'left) does not: it is the *value* of an \override, and blanking it would leave the assignment
                // looking unterminated — the skipper would then swallow the note that follows it.
                int j = i + 1;
                int depth = 0;
                for (; j < n; j++)
                {
                    if (sb[j] == '(') depth++;
                    else if (sb[j] == ')' && --depth == 0) { j++; break; }
                }
                stop = j;
            }

            if (stop < 0) continue;
            for (int k = i; k < stop && k < n; k++)
                if (sb[k] != '\n')
                    sb[k] = ' ';
            i = stop - 1;
        }

        return sb.ToString();
    }

    private static List<Tok> Lex(string s)
    {
        var toks = new List<Tok>();
        int i = 0;

        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            switch (c)
            {
                case '{': toks.Add(new(K.LBrace, "{", i, i + 1)); i++; continue;
                case '}': toks.Add(new(K.RBrace, "}", i, i + 1)); i++; continue;
                case '=': toks.Add(new(K.Eq, "=", i, i + 1)); i++; continue;
                case '|': toks.Add(new(K.Bar, "|", i, i + 1)); i++; continue;
                case '(': toks.Add(new(K.SlurOpen, "(", i, i + 1)); i++; continue;
                case ')': toks.Add(new(K.SlurClose, ")", i, i + 1)); i++; continue;
                case '[': toks.Add(new(K.BeamOpen, "[", i, i + 1)); i++; continue;
                case ']': toks.Add(new(K.BeamClose, "]", i, i + 1)); i++; continue;
                case '~': toks.Add(new(K.Tie, "~", i, i + 1)); i++; continue;

                case '#':
                {
                    int j = i + 1;
                    while (j < s.Length && !char.IsWhiteSpace(s[j]) && s[j] is not ('{' or '}')) j++;
                    toks.Add(new(K.Scheme, s[i..j], i, j));
                    i = j;
                    continue;
                }

                case '<':
                    if (i + 1 < s.Length && s[i + 1] == '<') { toks.Add(new(K.SimStart, "<<", i, i + 2)); i += 2; }
                    else { toks.Add(new(K.ChordStart, "<", i, i + 1)); i++; }
                    continue;

                case '>':
                    if (i + 1 < s.Length && s[i + 1] == '>') { toks.Add(new(K.SimEnd, ">>", i, i + 2)); i += 2; }
                    else { toks.Add(new(K.ChordEnd, ">", i, i + 1)); i++; }
                    continue;

                case '"':
                {
                    var sb = new StringBuilder();
                    int j = i + 1;
                    while (j < s.Length && s[j] != '"')
                    {
                        if (s[j] == '\\' && j + 1 < s.Length) j++;
                        sb.Append(s[j]);
                        j++;
                    }
                    toks.Add(new(K.Str, sb.ToString(), i, Math.Min(j + 1, s.Length)));
                    i = j + 1;
                    continue;
                }

                case '\\':
                {
                    int j = i + 1;
                    if (j < s.Length && s[j] == '\\') { toks.Add(new(K.VoiceSep, "\\\\", i, j + 1)); i = j + 1; continue; }
                    if (j < s.Length && s[j] is '(' or ')')
                    {
                        // A phrasing slur: it bows over the same notes an ordinary one would.
                        toks.Add(new(s[j] == '(' ? K.SlurOpen : K.SlurClose, s[j].ToString(), i, j + 1));
                        i = j + 1;
                        continue;
                    }
                    while (j < s.Length && char.IsLetter(s[j])) j++;
                    if (j == i + 1)
                    {
                        // \< \> \! — a hairpin. Lexed as a command so it can't be mistaken for a chord bracket.
                        if (j < s.Length) { toks.Add(new(K.Command, s[j].ToString(), i, j + 1)); i = j + 1; }
                        else i++;
                        continue;
                    }
                    toks.Add(new(K.Command, s[(i + 1)..j], i, j));
                    i = j;
                    continue;
                }

                case '-':
                case '^':
                case '_':
                {
                    char n1 = i + 1 < s.Length ? s[i + 1] : '\0';
                    if (n1 is '"' or '\\')
                    {
                        toks.Add(new(K.Dir, c.ToString(), i, i + 1));
                        i++;
                        continue;
                    }
                    if (c == '-' && n1 is '.' or '>' or '-' or '_' or '!' or '^' or '+')
                    {
                        toks.Add(new(K.Artic, n1.ToString(), i, i + 2));
                        i += 2;
                        continue;
                    }
                    if (c is '^' or '_' && n1 is '.' or '>' or '-' or '!' or '+')
                    {
                        toks.Add(new(K.Artic, n1.ToString(), i, i + 2));
                        i += 2;
                        continue;
                    }
                    i++;
                    continue;
                }

                default:
                {
                    int j = i;
                    while (j < s.Length && !char.IsWhiteSpace(s[j]) && !IsBreak(s[j])) j++;
                    if (j == i) { i++; continue; }
                    toks.Add(new(K.Word, s[i..j], i, j));
                    i = j;
                    continue;
                }
            }
        }

        return toks;
    }

    /// <summary>Characters that end a word. <c>!</c> <c>?</c> <c>'</c> <c>,</c> <c>.</c> <c>*</c> <c>/</c> <c>:</c>
    /// are <em>not</em> here — they are part of a note (<c>cis'4.</c>, <c>c4*2/3</c>) or a chord name
    /// (<c>g:7</c>).</summary>
    private static bool IsBreak(char c) =>
        c is '{' or '}' or '<' or '>' or '=' or '|' or '"' or '\\' or '%' or '#'
          or '(' or ')' or '[' or ']' or '~' or '^' or '_' or '-';
}
