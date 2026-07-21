using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Services.Initiatives.Graph.Model;

namespace Nexaflow.Features.ProductManager.Graph.ViewModels;

/// <summary>
/// A node on the canvas. Observable <see cref="X"/>/<see cref="Y"/> (top-left) are set by the layout pass;
/// <see cref="CenterX"/>/<see cref="CenterY"/> track them so bound edges follow when a node moves.
/// </summary>
public sealed partial class GraphNodeViewModel : ObservableObject
{
    public GraphNodeViewModel(GraphNode model) => Model = model;

    public GraphNode Model { get; }

    public string Id => Model.Id;
    /// <summary>The node kind (<see cref="NodeType"/>) — drives shape/colour. Named <c>Kind</c> to avoid clashing with the <c>NodeType</c> vocabulary class.</summary>
    public string Kind => Model.Type;
    public string Label => string.IsNullOrEmpty(Model.Label) ? Model.Id : Model.Label;
    public int? Community => Model.Community;
    public double Confidence => Model.Confidence;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CenterX))]
    private double _x;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CenterY))]
    private double _y;

    [ObservableProperty] private int _zIndex;

    /// <summary>Whether this node clears the current zoom's level-of-detail threshold (see <see cref="MinScale"/>).
    /// Set by the view-model as the view zooms — members fade out first when zoomed far out, product nodes never.</summary>
    [ObservableProperty] private bool _lodVisible = true;

    /// <summary>The minimum zoom at which this node kind is drawn — the 3-tier LOD: product always, then files,
    /// then types/externals, then members (finest, shown only when zoomed in). So a zoomed-out view reads as
    /// structure and blooms into detail as you zoom in.</summary>
    public double MinScale => Model.Type switch
    {
        NodeType.Product  => 0.0,
        NodeType.File     => 0.12,
        NodeType.Type     => 0.28,
        NodeType.External => 0.28,
        _                 => 0.55,   // member
    };

    /// <summary>The layout <b>target</b> position (top-left). The view eases <see cref="X"/>/<see cref="Y"/> toward
    /// it each frame so re-layouts glide rather than snap.</summary>
    public double Tx { get; set; }
    public double Ty { get; set; }

    /// <summary>True for a node revealed by the current focus change — the view grows it in.</summary>
    public bool IsNew { get; set; }

    /// <summary>Place the node immediately (display and target coincide — no tween).</summary>
    public void SnapTo(double x, double y)
    {
        Tx = x;
        Ty = y;
        X = x;
        Y = y;
    }

    /// <summary>Diameter in canvas units, by node kind.</summary>
    public double Size => Model.Type switch
    {
        NodeType.Product  => 24,
        NodeType.File     => 16,
        NodeType.Type     => 14,
        NodeType.External => 12,
        _                 => 9,   // member
    };

    public double CenterX => X + Size / 2;
    public double CenterY => Y + Size / 2;

    // ── Property-drawer surface ───────────────────────────────────────────────────────────────────
    public string? FilePath => Model.FilePath;
    public string? Language => Model.Language;
    public string? Source => Model.Source;
    public bool HasFile => !string.IsNullOrEmpty(Model.FilePath);
    public string ConfidenceText => Model.Confidence.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>The stable AST path (for deep-linking a code node into the Code viewer), if any.</summary>
    public string? Ast => Model.Metadata is { } m && m.TryGetValue("ast", out var a) ? a : null;

    public IReadOnlyList<KeyValuePair<string, string>> MetadataPairs =>
        Model.Metadata is { Count: > 0 } m ? m.ToList() : Array.Empty<KeyValuePair<string, string>>();
}
