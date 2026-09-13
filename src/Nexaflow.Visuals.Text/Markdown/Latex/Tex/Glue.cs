using System.Collections.Generic;

namespace XamlMath;

// Represents glueElement for holding together boxes.
internal sealed class Glue
{
    private static readonly IReadOnlyList<Glue> glueTypes;
    private static readonly int[, ,] glueRules;

    static Glue()
    {
        var parser = new GlueSettingsParser();
        glueTypes = parser.GetGlueTypes();
        glueRules = parser.GetGlueRules();
    }

    /// <summary>How much room TeX puts between a thing of one class and a thing of another, in the style given.</summary>
    public static double Between(TexAtomType leftAtomType, TexAtomType rightAtomType, TexEnvironment environment)
    {
        leftAtomType = leftAtomType > TexAtomType.Inner ? TexAtomType.Ordinary : leftAtomType;
        rightAtomType = rightAtomType > TexAtomType.Inner ? TexAtomType.Ordinary : rightAtomType;
        var glueType = glueRules[(int)leftAtomType, (int)rightAtomType, (int)environment.Style / 2];
        var texFont = environment.MathFont;
        var quad = texFont.GetQuad(texFont.GetMuFontId(), environment.Style);
        return (glueTypes[glueType].Space / 18.0f) * quad;
    }

    public Glue(double space, double stretch, double shrink, string name)
    {
        this.Space = space;
        this.Stretch = stretch;
        this.Shrink = shrink;
        this.Name = name;
    }

    public double Space { get; }
    public double Stretch { get; }
    public double Shrink { get; }
    public string Name { get; }
}
