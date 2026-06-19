using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.Controls;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Nexaflow.Core.ViewModels;

// ── Editor kind discriminator ─────────────────────────────────────────────────

public enum PropertyEditorKind
{
    TextBox,
    FolderPath,
    FilePath,
    EnumComboBox,
    ListComboBox,
    Toggle,
}

/// <summary>One choice in an enum combo: the persisted enum <paramref name="Name"/> and a friendly
/// <paramref name="Display"/> label (the enum's [Description] when set, else the name).</summary>
public sealed record EnumOption(string Name, string Display);

// ── Per-property view model ───────────────────────────────────────────────────

public partial class PropertyEditViewModel : ObservableObject
{
    /// <summary>The [Description] of an enum field if present, otherwise the field name.</summary>
    private static string EnumDisplayName(Type enumType, string name)
        => enumType.GetField(name)?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? name;

    private readonly PropertyInfo _pi;
    private readonly object       _editingClone;
    private readonly Action       _onChanged;   // notifies ConfigEditViewModel to recheck validity
    private readonly string[]     _fileExtensions = [];   // allowed extensions for FilePath editors

    public string             Label        { get; }
    public string             PropertyName { get; }
    public PropertyEditorKind EditorKind   { get; }
    public bool               IsRequired   { get; }

    /// <summary>Stable UI-automation id for this field's editor (e.g. <c>cfg_ApiKey</c>) so tests
    /// can drive a specific property regardless of label/layout.</summary>
    public string             AutomationId => $"cfg_{PropertyName}";

    /// <summary>Name of a sibling property whose state greys out this editor (DisabledIfSet/DisabledIfNotSet).</summary>
    public string?            DisabledIfProperty    { get; }
    /// <summary>True when the editor is disabled while the sibling is SET (DisabledIfSet); false for the inverse.</summary>
    public bool               DisabledWhenSiblingSet { get; }

    /// <summary>Enum options for EnumComboBox editors: the underlying name plus a friendly display
    /// label (from a <see cref="System.ComponentModel.DescriptionAttribute"/> when present).</summary>
    public IReadOnlyList<EnumOption>? EnumOptions { get; }

    /// <summary>Dynamic items for ListComboBox editors (populated via [ListSource]).</summary>
    public IReadOnlyList<string>? ListOptions { get; }

    [ObservableProperty] private object? _value;
    [ObservableProperty] private string? _validationError;
    [ObservableProperty] private bool    _isEnabled = true;

    private object? _originalValue;

    public bool IsValid    => ValidationError is null;
    public bool HasChanged => !Equals(Value, _originalValue);

    [RelayCommand]
    private void ClearValue() => Value = string.Empty;

    public void ResetOriginal() => _originalValue = Value;

    partial void OnValueChanged(object? value)
    {
        try
        {
            _pi.SetValue(_editingClone, ConvertToTargetType(value, _pi.PropertyType));
        }
        catch { /* type mismatch — ignore */ }

        if (IsEnabled) Validate(value);
        _onChanged();
    }

    // A disabled field (e.g. project directory while "Enable projects" is off) must not show as
    // invalid; re-validate when it becomes enabled again.
    partial void OnIsEnabledChanged(bool value)
    {
        if (value) Validate(Value);
        else       ValidationError = null;
        _onChanged();
    }

    private void Validate(object? value)
    {
        var path = value as string;
        ValidationError = EditorKind switch
        {
            // Empty is allowed (no-op); a non-empty path must exist.
            PropertyEditorKind.FolderPath when !string.IsNullOrWhiteSpace(path) && !Directory.Exists(path)
                => "Directory does not exist",
            PropertyEditorKind.FilePath when !string.IsNullOrWhiteSpace(path) && !File.Exists(path)
                => "File does not exist",
            _ => null,
        };
    }

    private static object? ConvertToTargetType(object? value, Type targetType)
    {
        if (value is null) return null;
        var target = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (target.IsEnum && value is string s)
            return Enum.Parse(target, s);

