using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Music.Abc.Stages;

/// <summary>
/// Says what each information field's value means for the letter it is written under (<see cref="AbcFieldNode"/>): the key and
/// clef of a <c>K:</c>, the meter of an <c>M:</c> and the sign it was written as, the unit note length of an <c>L:</c>, and the
/// voice, name and clef of a <c>V:</c> — on a line of its own or inline in the music.
///
/// <para>
/// Read off the words the parser split the value into — a key, figures, <c>key=value</c> settings — so nothing here takes
/// characters apart. First, because everything after it asks what the fields say: what is in force on a line, what a length
/// multiplies, how a tuplet counts. Said once here, none of them reads a field again.
/// </para>
/// </summary>
public sealed class ResolveFields : IAstStage
{
    public string Name => "abc:fields";

    public ContentNode Run(ContentNode tree) =>
        tree.Kind != AbcKinds.Tune
            ? tree
            : AstRewrite.Each(tree, node => node.Kind is AbcKinds.Field or AbcKinds.InlineField ? Field(node) : node);

    private static ContentNode Field(ContentNode field)
    {
        if (field.Part(Roles.Name)?.Text is not { Length: > 0 } name) return field;

        var written = name[0];
        var letter = char.ToUpperInvariant(written);
        var words = Words(field.Part(AbcRoles.Value));

        return new AbcFieldNode(
            field,
            written,
            ClefIn(letter, words),
            letter == 'K' ? Fifths(words) : null,
            letter == 'M' ? Meter(words) : null,
            letter == 'M' ? Sign(words) : MeterSign.Figures,
            letter == 'L' ? Unit(words) : null,
            letter == 'V' && words.Count > 0 && words[0].Print() is { Length: > 0 } voice ? voice : null,
            letter == 'V' ? Setting(words, "name") ?? Setting(words, "nm") : null);
    }

    /// <summary>The words a value was split into, without the space between them — none for a value held as prose.</summary>
    private static IReadOnlyList<ContentNode> Words(ContentNode? value) =>
        value is { IsLeaf: false } ? [.. value.Children.Where(child => child.Role != Roles.Trivia)] : [];

    /// <summary>
    /// How far round the circle of fifths the key a <c>K:</c> opens with sits: its tonic, the sharps or flats on it, and its
    /// mode — written straight after it, or as the word after it. Null where it opens with no key: <c>K:none</c>, or a field
    /// that only sets a clef.
    /// </summary>
    private static int? Fifths(IReadOnlyList<ContentNode> words)
    {
        if (words.Count == 0 || words[0] is not { Kind: AbcKinds.Key } key) return null;

        var step = Pitch.Letters.IndexOf(char.ToUpperInvariant(key.Part(Roles.Name)?.Text is { Length: > 0 } tonic ? tonic[0] : 'C'));
        var alter = (key.Part(AbcRoles.Accidental)?.Text ?? "").Sum(mark => mark == '#' ? 1 : -1);

        var mode = key.Part(AbcRoles.Mode)?.Text
                   ?? (words.Count > 1 && words[1] is { Kind: AbcKinds.Word } next && Keys.IsMode(next.Text) ? next.Text : "");

        return Keys.Fifths(step, alter, mode);
    }

    /// <summary>
    /// The meter an <c>M:</c> sets — beats over a beat unit — or null for a free one. <c>C</c> is common time and <c>C|</c> cut
    /// time; figures count every number before the stroke, so <c>(2+3)/8</c> is five eighths to the bar.
    /// </summary>
    private static (int Beats, int Unit)? Meter(IReadOnlyList<ContentNode> words)
    {
        if (words is [{ Kind: AbcKinds.Word } only]) return only.Text switch { "C" => (4, 4), "C|" => (2, 2), _ => null };

        // The figure written with a stroke is the meter; a number standing beside it — `M:4 3/4` — is not part of it.
        if ((words.FirstOrDefault(word => word.Kind == AbcKinds.Figures && Stroked(word))
             ?? words.FirstOrDefault(word => word.Kind == AbcKinds.Figures)) is not { } figures) return null;

        var (above, below) = Split(figures);
        var beats = above.Sum();

        return beats > 0 && below.FirstOrDefault() is var unit and > 0 ? (beats, unit) : null;
    }

