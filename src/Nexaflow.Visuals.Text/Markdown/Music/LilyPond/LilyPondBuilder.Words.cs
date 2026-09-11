using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music;
using Nexaflow.Markdown.Music.LilyPond;
using Nexaflow.Markdown.Music.LilyPond.Stages;

namespace Nexaflow.Visuals.Text.Markdown.Music.LilyPond;

/// <summary>
/// The words around the music: the lyrics sung under it, the chord names over it, and the header above it —
/// each naming the characters it was written with, so each can be selected where it is drawn.
/// </summary>
internal sealed partial class LilyPondBuilder
{
    // ── Lyrics ──────────────────────────────────────────────────────────────

    /// <summary>Every block of words, sung to the voice it names or the staff it was written beside.</summary>
    private void Sing()
    {
        foreach (var words in _lyrics)
        {
            var stave = (words.Target is { } id ? _staves.FirstOrDefault(s => s.Ids.Contains(id)) : null)
                        ?? words.Near
                        ?? _staves.FirstOrDefault(s => s.Piece == words.Piece);
            if (stave is null) continue;

            var syllables = new List<ContentPart>();
            Syllables(words.Music, syllables, []);
            if (syllables.Count > 0) Sing(stave, syllables, stave.Verses++);
        }
    }

    /// <summary>A block's syllables and the marks between them, in order, through any definitions it uses.</summary>
    private void Syllables(ContentPart part, List<ContentPart> into, HashSet<string> active)
    {
        switch (part.Kind)
        {
            case LilyPondKinds.Syllable or LilyPondKinds.LyricMark or LilyPondKinds.Quoted:
                into.Add(part);
                return;

            case LilyPondKinds.Command:
                switch (CommandName(part))
                {
                    case @"\skip":
                        into.Add(part);
                        return;

                    case @"\set" or @"\override" or @"\once" or @"\markup":
                        return;
                }

                if (Reference(part) is { } called && _definitions.TryGetValue(called, out var definition) && active.Add(called))
                {
                    Syllables(definition, into, active);
                    active.Remove(called);
                    return;
                }

                break;
        }

        foreach (var child in part.Children)
            if (child.Role is not (LilyPondRoles.Argument or Roles.Name)) Syllables(child, into, active);
    }

    /// <summary>
    /// One verse, under a staff's notes. A syllable lands on the next note that can take one: not a rest, and not
    /// a note a tie or a slur carries on from the one before, which LilyPond holds the syllable over by itself.
    /// A <c>--</c> joins a syllable to the next with a hyphen, a <c>__</c> draws a line under the notes the
    /// syllable is held over, and a <c>_</c> passes a note by.
    /// </summary>
    private static void Sing(Stave stave, List<ContentPart> syllables, int verse)
    {
        var notes = new List<(Event Event, bool Held)>();
        var tied = false;
        var slurs = 0;

        foreach (var sounded in stave.Stream.OfType<Sounded>())
        {
            var ev = sounded.Event;
            if (ev.IsRest) { tied = false; continue; }

            notes.Add((ev, tied || slurs > 0));
            slurs = Math.Max(0, slurs + ev.SlurOpen - ev.SlurClose);
            tied = ev.TieStart;
        }

        var at = 0;
        var extending = false;
        Event? sung = null;

        // The next note a syllable can land on, drawing the line of a held syllable under any it passes.
        int Next(ContentPart? mark)
        {
            while (at < notes.Count && notes[at].Held)
            {
                if (extending && mark is not null) notes[at].Event.Lyrics.Add((verse, "", false, true, mark));
                at++;
            }

            return at < notes.Count ? at++ : -1;
        }

        ContentPart? line = null;

        foreach (var piece in syllables)
        {
            if (piece.Kind == LilyPondKinds.LyricMark)
            {
                switch (piece.Text)
                {
                    case "--":
                        if (sung is not null && sung.Lyrics.Count > 0 && sung.Lyrics[^1].Verse == verse)
                            sung.Lyrics[^1] = sung.Lyrics[^1] with { Hyphen = true };
                        continue;

                    case "__":
                        extending = true;
                        line = piece;
                        continue;

                    default:
                        if (Next(line) is var passed and >= 0 && extending && line is not null)
                            notes[passed].Event.Lyrics.Add((verse, "", false, true, line));
                        continue;
                }
            }

            if (piece.Kind == LilyPondKinds.Command)
            {
                Next(line);
                continue;
            }

            if (Next(line) is not (var landed and >= 0)) return;

            extending = false;
            line = null;
            sung = notes[landed].Event;

            var text = piece.Kind == LilyPondKinds.Quoted ? Inside(piece).Text : Sung(piece.Text);
            sung.Lyrics.Add((verse, text, false, false, piece));
        }
    }

