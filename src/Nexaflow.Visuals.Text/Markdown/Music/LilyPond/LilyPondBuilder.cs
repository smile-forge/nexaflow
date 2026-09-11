using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music;
using Nexaflow.Markdown.Music.LilyPond;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Music.Rendering;
using ClefKind = Nexaflow.Visuals.Text.Markdown.Music.Model.ClefKind;

namespace Nexaflow.Visuals.Text.Markdown.Music.LilyPond;

/// <summary>
/// LilyPond's reading: the music played through, staff by staff, as rows of bars of events for the engraver
/// every notation shares.
///
/// <para>
/// More is left to do here than ABC leaves its builder, and not by choice. ABC writes its bar lines and its
/// beams; LilyPond implies both from the meter, and implies which accidentals print from the key and the bar.
/// All three depend on where a note is <em>played</em> — a definition's notes are played wherever it is used,
/// in whatever meter that place is in — so they cannot be hung on the tree, which holds one copy of the notes.
/// They are worked out here, as the music is played through, which is when they are known.
/// </para>
/// <para>
/// What is the same wherever a note is played — how long it lasts, what it sounds — the pipeline has already
/// hung on it, and this only reads it off.
/// </para>
/// </summary>
internal sealed partial class LilyPondBuilder : MusicBuilder
{
    private readonly (int Start, int Length)? _shownAsWritten;

    /// <param name="ly">The music, as it is written.</param>
    /// <param name="width">How much room it has to lay itself out in.</param>
    /// <param name="shownAsWritten">
    /// A stretch to show as the characters written rather than read as music — the piece being edited.
    /// </param>
    /// <param name="spacing">How much air to leave between things, or null for what the engraver normally uses.</param>
    public LilyPondBuilder(string ly, double width, Brush ink, double pixelsPerDip,
                           (int Start, int Length)? shownAsWritten = null, ScoreSpacing? spacing = null)
        : base(ly, width, ink, pixelsPerDip, spacing) =>
        _shownAsWritten = shownAsWritten;

    /// <summary>Reads and engraves LilyPond in one call.</summary>
    public static Laid Build(string ly, double width, Brush ink, double pixelsPerDip,
                             (int Start, int Length)? shownAsWritten = null, ScoreSpacing? spacing = null) =>
        new LilyPondBuilder(ly, width, ink, pixelsPerDip, shownAsWritten, spacing).Lay();

    protected override Tune ReadTune()
    {
        var reading = ContentReading.Of(LilyPondPipeline.Read(Source, _shownAsWritten));

        Pieces(reading.Root);
        var rows = Rows();
        Sing();
        Name();

        return new Tune(rows, Header(), reading);
    }

    // ── What the source holds ───────────────────────────────────────────────

    /// <summary>What each name was defined as, by the name.</summary>
    private readonly Dictionary<string, ContentPart> _definitions = new(StringComparer.Ordinal);

    /// <summary>Every staff found, in the order found.</summary>
    private readonly List<Stave> _staves = [];

    /// <summary>Every block of words, and every line of chord names, set against a staff once all are played.</summary>
    private readonly List<Words> _lyrics = [];
    private readonly List<Changes> _chords = [];

    /// <summary>Which piece of music the walk is in — each written at the top of the file is one.</summary>
    private int _piece;

    /// <summary>One staff, as it is played through.</summary>
    private sealed class Stave
    {
        public int Piece;

        /// <summary>Its place among its piece's staves, which is what says which of them sound together.</summary>
        public int Number;

        public string? Name;

        /// <summary>What it can be named by — <c>\new Voice = "melody"</c> — for words sung to it.</summary>
        public List<string> Ids = [];

        /// <summary>What playing it met, in the order it met it.</summary>
        public List<Played> Stream = [];

        /// <summary>How many verses have been sung to it so far.</summary>
        public int Verses;
    }

    /// <summary>A block of words, the voice it names, and the staff it was written beside.</summary>
    private sealed record Words(ContentPart Music, string? Target, Stave? Near, int Piece);

    /// <summary>A line of chord names, and the staff it was written beside.</summary>
    private sealed record Changes(ContentPart Music, Stave? Near, int Piece);

    // ── What playing a staff meets ──────────────────────────────────────────

    private abstract record Played;

    /// <summary>An event, and what deciding its bar, its beam and its accidentals needs to know about it.</summary>
    private sealed record Sounded(Event Event, Duration Lasts, Pitch[] Pitches, bool[] Forced) : Played
    {
        /// <summary>When it starts, from the start of its staff.</summary>
        public Duration Start { get; set; }

        /// <summary>Where it starts in its bar.</summary>
        public Duration At { get; set; }

