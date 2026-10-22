using Nexaflow.Features.Tabular.Detection;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Tabular.ViewModels;

/// <summary>
/// The words the Tabular viewer shows for its own notions — separator names, column types and the detected
/// shape — looked up in the current language each time they are shown. <see cref="CsvShape.Label"/> stays the
/// detector's English descriptor, the one the assistant reads; <see cref="ShapeLabel"/> is the person's.
/// </summary>
public static class TabularDisplayText
{
    /// <summary>The name of a field separator, as the Split-by menu and the shape label show it.</summary>
    public static string SeparatorName(char separator) => separator switch
    {
        '\t' => Str.Get("Tabular.Separator.Tab"),
        ','  => Str.Get("Tabular.Separator.Comma"),
        ';'  => Str.Get("Tabular.Separator.Semicolon"),
        '|'  => Str.Get("Tabular.Separator.Pipe"),
        ' '  => Str.Get("Tabular.Separator.Space"),
        '-'  => Str.Get("Tabular.Separator.Dash"),
        '/'  => Str.Get("Tabular.Separator.Slash"),
        ':'  => Str.Get("Tabular.Separator.Colon"),
        '_'  => Str.Get("Tabular.Separator.Underscore"),
        '.'  => Str.Get("Tabular.Separator.Period"),
        _    => Str.Format("Tabular.Separator.CharFormat", separator),
    };

    /// <summary>The name of a column type, as the Evaluate-as menu shows it.</summary>
    public static string TypeName(CsvDataType type) => type switch
    {
        CsvDataType.Integer  => Str.Get("Tabular.Type.Integer"),
        CsvDataType.Decimal  => Str.Get("Tabular.Type.Decimal"),
        CsvDataType.Boolean  => Str.Get("Tabular.Type.Boolean"),
        CsvDataType.Date     => Str.Get("Tabular.Type.Date"),
        CsvDataType.DateTime => Str.Get("Tabular.Type.DateTime"),
        CsvDataType.Time     => Str.Get("Tabular.Type.Time"),
        CsvDataType.Currency => Str.Get("Tabular.Type.Currency"),
        _                    => Str.Get("Tabular.Type.String"),
    };

    /// <summary>The toolbar's description of a detected shape: its separator, quoting and header, or that it is a
    /// single or fixed-width column layout.</summary>
    public static string ShapeLabel(CsvShape shape)
    {
        if (shape.FixedWidthBoundaries is not null)
            return Str.Format("Tabular.Shape.FixedWidthFormat", shape.FieldCount);
        if (shape.Separator is not char separator)
            return Str.Get("Tabular.Shape.SingleColumn");

        var name = SeparatorName(separator);
        return (shape.Quote, shape.HasHeader) switch
        {
            (char quote, true)  => Str.Format("Tabular.Shape.QuotedWithHeaderFormat", name, quote),
            (char quote, false) => Str.Format("Tabular.Shape.QuotedNoHeaderFormat", name, quote),
            (_, true)           => Str.Format("Tabular.Shape.NoQuotesWithHeaderFormat", name),
            _                   => Str.Format("Tabular.Shape.NoQuotesNoHeaderFormat", name),
        };
    }
}
