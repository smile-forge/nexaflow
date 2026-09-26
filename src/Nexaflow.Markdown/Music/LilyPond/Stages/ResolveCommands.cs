using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Music.LilyPond.Stages;

/// <summary>
/// Says what each command's arguments mean for its name (<see cref="LilyPondCommandNode"/>): the clef, key, meter and pickup it
/// sets, the bar line it draws, the number a tuplet prints, what a repeat does and how often, what a <c>\new</c> makes and what it
/// is called, the instrument a staff is named for, the ending a <c>\volta</c> labels, the voice a <c>\lyricsto</c> sings to, and
/// whether an <c>\omit</c> hides the meter.
///
/// <para>
/// All of it is true of the command wherever it is played, so it is said here, once. Where each takes effect — which bar a
/// <c>\key</c> changes, which staff a <c>\new</c> is — depends on where the music is played, and is the builder's walk.
/// </para>
/// </summary>
public sealed class ResolveCommands : IAstStage
{
    public string Name => "lilypond:commands";

    public ContentNode Run(ContentNode tree) =>
        AstRewrite.Each(tree, node => node.Kind == LilyPondKinds.Command && node is not LilyPondEventNode ? Said(node) : node);

    private static ContentNode Said(ContentNode command)
    {
        var part = ContentPart.Of(command);
        var name = part.Part(Roles.Name)?.Text ?? "";

        LilyPondCommandNode? said = name switch
        {
            @"\clef" => new(command) { Clef = ClefOf(Argument(part)) },
            @"\key" => KeyOf(part) is { } fifths ? new(command) { Fifths = fifths } : null,
            @"\time" => Fraction(part) is { } time ? new(command) { Meter = (time.Numerator, time.Denominator) } : null,
            @"\partial" => LilyPondTheory.Length(Argument(part)) is { } pickup ? new(command) { Pickup = pickup.Written * pickup.Scale } : null,
            @"\bar" => new(command) { Bar = Drawn(Argument(part)) },
            @"\tuplet" or @"\times" => new(command) { TupletNumber = Fraction(part) is { } f ? (name == @"\tuplet" ? f.Numerator : f.Denominator) : 3 },
            @"\repeat" => Repeated(command, part),
            @"\new" or @"\context" => Head(command, part),
            @"\set" => Property(part, "instrumentName") is { } named ? new(command) { Instrument = named } : null,
            @"\with" => Setting(part, "instrumentName") is { } with ? new(command) { Instrument = with } : null,
            @"\volta" => new(command) { Label = Argument(part) },
            @"\lyricsto" => new(command) { Id = LilyPondText.Said(part.Part(LilyPondRoles.Argument)) ?? "" },
            @"\omit" or @"\hide" => Argument(part).EndsWith("TimeSignature", StringComparison.Ordinal) ? new(command) { HidesMeter = true } : null,
            _ => null,
        };

        return said ?? command;
    }

    /// <summary>What a command's first argument says — a word, or a quoted string — or nothing.</summary>
    private static string Argument(ContentPart command) =>
        LilyPondText.Said(command.Children.FirstOrDefault(c => c.Role == LilyPondRoles.Argument)) ?? "";

    /// <summary>The first of a command's arguments written as a fraction — a <c>\time</c>'s, a tuplet's.</summary>
    private static (int Numerator, int Denominator)? Fraction(ContentPart command) =>
        command.Children
            .Where(c => c.Role == LilyPondRoles.Argument)
            .Select(c => LilyPondTheory.Fraction(c.Text))
            .FirstOrDefault(f => f is not null);

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

        return Keys.Fifths(key.Step, key.Alter, mode is null ? "major" : (mode.Part(Roles.Name)?.Text ?? "").TrimStart('\\'));
    }

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

    /// <summary>
    /// <c>\repeat volta 2 { … }</c>: what the repeat does — <c>volta</c> where it says nothing — and how many times.
    /// </summary>
    private static LilyPondCommandNode Repeated(ContentNode command, ContentPart part)
    {
        var args = part.Children.Where(c => c.Role == LilyPondRoles.Argument).Select(c => c.Text).ToList();

        return new LilyPondCommandNode(command)
        {
            Repeat = args.Count > 0 ? args[0] : "volta",
            Times = args.Count > 1 && int.TryParse(args[1], out var count) ? count : 2,
        };
    }

    /// <summary>A <c>\new</c>'s context, the name it is given, and the instrument name its <c>\with</c> sets.</summary>
    private static LilyPondCommandNode Head(ContentNode command, ContentPart part)
    {
        var args = part.Children.Where(c => c.Role == LilyPondRoles.Argument).ToList();
        var kind = args.Count > 0 ? LilyPondText.Said(args[0]) ?? "" : "";

        var assigned = part.Children.Any(c => c.Role == LilyPondRoles.Assign);

        return new LilyPondCommandNode(command)
        {
            Context = Kindly(kind),
            Id = assigned && args.Count > 1 ? LilyPondText.Said(args[1]) : null,
            Instrument = args.Where(a => a.Kind == LilyPondKinds.Command && a.Part(Roles.Name)?.Text == @"\with")
                             .Select(with => Setting(with, "instrumentName"))
                             .FirstOrDefault(name => name is not null),
        };
    }

    private static LilyPondContext Kindly(string context) => context switch
    {
        "Staff" or "RhythmicStaff" or "DrumStaff" or "TabStaff" or "Voice" or "NullVoice" or "VaticanaStaff"
            or "MensuralStaff" or "CueVoice" => LilyPondContext.Staff,
        "StaffGroup" or "ChoirStaff" or "PianoStaff" or "GrandStaff" or "Score" or "ChoirStaffGroup" => LilyPondContext.Group,
        "Lyrics" => LilyPondContext.Lyrics,
        "ChordNames" => LilyPondContext.Chords,
        _ => LilyPondContext.Other,
    };

    /// <summary>
    /// What a setting inside a block is set to — <c>instrumentName = "Soprano"</c> inside a <c>\with</c> — or
    /// null where it is not set.
    /// </summary>
    private static string? Setting(ContentPart block, string property)
    {
        foreach (var inner in Written(block))
        {
            if (inner.Kind != LilyPondKinds.Assignment) continue;
            if (LilyPondText.Said(inner.Part(Roles.Name)) is not { } name || !name.EndsWith(property, StringComparison.Ordinal)) continue;
            if (inner.Part(LilyPondRoles.Value) is { } value && Text(value) is { } text) return text;
        }

        return null;
    }

    /// <summary>What <c>\set Staff.instrumentName = "Flute"</c> sets a property to.</summary>
    private static string? Property(ContentPart set, string property)
    {
        var args = set.Children.Where(c => c.Role == LilyPondRoles.Argument).ToList();
        if (args.Count < 2 || !args[0].Text.EndsWith(property, StringComparison.Ordinal)) return null;

        return Text(args[1]);
    }

    /// <summary>What a value says: a quoted string's text, a word, or the first string of a markup.</summary>
    internal static string? Text(ContentPart value) => value.Kind switch
    {
        LilyPondKinds.Quoted or LilyPondKinds.Word => LilyPondText.Said(value),
        _ => Written(value).FirstOrDefault(p => p.Kind == LilyPondKinds.Quoted) is { } first ? LilyPondText.Said(first) : null,
    };

    /// <summary>Everything written, outermost first, leaving out what a stage worked out.</summary>
    private static IEnumerable<ContentPart> Written(ContentPart part)
    {
        if (part.Derived) yield break;

        yield return part;
        foreach (var child in part.Children)
            foreach (var inner in Written(child))
                yield return inner;
    }
}
