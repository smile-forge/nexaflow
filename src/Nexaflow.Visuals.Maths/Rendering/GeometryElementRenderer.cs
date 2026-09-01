using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using WpfMath.Fonts;
using XamlMath;
using XamlMath.Boxes;
using XamlMath.Rendering;
using XamlMath.Rendering.Transformations;

namespace WpfMath.Rendering;

/// <summary>A renderer that renders the elements to a provided <see cref="GeometryGroup"/> instance.</summary>
public class GeometryElementRenderer : IElementRenderer
{
    private readonly GeometryGroup _geometry;
    private readonly double _scale;

    public GeometryElementRenderer(GeometryGroup geometry, double scale)
    {
        _geometry = geometry;
        _scale = scale;
    }

    public void RenderElement(Box box, double x, double y) => box.RenderTo(this, x, y);
    public void RenderLine(Point point0, Point point1, IBrush? foreground)
    {
        point0 = GeometryHelper.ScalePoint(_scale, point0);
        point1 = GeometryHelper.ScalePoint(_scale, point1);
        var lineGeometry = new LineGeometry(point0.ToWpf(), point1.ToWpf());
        _geometry.Children.Add(lineGeometry);
    }

    public void RenderCharacter(CharInfo info, double x, double y, IBrush? foreground)
    {
        var glyph = info.GetGlyphRun(x, y, _scale);
        var glyphGeometry = glyph.BuildGeometry();
        _geometry.Children.Add(glyphGeometry);
    }

    public void RenderRectangle(Rectangle rectangle, IBrush? foreground)
    {
        var rectangleGeometry = new RectangleGeometry(GeometryHelper.ScaleRectangle(_scale, rectangle).ToWpf());
        _geometry.Children.Add(rectangleGeometry);
    }

    public void RenderTransformed(Box box, IEnumerable<Transformation> transforms, double x, double y)
    {
        var group = new GeometryGroup();
        var scaledTransforms = transforms.Select(t => t.Scale(_scale));
        ApplyTransformations(scaledTransforms, group);
        var nestedRenderer = new GeometryElementRenderer(group, _scale);
        nestedRenderer.RenderElement(box, x, y);
        _geometry.Children.Add(group);
    }

    public void FinishRendering() {}

    private static void ApplyTransformations(IEnumerable<Transformation> transformations, GeometryGroup geometry)
    {
        // Transform.Value hands back a Matrix by value, so the transformations have to be accumulated
        // here and assigned once — mutating that copy left the geometry untransformed.
        //
        // Each one is *prepended*, which is what makes the list read the same way as it does through
        // the drawing-context renderer: there the last transformation pushed is the first the content
        // sees, so a [translate, rotate] pair rotates and then translates.
        var matrix = Matrix.Identity;
        foreach (var transformation in transformations)
        {
            ApplyTransformation(transformation, ref matrix);
        }

        geometry.Transform = new MatrixTransform(matrix);
    }

    private static void ApplyTransformation(Transformation transformation, ref Matrix matrix)
    {
        switch (transformation.Kind)
        {
            case TransformationKind.Translate:
                var tt = (Transformation.Translate)transformation;
                matrix.TranslatePrepend(tt.X, tt.Y);
                break;
            case TransformationKind.Rotate:
                var rt = (Transformation.Rotate)transformation;
                matrix.RotatePrepend(rt.RotationDegrees);
                break;
            default:
                throw new NotSupportedException($"Unknown {nameof(Transformation)} kind: {transformation.Kind}");
        }
    }
}
