using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.IO.Hdf5;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Hdf5.ViewModels;

/// <summary>What a row of the object tree stands for: an object, or the tree's own bookkeeping around a group.</summary>
public enum Hdf5NodeRole
{
    Object,
    /// <summary>Stands in for a group's members until it is first expanded.</summary>
    Loading,
    /// <summary>Selecting it lists the next page of a large group.</summary>
    More,
    /// <summary>Listing the group stopped at a member the reader could not decode; the reason is its detail.</summary>
    Stopped,
}

/// <summary>
/// One row of the object tree. A group lists its members when first expanded, a page at a time, so a file of any
/// size opens at once and a group of a million members costs only what is looked at.
/// </summary>
public sealed partial class Hdf5NodeViewModel : ObservableObject
{
    private readonly Func<Hdf5NodeViewModel, Task>? _expand;
    private bool _expansionStarted;

    private Hdf5NodeViewModel(Hdf5NodeViewModel? parent, Hdf5NodeRole role, Hdf5Object? obj,
                              string display, string detail, Func<Hdf5NodeViewModel, Task>? expand)
    {
        Parent  = parent;
        Role    = role;
        Object  = obj;
        Display = display;
        Detail  = detail;
        _expand = expand;
        if (obj is { Kind: Hdf5ObjectKind.Group }) Children.Add(Placeholder(this));
    }

    /// <summary>A row for an object; a group's rows list its members through <paramref name="expand"/>.</summary>
    public static Hdf5NodeViewModel For(Hdf5NodeViewModel? parent, Hdf5Object o, Func<Hdf5NodeViewModel, Task> expand) =>
        new(parent, Hdf5NodeRole.Object, o, o.Name, DetailOf(o), expand);

    internal static Hdf5NodeViewModel Placeholder(Hdf5NodeViewModel parent) =>
        new(parent, Hdf5NodeRole.Loading, null, Str.Get("Hdf5.Tree.Loading"), string.Empty, null);

    internal static Hdf5NodeViewModel More(Hdf5NodeViewModel parent, int nextSkip) =>
        new(parent, Hdf5NodeRole.More, null, Str.Get("Hdf5.Tree.More"), string.Empty, null) { NextSkip = nextSkip };

    internal static Hdf5NodeViewModel Stopped(Hdf5NodeViewModel parent, string problem) =>
        new(parent, Hdf5NodeRole.Stopped, null, Str.Get("Hdf5.Tree.ListingStopped"), problem, null);

    public Hdf5NodeViewModel? Parent { get; }
    public Hdf5NodeRole Role { get; }
    public Hdf5Object? Object { get; }
    public string Detail { get; }
    public ObservableCollection<Hdf5NodeViewModel> Children { get; } = [];

    /// <summary>For a <see cref="Hdf5NodeRole.More"/> row, where the next page starts.</summary>
    internal int NextSkip { get; private init; }

    /// <summary>True once the first page of members has replaced the placeholder.</summary>
    public bool ChildrenLoaded { get; private set; }

    [ObservableProperty] private string _display = string.Empty;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isVisible = true;

    public string Glyph => Role switch
    {
        Hdf5NodeRole.Loading => "…",
        Hdf5NodeRole.More    => "⋯",
        Hdf5NodeRole.Stopped => "⚠",
        _ => Object?.Kind switch
        {
            Hdf5ObjectKind.Group          => "📁",
            Hdf5ObjectKind.Dataset        => "▦",
            Hdf5ObjectKind.NamedDatatype  => "🏷",
            Hdf5ObjectKind.UnresolvedLink => "⛓",
            _                             => "⚠",
        },
    };

    /// <summary>The object rows below this one, without the tree's own bookkeeping rows.</summary>
    public IEnumerable<Hdf5NodeViewModel> ObjectChildren => Children.Where(c => c.Role == Hdf5NodeRole.Object);

    /// <summary>
    /// Folds a page of members in: the bookkeeping rows at the end go, the page's members follow what is already
    /// listed, then a row to fetch the next page or one saying why listing stopped.
    /// </summary>
    internal void Apply(Hdf5Listing listing, int skip, Func<Hdf5Object, Hdf5NodeViewModel> create)
    {
        for (int i = Children.Count - 1; i >= 0 && Children[i].Role != Hdf5NodeRole.Object; i--) Children.RemoveAt(i);
        foreach (var o in listing.Items) Children.Add(create(o));
        if (listing.HasMore) Children.Add(More(this, skip + listing.Items.Count));
        if (listing.Problem is { } problem) Children.Add(Stopped(this, problem));
        ChildrenLoaded = true;
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (!value || _expansionStarted || _expand is null || Object is not { Kind: Hdf5ObjectKind.Group }) return;
        _expansionStarted = true;
        _ = _expand(this);
    }

    /// <summary>Marks the first page as requested by someone other than the expander, so expanding does not ask twice.</summary>
    internal void MarkExpansionStarted() => _expansionStarted = true;

    private static string DetailOf(Hdf5Object o) => o switch
    {
        { Dataset: { } d }            => $"{d.Type.DisplayName} · {ShapeText(d.Space)}",
        { Problem: { Length: > 0 } p } => p,
        _                              => string.Empty,
    };

    /// <summary>A dataspace as the viewer names it — dimensions, or the word for a scalar or a null space.</summary>
    internal static string ShapeText(Hdf5Dataspace space) => space.Kind switch
    {
        Hdf5SpaceKind.Scalar => Str.Get("Hdf5.Space.Scalar"),
        Hdf5SpaceKind.Null   => Str.Get("Hdf5.Space.Null"),
        _                    => Hdf5Dataspace.FormatDims(space.Shape.Select(d => (ulong?)d)),
    };
}
