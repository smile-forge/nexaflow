using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Music.Abc.Stages;

/// <summary>
/// Says what key, meter, unit note length and voice are in force on every line of music (<see cref="AbcLineNode"/>).
///
/// <para>
/// None of it is written on the line. All of it is written above it — possibly a long way above it,
/// possibly changed halfway down, possibly set inline in the middle of a bar — so it is worked out once,
/// by walking the tune in the order it was written, rather than looked up again by everything that needs
/// it. That is the shape every context has: a fact about the descent, not about the piece.
/// </para>
/// <para>
/// Straight after the fields are read, because nothing after it can do its job without the answer. What a length suffix
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
            // A music line is told what is in force as it starts, the voice it opens by naming included. An
            // inline field further along changes it from there, which the stages reading this handle as they walk.
            if (line.Kind == AbcKinds.Line)
            {
                lines.Add(new AbcLineNode(line, Opening(line, context)));
                moved = true;

                context = Inline(line, context, ref explicitUnit);
                continue;
            }

            lines.Add(line);
            if (line is AbcFieldNode field) context = Set(field, context, ref explicitUnit);
        }

        return moved ? tree.With(lines) : tree;
    }

    /// <summary>
    /// What a line starts in: what is in force, and the voice it names before any of its music. A part song is
    /// written a line per voice — <c>[V:1] e |</c> — and that line is voice 1's, not a line of whichever voice came
    /// before it that switches on its first character. Told the voice before it, every part of a chorale took its
    /// neighbour's clef.
    /// </summary>
    private static AbcContext Opening(ContentNode line, AbcContext context)
    {
        foreach (var piece in line.Children)
        {
            if (piece.Kind is AbcKinds.Note or AbcKinds.Rest or AbcKinds.Chord or AbcKinds.Grace) break;
            if (piece is not AbcFieldNode { Voice: { } voice } field || char.ToUpperInvariant(field.Letter) != 'V') continue;

            return context with { Voice = voice };
        }

        return context;
    }

    /// <summary>What is in force after the inline fields written along a line.</summary>
    private static AbcContext Inline(ContentNode line, AbcContext context, ref bool explicitUnit)
    {
        foreach (var piece in line.Children)
            if (piece is AbcFieldNode field) context = Set(field, context, ref explicitUnit);

        return context;
    }

    /// <summary>What is in force once a field has said what it says.</summary>
    internal static AbcContext Set(AbcFieldNode field, AbcContext context, ref bool explicitUnit)
    {
        switch (char.ToUpperInvariant(field.Letter))
        {
            case 'K':
                return context with { Fifths = field.Fifths ?? context.Fifths };

            case 'M':
                if (field.Meter is not { } meter) return context;

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
                if (field.Unit is not { } unit) return context;
                explicitUnit = true;
                return context with { Unit = unit };

            case 'V':
                return field.Voice is { } voice ? context with { Voice = voice } : context;

            default:
                return context;
        }
    }
}
