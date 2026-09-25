using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Music.LilyPond;

/// <summary>
/// What LilyPond's words, strings, names and syllables say, read off the pieces the parser made of them — so nothing drawing
/// a score has to take a string apart to find out.
/// </summary>
public static class LilyPondText
{
    /// <summary>
    /// The characters a quoted string says, in order, each the piece it is written as: a letter, or an escape — <c>\"</c>,
    /// <c>\\</c> — standing for the one character it writes.
    /// </summary>
    public static IReadOnlyList<ContentPart> Letters(ContentPart quoted) =>
        [.. quoted.Children.Where(part => part.Kind is LilyPondKinds.Letter or LilyPondKinds.Escape)];

    /// <summary>What one letter of a string says: itself, or for an escape the character its backslash escapes.</summary>
    public static string Says(ContentPart letter) =>
        letter.Kind == LilyPondKinds.Escape && letter.Text.Length > 1 ? letter.Text[1..] : letter.Text;

    /// <summary>What a word or a quoted string says — or null for anything else.</summary>
    public static string? Said(ContentPart? part) => part?.Kind switch
    {
        LilyPondKinds.Quoted => string.Concat(Letters(part).Select(Says)),
        LilyPondKinds.Word => part.Text,
        _ => null,
    };

    /// <summary>
    /// The variable a command calls on — <c>\melody</c>, or <c>\"voice1"</c> in quotes, which is how a name holding a digit is
    /// written — or null where the command is not one.
    /// </summary>
    public static string? Called(ContentPart command)
    {
        if (command.Kind != LilyPondKinds.Command || command.Part(Roles.Name) is not { } name) return null;

        if (name.Kind == LilyPondKinds.Quoted) return Said(name);

        // A command given nothing but its name, less the backslash it is written with.
        return command.Children.Count == 1 && name.Text.Length > 1 ? name.Text[1..] : null;
    }

    /// <summary>
    /// What a syllable sings: its words, less any duration written after them, with a <c>_</c> or a <c>~</c> in them being the
    /// space it joins two words on one note with.
    /// </summary>
    public static string Sung(ContentPart syllable)
    {
        var words = syllable.Children.Count == 0
            ? syllable.Text
            : syllable.Children.FirstOrDefault(part => part.Role != LilyPondRoles.Duration)?.Text ?? string.Empty;

        return words.Replace('_', ' ').Replace('~', ' ');
    }

    /// <summary>
    /// What kind of chord a chord's name says, as a chord symbol spells it: <c>m7</c>, <c>maj7</c> for <c>maj</c>, a <c>^</c>
    /// leaving a note out as <c>no</c>, the dots between modifiers dropped — nothing for a plain major chord.
    /// </summary>
    public static string Quality(ContentPart chordName) => chordName.Part(LilyPondRoles.Quality)?.Text switch
    {
        null or "" => "",
        "maj" => "maj7",
        var modifiers => modifiers.Replace("^", "no").Replace(".", ""),
    };
}
