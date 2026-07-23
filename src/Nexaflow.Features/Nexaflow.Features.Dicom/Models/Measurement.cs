using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Features.Dicom.Models;

/// <summary>The armed measurement/annotation tool. <see cref="Probe"/> is a hover readout, not a committed
/// annotation.</summary>
public enum MeasurementTool
{
    None,
    Length,
    Angle,
    Rectangle,
    Ellipse,
    Probe,
}

/// <summary>
/// A committed annotation on one image instance. <see cref="Points"/> are stored in <b>image space</b>
/// (source-pixel coordinates) so the overlay re-projects correctly under any zoom/pan. <see cref="Label"/>
/// is the computed readout (mm / degrees / mm² + HU).
/// </summary>
public sealed partial class Measurement : ObservableObject
{
    public MeasurementTool Tool { get; init; }

    /// <summary>Defining points in source-image pixel coordinates (2 for length, 3 for angle, 2 corners for ROI).</summary>
    public IReadOnlyList<Point> Points { get; init; } = [];

    [ObservableProperty] private string _label = string.Empty;
}
