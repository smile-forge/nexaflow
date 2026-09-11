using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music;
using Nexaflow.Markdown.Music.LilyPond;
using Nexaflow.Markdown.Music.LilyPond.Stages;
using Nexaflow.Visuals.Text.Markdown.Music.Rendering;
using AnnotationPlacement = Nexaflow.Visuals.Text.Markdown.Music.Model.AnnotationPlacement;
using ClefKind = Nexaflow.Visuals.Text.Markdown.Music.Model.ClefKind;

namespace Nexaflow.Visuals.Text.Markdown.Music.LilyPond;

/// <summary>
/// Playing a staff's music through: every event in the order it sounds, with what is written after it hung on
/// it, and every change of key, meter and clef in the place it happens.
/// </summary>
internal sealed partial class LilyPondBuilder
{
    /// <summary>How a staff is being played, at the point the walk has reached.</summary>
    private sealed class Playing(Stave stave)
    {
        public readonly Stave Stave = stave;

        /// <summary>The event just played — what a tie, a slur or an articulation written after it belongs to.</summary>
        public Sounded? Last;

        /// <summary>The tuplet being played, and the number printed over it.</summary>
        public ISourcePart? Tuplet;
        public int TupletNumber;

        /// <summary>A beam written by hand and not yet closed.</summary>
        public List<Sounded>? Beam;

        /// <summary>Whether what is played is grace notes, which take no time and go with the next event.</summary>
        public bool Grace;
        public bool Slashed;
        public readonly List<(int Half, int Value)> Graces = [];

        public bool AutoBeam = true;
    }

    private void Play(ContentPart music, Stave stave, HashSet<string> active) =>
        Play(music, new Playing(stave), active);

    private void Play(ContentPart part, Playing playing, HashSet<string> active)
    {
        switch (part.Kind)
        {
            case LilyPondKinds.Sequential:
                Sequence(part, playing, active);
                return;

            case LilyPondKinds.Simultaneous:
                Strands(part, playing, active);
                return;

            case LilyPondKinds.Note or LilyPondKinds.Rest or LilyPondKinds.Chord or LilyPondKinds.ChordRepeat
                when IsEvent(part):
                Sound(part, playing);
                return;

            case LilyPondKinds.Tie:
                if (playing.Last is { } tied) tied.Event.TieStart = true;
                return;

            case LilyPondKinds.SlurOpen:
                if (playing.Last is { } opening) opening.Event.SlurOpen++;
                return;

            case LilyPondKinds.SlurClose:
                if (playing.Last is { } closing) closing.Event.SlurClose++;
                return;

            case LilyPondKinds.BeamOpen:
                // Written after the note it starts on, so that note is the one already in hand.
                playing.Beam = playing.Last is { } first ? [first] : [];
                return;

            case LilyPondKinds.BeamClose:
                ByHand(playing);
                return;

            case LilyPondKinds.Articulation:
                if (playing.Last is { } marked && Shorthand(part.Text) is { } mark) Mark(marked.Event, mark);
                return;

            case LilyPondKinds.Script:
                Script(part, playing);
                return;

            case LilyPondKinds.BarCheck:
                playing.Stave.Stream.Add(new Checked(part));
                return;

            case LilyPondKinds.Command:
                Command(part, playing, active);
                return;
        }
    }

    /// <summary>
    /// Music one thing after another. A <c>\repeat</c> with an <c>\alternative</c> written after it takes the
    /// alternative as its endings, which is the one place LilyPond reads two neighbours as one thing.
    /// </summary>
    private void Sequence(ContentPart group, Playing playing, HashSet<string> active)
    {
        var children = group.Children;

        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];

            if (child.Kind == LilyPondKinds.Command && CommandName(child) == @"\repeat"
                && Following(children, i) is var next and >= 0
                && children[next].Kind == LilyPondKinds.Command && CommandName(children[next]) == @"\alternative")
            {
                Repeat(child, children[next], playing, active);
                i = next;
                continue;
            }

