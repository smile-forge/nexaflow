using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Dicom.Models;
using Nexaflow.Features.Dicom.Services;

namespace Nexaflow.Features.Dicom.ViewModels;

/// <summary>
/// Owns the measurement/annotation state for the image pane: the armed tool, and the committed annotations
/// per image frame (so switching series/frames and back restores them). Label computation and pixel
/// sampling are delegated to <see cref="MeasurementMath"/> / <see cref="PixelSampler"/>.
/// </summary>
public sealed partial class MeasurementViewModel : ObservableObject
{
    private readonly Dictionary<string, ObservableCollection<Measurement>> _byFrame = new(StringComparer.Ordinal);
    private DicomInstanceInfo? _info;
    private PixelSampler? _sampler;

    [ObservableProperty] private MeasurementTool _activeTool = MeasurementTool.None;

    /// <summary>Committed annotations for the frame currently shown.</summary>
    [ObservableProperty] private ObservableCollection<Measurement> _current = [];

    /// <summary>Points the active tool wants collected before it commits (2 length, 3 angle, 2 ROI corners).</summary>
    public int PointsNeeded => ActiveTool switch
    {
        MeasurementTool.Length => 2,
        MeasurementTool.Angle => 3,
        MeasurementTool.Rectangle or MeasurementTool.Ellipse => 2,
        _ => 0,
    };

    /// <summary>Switches the pixel context to a specific frame of an image instance and surfaces that
    /// frame's annotation collection.</summary>
    public void SetFrame(DicomInstanceInfo? info, int frame)
    {
        _info = info;
        _sampler = info is { IsImage: true } ? new PixelSampler(info, frame) : null;

        var key = info is null ? null : $"{info.FilePath}#{frame}";
        if (key is null) { Current = []; return; }
        if (!_byFrame.TryGetValue(key, out var coll)) _byFrame[key] = coll = [];
        Current = coll;
    }

    /// <summary>Commits a completed annotation from image-space points, computing its label.</summary>
    public Measurement Commit(MeasurementTool tool, IReadOnlyList<Point> points)
    {
        var label = tool switch
        {
            MeasurementTool.Length when points.Count >= 2 => MeasurementMath.LengthLabel(points[0], points[1], _info),
            MeasurementTool.Angle when points.Count >= 3 => MeasurementMath.AngleLabel(points[0], points[1], points[2]),
            MeasurementTool.Rectangle when points.Count >= 2 => MeasurementMath.RoiLabel(points[0], points[1], ellipse: false, _info, _sampler),
            MeasurementTool.Ellipse when points.Count >= 2 => MeasurementMath.RoiLabel(points[0], points[1], ellipse: true, _info, _sampler),
            _ => string.Empty,
        };
        var m = new Measurement { Tool = tool, Points = points, Label = label };
        Current.Add(m);
        return m;
    }

    public void Clear() => Current.Clear();

    /// <summary>Hover readout for the pixel probe: coordinate + raw value (or HU for CT), or null.</summary>
    public string? ProbeAt(Point imagePoint)
    {
        if (_sampler is null || !_sampler.Available) return null;
        var x = (int)Math.Floor(imagePoint.X);
        var y = (int)Math.Floor(imagePoint.Y);
        var v = _sampler.Value(x, y);
        if (v is null) return null;
        return _sampler.IsHounsfield ? $"({x}, {y})  {v:0} HU" : $"({x}, {y})  {v:0}";
    }
}
