namespace Nexaflow.Features.Tabular.Detection;

public enum CsvDataType
{
    String,
    Integer,
    Decimal,
    Boolean,
    Date,
    DateTime,
    Time,
    Currency,
}

public static class CsvDataTypeIcons
{
    public static string Glyph(CsvDataType type) => type switch
    {
        CsvDataType.String   => "Aa",
        CsvDataType.Integer  => "#",
        CsvDataType.Decimal  => "0.0",
        CsvDataType.Boolean  => "☑",
        CsvDataType.Date     => "📅",
        CsvDataType.DateTime => "⏱",
        CsvDataType.Time     => "🕐",
        CsvDataType.Currency => "¤",
        _                    => "?"
    };
}
