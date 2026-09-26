using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Music.Abc.Stages;

/// <summary>
/// Says what each information field's value means for the letter it is written under (<see cref="AbcFieldNode"/>): the key and
/// clef of a <c>K:</c>, the meter of an <c>M:</c> and the sign it was written as, the unit note length of an <c>L:</c>, and the
/// voice, name and clef of a <c>V:</c> — on a line of its own or inline in the music.
///
/// <para>
/// First, because everything after it asks what the fields say: what is in force on a line, what a length multiplies, how a
/// tuplet counts. Said once here, none of them reads a field's characters again.
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
        var value = field.Part(AbcRoles.Value)?.Text ?? "";

        return new AbcFieldNode(
            field,
            written,
            ClefIn(letter, value),
            letter == 'K' ? AbcTheory.Fifths(value) : null,
            letter == 'M' ? AbcTheory.Meter(value) : null,
            letter == 'M' ? Sign(value) : MeterSign.Figures,
            letter == 'L' ? AbcTheory.UnitLength(value) : null,
            letter == 'V' && Voice(value) is { Length: > 0 } voice ? voice : null,
            letter == 'V' ? VoiceName(value) : null);
    }

    /// <summary>The sign a meter was written as, where it was written as one rather than as figures.</summary>
    private static MeterSign Sign(string value) => value.Trim() switch
    {
        "C" => MeterSign.Common,
        "C|" => MeterSign.Cut,
        _ => MeterSign.Figures,
    };

    /// <summary>A <c>V:</c> value's voice: its first word.</summary>
    private static string Voice(string value) => value.Trim().Split([' ', '\t'], 2)[0];

    /// <summary>The name a <c>V:</c> asks its voice to be labelled with — <c>V:1 clef=treble name="Soprano"</c>.</summary>
    private static string? VoiceName(string value)
    {
        var at = value.IndexOf("name=", StringComparison.OrdinalIgnoreCase);
        if (at < 0) at = value.IndexOf("nm=", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;

        var said = value[(value.IndexOf('=', at) + 1)..].TrimStart();
        if (said.StartsWith('"'))
        {
            var close = said.IndexOf('"', 1);
            return close > 0 ? said[1..close] : null;
        }

        var word = said.Split([' ', '\t'], 2)[0].Trim();
        return word.Length > 0 ? word : null;
    }

    /// <summary>
    /// The clef a field asks for, or null. <c>clef=</c> is optional on <c>K:</c> and <c>V:</c> (<c>K:F bass</c> is
    /// <c>K:F clef=bass</c>), so a bare name counts there too; elsewhere only an explicit <c>clef=</c> does.
    /// </summary>
    private static ClefKind? ClefIn(char letter, string value)
    {
        var voice = letter == 'V';
        var bare = voice || letter == 'K';

        foreach (var word in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Skip(voice ? 1 : 0))
        {
            var written = word.StartsWith("clef=", StringComparison.OrdinalIgnoreCase);
            if (!written && !bare) continue;

            var name = (written ? word[5..] : word).Trim('"').ToLowerInvariant();
            if (name.StartsWith("bass", StringComparison.Ordinal)) return ClefKind.Bass;
            if (name.StartsWith("alto", StringComparison.Ordinal)) return ClefKind.Alto;
            if (name.StartsWith("tenor", StringComparison.Ordinal)) return ClefKind.Tenor;
            if (name.StartsWith("treble", StringComparison.Ordinal)) return ClefKind.Treble;
        }

        return null;
    }
}
