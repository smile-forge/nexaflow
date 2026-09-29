using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.Controls;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;
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

// ── Per-property view model ───────────────────────────────────────────────────

public partial class PropertyEditViewModel : ObservableObject
{
    /// <summary>The string-table key a config property or enum field names with [ConfigDisplayName] — the feature's
    /// attribute or the provider's — or null when it names none.</summary>
    internal static string? LabelKey(MemberInfo member)
        => member.GetCustomAttribute<Nexaflow.Features.Common.ConfigDisplayNameAttribute>()?.DisplayName
           ?? member.GetCustomAttribute<Nexaflow.Providers.Common.ConfigDisplayNameAttribute>()?.DisplayName;

    /// <summary>What an enum value reads as: its [ConfigDisplayName] key looked up, otherwise the field name.</summary>
    private static string EnumDisplayName(Type enumType, string name)
        => enumType.GetField(name) is { } field && LabelKey(field) is { } key ? Str.Get(key) : name;

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

    /// <summary>Enum options for EnumComboBox editors: the underlying name as the value, and what it reads as.</summary>
    public IReadOnlyList<ConfigListOption>? EnumOptions { get; }

    /// <summary>Dynamic items for ListComboBox editors (populated via [ListSource]).</summary>
    public IReadOnlyList<ConfigListOption>? ListOptions { get; }

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
                => Str.Get("Shell.Options.DirectoryMissing"),
            PropertyEditorKind.FilePath when !string.IsNullOrWhiteSpace(path) && !File.Exists(path)
                => Str.Get("Shell.Options.FileMissing"),
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

        Label        = LabelKey(pi) is { } key ? Str.Get(key) : pi.Name;
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
                .Select(n => new ConfigListOption(n, EnumDisplayName(pi.PropertyType, n)))
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

    // Explicit-control section: an already-instantiated custom control (not discovered from a
    // [CustomControl] attribute). The control owns its own editing state + persistence (its
    // ICustomConfigApply.Apply), so there's no reflected property grid. Used for the workspace-identity
    // page, whose editor targets a Workspace that can't carry a Core control reference.
    private ConfigEditViewModel(System.Windows.FrameworkElement control, string configName, string friendlyName)
    {
        RealConfig   = control;
        EditingClone = control;
        ConfigName   = configName;
        FriendlyName = friendlyName;
        CustomControlInstance = control;

        if (control is Nexaflow.Features.Common.IConfigChangeTracker tracker)
            tracker.HasChangesChanged += (_, _) => RecheckValidity();
        if (control is Nexaflow.Features.Common.IConfigValidation validator)
            validator.IsValidChanged += (_, _) => RecheckValidity();

        RecheckValidity();
    }

    /// <summary>Builds a section backed by an explicit custom control (see the private constructor).</summary>
    public static ConfigEditViewModel ForCustomControl(
        System.Windows.FrameworkElement control, string configName, string friendlyName)
        => new(control, configName, friendlyName);

    public ConfigEditViewModel(object realConfig, string configName, string friendlyName,
                               Nexaflow.Features.Common.IShellServices? shell = null)
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

                // Hand the control the shell so it can use the themed file/folder pickers.
                if (ctrl is Nexaflow.Features.Common.IShellAware aware && shell is not null)
                    aware.AttachShell(shell);

                if (ctrl is Nexaflow.Features.Common.IConfigChangeTracker tracker)
                    tracker.HasChangesChanged += (_, _) => RecheckValidity();

                if (ctrl is Nexaflow.Features.Common.IConfigValidation validator)
                    validator.IsValidChanged += (_, _) => RecheckValidity();
            }
            catch { /* fall back to property grid if control can't be instantiated */ }

            RecheckValidity();
            return;  // skip property reflection
        }

        foreach (var pi in EditableProperties(EditingClone.GetType()))
            Properties.Add(new PropertyEditViewModel(pi, EditingClone, RecheckValidity));

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

    /// <summary>
    /// The properties the grid gives a row: public and read-write — only those round-trip through ApplyToReal on
    /// Save — other than the interface's identity members, and of a type an editor can hold. A list or an object a
    /// feature persists for itself (the Solver's recent symbols) is state, not a setting.
    /// </summary>
    internal static IEnumerable<PropertyInfo> EditableProperties(Type configType)
        => configType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite
                        && p.Name is not (nameof(IFeatureConfig.ConfigName) or nameof(IFeatureConfig.FriendlyName))
                        && IsEditable(p.PropertyType));

    private static bool IsEditable(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t == typeof(string) || t == typeof(bool) || t.IsEnum
            || t == typeof(int) || t == typeof(long) || t == typeof(double) || t == typeof(decimal);
    }
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

    public OptionsViewModel(Nexaflow.Features.Common.IShellServices? shell = null)
    {
        // Options lists every feature's global config. With lazy feature loading a config is registered only
        // when its assembly activates, so make sure they're all activated before reading the registry —
        // otherwise the panel would be missing sections for features not yet warmed up.
        FeatureCatalog.Instance.EnsureAllActivated();

        // Stable sort: keep registration order but always float "About" to the bottom.
        var configs = ConfigManager.Instance.GetAll().OfType<IFeatureConfig>()
            .OrderBy(c => c is AboutConfig ? 1 : 0);
        foreach (var config in configs)
        {
            string friendlyName = config.FriendlyName;
            string configName   = config.ConfigName;

            // Pass the shell so IShellAware custom controls (e.g. the File Type Actions editor's themed
            // confirmation, file/folder pickers) get AttachShell'd.
            var section = new ConfigEditViewModel(config, configName, friendlyName, shell);
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

        // Read before anything is applied: applying is what clears a custom editor's change flag.
        var changed = ConfigTypesToRefresh(Sections);

        foreach (var section in Sections)
        {
            section.ApplyToReal();
            try
            {
                ConfigManager.Instance.Save(section.RealConfig, section.ConfigName);
            }
            catch (Exception ex)
            {
                SaveError?.Invoke(Str.Format("Shell.Options.SaveFailed", section.FriendlyName, ex.Message));
                return;
            }
        }

        // Request tab refresh for the feature configs that changed (only meaningful when a window is open).
        // A refresh closes and reopens the tab, so reopening every feature's tabs on any save threw away
        // pages whose settings nobody touched — a Network sweep in flight among them.
        var activeCtx = WorkspaceManager.Instance.FirstActive;
        var pageKindsToRefresh = activeCtx is null ? [] : changed
            .SelectMany(type => FeatureManager.Instance.GetPageKindsForConfig(type, activeCtx))
            .Distinct()
            .ToList();

        if (pageKindsToRefresh.Count > 0)
            TabRefreshRequested?.Invoke(pageKindsToRefresh);

        SaveCompleted?.Invoke();
    }

    /// <summary>The feature config types whose tabs a save should reopen: the ones whose section changed.</summary>
    internal static IReadOnlyList<Type> ConfigTypesToRefresh(IEnumerable<ConfigEditViewModel> sections)
        => [.. sections.Where(s => s.HasChanges && s.RealConfig is IFeatureConfig)
                       .Select(s => s.RealConfig.GetType())
                       .Distinct()];
}
