using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Tabular.Detection;
using Nexaflow.Visuals.Common.Controls;

namespace Nexaflow.Features.Tabular.ViewModels;

public sealed partial class TabularColumnViewModel : VirtualizedColumn
{
    [ObservableProperty] private CsvDataType   _detectedType  = CsvDataType.String;
    [ObservableProperty] private CsvDataType   _displayType   = CsvDataType.String;
    /// <summary>True iff the user explicitly set the type (via Evaluate As). When false,
    /// the orchestrator re-classifies from sample data after every window refresh.</summary>
    [ObservableProperty] private bool          _isTypeExplicit;

    /// <summary>0-based index in the current effective column layout (updated after morphs).</summary>
    [ObservableProperty] private int _index;

    /// <summary>Filter object — its concrete type depends on <see cref="DisplayType"/>.</summary>
    [ObservableProperty] private ColumnFilter _filter = new StringColumnFilter();

    /// <summary>
    /// Per-column sample values gathered from the initial 150-row window. Drives the
    /// dynamic header context menu — only show "Split by &lt;sep&gt;" for separators that
    /// actually appear in this column's data, and "Evaluate as &lt;type&gt;" for types
    /// that successfully parse this column's data.
    /// </summary>
    public List<string> SampleValues { get; } = new();

    public string TypeIcon => CsvDataTypeIcons.Glyph(DisplayType);

    public TabularColumnViewModel() => Glyph = TypeIcon;

    /// <summary>Forces a Filter-changed notification when only inner state of the existing
    /// filter object changed (the auto-generated setter short-circuits on reference equality).</summary>
    public void NotifyFilterChanged() => OnPropertyChanged(nameof(Filter));

    partial void OnDisplayTypeChanged(CsvDataType value)
    {
        OnPropertyChanged(nameof(TypeIcon));
        Glyph = TypeIcon;
        // Reset filter to a type-appropriate instance when the type changes.
        Filter = ColumnFilter.ForType(value);
    }
}
