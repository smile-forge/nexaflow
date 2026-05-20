using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;

namespace Nexaflow.Core.ViewModels;

// ── Editor kind discriminator ─────────────────────────────────────────────────

public enum PropertyEditorKind
{
    TextBox,
    FolderPath,
    EnumComboBox,
    ListComboBox,
}

// ── Per-property view model ───────────────────────────────────────────────────

public partial class PropertyEditViewModel : ObservableObject
{
    private readonly PropertyInfo _pi;
    private readonly object       _editingClone;
    private readonly Action       _onChanged;   // notifies ConfigEditViewModel to recheck validity

    public string             Label        { get; }
    public string             PropertyName { get; }
    public PropertyEditorKind EditorKind   { get; }

    /// <summary>Enum names for EnumComboBox editors.</summary>
    public IReadOnlyList<string>? EnumOptions { get; }

    /// <summary>Dynamic items for ListComboBox editors (populated via [ListSource]).</summary>
    public IReadOnlyList<string>? ListOptions { get; }

    [ObservableProperty] private object? _value;
    [ObservableProperty] private string? _validationError;

    public bool IsValid => ValidationError is null;

    partial void OnValueChanged(object? value)
    {
        try
        {
            _pi.SetValue(_editingClone, ConvertToTargetType(value, _pi.PropertyType));
        }
        catch { /* type mismatch — ignore */ }

        Validate(value);
        _onChanged();
    }

    private void Validate(object? value)
    {
        if (EditorKind == PropertyEditorKind.FolderPath)
        {
            var path = value as string;
            ValidationError = string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)
                ? "Directory does not exist"
                : null;
        }
        else
        {
            ValidationError = null;
        }
    }

    private static object? ConvertToTargetType(object? value, Type targetType)
    {
        if (value is null) return null;
        if (targetType.IsEnum && value is string s)
            return Enum.Parse(targetType, s);
        return value;
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var selected = FolderBrowserWindow.Show(Value as string);
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

        var folderAttr  = pi.GetCustomAttribute<FolderPathAttribute>();
        var listAttr    = pi.GetCustomAttribute<ListSourceAttribute>();

        if (pi.PropertyType.IsEnum)
        {
            EditorKind  = PropertyEditorKind.EnumComboBox;
            EnumOptions = Enum.GetNames(pi.PropertyType);
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
        else
        {
            EditorKind = PropertyEditorKind.TextBox;
        }

        // Snapshot current value; store enums as strings so ComboBox string-items can bind
        var raw = pi.GetValue(editingClone);
        _value = pi.PropertyType.IsEnum && raw is not null ? raw.ToString() : raw;
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

    [ObservableProperty] private bool _isValid = true;

    /// <summary>
    /// When set, the Options panel renders this control instead of the property grid.
    /// The control's DataContext is the live <see cref="RealConfig"/> instance.
    /// </summary>
    public object? CustomControlInstance { get; }
    public bool    HasCustomControl      => CustomControlInstance is not null;

    private void RecheckValidity()
    {
        IsValid = Properties.All(p => p.IsValid);
    }

    public void ApplyToReal()
    {
        if (HasCustomControl)
        {
            if (CustomControlInstance is ICustomConfigApply applyable)
                applyable.Apply();
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
        var customAttr = realConfig.GetType().GetCustomAttribute<Nexaflow.Features.Common.CustomControlAttribute>();
        if (customAttr is not null)
        {
            try
            {
                var ctrl = System.Activator.CreateInstance(customAttr.ControlType);
                if (ctrl is System.Windows.FrameworkElement fe)
                    fe.DataContext = realConfig;
                CustomControlInstance = ctrl;
            }
            catch { /* fall back to property grid if control can't be instantiated */ }

            RecheckValidity();
            return;  // skip property reflection
        }

        // Reflect over the concrete type; skip interface-declared identity members
        var skip = new HashSet<string> { "ConfigName", "FriendlyName" };
        foreach (var pi in EditingClone.GetType()
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanRead && !skip.Contains(p.Name)))
        {
            Properties.Add(new PropertyEditViewModel(pi, EditingClone, RecheckValidity));
        }

        RecheckValidity();
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

    public OptionsViewModel()
    {
        foreach (var config in ConfigManager.Instance.GetAll())
        {
            string friendlyName = config is IFeatureConfig fc ? fc.FriendlyName
                                : config is IProviderConfig pc ? pc.FriendlyName
                                : config.GetType().Name;
            string configName   = config is IFeatureConfig fc2 ? fc2.ConfigName
                                : config is IProviderConfig pc2 ? pc2.ConfigName
                                : config.GetType().Name.ToLowerInvariant();

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

        // Request tab refresh for feature configs
        var pageKindsToRefresh = Sections
            .Where(s => s.RealConfig is IFeatureConfig)
            .SelectMany(s => FeatureManager.Instance.GetPageKindsForConfig(s.RealConfig.GetType()))
            .Distinct()
            .ToList();

        if (pageKindsToRefresh.Count > 0)
            TabRefreshRequested?.Invoke(pageKindsToRefresh);

        SaveCompleted?.Invoke();
    }
}
