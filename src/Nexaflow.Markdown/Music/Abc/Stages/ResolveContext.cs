using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Music.Abc.Stages;

/// <summary>
/// Hangs the key, the meter, the unit note length and the voice in force on every line of music.
///
/// <para>
/// None of it is written on the line. All of it is written above it — possibly a long way above it,
/// possibly changed halfway down, possibly set inline in the middle of a bar — so it is worked out once,
/// by walking the tune in the order it was written, rather than looked up again by everything that needs
/// it. That is the shape every context has: a fact about the descent, not about the piece.
/// </para>
/// <para>
/// The first stage, because nothing after it can do its job without the answer. What a length suffix
/// multiplies depends on <c>L:</c>; what a bare <c>F</c> sounds depends on <c>K:</c>; how long a bar is
/// depends on <c>M:</c>; and which staff a line belongs to depends on <c>V:</c>.
/// </para>
/// </summary>
public sealed class ResolveContext : IAstStage
{
    public string Name => "abc:context";

    public ContentNode Run(ContentNode tree)
    {
        if (tree.Kind != AbcKinds.Tune) return tree;

        var context = AbcContext.Default;
        var explicitUnit = false;

        var lines = new List<ContentNode>(tree.Children.Count);
        var moved = false;

        foreach (var line in tree.Children)
        {
            // A music line is told what is in force as it starts. An inline field further along changes
            // it from there, which the stages reading this handle as they walk.
            if (line.Kind == AbcKinds.Line)
            {
                var told = Told(line, context);
                moved |= !ReferenceEquals(told, line);
                lines.Add(told);

                context = Inline(line, context, ref explicitUnit);
                continue;
            }

            lines.Add(line);
            if (line.Kind == AbcKinds.Field) context = Field(line, context, ref explicitUnit);
        }

        return moved ? tree.With(lines) : tree;
    }

    /// <summary>The line, with what is in force as it starts.</summary>
    private static ContentNode Told(ContentNode line, AbcContext context) =>
        line.Saying(
            (AbcKinds.Text, AbcRoles.Context, $"{context.Fifths}"),
            (AbcKinds.Text, AbcRoles.Length, context.Unit.ToString()),
            (AbcKinds.Text, AbcRoles.Duration, $"{context.Beats}/{context.BeatUnit}"),
            (AbcKinds.Text, AbcRoles.Value, context.Voice));

    /// <summary>What is in force after a field line.</summary>
    private static AbcContext Field(ContentNode field, AbcContext context, ref bool explicitUnit)
    {
        if (field.Part(Roles.Name)?.Text is not { Length: 2 } name) return context;
        var value = field.Part(AbcRoles.Value)?.Text ?? "";

        return Set(name[0], value, context, ref explicitUnit);
    }

    /// <summary>What is in force after the inline fields written along a line.</summary>
    private static AbcContext Inline(ContentNode line, AbcContext context, ref bool explicitUnit)
    {
        foreach (var piece in line.Children)
        {
            if (piece.Kind != AbcKinds.InlineField) continue;
            if (piece.Part(Roles.Name)?.Text is not { Length: 2 } name) continue;

            context = Set(name[0], piece.Part(AbcRoles.Value)?.Text ?? "", context, ref explicitUnit);
        }

        return context;
    }

    private static AbcContext Set(char letter, string value, AbcContext context, ref bool explicitUnit)
    {
        switch (char.ToUpperInvariant(letter))
        {
            case 'K':
                return context with { Fifths = AbcTheory.Fifths(value) ?? context.Fifths };

            case 'M':
                if (AbcTheory.Meter(value) is not { } meter) return context;

                // A meter sets the unit note length as well, unless somebody said otherwise: ABC's own
                // rule is an eighth in anything under three quarters to the bar and a quarter above it.
                // Once an L: has been written the meter stops having an opinion.
                return context with
                {
                    Beats = meter.Beats,
                    BeatUnit = meter.Unit,
                    Unit = explicitUnit ? context.Unit : AbcTheory.UnitFor(meter.Beats, meter.Unit),
                };

            case 'L':
                if (AbcTheory.UnitLength(value) is not { } unit) return context;
                explicitUnit = true;
                return context with { Unit = unit };

            case 'V':
                var id = value.Split([' ', '\t'], 2)[0].Trim();
                return id.Length == 0 ? context : context with { Voice = id };

            default:
                return context;
        }
    }

    // ── Reading the answer back ─────────────────────────────────────────────

    /// <summary>What was in force where this line starts.</summary>
    public static AbcContext Of(ContentNode line) => new(
        int.TryParse(line.Said(AbcRoles.Context), out var fifths) ? fifths : 0,
        Beats(line.Said(AbcRoles.Duration)).Beats,
        Beats(line.Said(AbcRoles.Duration)).Unit,
        line.Said(AbcRoles.Length) is { } unit ? AbcLength.Parse(unit) : AbcContext.Default.Unit,
        line.Said(AbcRoles.Value) ?? "");

    private static (int Beats, int Unit) Beats(string? text)
    {
        if (text is null) return (AbcContext.Default.Beats, AbcContext.Default.BeatUnit);

        var parts = text.Split('/');
        return parts.Length == 2 && int.TryParse(parts[0], out var beats) && int.TryParse(parts[1], out var unit)
            ? (beats, unit)
            : (AbcContext.Default.Beats, AbcContext.Default.BeatUnit);
    }
}
