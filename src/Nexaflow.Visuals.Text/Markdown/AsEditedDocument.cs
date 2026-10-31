using System;
using System.Globalization;
using System.Windows.Data;

using Nexaflow.Markdown.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Text a page only shows, as the document a surface shows it from (<see cref="MarkdownSurface.Document"/>): a document of its
/// own for each text it is handed, which is what showing a reply that grows, a help page or a message is — something loaded
/// whole, never written back. A page that is written in holds its own <see cref="EditedDocument"/> instead.
/// </summary>
[ValueConversion(typeof(string), typeof(EditedDocument))]
public sealed class AsEditedDocument : IValueConverter
{
    /// <summary>The one there need ever be: it holds nothing.</summary>
    public static AsEditedDocument Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => new EditedDocument(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
