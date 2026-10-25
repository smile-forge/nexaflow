namespace Nexaflow.Markdown.Barcode;

/// <summary>The pieces a barcode's layout is made of.</summary>
public static class BarcodePiece
{
    /// <summary>The whole symbol, quiet zone included.</summary>
    public const string Symbol = "Symbol";

    public const string Bars = "Bars";

    /// <summary>The line over a publication's bars naming the number.</summary>
    public const string Caption = "Caption";

    /// <summary>A run of the printed number with a character of the value in it.</summary>
    public const string Group = "Group";

    /// <summary>A printed character that is a character of the value, and can be edited as one.</summary>
    public const string Character = "Character";

    /// <summary>Printed characters worked out from the value rather than taken from it — nowhere for a caret to stand.</summary>
    public const string EncodedText = "EncodedText";
}