            Play(child, playing, active);
        }
    }

    /// <summary>Where the next thing after the one at <paramref name="at"/> is, past space and comments, or -1.</summary>
    private static int Following(IReadOnlyList<ContentPart> children, int at)
    {
        for (var i = at + 1; i < children.Count; i++)
            if (children[i].Kind is not (Kinds.Space or Kinds.Comment)) return i;

        return -1;
    }

    /// <summary>
    /// Music sounding together inside one staff.
    ///
    /// <para>
    /// The engraver draws one voice to a staff, so the first strand that plays anything is what is drawn. A
    /// strand that only sets things — the <c>\global</c> of <c>&lt;&lt; \global \melody &gt;&gt;</c> — is
    /// played first, because what it sets holds for the whole staff. Words and chord names written alongside
    /// are set against this staff.
    /// </para>
    /// </summary>
    private void Strands(ContentPart group, Playing playing, HashSet<string> active)
    {
        var strands = new List<ContentPart>();

        foreach (var child in group.Children)
        {
            if (child.Kind is Kinds.Space or Kinds.Comment or LilyPondKinds.VoiceSeparator) continue;
            if (child.Role is Roles.Open or Roles.Close) continue;

            if (child.Kind == LilyPondKinds.Command && CommandName(child) is @"\new" or @"\context")
            {
                var (kind, _, _) = Head(child);
                if (Kindly(kind) == Ctx.Staff && Body(child) is { } voice)
                {
                    Named(child, playing.Stave);
                    strands.Add(voice);
                }
                else
                {
                    Context(child, playing, active);
                }

                continue;
            }

            if (child.Kind == LilyPondKinds.Command && CommandName(child) == @"\addlyrics")
            {
                if (Body(child) is { } words) _lyrics.Add(new Words(words, null, playing.Stave, _piece));
                continue;
            }

            strands.Add(child);
        }

        var drawn = strands.FirstOrDefault(strand => HasEvents(strand, active));

        foreach (var strand in strands)
            if (!ReferenceEquals(strand, drawn) && !HasEvents(strand, active)) Play(strand, playing, active);

        if (drawn is not null) Play(drawn, playing, active);
    }

    /// <summary>What a <c>\new Voice = "x" \with { … }</c> inside a staff says about that staff.</summary>
    private static void Named(ContentPart context, Stave stave)
    {
        var (_, id, label) = Head(context);
        if (id is not null) stave.Ids.Add(id);
        stave.Name ??= label;
    }

    // ── Events ──────────────────────────────────────────────────────────────

    /// <summary>A note, a rest or a chord, played — or, inside a grace, crushed in before the next event.</summary>
    private void Sound(ContentPart part, Playing playing)
    {
        var (pitches, forced) = Pitches(part);
        var written = ResolveDurations.WrittenOf(part.Node);
        var lasts = ResolveDurations.SoundsOf(part.Node);
        var (value, dots) = Value(written.Quarters);

        if (playing.Grace)
        {
            foreach (var pitch in pitches) playing.Graces.Add((pitch.DiatonicIndex, Math.Max(value, 8)));
            return;
        }

        var rest = part.Kind == LilyPondKinds.Rest ? part.Part(Roles.Name)?.Text : null;

        // R1*3 is three bars' rest and s1*2 two bars' silence, and each of them is a bar of its own.
        var bars = rest is "R" or "s" && written.Quarters > 0
            ? Math.Max(1, (int)Math.Round(lasts.Quarters / written.Quarters))
            : 1;
        var each = bars > 1 ? written : lasts;

        for (var n = 0; n < bars; n++)
        {
            var ev = new Event
            {
                Part = part,
                Heads = [.. pitches.Select(p => p.DiatonicIndex)],
                BaseValue = value,
                Dots = dots,
                IsRest = rest is not null,
                Invisible = rest == "s",
                WholeBar = rest == "R",
                Quarters = each.Quarters,
                Tuplet = playing.Tuplet,
                TupletNumber = playing.TupletNumber,
            };

            Add(playing, ev, each, pitches, forced);
        }
    }

    /// <summary>
    /// An event, onto the staff: the grace notes waiting for it go with it, and it is what comes next hangs on.
    /// </summary>
    private static void Add(Playing playing, Event ev, Duration lasts, Pitch[] pitches, bool[] forced)
    {
        ev.Graces.AddRange(playing.Graces);
        ev.GraceSlashed = playing.Slashed && playing.Graces.Count > 0;
        playing.Graces.Clear();
        playing.Slashed = false;

        var sounded = new Sounded(ev, lasts, pitches, forced) { Beamed = playing.AutoBeam };
        playing.Stave.Stream.Add(sounded);
        playing.Last = sounded;
        playing.Beam?.Add(sounded);
    }

    /// <summary>What an event sounds, lowest first, and which of its notes asked for their accidental outright.</summary>
    private static (Pitch[] Pitches, bool[] Forced) Pitches(ContentPart part)
    {
        var found = new List<(Pitch Pitch, bool Forced)>();

        switch (part.Kind)
        {
            case LilyPondKinds.Note:
                if (ResolvePitches.PitchOf(part.Node) is { } pitch)
                    found.Add((pitch, part.Part(LilyPondRoles.Force) is not null));
                break;

            case LilyPondKinds.Chord:
                foreach (var member in part.Children)
                    if (member.Kind == LilyPondKinds.Note && ResolvePitches.PitchOf(member.Node) is { } sounded)
                        found.Add((sounded, member.Part(LilyPondRoles.Force) is not null));
                break;

            case LilyPondKinds.ChordRepeat:
                found.AddRange(ResolvePitches.PitchesOf(part.Node).Select(p => (p, false)));
                break;
        }

        found.Sort((a, b) => a.Pitch.DiatonicIndex.CompareTo(b.Pitch.DiatonicIndex));
        return ([.. found.Select(f => f.Pitch)], [.. found.Select(f => f.Forced)]);
    }

    /// <summary>A beam asked for by hand closes: what it holds is one group, whatever the meter would have said.</summary>
    private static void ByHand(Playing playing)
    {
        if (playing.Beam is not { } run) return;
        playing.Beam = null;

        var beamable = run.Where(s => s.Event.Beamable).ToList();
        if (beamable.Count < 2) return;

        var group = Across(beamable.Select(s => s.Event.Part));
        foreach (var sounded in beamable)
        {
            sounded.Event.Beam = group;
            sounded.ByHand = true;
        }
    }

    /// <summary>Something put on the note before it in a direction: text above or below, or a named mark.</summary>
    private static void Script(ContentPart script, Playing playing)
    {
        if (playing.Last is not { } on) return;

        var direction = script.Part(Roles.Name)?.Text;
        var target = script.Children.LastOrDefault(c => c.Role != Roles.Name);

        switch (target?.Kind)
        {
            case LilyPondKinds.Quoted:
                Annotate(on.Event, Inside(target).Text, direction);
                return;

            case LilyPondKinds.Command when CommandName(target) is @"\markup" or @"\markuplist":
                if (FirstProse(target) is { } text) Annotate(on.Event, text.Text, direction);
                return;

            case LilyPondKinds.Command:
                if (Mark(CommandName(target)) is { } mark) Mark(on.Event, mark);
                return;
        }
    }

    private static void Annotate(Event ev, string text, string? direction)
    {
        if (text.Length > 0) ev.Annotations.Add((text, direction == "_" ? AnnotationPlacement.Below : AnnotationPlacement.Above));
    }

    private static void Mark(Event ev, (int Glyph, bool Head) mark)
    {
        if (mark.Head) ev.HeadMarks.Add(mark.Glyph);
        else ev.StaffMarks.Add(mark.Glyph);
    }

    /// <summary>The marks LilyPond spells as punctuation after a <c>-</c>, <c>^</c> or <c>_</c>.</summary>
    private static (int Glyph, bool Head)? Shorthand(string written) => written.Length != 2 ? null : written[1] switch
    {
        '.' or '!' => (Smufl.ArticStaccatoAbove, true),
        '>' => (Smufl.ArticAccentAbove, true),
        '-' or '_' => (Smufl.ArticTenutoAbove, true),
        '^' => (Smufl.ArticMarcatoAbove, true),
        '+' => (Smufl.OrnamentMordent, false),
        _ => null,
    };

    /// <summary>The marks LilyPond names, and whether each hugs the head or stands clear of the staff.</summary>
    private static (int Glyph, bool Head)? Mark(string command) => command switch
    {
        @"\staccato" or @"\staccatissimo" => (Smufl.ArticStaccatoAbove, true),
        @"\tenuto" or @"\portato" => (Smufl.ArticTenutoAbove, true),
        @"\accent" => (Smufl.ArticAccentAbove, true),
        @"\marcato" => (Smufl.ArticMarcatoAbove, true),
        @"\fermata" or @"\shortfermata" or @"\longfermata" or @"\verylongfermata" => (Smufl.FermataAbove, false),
        @"\trill" => (Smufl.OrnamentTrill, false),
        @"\turn" or @"\reverseturn" => (Smufl.OrnamentTurn, false),
        @"\prall" or @"\prallprall" or @"\upprall" or @"\downprall" => (Smufl.OrnamentMordent, false),
        @"\mordent" or @"\lineprall" => (Smufl.OrnamentLowerMordent, false),
        @"\upbow" => (Smufl.StringsUpBow, false),
        @"\downbow" => (Smufl.StringsDownBow, false),
        @"\segno" => (Smufl.Segno, false),
        @"\coda" or @"\varcoda" => (Smufl.Coda, false),
        _ => null,
    };

    // ── Commands ────────────────────────────────────────────────────────────

    private void Command(ContentPart command, Playing playing, HashSet<string> active)
    {
        var stream = playing.Stave.Stream;
        var name = CommandName(command);

        switch (name)
        {
            case @"\relative" or @"\fixed" or @"\absolute" or @"\transpose" or @"\sequential" or @"\simultaneous"
                or @"\once" or @"\temporary":
                foreach (var body in Bodies(command)) Play(body, playing, active);
                return;

            case @"\clef":
                stream.Add(new Clefed(ClefOf(Argument(command))));
                return;

            case @"\key":
                if (KeyOf(command) is { } fifths) stream.Add(new Keyed(fifths));
                return;

            case @"\time":
                if (TimeOf(command) is { } time) stream.Add(new Metered(time.Beats, time.Unit));
                return;

            case @"\numericTimeSignature" or @"\defaultTimeSignature":
                stream.Add(new Figured(name == @"\numericTimeSignature"));
                return;

            case @"\partial":
                if (LilyPondTheory.Length(Argument(command)) is { } pickup)
                    stream.Add(new Pickup(pickup.Written * pickup.Scale));
                return;

            case @"\bar":
                stream.Add(new Lined(Drawn(Argument(command)), command));
                return;

            case @"\break":
                stream.Add(new Broken());
                return;

            case @"\cadenzaOn" or @"\cadenzaOff":
                stream.Add(new Cadenza(name == @"\cadenzaOn"));
                return;

            case @"\autoBeamOff" or @"\autoBeamOn":
                playing.AutoBeam = name == @"\autoBeamOn";
                return;

            case @"\repeat":
                Repeat(command, null, playing, active);
                return;

            case @"\alternative":
                Alternatives(command, playing, active);
                return;

            case @"\tuplet" or @"\times":
                Tuplet(command, playing, active);
                return;

            case @"\grace" or @"\acciaccatura" or @"\appoggiatura" or @"\slashedGrace":
                Grace(command, playing, active);
                return;

            case @"\afterGrace":
                if (Bodies(command).FirstOrDefault() is { } main) Play(main, playing, active);
                return;

            case @"\new" or @"\context":
                Context(command, playing, active);
                return;

            case @"\addlyrics":
                if (Body(command) is { } words) _lyrics.Add(new Words(words, null, playing.Stave, _piece));
                return;

            case @"\lyricsto":
                _lyrics.Add(Lyrics(command, playing.Stave));
                return;

            case @"\chordmode" or @"\chords":
                if (Body(command) is { } changes) _chords.Add(new Changes(changes, playing.Stave, _piece));
                return;

            case @"\set":
                if (Property(command, "instrumentName") is { } label) playing.Stave.Name ??= label;
                return;

            case @"\with":
                playing.Stave.Name ??= Setting(command, "instrumentName");
                return;

            case @"\omit" or @"\hide":
                if (Argument(command).EndsWith("TimeSignature", StringComparison.Ordinal)) stream.Add(new Hidden());
                return;

            case @"\skip":
                Skip(command, playing);
                return;
        }

        if (Mark(name) is { } mark)
        {
            if (playing.Last is { } marked) Mark(marked.Event, mark);
            return;
        }

        // A variable's name: its music, played here, as many times as it is used.
        if (Reference(command) is { } called && _definitions.TryGetValue(called, out var definition) && active.Add(called))
        {
            Play(definition, playing, active);
            active.Remove(called);
        }
    }

    /// <summary>
    /// <c>\repeat volta 2 { … }</c> — the repeat bar lines an ABC writer types by hand — and the numbered endings
    /// that may follow it. <c>\repeat unfold</c> genuinely repeats the music, so it is played that many times.
    /// </summary>
    private void Repeat(ContentPart command, ContentPart? alternative, Playing playing, HashSet<string> active)
    {
        var args = command.Children.Where(c => c.Role == LilyPondRoles.Argument).Select(c => c.Text).ToList();
        var kind = args.Count > 0 ? args[0] : "volta";
        var times = args.Count > 1 && int.TryParse(args[1], out var count) ? count : 2;

        if (Body(command) is not { } body) return;

        if (kind == "unfold")
        {
            for (var n = 0; n < Math.Clamp(times, 1, 8); n++) Play(body, playing, active);
            return;
        }

        if (kind is "percent" or "tremolo")
        {
            Play(body, playing, active);
            return;
        }

        var stream = playing.Stave.Stream;
        stream.Add(new Lined("[|:", command.Part(Roles.Name)!));

        var from = stream.Count;
        Play(body, playing, active);

        // Numbered endings may be written inside the repeat, as LilyPond 2.24 does, or after it. Either way the
        // repeat goes back at the end of the first ending, not at the end of the body.
        if (stream.Skip(from).Any(p => p is Bracketed)) return;

        if (alternative is not null) Alternatives(alternative, playing, active);
        else stream.Add(new Lined(":|]", body.Part(Roles.Close) ?? (ISourcePart)command));
    }

    /// <summary>The numbered endings of a repeat, each played, the first of them going back to the start.</summary>
    private void Alternatives(ContentPart command, Playing playing, HashSet<string> active)
    {
        if (Body(command) is not { } body) return;

        // An ending is braced music, a definition used as one, or — LilyPond's own spelling since 2.24, and
        // abc2ly's — music under a \volta that says which times through it is played.
        var endings = body.Children
            .Where(c => c.Kind is LilyPondKinds.Sequential or LilyPondKinds.Simultaneous
                        || (c.Kind == LilyPondKinds.Command && (Numbered(c) || Reference(c) is not null)))
            .ToList();

        // `\alternative { g1 }`, with no braces inside, is one ending.
        if (endings.Count == 0) endings.Add(body);

        var stream = playing.Stave.Stream;

        for (var n = 0; n < endings.Count; n++)
        {
            var music = Numbered(endings[n]) ? Body(endings[n]) ?? endings[n] : endings[n];

            stream.Add(new Bracketed(Numbered(endings[n]) ? Argument(endings[n]) : $"{n + 1}", music));
            Play(music, playing, active);
            if (n < endings.Count - 1)
                stream.Add(new Lined(":|]", music.Part(Roles.Close) ?? (ISourcePart)music));
        }

        // The last ending carries on into whatever follows, and its bracket stops where it does.
        stream.Add(new Unbracketed());

        static bool Numbered(ContentPart ending) =>
            ending.Kind == LilyPondKinds.Command && CommandName(ending) == @"\volta";
    }

    /// <summary><c>\tuplet 3/2 { … }</c>: the music inside, marked as one tuplet with its number over it.</summary>
    private void Tuplet(ContentPart command, Playing playing, HashSet<string> active)
    {
        var fraction = command.Children
            .Where(c => c.Role == LilyPondRoles.Argument)
            .Select(c => LilyPondTheory.Fraction(c.Text))
            .FirstOrDefault(f => f is not null);

        // \tuplet 3/2 is three in the time of two; \times 2/3 says the same the other way round.
        var number = fraction is { } f ? (CommandName(command) == @"\tuplet" ? f.Numerator : f.Denominator) : 3;

        var (tuplet, printed) = (playing.Tuplet, playing.TupletNumber);
        (playing.Tuplet, playing.TupletNumber) = (command, number);

        foreach (var body in Bodies(command)) Play(body, playing, active);

        (playing.Tuplet, playing.TupletNumber) = (tuplet, printed);
    }

    /// <summary>Grace notes: played for their pitches, and crushed in before the event after them.</summary>
    private void Grace(ContentPart command, Playing playing, HashSet<string> active)
    {
        var was = playing.Grace;
        playing.Grace = true;

        foreach (var body in Bodies(command)) Play(body, playing, active);

        playing.Grace = was;
        playing.Slashed = CommandName(command) is @"\acciaccatura" or @"\slashedGrace";
    }

    /// <summary><c>\skip 4</c>: time that passes with nothing printed in it.</summary>
    private static void Skip(ContentPart command, Playing playing)
    {
        var lasts = ResolveDurations.SoundsOf(command.Node);
        var (value, dots) = Value(ResolveDurations.WrittenOf(command.Node).Quarters);

        var ev = new Event
        {
            Part = command,
            IsRest = true,
            Invisible = true,
            BaseValue = value,
            Dots = dots,
            Quarters = lasts.Quarters,
        };

        Add(playing, ev, lasts, [], []);
    }

    // ── Small readers ───────────────────────────────────────────────────────

    /// <summary>A clef's name, as the engraver draws it. An octave mark — <c>treble_8</c> — is not drawn.</summary>
    private static ClefKind ClefOf(string name)
    {
        var clef = name.Trim().ToLowerInvariant();

        if (clef.Contains("bass") || clef.StartsWith('f') || clef.Contains("baritone")) return ClefKind.Bass;
        if (clef.Contains("tenor")) return ClefKind.Tenor;
        if (clef.Contains("alto") || clef == "c" || clef.StartsWith("c_") || clef.Contains("soprano")) return ClefKind.Alto;
        return ClefKind.Treble;
    }

    /// <summary>Where a <c>\key</c> sits round the circle of fifths: its tonic and its mode.</summary>
    private static int? KeyOf(ContentPart command)
    {
        var tonic = command.Children.FirstOrDefault(c => c.Role == LilyPondRoles.Argument && c.Kind == LilyPondKinds.Note);
        var mode = command.Children.FirstOrDefault(c => c.Role == LilyPondRoles.Argument && c.Kind == LilyPondKinds.Command);

        if (tonic?.Part(LilyPondRoles.NoteName)?.Text is not { } name || LilyPondTheory.Name(name) is not { } key)
            return null;

        return Keys.Fifths(key.Step, key.Alter, mode is null ? "major" : CommandName(mode).TrimStart('\\'));
    }

    /// <summary>A <c>\time</c>'s fraction.</summary>
    private static (int Beats, int Unit)? TimeOf(ContentPart command) =>
        command.Children
            .Where(c => c.Role == LilyPondRoles.Argument)
            .Select(c => LilyPondTheory.Fraction(c.Text))
            .FirstOrDefault(f => f is not null) is { } fraction
            ? (fraction.Numerator, fraction.Denominator)
            : null;

    /// <summary>
    /// A <c>\bar</c>'s string in the spelling the engraver draws — ABC's, where each mark is a stroke: <c>|</c>
    /// thin, <c>[</c> and <c>]</c> thick, <c>:</c> the dots of a repeat. LilyPond's <c>.</c> is the thick stroke.
    /// </summary>
    private static string Drawn(string bar) => bar switch
    {
        "||" => "||",
        "|." or "|.|" => "|]",
        ".|" => "[|",
        ".|:" or "[|:" => "[|:",
        "|:" => "|:",
        ":|." or ":|]" => ":|]",
        ":|" => ":|",
        ":|.|:" or ":|][|:" => ":|]|:",
        ":..:" or ":|.:" or ":.|.:" => ":||:",
        "" => "",
        _ => "|",
    };
}
