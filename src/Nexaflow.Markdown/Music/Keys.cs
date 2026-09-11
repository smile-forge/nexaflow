namespace Nexaflow.Markdown.Music;

/// <summary>
/// What a key signature is and what it alters — theory every notation reads the same way, whatever it writes
/// a key as. ABC writes <c>K:Bb</c> and LilyPond <c>\key bes \major</c>; both are a tonic and a mode, and both
/// land on the same place round the circle of fifths.
/// </summary>
public static class Keys
{
    /// <summary>The order sharps are added to a key signature: F C G D A E B.</summary>
    private static readonly int[] SharpOrder = [3, 0, 4, 1, 5, 2, 6];

    /// <summary>The order flats are added: B E A D G C F.</summary>
    private static readonly int[] FlatOrder = [6, 2, 5, 1, 4, 0, 3];

    /// <summary>Where each natural key sits on the circle of fifths: C=0, G=1, D=2, A=3, E=4, B=5, F=-1.</summary>
    private static readonly int[] Natural = [0, 2, 4, -1, 1, 3, 5];

    /// <summary>How the key signature alters this step: +1 for a sharp, -1 for a flat, 0 for neither.</summary>
    public static int AlterFor(int step, int fifths)
    {
        if (fifths > 0)
            for (var i = 0; i < Math.Min(fifths, 7); i++)
                if (SharpOrder[i] == step) return 1;

        if (fifths < 0)
            for (var i = 0; i < Math.Min(-fifths, 7); i++)
                if (FlatOrder[i] == step) return -1;

        return 0;
    }

    /// <summary>
    /// How far round the circle of fifths a key sits, from its tonic and its mode: sharps positive, flats
    /// negative. Each sharp on the tonic is seven fifths further round, which is why F sharp major has six.
    /// </summary>
    public static int Fifths(int step, int alter, string mode) => Natural[step] + (7 * alter) + ModeShift(mode);

    /// <summary>
    /// How far a mode moves a key off its major. Named in full or by the first three letters, in any case,
    /// which is what both ABC and LilyPond allow.
    /// </summary>
    public static int ModeShift(string mode)
    {
        var name = new string([.. mode.Where(char.IsLetter)]).ToLowerInvariant();
        if (name.Length == 0) return 0;

        // "maj" and "min" are also written "m" on its own, which is the commonest of all.
        if (name is "m") return -3;

        var head = name.Length >= 3 ? name[..3] : name;
        return head switch
        {
            "maj" or "ion" => 0,
            "min" or "aeo" => -3,
            "dor" => -2,
            "phr" => -4,
            "lyd" => 1,
            "mix" => -1,
            "loc" => -5,
            _ => 0,
        };
    }
}
