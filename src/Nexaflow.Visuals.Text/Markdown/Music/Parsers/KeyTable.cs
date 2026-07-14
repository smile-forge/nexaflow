using System;
using Nexaflow.Visuals.Text.Markdown.Music.Model;

namespace Nexaflow.Visuals.Text.Markdown.Music.Parsers;

/// <summary>
/// Maps a tonic + mode to a circle-of-fifths count, shared by both notation parsers (ABC <c>K:</c> and
/// LilyPond <c>\key</c>). Major fifths come from the natural circle offset by 7 per accidental; a mode
/// shifts that (dorian −2, minor −3, …).
/// </summary>
public static class KeyTable
{
    // Fifths of the natural major keys, indexed by diatonic step (C,D,E,F,G,A,B).
    private static readonly int[] NaturalMajorFifths = [0, 2, 4, -1, 1, 3, 5];

    /// <summary>Circle-of-fifths value of a major key whose tonic is (step, alter).</summary>
    public static int MajorFifths(int step, int alter) =>
        NaturalMajorFifths[((step % 7) + 7) % 7] + 7 * alter;

    /// <summary>Fifths offset for a church mode relative to the same-tonic major (ionian).</summary>
    public static int ModeOffset(string mode)
    {
        mode = mode.Trim().ToLowerInvariant();
        if (mode.Length == 0 || mode.StartsWith("maj") || mode.StartsWith("ion")) return 0;
        if (mode.StartsWith("mix")) return -1;
        if (mode.StartsWith("dor")) return -2;
        if (mode.StartsWith("phr")) return -4;
        if (mode.StartsWith("lyd")) return 1;
        if (mode.StartsWith("loc")) return -5;
        if (mode.StartsWith("aeo")) return -3;
        if (mode.StartsWith('m')) return -3;   // "m" / "min" — minor
        return 0;
    }

    /// <summary>Combines a tonic (step+alter) and mode into a clamped key signature.</summary>
    public static KeySignature FromTonic(int step, int alter, string mode)
    {
        int fifths = MajorFifths(step, alter) + ModeOffset(mode);
        return KeySignature.FromFifths(Math.Clamp(fifths, -7, 7));
    }

    /// <summary>Which explicit alteration the key signature imposes on a diatonic <paramref name="step"/>
    /// (+1 for a sharp in the signature, −1 for a flat, 0 otherwise). Lets a parser set a note's chromatic
    /// alter without the source repeating the accidental.</summary>
    public static int KeyAlterFor(int step, int fifths)
    {
        step = ((step % 7) + 7) % 7;
        if (fifths > 0)
            for (int i = 0; i < fifths && i < 7; i++)
                if (KeySignature.SharpOrder[i] == step) return 1;
        if (fifths < 0)
            for (int i = 0; i < -fifths && i < 7; i++)
                if (KeySignature.FlatOrder[i] == step) return -1;
        return 0;
    }
}
