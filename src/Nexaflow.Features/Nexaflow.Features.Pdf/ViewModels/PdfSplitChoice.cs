namespace Nexaflow.Features.Pdf.ViewModels;

/// <summary>One of the choices a split offers — how many parts, which way — and what it is called.</summary>
public sealed record PdfSplitChoice<T>(T Value, string Label);