    /// <summary>
    /// A syllable as it is printed: without the duration a syllable may carry — <c>Ly4</c> — but with a full stop
    /// that ends a sentence. A duration is digits, dotted or not, so dots count as one only after digits. An
    /// underscore or a tilde inside a word is a space.
    /// </summary>
    private static string Sung(string written)
    {
        var end = written.Length;
        while (end > 0 && written[end - 1] == '.') end--;

        var digits = end;
        while (end > 0 && char.IsAsciiDigit(written[end - 1])) end--;

        var text = end < digits && end > 0 ? written[..end] : written;
        return text.Replace('_', ' ').Replace('~', ' ');
    }

    // ── Chord names ─────────────────────────────────────────────────────────

    /// <summary>
    /// Every line of chord names, each name set over the note that starts when it does. A chord line runs beside
    /// the music rather than inside it, so time is the only thing the two have in common.
    /// </summary>
    private void Name()
    {
        foreach (var changes in _chords)
        {
            var stave = changes.Near ?? _staves.FirstOrDefault(s => s.Piece == changes.Piece);
            if (stave is null) continue;

            var names = new List<(Duration At, string Text, ContentPart Part)>();
            Names(changes.Music, names, Duration.Zero, []);

            var events = stave.Stream.OfType<Sounded>().ToList();
            var next = 0;

            foreach (var (at, text, part) in names)
            {
                while (next < events.Count && events[next].Start < at) next++;
                if (next >= events.Count || events[next].Start != at) continue;

                events[next].Event.ChordSymbol = text;
                events[next].Event.ChordPart = part;
                next++;
            }
        }
    }

    /// <summary>The chord names in a chord line, each with when it starts, returning when the line ends.</summary>
    private Duration Names(ContentPart part, List<(Duration At, string Text, ContentPart Part)> into, Duration now,
                           HashSet<string> active)
    {
        switch (part.Kind)
        {
            case LilyPondKinds.ChordName:
                into.Add((now, Spelled(part), part));
                return now + ResolveDurations.SoundsOf(part.Node);

            case LilyPondKinds.Rest:
                return now + ResolveDurations.SoundsOf(part.Node);

            case LilyPondKinds.Command when CommandName(part) == @"\skip":
                return now + ResolveDurations.SoundsOf(part.Node);

            case LilyPondKinds.Command when Reference(part) is { } called
                                           && _definitions.TryGetValue(called, out var definition) && active.Add(called):
                now = Names(definition, into, now, active);
                active.Remove(called);
                return now;
        }

        foreach (var child in part.Children)
            if (child.Role is not (LilyPondRoles.Argument or Roles.Name)) now = Names(child, into, now, active);

        return now;
    }

