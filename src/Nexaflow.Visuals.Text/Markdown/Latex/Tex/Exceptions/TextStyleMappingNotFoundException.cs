namespace Nexaflow.Visuals.Text.Markdown.Latex.Tex.Exceptions;

public sealed class TextStyleMappingNotFoundException : TexException
{
    internal TextStyleMappingNotFoundException(string textStyleName)
        : base($"Cannot find mapping for the style with name '{textStyleName}'.")
    {
    }
}
