namespace Nexaflow.Markdown.Barcode;

/// <summary>What a piece of a <c>barcode</c> block is, beyond the fields every code block is written in (<see cref="Matrix.MatrixKinds"/>).</summary>
public static class BarcodeKinds
{
    /// <summary>One character of the value: what a character printed under the bars stands for, and where a caret stands beside it.</summary>
    public const string Character = "character";

    /// <summary>The bars the value encodes to — or, while it will not, a faint symbol of its kind.</summary>
    public const string Bars = "bars";

    /// <summary>The line printed over the bars naming the number a publication's symbol stands for.</summary>
    public const string Caption = "caption";

    /// <summary>A run of the printed number: under the bars, beside them, or over an add-on's.</summary>
    public const string Group = "group";

    /// <summary>
    /// Printed characters worked out from the value rather than taken from it — a check digit, a Codabar start or stop mark, a
    /// scheme's name, a number with its hyphens taken out. A run is one of these whole where none of the value is in it.
    /// </summary>
    public const string Worked = "worked";

    /// <summary>A printed character that is a character of the value, standing for that character as written.</summary>
    public const string Printed = "printed";
}

/// <summary>What a piece of a <c>barcode</c> block is <em>to</em> the piece holding it.</summary>
public static class BarcodeRoles
{
    /// <summary>The <c>value:</c> line the bars encode — the last one written, where there are several.</summary>
    public const string Encoded = "encoded";
}