    /// <summary>
    /// A chord's name spelled the way a lead sheet spells it. LilyPond's modifiers are mostly that already —
    /// <c>:m7</c> reads "m7" — and only the two that are LilyPond's own spelling are rewritten.
    /// </summary>
    private static string Spelled(ContentPart name)
    {
        var (step, alter) = LilyPondTheory.Name(name.Part(LilyPondRoles.NoteName)?.Text ?? "") ?? (0, 0);
        var spelled = $"{Pitch.Letters[step]}{Accidental(alter)}";

        var quality = name.Part(LilyPondRoles.Quality)?.Text ?? "";
        var slash = quality.IndexOf('/');
        var modifiers = (slash >= 0 ? quality[..slash] : quality).TrimStart(':').Trim();
        var bass = slash >= 0 ? quality[(slash + 1)..].TrimStart('+') : "";

        spelled += modifiers switch
        {
            "" => "",
            "maj" => "maj7",
            _ => modifiers.Replace("^", "no").Replace(".", ""),
        };

        var letters = new string([.. bass.TakeWhile(char.IsAsciiLetterLower)]);
        if (LilyPondTheory.Name(letters) is { } low) spelled += $"/{Pitch.Letters[low.Step]}{Accidental(low.Alter)}";

        return spelled;
    }

    private static string Accidental(int alter) => alter switch
    {
        2 => "##",
        1 => "#",
        -1 => "b",
        -2 => "bb",
        _ => "",
    };

    // ── The header ──────────────────────────────────────────────────────────

    /// <summary>Every header field, in the order written, and the top-level markup that stands for a title.</summary>
    private readonly List<(string Field, ContentPart Value)> _fields = [];
    private MusicHeader.Prose? _markup;

    private void Heading(ContentPart header)
    {
        if (Body(header) is not { } block) return;

        foreach (var field in block.Children)
            if (field.Kind == LilyPondKinds.Assignment && NameOf(field) is { } name
                && field.Part(LilyPondRoles.Value) is { } value)
                _fields.Add((name.ToLowerInvariant(), value));
    }

    /// <summary>
    /// The header mapped onto the page by where LilyPond prints each field: the title block centred, the poet or
    /// meter at the top left, the composer at the top right with the opus after it.
    /// </summary>
    private MusicHeader Header()
    {
        MusicHeader.Prose? title = null, composer = null, origin = null, rhythm = null;
        string? source = null;
        var subtitles = new List<MusicHeader.Prose>();
        var footer = new List<MusicHeader.Prose>();

        foreach (var (field, value) in _fields)
        {
            if (Prose(value) is not { } prose || prose.Text.Trim().Length == 0) continue;

            switch (field)
            {
                case "title": title ??= prose; break;
                case "subtitle" or "subsubtitle" or "dedication" or "instrument": subtitles.Add(prose); break;
                case "composer": composer ??= prose; break;
                case "opus" or "arranger": origin ??= prose; break;
                case "poet" or "meter": rhythm ??= prose; break;
                case "source": source ??= prose.Text; break;
                case "copyright": footer.Add(prose); break;
            }
        }

        return new MusicHeader
        {
            Title = title ?? _markup,
            Subtitles = subtitles,
            Composer = composer,
            Origin = origin,
            Rhythm = rhythm,
            Source = source,
            Footer = footer,
        };
    }

    /// <summary>
    /// A value as prose, naming the characters it is written with where it can: a quoted string's own text, a
    /// word, or the first string of a markup.
    /// </summary>
    private static MusicHeader.Prose? Prose(ContentPart value) => value.Kind switch
    {
        LilyPondKinds.Quoted => Inside(value),
        LilyPondKinds.Word => new MusicHeader.Prose(value.Text, value),
        _ => FirstProse(value),
    };

    /// <summary>The first string written inside something — a markup's text.</summary>
    private static MusicHeader.Prose? FirstProse(ContentPart part) =>
        Written(part).FirstOrDefault(p => p.Kind == LilyPondKinds.Quoted) is { } first ? Inside(first) : null;

    /// <summary>
    /// A quoted string's text, naming the characters between its quotes — which is what lets it be selected a
    /// letter at a time. Where an escape makes the text differ from its characters, it names the whole string.
    /// </summary>
    private static MusicHeader.Prose Inside(ContentPart quoted)
    {
        var inner = quoted.Text.Length >= 2 ? quoted.Text[1..^1] : "";
        var text = inner.Replace("\\\"", "\"").Replace("\\\\", "\\");

        return new MusicHeader.Prose(text, text == inner ? new SourceSpan(quoted.Start + 1, inner.Length) : quoted);
    }
}