        // TextBox editors hand back strings; coerce to the property's numeric type.
        if (value is string str && target != typeof(string) && target.IsValueType)
        {
            if (string.IsNullOrWhiteSpace(str)) return Activator.CreateInstance(target);
            if (target == typeof(int))     return int.Parse(str);
            if (target == typeof(long))    return long.Parse(str);
            if (target == typeof(double))  return double.Parse(str);
            if (target == typeof(decimal)) return decimal.Parse(str);
        }
        return value;
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var selected = FolderBrowserWindow.Show(Value as string);
        if (selected is not null) Value = selected;
    }

    [RelayCommand]
    private void BrowseFile()
    {
        var selected = FileBrowserWindow.Show(Value as string, _fileExtensions);
        if (selected is not null) Value = selected;
    }

    public PropertyEditViewModel(PropertyInfo pi, object editingClone, Action onChanged)
    {
        _pi          = pi;
        _editingClone = editingClone;
        _onChanged   = onChanged;

        var displayAttr = pi.GetCustomAttribute<Nexaflow.Features.Common.ConfigDisplayNameAttribute>()?.DisplayName
                       ?? pi.GetCustomAttribute<Nexaflow.Providers.Common.ConfigDisplayNameAttribute>()?.DisplayName;
        Label        = displayAttr ?? pi.Name;
        PropertyName = pi.Name;
        IsRequired   = pi.GetCustomAttribute<RequiredAttribute>() is not null;
        var disabledIfSet =
            pi.GetCustomAttribute<Nexaflow.Features.Common.DisabledIfSetAttribute>()?.PropertyName
            ?? pi.GetCustomAttribute<Nexaflow.Providers.Common.DisabledIfSetAttribute>()?.PropertyName;
        var disabledIfNotSet =
            pi.GetCustomAttribute<Nexaflow.Features.Common.DisabledIfNotSetAttribute>()?.PropertyName
            ?? pi.GetCustomAttribute<Nexaflow.Providers.Common.DisabledIfNotSetAttribute>()?.PropertyName;
        if (disabledIfSet is not null)      { DisabledIfProperty = disabledIfSet;    DisabledWhenSiblingSet = true;  }
        else if (disabledIfNotSet is not null) { DisabledIfProperty = disabledIfNotSet; DisabledWhenSiblingSet = false; }

        var folderAttr  = pi.GetCustomAttribute<FolderPathAttribute>();
        var fileAttr    = pi.GetCustomAttribute<FilePathAttribute>();
        var listAttr    = pi.GetCustomAttribute<ListSourceAttribute>();

        if (pi.PropertyType == typeof(bool))
        {
            EditorKind = PropertyEditorKind.Toggle;
        }
        else if (pi.PropertyType.IsEnum)
        {
            EditorKind  = PropertyEditorKind.EnumComboBox;
            EnumOptions = Enum.GetNames(pi.PropertyType)
                .Select(n => new EnumOption(n, EnumDisplayName(pi.PropertyType, n)))
                .ToList();
        }
        else if (listAttr is not null)
        {
            EditorKind   = PropertyEditorKind.ListComboBox;
            ListOptions  = listAttr.Invoke().ToList();
        }
        else if (folderAttr is not null)
        {
            EditorKind = PropertyEditorKind.FolderPath;
        }
        else if (fileAttr is not null)
        {
            EditorKind      = PropertyEditorKind.FilePath;
            _fileExtensions = fileAttr.Extensions.ToArray();
        }
        else
        {
            EditorKind = PropertyEditorKind.TextBox;
        }

        // Snapshot current value; store enums as strings so ComboBox string-items can bind
        var raw = pi.GetValue(editingClone);
        _value         = pi.PropertyType.IsEnum && raw is not null ? raw.ToString() : raw;
        _originalValue = _value;
        // Initial validation
        Validate(_value);
    }
}

// ── Per-section view model ────────────────────────────────────────────────────

public partial class ConfigEditViewModel : ObservableObject
{
    public string FriendlyName { get; }
    public string ConfigName   { get; }
    public object EditingClone { get; }
    public object RealConfig   { get; }