        /// <summary>Whether a beam was asked for by hand, which the meter's beaming leaves alone.</summary>
        public bool ByHand { get; set; }

        /// <summary>Whether the meter may beam it at all — <c>\autoBeamOff</c> says not.</summary>
        public bool Beamed { get; set; } = true;
    }

    /// <summary>A <c>|</c>, which checks where a bar ends and names the line that ends it.</summary>
    private sealed record Checked(ISourcePart Part) : Played;

    /// <summary>A bar line somebody wrote — <c>\bar "||"</c>, a repeat — in the spelling the engraver draws.</summary>
    private sealed record Lined(string Drawn, ISourcePart Part) : Played;

    private sealed record Keyed(int Fifths) : Played;

    private sealed record Metered(int Beats, int Unit) : Played;

    /// <summary><c>\numericTimeSignature</c>, or <c>\defaultTimeSignature</c> taking it back.</summary>
    private sealed record Figured(bool Numeric) : Played;

    private sealed record Clefed(ClefKind Clef) : Played;

    /// <summary><c>\partial</c>: how much of a bar is left from here.</summary>
    private sealed record Pickup(Duration Length) : Played;

    /// <summary><c>\cadenzaOn</c>, where nothing but a written bar line ends a bar.</summary>
    private sealed record Cadenza(bool On) : Played;

    /// <summary><c>\break</c>: the next bar starts a line.</summary>
    private sealed record Broken : Played;

    /// <summary><c>\omit Staff.TimeSignature</c>: the meter is kept and not printed.</summary>
    private sealed record Hidden : Played;

    /// <summary>A numbered ending starts here.</summary>
    private sealed record Bracketed(string Label, ISourcePart Part) : Played;


    /// <summary>The last numbered ending stops here, and its bracket with it.</summary>
    private sealed record Unbracketed : Played;

    // ── The pieces, and their staves ────────────────────────────────────────

    /// <summary>
    /// Finds what the source holds: its definitions first — one may be used above where it is written — then the
    /// music written at the top of it, each a piece, each played through staff by staff.
    /// </summary>
    private void Pieces(ContentPart root)
    {
        foreach (var item in root.Children)
            if (item.Kind == LilyPondKinds.Assignment && NameOf(item) is { } name
                && item.Part(LilyPondRoles.Value) is { } value)
                _definitions[name] = value;

        var pieces = new List<ContentPart>();

        foreach (var item in root.Children)
        {
            if (item.Kind is LilyPondKinds.Sequential or LilyPondKinds.Simultaneous or LilyPondKinds.Chord
                or LilyPondKinds.Note or LilyPondKinds.Rest)
            {
                pieces.Add(item);
                continue;
            }

            if (item.Kind != LilyPondKinds.Command) continue;

            switch (CommandName(item))
            {
                case @"\header":
                    Heading(item);
                    continue;

                case @"\markup" or @"\markuplist":
                    _markup ??= FirstProse(item);
                    continue;

                case @"\version" or @"\include" or @"\language" or @"\paper" or @"\layout" or @"\midi":
                    continue;

                default:
                    pieces.Add(item);
                    continue;
            }
        }

        // A file of definitions and no music is a fragment meant to be included somewhere. What a reader of it
        // is looking at is the definition with the most music in it — the melody, not the settings block.
        if (pieces.Count == 0 && Richest() is { } richest) pieces.Add(richest);

        Stave? last = null;

        foreach (var piece in pieces)
        {
            // `music \addlyrics { … }` sings the words to the music before them, which is the staff it made.
            if (piece.Kind == LilyPondKinds.Command && CommandName(piece) == @"\addlyrics")
            {
                if (Body(piece) is { } words) _lyrics.Add(new Words(words, null, last, _piece));
                continue;
            }

            _piece++;
            var before = _staves.Count;
            Structure(piece, []);

            // Music with no \new Staff around it is itself the staff.
            if (_staves.Count == before && HasEvents(piece, [])) Play(piece, NewStave(), []);
            if (_staves.Count > before) last = _staves[^1];
        }
    }