    /// <summary>The sign a meter was written as, where it was written as one rather than as figures.</summary>
    private static MeterSign Sign(IReadOnlyList<ContentNode> words) => words switch
    {
        [{ Kind: AbcKinds.Word, Text: "C" }] => MeterSign.Common,
        [{ Kind: AbcKinds.Word, Text: "C|" }] => MeterSign.Cut,
        _ => MeterSign.Figures,
    };

    /// <summary>The unit note length an <c>L:</c> sets, in quarter notes.</summary>
    private static Duration? Unit(IReadOnlyList<ContentNode> words)
    {
        if (words.FirstOrDefault(word => word.Kind == AbcKinds.Figures) is not { } figures) return null;

        var (above, below) = Split(figures);

        return above.FirstOrDefault() is var numerator and > 0 && below.FirstOrDefault() is var denominator and > 0
            ? Duration.Of(numerator * 4, denominator)
            : null;
    }

    /// <summary>The numbers written above a figure's stroke and below it.</summary>
    private static (List<int> Above, List<int> Below) Split(ContentNode figures)
    {
        var above = new List<int>();
        var below = new List<int>();
        var under = false;

        foreach (var piece in figures.Children)
        {
            if (piece.Kind == Kinds.Token && piece.Text == "/") under = true;
            else if (piece.Kind == AbcKinds.Number && int.TryParse(piece.Text, out var number)) (under ? below : above).Add(number);
        }

        return (above, below);
    }

    /// <summary>Whether a figure is written with a stroke, as a meter's is.</summary>
    private static bool Stroked(ContentNode figures) =>
        figures.Children.Any(piece => piece.Kind == Kinds.Token && piece.Text == "/");

    /// <summary>What a <c>key=value</c> setting of the given name is set to, or null where none is — or where its quote was never closed.</summary>
    private static string? Setting(IReadOnlyList<ContentNode> words, string key)
    {
        foreach (var word in words)
            if (word.Kind == AbcKinds.Setting && string.Equals(word.Part(Roles.Name)?.Text, key, StringComparison.OrdinalIgnoreCase))
                return Said(word);

        return null;
    }

    /// <summary>What a setting is set to: a word, or what is between its quotes.</summary>
    private static string? Said(ContentNode setting) => setting.Part(AbcRoles.Value) switch
    {
        { IsLeaf: true } word => word.Text,
        { } quoted when quoted.Part(Roles.Close) is not null => quoted.Part(Roles.Body)?.Text ?? "",
        _ => null,
    };

    /// <summary>
    /// The clef a field asks for, or null. <c>clef=</c> is optional on <c>K:</c> and <c>V:</c> (<c>K:F bass</c> is
    /// <c>K:F clef=bass</c>), so a bare name counts there too — after a voice's name, on a <c>V:</c>; elsewhere only an
    /// explicit <c>clef=</c> does.
    /// </summary>
    private static ClefKind? ClefIn(char letter, IReadOnlyList<ContentNode> words)
    {
        var voice = letter == 'V';
        var bare = voice || letter == 'K';

        for (var at = 0; at < words.Count; at++)
        {
            var named = words[at] switch
            {
                { Kind: AbcKinds.Setting } setting when string.Equals(setting.Part(Roles.Name)?.Text, "clef", StringComparison.OrdinalIgnoreCase) => Said(setting),
                { Kind: AbcKinds.Word } word when bare && !(voice && at == 0) => word.Text,
                _ => null,
            };

            if (named is null) continue;

            var name = named.Trim('"').ToLowerInvariant();
            if (name.StartsWith("bass", StringComparison.Ordinal)) return ClefKind.Bass;
            if (name.StartsWith("alto", StringComparison.Ordinal)) return ClefKind.Alto;
            if (name.StartsWith("tenor", StringComparison.Ordinal)) return ClefKind.Tenor;
            if (name.StartsWith("treble", StringComparison.Ordinal)) return ClefKind.Treble;
        }

        return null;
    }
}