    public ObservableCollection<PropertyEditViewModel> Properties { get; } = [];

    [ObservableProperty] private bool _isValid             = true;
    [ObservableProperty] private bool _hasChanges          = false;
    [ObservableProperty] private bool _isRequiredSatisfied = true;

    /// <summary>
    /// When set, the Options panel renders this control instead of the property grid.
    /// The control's DataContext is the live <see cref="RealConfig"/> instance.
    /// </summary>
    public object? CustomControlInstance { get; }
    public bool    HasCustomControl      => CustomControlInstance is not null;

    private void RecheckValidity()
    {
        IsValid = HasCustomControl
            ? (CustomControlInstance is not Nexaflow.Features.Common.IConfigValidation v || v.IsValid)
            : Properties.All(p => p.IsValid);
        HasChanges = HasCustomControl
            ? (CustomControlInstance is not Nexaflow.Features.Common.IConfigChangeTracker t || t.HasChanges)
            : Properties.Any(p => p.HasChanged);

        var required = Properties.Where(p => p.IsRequired).ToList();
        IsRequiredSatisfied = required.Count == 0
            || required.All(p =>  string.IsNullOrWhiteSpace(p.Value as string))   // all empty = valid (no-op)
            || required.All(p => !string.IsNullOrWhiteSpace(p.Value as string));  // all filled = valid
    }

    /// <summary>
    /// Returns true if <paramref name="config"/> has no [Required] properties or all such properties have non-empty values.
    /// Used by the AI ability grid to filter the provider dropdown to configured providers only.
    /// </summary>
    public static bool AreRequiredPropertiesSatisfied(object config)
    {
        var required = config.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<RequiredAttribute>() is not null)
            .ToList();
        return required.Count == 0
            || required.All(p => !string.IsNullOrWhiteSpace(p.GetValue(config) as string));
    }

    public void ResetChanges()
    {
        foreach (var p in Properties)
            p.ResetOriginal();
        RecheckValidity();
    }

    public void ApplyToReal()
    {
        if (HasCustomControl)
        {
            if (CustomControlInstance is Nexaflow.Features.Common.ICustomConfigApply applyable)
                applyable.Apply();
            else if (CustomControlInstance is Nexaflow.Providers.Common.ICustomConfigApply applyable2)
                applyable2.Apply();
            return;
        }
        foreach (var pi in EditingClone.GetType().GetProperties()
                     .Where(p => p.CanRead && p.CanWrite))
            pi.SetValue(RealConfig, pi.GetValue(EditingClone));
    }

    public ConfigEditViewModel(object realConfig, string configName, string friendlyName)
    {
        RealConfig   = realConfig;
        ConfigName   = configName;
        FriendlyName = friendlyName;
        EditingClone = ConfigManager.Clone(realConfig);

        // Check for a custom control at the class level before doing property reflection
        var customControlType = realConfig.GetType().GetCustomAttribute<Nexaflow.Features.Common.CustomControlAttribute>()?.ControlType
                             ?? realConfig.GetType().GetCustomAttribute<Nexaflow.Providers.Common.CustomControlAttribute>()?.ControlType;
        if (customControlType is not null)
        {
            try
            {
                var ctrl = System.Activator.CreateInstance(customControlType);
                if (ctrl is System.Windows.FrameworkElement fe)
                    fe.DataContext = realConfig;
                CustomControlInstance = ctrl;

                if (ctrl is Nexaflow.Features.Common.IConfigChangeTracker tracker)
                    tracker.HasChangesChanged += (_, _) => RecheckValidity();

                if (ctrl is Nexaflow.Features.Common.IConfigValidation validator)
                    validator.IsValidChanged += (_, _) => RecheckValidity();
            }
            catch { /* fall back to property grid if control can't be instantiated */ }

            RecheckValidity();
            return;  // skip property reflection
        }

        // Reflect over the concrete type; skip interface-declared identity members and
        // read-only/computed properties (they can't be edited and only ApplyToReal-writable
        // properties round-trip on Save).
        var skip = new HashSet<string> { "ConfigName", "FriendlyName" };
        foreach (var pi in EditingClone.GetType()
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanRead && p.CanWrite && !skip.Contains(p.Name)))
        {
            Properties.Add(new PropertyEditViewModel(pi, EditingClone, RecheckValidity));
        }

        WireConditionalEnables();
        RecheckValidity();
    }

    /// <summary>
    /// Wires [DisabledIfSet]: each property that names a sibling is greyed out while that sibling is
    /// "set", re-evaluated whenever the sibling's value changes. Two of them on a pair → mutual exclusion.
    /// </summary>
    private void WireConditionalEnables()
    {
        foreach (var dependent in Properties.Where(p => p.DisabledIfProperty is not null))
        {
            var src = Properties.FirstOrDefault(q => q.PropertyName == dependent.DisabledIfProperty);
            if (src is null) continue;

            void Update() => dependent.IsEnabled =
                dependent.DisabledWhenSiblingSet ? !IsValueSet(src.Value) : IsValueSet(src.Value);
            Update();
            src.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PropertyEditViewModel.Value)) Update();
            };
        }
    }

    private static bool IsValueSet(object? v) => v switch
    {
        null     => false,
        bool b   => b,
        string s => !string.IsNullOrWhiteSpace(s) && s.Trim() != "0",
        _        => System.Convert.ToDouble(v) != 0,
    };
}

