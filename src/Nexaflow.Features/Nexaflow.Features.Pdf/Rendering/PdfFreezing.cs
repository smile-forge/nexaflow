using System.Windows;

namespace Nexaflow.Features.Pdf.Rendering;

/// <summary>Freezing in the middle of an expression: everything a layout tree holds is frozen, and most of it is built inline.</summary>
internal static class PdfFreezing
{
    /// <summary><paramref name="freezable"/>, frozen.</summary>
    public static T Frozen<T>(this T freezable) where T : Freezable
    {
        if (freezable.CanFreeze) freezable.Freeze();
        return freezable;
    }
}
