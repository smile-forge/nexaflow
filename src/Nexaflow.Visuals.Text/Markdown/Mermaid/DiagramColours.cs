using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// A run of colours a number is read along: what a heat map is coloured by, and what a legend showing one
/// is a bar rather than a row of squares.
///
/// <para>
/// <see cref="DiagramInk.Series"/> answers the other colour question — which of a few colours a group
/// takes — and the two are genuinely different. A series colour says <em>which</em>; a ramp says
/// <em>how much</em>, and has to be read as an ordered quantity at a glance. That is why the ramps here
/// are the ones designed for it rather than anything pretty: viridis and its family stay ordered when
/// they are printed grey and when they are read by the eight percent of men who do not see red and green
/// apart.
/// </para>
/// <para>
/// A diverging ramp is the other half of it — two runs from a middle, for a number with a meaningful
/// nought. A correlation is exactly that, which is why one belongs here at all.
/// </para>
/// </summary>
internal enum DiagramRamp
{
    Viridis,
    Magma,
    Plasma,
    Inferno,

    Blues,
    Reds,
    Greens,

    /// <summary>Diverging: blue at the low end, through white, to red at the high one.</summary>
    BlueRed,
}

/// <summary>The colours <see cref="DiagramRamp"/> runs through, and reading a number along one.</summary>
internal static class DiagramColours
{
    /// <summary>The ramp a name stands for, or null where it names none.</summary>
    public static DiagramRamp? Named(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "viridis" => DiagramRamp.Viridis,
        "magma" => DiagramRamp.Magma,
        "plasma" => DiagramRamp.Plasma,
        "inferno" => DiagramRamp.Inferno,
        "blues" or "blue" => DiagramRamp.Blues,
        "reds" or "red" => DiagramRamp.Reds,
        "greens" or "green" => DiagramRamp.Greens,
        "rdbu" or "bluered" or "blue-red" or "diverging" => DiagramRamp.BlueRed,
        _ => null,
    };

    /// <summary>What the ramps are called, for saying what a block could have written instead.</summary>
    public static readonly string Names =
        "viridis, magma, plasma, inferno, blues, reds, greens, rdbu — or two colours, or three with a middle";

    /// <summary>Whether a ramp runs out from a middle rather than along from one end.</summary>
    public static bool Diverges(DiagramRamp ramp) => ramp == DiagramRamp.BlueRed;

    /// <summary>The colour a share from nought to one takes along a ramp.</summary>
    public static Color At(DiagramRamp ramp, double share) => At(Stops(ramp), share);

    /// <summary>
    /// The colour a share takes along a run of colours — two for a plain run, three for one with a
    /// middle, or as many as were written.
    /// </summary>
    public static Color At(IReadOnlyList<Color> stops, double share)
    {
        if (stops.Count == 0) return Colors.Transparent;
        if (stops.Count == 1) return stops[0];

        if (double.IsNaN(share)) share = 0;
        share = Math.Clamp(share, 0, 1);

        var along = share * (stops.Count - 1);
        var at = Math.Min((int)along, stops.Count - 2);

        return Between(stops[at], stops[at + 1], along - at);
    }

    /// <summary>The colours a ramp is made of, from its low end to its high one.</summary>
    public static IReadOnlyList<Color> Stops(DiagramRamp ramp) => Ramps[ramp];

    private static Color Between(Color from, Color to, double share) => Color.FromArgb(
        Mix(from.A, to.A, share), Mix(from.R, to.R, share), Mix(from.G, to.G, share), Mix(from.B, to.B, share));

    private static byte Mix(byte from, byte to, double share) =>
        (byte)Math.Round(from + ((to - from) * share));

    private static readonly IReadOnlyDictionary<DiagramRamp, IReadOnlyList<Color>> Ramps =
        new Dictionary<DiagramRamp, IReadOnlyList<Color>>
        {
            // The matplotlib families, sampled at nine points each — dense enough that reading between
            // two of them is indistinguishable from the curve they were taken off.
            [DiagramRamp.Viridis] = Read("440154 472d7b 3b528b 2c728e 21918c 27ad81 5ec962 aadc32 fde725"),
            [DiagramRamp.Magma] = Read("000004 1c1044 4f127b 812581 b5367a e55064 fb8761 fec287 fcfdbf"),
            [DiagramRamp.Plasma] = Read("0d0887 41049d 6a00a8 8f0da4 b12a90 cb4679 e16462 f89441 f0f921"),
            [DiagramRamp.Inferno] = Read("000004 1b0c41 4a0c6b 781c6d a52c60 cf4446 ed6925 fb9b06 fcffa4"),

            // ColorBrewer's sequential runs, which are what a count is read along.
            [DiagramRamp.Blues] = Read("f7fbff deebf7 c6dbef 9ecae1 6baed6 3182bd 08519c"),
            [DiagramRamp.Reds] = Read("fff5f0 fee0d2 fcbba1 fc9272 fb6a4a de2d26 a50f15"),
            [DiagramRamp.Greens] = Read("f7fcf5 e5f5e0 c7e9c0 a1d99b 74c476 31a354 006d2c"),

            // ColorBrewer's RdBu, turned about so the low end is blue — which is the way round anybody
            // reading a correlation expects it.
            [DiagramRamp.BlueRed] = Read("053061 2166ac 4393c3 92c5de d1e5f0 f7f7f7 fddbc7 f4a582 d6604d b2182b 67001f"),
        };

    private static IReadOnlyList<Color> Read(string hexes) =>
    [
        .. hexes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(hex => Color.FromRgb(Convert.ToByte(hex[..2], 16),
                                             Convert.ToByte(hex.Substring(2, 2), 16),
                                             Convert.ToByte(hex[4..], 16))),
    ];
}
