using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.ProductManager.Model;

namespace Nexaflow.Features.ProductManager.ViewModels;

/// <summary>Status display text + theme-resolved brushes (colours come from contributed Product.Status.* tokens).</summary>
public static class StatusInfo
{
    public static readonly IReadOnlyList<Status> All = [Status.Should, Status.Shouldnt, Status.Done, Status.Faulted];

    public static string Display(Status s) => s switch
    {
        Status.Should   => "should",
        Status.Shouldnt => "shouldn't",
        Status.Done     => "done",
        Status.Faulted  => "faulted",
        _               => s.ToString().ToLowerInvariant()
    };

    public static Brush Brush(Status s) => Res(s switch
    {
        Status.Done     => "Product.Status.Done",
        Status.Faulted  => "Product.Status.Faulted",
        Status.Should   => "Product.Status.Should",
        _               => "Product.Status.Shouldnt"
    });

    public static Brush Res(string key) => Application.Current?.Resources[key] as Brush ?? Brushes.Gray;
}

/// <summary>
/// One cross-cutting concern shown as a dashboard box: the concern tag + a clickable status that cycles
/// should → shouldn't → done → faulted, an attachment affordance when it carries snaplinks, and (on hover)
/// removal. Edits are gated to the editable (Current) version.
/// </summary>
public partial class ConcernBox : ObservableObject
{
    public ConcernLink Model { get; }
    public bool IsEditable { get; }
    private readonly Action _save;
    private readonly Action<ConcernBox> _remove;
    private readonly Action<ConcernBox> _editSnaplinks;

    [ObservableProperty] private Status _status;

    public ConcernBox(ConcernLink model, bool editable, Action save, Action<ConcernBox> remove, Action<ConcernBox> editSnaplinks)
    {
        Model           = model;
        IsEditable      = editable;
        _save           = save;
        _remove         = remove;
        _editSnaplinks  = editSnaplinks;
        _status         = model.Status;
    }

    public string Tag => Model.Tag;
    public Brush StatusBrush => StatusInfo.Brush(Status);
    public bool HasSnaplinks => Model.Snaplinks is { Count: > 0 };
    public IReadOnlyList<Snaplink> Snaplinks => Model.Snaplinks ?? [];

    partial void OnStatusChanged(Status value)
    {
        Model.Status = value;
        OnPropertyChanged(nameof(StatusBrush));
        _save();
    }

    [RelayCommand]
    private void Cycle()
    {
        if (!IsEditable) return;
        Status = Status switch
        {
            Status.Should   => Status.Shouldnt,
            Status.Shouldnt => Status.Done,
            Status.Done     => Status.Faulted,
            _               => Status.Should
        };
    }

    [RelayCommand] private void Remove() { if (IsEditable) _remove(this); }
    [RelayCommand] private void EditSnaplinks() => _editSnaplinks(this);

    /// <summary>Re-raise the snaplink-derived properties after the concern's snaplink list is edited.</summary>
    public void RefreshSnaplinks()
    {
        OnPropertyChanged(nameof(HasSnaplinks));
        OnPropertyChanged(nameof(Snaplinks));
    }
}

/// <summary>A defined concern row in the Settings overlay: name + a default toggle + remove.</summary>
public partial class ConcernDefRow : ObservableObject
{
    public string Name { get; }
    private readonly Action<ConcernDefRow> _remove;

    [ObservableProperty] private bool _isDefault;

    public ConcernDefRow(string name, bool isDefault, Action<ConcernDefRow> remove)
    {
        Name      = name;
        _isDefault = isDefault;
        _remove   = remove;
    }

    [RelayCommand] private void Remove() => _remove(this);
}

/// <summary>A snaplink row (type + target display + editable status + remove). Used for node and concern snaplinks.</summary>
public partial class SnaplinkRow : ObservableObject
{
    public Snaplink Model { get; }
    private readonly Action _save;
    private readonly Action<SnaplinkRow> _remove;
    private readonly Action<SnaplinkRow>? _open;

    public string Type => Model.Type;
    public string Display => Model.Display;
    public IReadOnlyList<Status> AllStatuses => StatusInfo.All;

    [ObservableProperty] private Status _status;

    public SnaplinkRow(Snaplink model, Action save, Action<SnaplinkRow> remove, Action<SnaplinkRow>? open = null)
    {
        Model   = model;
        _save   = save;
        _remove = remove;
        _open   = open;
        _status = model.Status ?? Status.Should;
    }

    partial void OnStatusChanged(Status value) { Model.Status = value; _save(); }

    [RelayCommand] private void Remove() => _remove(this);
    [RelayCommand] private void Open() => _open?.Invoke(this);
}
