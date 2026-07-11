using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.GraphViewer.Converters;
using Nexaflow.Services.Initiatives.Graph.Model;

namespace Nexaflow.Features.GraphViewer.ViewModels;

/// <summary>One spoke of a hyperedge: a line from the connector glyph to one roled endpoint node. The endpoint end
/// binds to the node's <c>CenterX/CenterY</c> (so it follows the node); the connector end binds to the parent
/// hyperedge's centre.</summary>
public sealed class HyperSpokeViewModel
{
    public HyperSpokeViewModel(GraphNodeViewModel node, string role)
    {
        Node = node;
        Role = role;
    }

    public GraphNodeViewModel Node { get; }
    public string Role { get; }
}

/// <summary>
/// An n-ary hyperedge on the canvas: a small connector glyph at the centroid of its endpoints, with a spoke to
/// each. The centroid (<see cref="CenterX"/>/<see cref="CenterY"/>) is recomputed from the endpoint positions by
/// the view's tween loop as nodes settle, so the glyph tracks its members. Colour encodes the relationship kind.
/// </summary>
public sealed partial class GraphHyperEdgeViewModel : ObservableObject
{
    public GraphHyperEdgeViewModel(GraphHyperEdge model, IReadOnlyList<HyperSpokeViewModel> spokes)
    {
        Model = model;
        Spokes = spokes;
        UpdateCentre();   // seed from the endpoints' current positions so the glyph doesn't flash at the origin
    }

    public GraphHyperEdge Model { get; }
    public IReadOnlyList<HyperSpokeViewModel> Spokes { get; }

    public string Relationship => Model.Relationship;
    public double Confidence => Model.Confidence;

    /// <summary>Hidden when any endpoint is LOD-hidden at the current zoom.</summary>
    [ObservableProperty] private bool _lodVisible = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Center))]
    private double _centerX;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Center))]
    private double _centerY;

    /// <summary>The glyph centre as a point (bound by the connector's <c>EllipseGeometry.Center</c>).</summary>
    public Point Center => new(CenterX, CenterY);

    /// <summary>Recompute the centroid from the (possibly just-moved) endpoint centres. Called each tween frame.</summary>
    public void UpdateCentre()
    {
        if (Spokes.Count == 0) return;
        double sx = 0, sy = 0;
        foreach (var s in Spokes) { sx += s.Node.CenterX; sy += s.Node.CenterY; }
        CenterX = sx / Spokes.Count;
        CenterY = sy / Spokes.Count;
    }

    /// <summary>Fainter for lower-confidence (inferred) hyperedges, mirroring the binary-edge convention.</summary>
    public double StrokeOpacity => 0.25 + (0.55 * Confidence);

    private Brush? _glyph;
    public Brush GlyphBrush => _glyph ??= ThemeBrush.Resolve(GlyphKey, Fallback);

    private string GlyphKey => Relationship switch
    {
        HyperRelationship.Calls     => "Swatch.Cyan",
        HyperRelationship.Signature => "Swatch.Purple",
        HyperRelationship.Annotated => "Swatch.Amber",
        _                           => "AccentBrush",
    };

    private static readonly Color Fallback = Color.FromRgb(0x8A, 0x8A, 0x8A);

    public string Tooltip =>
        $"{Relationship}: {string.Join(", ", Spokes.Select(s => $"{s.Role}={s.Node.Label}"))}";
}
