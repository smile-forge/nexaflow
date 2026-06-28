using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Nexaflow.Features.Model3D.Loaders;

/// <summary>
/// The shared material-colouring decision used by every loader, so all formats separate their materials the
/// same way. Textured materials keep their own colour. Among the untextured ones, the file's authored
/// colours are kept only when they are <i>already all distinct</i>; otherwise every untextured material is
/// tinted from the categorical palette in order — so multiple materials that the file left the same colour
/// (e.g. an FBX where every material defaults to grey) become individually visible.
/// </summary>
internal static class CategoricalTinting
{
    /// <summary>Display colour per material (index-aligned to <paramref name="materials"/>); null means
    /// "no colour" (the caller falls back to a neutral default).</summary>
    public static Color?[] Resolve(
        IReadOnlyList<(Color? Colour, bool HasTexture)> materials, IReadOnlyList<Color> palette)
    {
        var result = new Color?[materials.Count];

        var untextured = Enumerable.Range(0, materials.Count).Where(i => !materials[i].HasTexture).ToList();
        var authored = untextured.Where(i => materials[i].Colour is not null)
                                 .Select(i => materials[i].Colour!.Value).ToList();

        // Keep authored colours only if every untextured material has one and they're all different.
        var allDistinct = authored.Count == untextured.Count && authored.Distinct().Count() == authored.Count;
        var tint = palette.Count > 0 && untextured.Count > 0 && !allDistinct;

        var next = 0;
        for (var i = 0; i < materials.Count; i++)
        {
            if (materials[i].HasTexture) { result[i] = materials[i].Colour; continue; }
            result[i] = tint ? palette[next++ % palette.Count] : materials[i].Colour;
        }

        return result;
    }

    /// <summary>A frozen diffuse material for a resolved colour (neutral grey when none).</summary>
    public static Material ToDiffuseMaterial(Color? colour)
    {
        var brush = new SolidColorBrush(colour ?? Colors.LightGray);
        brush.Freeze();
        var material = new DiffuseMaterial(brush);
        material.Freeze();
        return material;
    }
}
