using System.Windows;
using Nexaflow.Markdown.Typesetting.Tex.Rendering;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex.Rendering;

namespace Nexaflow.Visuals.Text.Markdown.Latex.Tex.Rendering;

internal static class WpfRectangleExtensions
{
    public static Rect ToWpf(this Rectangle rectangle) =>
        new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
}
