using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Services.Initiatives.Graph.Model;

namespace Nexaflow.Features.ProductManager.Graph.ViewModels;

/// <summary>
/// A binary edge. Endpoints bind straight to the two node view-models' <c>CenterX/CenterY</c>, so the drawn
/// line follows the nodes under pan/zoom and any future drag/relayout without the edge tracking state itself.
/// </summary>
public sealed partial class GraphEdgeViewModel : ObservableObject
{
    public GraphEdgeViewModel(GraphEdge model, GraphNodeViewModel from, GraphNodeViewModel to)
    {
        Model = model;
        From = from;
        To = to;
    }

    /// <summary>Hidden when either endpoint is LOD-hidden at the current zoom (an edge to nowhere reads as noise).</summary>
    [ObservableProperty] private bool _lodVisible = true;

    public GraphEdge Model { get; }
    public GraphNodeViewModel From { get; }
    public GraphNodeViewModel To { get; }

    public string Relationship => Model.Relationship;
    public double Confidence => Model.Confidence;

    /// <summary>Inferred (name-resolved) edges render dashed + fainter; extracted edges (confidence 1.0) are solid.</summary>
    public bool IsInferred => Model.Confidence < 0.999;

    /// <summary>Line opacity scales with confidence, so weak guesses read as tentative.</summary>
    public double StrokeOpacity => 0.2 + (0.6 * Model.Confidence);

    public DoubleCollection? DashArray => IsInferred ? Dashed : null;

    private static readonly DoubleCollection Dashed = CreateDashed();

    private static DoubleCollection CreateDashed()
    {
        var d = new DoubleCollection { 3.0, 2.5 };
        d.Freeze();
        return d;
    }
}