// ── Root Options view model ───────────────────────────────────────────────────

public partial class OptionsViewModel : ObservableObject
{
    public ObservableCollection<ConfigEditViewModel> Sections { get; } = [];

    [ObservableProperty] private ConfigEditViewModel? _selectedSection;

    [ObservableProperty] private bool _canSave;

    public event Action?                      SaveCompleted;
    public event Action<string>?              SaveError;
    public event Action<IEnumerable<string>>? TabRefreshRequested;

    public OptionsViewModel()
    {
        // Stable sort: keep registration order but always float "About" to the bottom.
        var configs = ConfigManager.Instance.GetAll().OfType<IFeatureConfig>()
            .OrderBy(c => c is AboutConfig ? 1 : 0);
        foreach (var config in configs)
        {
            string friendlyName = config.FriendlyName;
            string configName   = config.ConfigName;

            var section = new ConfigEditViewModel(config, configName, friendlyName);
            section.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ConfigEditViewModel.IsValid))
                    RecheckCanSave();
            };
            Sections.Add(section);
        }

        SelectedSection = Sections.FirstOrDefault();
        RecheckCanSave();
    }

    private void RecheckCanSave() => CanSave = Sections.All(s => s.IsValid);

    /// <summary>Selects the section for the given config name (e.g. returning to the Workspaces tab).</summary>
    public void SelectSection(string configName)
    {
        var match = Sections.FirstOrDefault(
            s => string.Equals(s.ConfigName, configName, StringComparison.OrdinalIgnoreCase));
        if (match is not null) SelectedSection = match;
    }

    [RelayCommand]
    private void Save()
    {
        if (!CanSave) return;

        foreach (var section in Sections)
        {
            section.ApplyToReal();
            try
            {
                ConfigManager.Instance.Save(section.RealConfig, section.ConfigName);
            }
            catch (Exception ex)
            {
                SaveError?.Invoke($"Could not save {section.FriendlyName} settings: {ex.Message}");
                return;
            }
        }

        // Request tab refresh for feature configs (only meaningful when a window is open).
        var activeCtx = WorkspaceManager.Instance.FirstActive;
        var pageKindsToRefresh = activeCtx is null ? [] : Sections
            .Where(s => s.RealConfig is IFeatureConfig)
            .SelectMany(s => FeatureManager.Instance.GetPageKindsForConfig(
                s.RealConfig.GetType(),
                activeCtx))
            .Distinct()
            .ToList();

        if (pageKindsToRefresh.Count > 0)
            TabRefreshRequested?.Invoke(pageKindsToRefresh);

        SaveCompleted?.Invoke();
    }
}
