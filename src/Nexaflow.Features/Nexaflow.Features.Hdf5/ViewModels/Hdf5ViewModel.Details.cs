using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.IO.Hdf5;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Hdf5.ViewModels;

/// <summary>The details drawer: what the selected object is, how it is stored, and its attributes.</summary>
public sealed partial class Hdf5ViewModel
{
    private IReadOnlyList<Hdf5AttributeRow> _allAttributes = [];

    public ObservableCollection<Hdf5DetailRow> ObjectDetails { get; } = [];
    public ObservableCollection<Hdf5DetailRow> StorageDetails { get; } = [];
    public ObservableCollection<Hdf5AttributeRow> Attributes { get; } = [];

    /// <summary>Every attribute of the selected object, filter or not.</summary>
    public IReadOnlyList<Hdf5AttributeRow> AllAttributes => _allAttributes;

    [ObservableProperty] private bool _detailsOpen = true;
    [ObservableProperty] private bool _hasStorage;
    [ObservableProperty] private string _attributeFilter = string.Empty;
    [ObservableProperty] private string? _attributesProblem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoAttributes))]
    private bool _isLoadingAttributes;

    public string AttributesHeader => Str.Format("Hdf5.Details.AttributesHeaderFormat", _allAttributes.Count);

    public bool HasNoAttributes => !IsLoadingAttributes && AttributesProblem is null && _allAttributes.Count == 0;

    private void ShowDetails(Hdf5Object o)
    {
        ObjectDetails.Clear();
        StorageDetails.Clear();
        ObjectDetails.Add(new(Str.Get("Hdf5.Details.Path"), o.Path));
        ObjectDetails.Add(new(Str.Get("Hdf5.Details.Kind"), KindText(o.Kind)));
        if (o.Dataset is { } d)
        {
            ObjectDetails.Add(new(Str.Get("Hdf5.Details.Type"), d.Type.DisplayName));
            ObjectDetails.Add(new(Str.Get("Hdf5.Details.Shape"), Hdf5NodeViewModel.ShapeText(d.Space)));
            if (d.Space.Kind == Hdf5SpaceKind.Simple)
                ObjectDetails.Add(new(Str.Get("Hdf5.Details.MaxShape"), Hdf5Dataspace.FormatDims(d.Space.MaxShape)));
            ObjectDetails.Add(new(Str.Get("Hdf5.Details.Elements"), d.Space.ElementCount.ToString("N0", CultureInfo.CurrentCulture)));
            ObjectDetails.Add(new(Str.Get("Hdf5.Details.ElementSize"), Str.Format("Hdf5.Details.BytesFormat", d.Type.Size)));

            StorageDetails.Add(new(Str.Get("Hdf5.Details.Layout"), LayoutText(d.Layout)));
            if (d.ChunkShape.Count > 0)
                StorageDetails.Add(new(Str.Get("Hdf5.Details.ChunkShape"), Hdf5Dataspace.FormatDims(d.ChunkShape.Select(c => (ulong?)c))));
            StorageDetails.Add(new(Str.Get("Hdf5.Details.FillValue"), d.FillValue ?? Str.Get("Hdf5.Details.FillValueNone")));
        }
        if (o.Problem is { Length: > 0 } problem) ObjectDetails.Add(new(Str.Get("Hdf5.Details.Problem"), problem));
        HasStorage = StorageDetails.Count > 0;
    }

    private async Task LoadAttributesAsync(Hdf5Object o, CancellationToken ct)
    {
        _allAttributes      = [];
        AttributesProblem   = null;
        IsLoadingAttributes = true;
        ApplyAttributeFilter();
        if (Source is not { } src) return;

        try
        {
            var attributes = await Task.Run(() => src.Attributes(o.Path, ct), ct);
            if (ct.IsCancellationRequested) return;
            _allAttributes = [.. attributes.Select(RowOf)];
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) when (ex is Hdf5Exception or ObjectDisposedException)
        {
            AttributesProblem = ex.Message;
        }
        IsLoadingAttributes = false;
        ApplyAttributeFilter();
    }

    private static Hdf5AttributeRow RowOf(Hdf5Attribute a)
    {
        var type = a switch
        {
            { Type: null }                                    => string.Empty,
            { Space: { Kind: Hdf5SpaceKind.Simple } s, Type: { } t } => $"{t.DisplayName} [{Hdf5Dataspace.FormatDims(s.Shape.Select(d => (ulong?)d))}]",
            { Type: { } t }                                   => t.DisplayName,
        };
        var note = a.Problem is { } problem
            ? Str.Format("Hdf5.Details.AttributeProblemFormat", problem)
            : a.IsTruncated
                ? Str.Format("Hdf5.Details.TruncatedFormat", Hdf5Attribute.PreviewElements, a.Space?.ElementCount ?? 0)
                : null;
        return new Hdf5AttributeRow(a.Name, type, a.Value, note);
    }

    partial void OnAttributeFilterChanged(string value) => ApplyAttributeFilter();

    partial void OnAttributesProblemChanged(string? value) => OnPropertyChanged(nameof(HasNoAttributes));

    private void ApplyAttributeFilter()
    {
        var filter = AttributeFilter.Trim();
        Attributes.Clear();
        foreach (var row in _allAttributes.Where(r => r.Matches(filter))) Attributes.Add(row);
        OnPropertyChanged(nameof(AttributesHeader));
        OnPropertyChanged(nameof(HasNoAttributes));
    }
}
