namespace Nexaflow.Features.Common;

/// <summary>Friendly label shown for a property row in the Options panel.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ConfigDisplayNameAttribute(string displayName) : Attribute
{
    public string DisplayName { get; } = displayName;
}

/// <summary>
/// Marks a string property as a filesystem folder path.
/// The Options panel renders a TextBox with a "…" browse button and validates that the path exists.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FolderPathAttribute : Attribute { }

/// <summary>
/// Marks a string property as a filesystem file path.
/// The Options panel renders a TextBox with a "…" browse button (a styled file picker)
/// and validates that a non-empty path points at an existing file.
/// </summary>
/// <param name="extensions">
/// Allowed file extensions, e.g. ".exe". When given, the picker only offers matching files.
/// Empty means any file.
/// </param>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FilePathAttribute(params string[] extensions) : Attribute
{
    public IReadOnlyList<string> Extensions { get; } = extensions;
}

/// <summary>
/// Applied to a property to grey out its editor whenever the named sibling property is "set"
/// (a true bool, a non-zero number, or a non-empty string). Use two of these — one on each property —
/// to make a pair mutually exclusive.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class DisabledIfSetAttribute(string propertyName) : Attribute
{
    public string PropertyName { get; } = propertyName;
}

/// <summary>The inverse of <see cref="DisabledIfSetAttribute"/>: grey out this editor UNTIL the named
/// sibling property is set (e.g. a project folder only matters once "Enable projects" is on).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class DisabledIfNotSetAttribute(string propertyName) : Attribute
{
    public string PropertyName { get; } = propertyName;
}

/// <summary>
/// Marks a string property as list-sourced.
/// The Options panel invokes <see cref="SourceType"/>.<see cref="MethodName"/>() —
/// a public static parameterless method — and uses the returned <see cref="IEnumerable{T}"/>
/// of strings as ComboBox items.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ListSourceAttribute(Type sourceType, string methodName) : Attribute
{
    public Type   SourceType { get; } = sourceType;
    public string MethodName { get; } = methodName;

    public IEnumerable<string> Invoke()
    {
        var m = SourceType.GetMethod(MethodName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        return m?.Invoke(null, null) as IEnumerable<string> ?? [];
    }
}

/// <summary>
/// Applied to an <see cref="IFeatureConfig"/> class to replace the default property-grid
/// editor in Options with a custom WPF UserControl.
/// The control's DataContext is set to the config instance before display.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CustomControlAttribute(Type controlType) : Attribute
{
    public Type ControlType { get; } = controlType;
}

/// <summary>
/// Applied to an <see cref="IFeatureConfig"/> class to mark it as <b>per-workspace</b> rather than
/// global. A scoped config is never registered with the global <c>ConfigManager</c>; instead one
/// instance is created per workspace and stored under <c>Contexts\&lt;name&gt;\&lt;ConfigName&gt;</c>
/// (exactly like a provider config), edited from the per-workspace Configure panel, and injected
/// into features from the owning workspace. Default (no attribute) = global.
/// A feature needing both scopes ships two config classes — one annotated, one not.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class WorkspaceScopedConfigAttribute : Attribute { }

/// <summary>
/// Applied to a config class (feature, provider persona, etc.) to include it as a step in the
/// first-run / post-update setup wizard. Without it, the config is skipped by the wizard (it stays
/// fully editable in Options / the Configure panel). Use it only for settings a user truly must see
/// up front — the wizard's essential AI bootstrap (provider, key, model) always runs regardless.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class MandatorySetupAttribute : Attribute { }

/// <summary>
/// Implemented by a custom Options control to participate in the Options panel Save flow.
/// <see cref="Apply"/> is called when the user clicks Save in the Options panel.
/// </summary>
public interface ICustomConfigApply
{
    void Apply();
}

/// <summary>
/// Optionally implemented by a custom config control to expose whether its
/// current state differs from the last saved state.
/// Controls that don't implement this are assumed to always have changes.
/// </summary>
public interface IConfigChangeTracker
{
    bool HasChanges { get; }
    event EventHandler? HasChangesChanged;
}

/// <summary>
/// Optionally implemented by a custom config control to report whether its current state is
/// valid. The Options panel blocks Save while any section reports invalid. Controls that don't
/// implement this are always considered valid.
/// </summary>
public interface IConfigValidation
{
    bool IsValid { get; }
    event EventHandler? IsValidChanged;
}
