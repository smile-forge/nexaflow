using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music;
using Nexaflow.Markdown.Music.LilyPond;

using Nexaflow.Visuals.Text.Markdown.Music.Rendering;


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
                if (playing.Last is { } marked && part.Node is MusicMarkNode shorthand) Marking(marked.Event, shorthand.Mark);
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
                if ((child.Node as LilyPondCommandNode)?.Context == LilyPondContext.Staff && Body(child) is { } voice)
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
        var said = context.Node as LilyPondCommandNode;
        if (said?.Id is { } id) stave.Ids.Add(id);
        stave.Name ??= said?.Instrument;
    }

    // ── Events ──────────────────────────────────────────────────────────────

    /// <summary>A note, a rest or a chord, played — or, inside a grace, crushed in before the next event.</summary>
    private void Sound(ContentPart part, Playing playing)
    {
        var (pitches, forced) = Pitches(part);
        var read = part.Node as LilyPondEventNode;
        var written = read?.WrittenAs ?? Duration.Zero;
        var lasts = read?.Lasts ?? Duration.Zero;
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
                if (Played(part) is [var pitch]) found.Add((pitch, part.Part(LilyPondRoles.Force) is not null));
                break;

            case LilyPondKinds.Chord:
                foreach (var member in part.Children)
                    if (member.Kind == LilyPondKinds.Note && Played(member) is [var sounded])
                        found.Add((sounded, member.Part(LilyPondRoles.Force) is not null));
                break;

            case LilyPondKinds.ChordRepeat:
                found.AddRange(Played(part).Select(p => (p, false)));
                break;
        }

        found.Sort((a, b) => a.Pitch.DiatonicIndex.CompareTo(b.Pitch.DiatonicIndex));
        return ([.. found.Select(f => f.Pitch)], [.. found.Select(f => f.Forced)]);

        static IReadOnlyList<Pitch> Played(ContentPart part) => (part.Node as LilyPondEventNode)?.Pitches ?? [];
    }

    /// <summary>A beam asked for by hand closes: what it holds is one group, whatever the meter would have said.</summary>
    private static void ByHand(Playing playing)
    {
        if (playing.Beam is not { } run) return;
        playing.Beam = null;

        var beamable = run.Where(s => s.Event.Beamable).ToList();
        if (beamable.Count < 2) return;

        var group = PartRun.Of(beamable.Select(s => s.Event.Part));
        foreach (var sounded in beamable)
        {
            sounded.Event.Beam = group;
            sounded.ByHand = true;
        }
    }

    /// <summary>Something put on the note before it in a direction: words above or below it, or a mark.</summary>
    private static void Script(ContentPart script, Playing playing)
    {
        if (playing.Last is not { } on) return;

        if (script.Node is MusicAnnotationNode said)
        {
            on.Event.Annotations.Add((said.Said, said.Placement ?? AnnotationPlacement.Above));
            return;
        }

        if (script.Children.LastOrDefault(c => c.Role != Roles.Name)?.Node is MusicMarkNode mark) Marking(on.Event, mark.Mark);
    }

    // ── Commands ────────────────────────────────────────────────────────────

    private void Command(ContentPart command, Playing playing, HashSet<string> active)
    {
        var stream = playing.Stave.Stream;
        var name = CommandName(command);
        var said = command.Node as LilyPondCommandNode;

        switch (name)
        {
            case @"\relative" or @"\fixed" or @"\absolute" or @"\transpose" or @"\sequential" or @"\simultaneous"
                or @"\once" or @"\temporary":
                foreach (var body in Bodies(command)) Play(body, playing, active);
                return;

            case @"\clef":
                stream.Add(new Clefed(said?.Clef ?? ClefKind.Treble));
                return;

            case @"\key":
                if (said?.Fifths is { } fifths) stream.Add(new Keyed(fifths));
                return;

            case @"\time":
                if (said?.Meter is { } time) stream.Add(new Metered(time.Beats, time.Unit));
                return;

            case @"\numericTimeSignature" or @"\defaultTimeSignature":
                stream.Add(new Figured(name == @"\numericTimeSignature"));
                return;

            case @"\partial":
                if (said?.Pickup is { } pickup) stream.Add(new Pickup(pickup));
                return;

            case @"\bar":
                stream.Add(new Lined(said?.Bar ?? "|", command));
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
                if (said?.Instrument is { } label) playing.Stave.Name ??= label;
                return;

            case @"\with":
                playing.Stave.Name ??= said?.Instrument;
                return;

            case @"\omit" or @"\hide":
                if (said?.HidesMeter == true) stream.Add(new Hidden());
                return;

            case @"\skip":
                Skip(command, playing);
                return;
        }

        if (command.Node is MusicMarkNode marked)
        {
            if (playing.Last is { } last) Marking(last.Event, marked.Mark);
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
        var said = command.Node as LilyPondCommandNode;
        var kind = said?.Repeat ?? "volta";
        var times = said?.Times ?? 2;

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

            stream.Add(new Bracketed(Numbered(endings[n]) ? (endings[n].Node as LilyPondCommandNode)?.Label ?? "" : $"{n + 1}", music));
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
        var number = (command.Node as LilyPondCommandNode)?.TupletNumber ?? 3;

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
        var read = command.Node as LilyPondEventNode;
        var lasts = read?.Lasts ?? Duration.Zero;
        var (value, dots) = Value((read?.WrittenAs ?? Duration.Zero).Quarters);

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
}