    /// <summary>
    /// Walks the scaffolding around the music — scores, staff groups, <c>&lt;&lt; &gt;&gt;</c>, definitions that
    /// hold staves — making a staff of every <c>\new Staff</c> it finds. It goes no further into a staff than
    /// that: <see cref="Play(ContentPart, Stave, HashSet{string})"/> does.
    /// </summary>
    private void Structure(ContentPart part, HashSet<string> active)
    {
        if (part.Kind is LilyPondKinds.Sequential or LilyPondKinds.Simultaneous)
        {
            foreach (var child in part.Children)
            {
                if (child.Kind == LilyPondKinds.Command && CommandName(child) == @"\addlyrics")
                {
                    if (Body(child) is { } words) _lyrics.Add(new Words(words, null, Latest(), _piece));
                    continue;
                }

                Structure(child, active);
            }

            return;
        }

        if (part.Kind != LilyPondKinds.Command) return;

        switch (CommandName(part))
        {
            case @"\new" or @"\context":
                Context(part, null, active);
                return;

            case @"\header":
                Heading(part);
                return;

            case @"\chords" or @"\chordmode":
                if (Body(part) is { } changes) _chords.Add(new Changes(changes, Latest(), _piece));
                return;

            case @"\lyricsto":
                _lyrics.Add(Lyrics(part, Latest()));
                return;

            case @"\score" or @"\book" or @"\bookpart" or @"\relative" or @"\fixed" or @"\absolute" or @"\transpose"
                or @"\sequential" or @"\simultaneous":
                foreach (var body in Bodies(part)) Structure(body, active);
                return;
        }

        // A definition that is itself scaffolding: `music = << \new Staff … >>`.
        if (Reference(part) is { } called && _definitions.TryGetValue(called, out var definition) && active.Add(called))
        {
            Structure(definition, active);
            active.Remove(called);
        }
    }

    /// <summary>
    /// A <c>\new</c> context: a staff to play, a group of staves to walk, or words and chord names to set against
    /// a staff. Inside a staff's music, a <c>\new Voice</c> is more of that same staff.
    /// </summary>
    private void Context(ContentPart command, Playing? inside, HashSet<string> active)
    {
        var (kind, id, label) = Head(command);
        var body = Body(command);

        switch (Kindly(kind))
        {
            case Ctx.Staff when inside is not null:
                if (id is not null) inside.Stave.Ids.Add(id);
                inside.Stave.Name ??= label;
                if (body is not null) Play(body, inside, active);
                return;

            case Ctx.Staff:
                var stave = NewStave();
                stave.Name = label;
                if (id is not null) stave.Ids.Add(id);
                if (body is not null) Play(body, stave, active);
                return;

            case Ctx.Group when inside is null && body is not null:
                Structure(body, active);
                return;

            case Ctx.Lyrics when body is not null:
                _lyrics.Add(Lyrics(body, inside?.Stave ?? Latest()));
                return;

            case Ctx.Chords when body is not null:
                _chords.Add(new Changes(body, inside?.Stave ?? Latest(), _piece));
                return;
        }
    }

    private enum Ctx { Staff, Group, Lyrics, Chords, Other }

    private static Ctx Kindly(string context) => context switch
    {
        "Staff" or "RhythmicStaff" or "DrumStaff" or "TabStaff" or "Voice" or "NullVoice" or "VaticanaStaff"
            or "MensuralStaff" or "CueVoice" => Ctx.Staff,
        "StaffGroup" or "ChoirStaff" or "PianoStaff" or "GrandStaff" or "Score" or "ChoirStaffGroup" => Ctx.Group,
        "Lyrics" => Ctx.Lyrics,
        "ChordNames" => Ctx.Chords,
        _ => Ctx.Other,
    };

    /// <summary>A <c>\new</c>'s context, the name it is given, and the instrument name its <c>\with</c> sets.</summary>
    private static (string Kind, string? Id, string? Label) Head(ContentPart command)
    {
        var args = command.Children.Where(c => c.Role == LilyPondRoles.Argument).ToList();
        var kind = args.Count > 0 && args[0].Kind != LilyPondKinds.Command ? Unquoted(args[0].Text) : "";

        var assigned = command.Children.Any(c => c.Role == LilyPondRoles.Assign);
        var id = assigned && args.Count > 1 && args[1].Kind != LilyPondKinds.Command ? Unquoted(args[1].Text) : null;

        var label = args.Where(a => a.Kind == LilyPondKinds.Command && CommandName(a) == @"\with")
                        .Select(with => Setting(with, "instrumentName"))
                        .FirstOrDefault(name => name is not null);

        return (kind, id, label);
    }

    /// <summary>Words, and the voice a <c>\lyricsto</c> names for them.</summary>
    private Words Lyrics(ContentPart body, Stave? near) =>
        body.Kind == LilyPondKinds.Command && CommandName(body) == @"\lyricsto"
            ? new Words(Body(body) ?? body, Unquoted(body.Part(LilyPondRoles.Argument)?.Text), near, _piece)
            : new Words(body, null, near, _piece);

    private Stave NewStave()
    {
        var stave = new Stave { Piece = _piece, Number = _staves.Count(s => s.Piece == _piece) };
        _staves.Add(stave);
        return stave;
    }

