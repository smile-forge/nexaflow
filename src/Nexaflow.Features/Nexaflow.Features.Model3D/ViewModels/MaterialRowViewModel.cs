using System.Windows.Media;
using Nexaflow.Features.Model3D.Loaders;

namespace Nexaflow.Features.Model3D.ViewModels;

/// <summary>One row in the inspector's material list: the material's name plus the bits we can show —
/// a colour swatch (the model's own diffuse colour, file content rather than UI chrome) and a detail
/// line with the hex value and any texture reference.</summary>
public sealed class MaterialRowViewModel
{
    public MaterialRowViewModel(ModelMaterial material)
    {
        Name = material.Name;

        if (material.DiffuseColor is { } color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Swatch = brush;
        }

        var parts = new List<string>();
        if (material.DiffuseColor is { } c) parts.Add($"#{c.R:X2}{c.G:X2}{c.B:X2}");
        if (!string.IsNullOrWhiteSpace(material.Texture)) parts.Add(material.Texture!);
        Detail = string.Join("  ·  ", parts);
    }

    public string Name { get; }

    /// <summary>The material's own diffuse colour as a frozen brush, or null when the material defines none.</summary>
    public Brush? Swatch { get; }

    public bool HasSwatch => Swatch is not null;

    public string Detail { get; }
}
