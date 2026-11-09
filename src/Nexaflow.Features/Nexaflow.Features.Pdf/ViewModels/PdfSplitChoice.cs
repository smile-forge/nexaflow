namespace Nexaflow.Features.Pdf.ViewModels;

/// <summary>One of the choices a sheet edit offers — how many parts, which way, what order — and what it is called.</summary>
public sealed record PdfSplitChoice<T>(T Value, string Label)
{
    /// <summary>What it is called — what a screen reader says of it in a list, as a list item's name is its text.</summary>
    public override string ToString() => Label;
}