    /// <summary>The staff most recently found in this piece — what words written beside it are sung to.</summary>
    private Stave? Latest() => _staves.LastOrDefault(s => s.Piece == _piece);

    /// <summary>The definition with the most music in it, or null where none has any.</summary>
    private ContentPart? Richest() =>
        _definitions.Values
            .Select(value => (Value: value, Events: Written(value).Count(IsEvent)))
            .Where(d => d.Events > 0)
            .OrderByDescending(d => d.Events)
            .Select(d => d.Value)
            .FirstOrDefault();

    /// <summary>Whether playing this would sound anything, through any definitions it uses.</summary>
    private bool HasEvents(ContentPart part, HashSet<string> active)
    {
        foreach (var inner in Written(part))
        {
            if (IsEvent(inner)) return true;
            if (Reference(inner) is not { } called || !_definitions.TryGetValue(called, out var definition)
                || !active.Add(called)) continue;

            var found = HasEvents(definition, active);
            active.Remove(called);
            if (found) return true;
        }

        return false;
    }

    // ── Reading the tree ────────────────────────────────────────────────────

    /// <summary>A note, a rest, a chord or a repeated chord that is played, rather than handed to a command.</summary>
    private static bool IsEvent(ContentPart part) =>
        part.Kind is LilyPondKinds.Note or LilyPondKinds.Rest or LilyPondKinds.Chord or LilyPondKinds.ChordRepeat
        && part.Role is not (LilyPondRoles.Argument or LilyPondRoles.Note);

    /// <summary>Everything written, outermost first, leaving out what a stage worked out.</summary>
    private static IEnumerable<ContentPart> Written(ContentPart part)
    {
        if (part.Derived) yield break;

        yield return part;
        foreach (var child in part.Children)
            foreach (var inner in Written(child))
                yield return inner;
    }

    private static string CommandName(ContentPart command) => command.Part(Roles.Name)?.Text ?? "";

    private static ContentPart? Body(ContentPart command) => command.Part(Roles.Body);

    private static IEnumerable<ContentPart> Bodies(ContentPart command) =>
        command.Children.Where(c => c.Role == Roles.Body);

    /// <summary>The first thing a command was handed, as written, quotes taken off.</summary>
    private static string Argument(ContentPart command) =>
        Unquoted(command.Children.FirstOrDefault(c => c.Role == LilyPondRoles.Argument)?.Print());

    /// <summary>The name a command uses, where it is a variable's — <c>\melody</c>, <c>\"voice1"</c>.</summary>
    private static string? Reference(ContentPart command)
    {
        if (command.Kind != LilyPondKinds.Command || command.Children.Count != 1) return null;

        var name = CommandName(command);
        if (name.Length < 2) return null;

        return name[1] == '"' ? Unquoted(name[1..]) : name[1..];
    }

    /// <summary>The name a definition defines.</summary>
    private static string? NameOf(ContentPart assignment) =>
        assignment.Part(Roles.Name) is { } name ? Unquoted(name.Print()) : null;

    private static string Unquoted(string? text) =>
        text is { Length: >= 2 } quoted && quoted[0] == '"' && quoted[^1] == '"' ? quoted[1..^1] : text ?? "";

    /// <summary>
    /// What a setting inside a block is set to — <c>instrumentName = "Soprano"</c> inside a <c>\with</c> — or
    /// null where it is not set.
    /// </summary>
    private static string? Setting(ContentPart block, string property)
    {
        foreach (var inner in Written(block))
        {
            if (inner.Kind != LilyPondKinds.Assignment) continue;
            if (NameOf(inner) is not { } name || !name.EndsWith(property, StringComparison.Ordinal)) continue;
            if (inner.Part(LilyPondRoles.Value) is { } value && Prose(value) is { } prose) return prose.Text;
        }

        return null;
    }

    /// <summary>What <c>\set Staff.instrumentName = "Flute"</c> sets a property to.</summary>
    private static string? Property(ContentPart set, string property)
    {
        var args = set.Children.Where(c => c.Role == LilyPondRoles.Argument).ToList();
        if (args.Count < 2 || !args[0].Text.EndsWith(property, StringComparison.Ordinal)) return null;

        return Prose(args[1])?.Text;
    }

    /// <summary>
    /// The stretch of source a run of parts covers, as one part in its own right — which is also what makes a
    /// beam or a bar something the whole of can be selected.
    /// </summary>
    private static ISourcePart? Across(IEnumerable<ISourcePart> parts)
    {
        var start = int.MaxValue;
        var end = int.MinValue;

        foreach (var part in parts)
        {
            start = Math.Min(start, part.Start);
            end = Math.Max(end, part.End());
        }

        return start > end ? null : new SourceSpan(start, end - start);
    }
}
