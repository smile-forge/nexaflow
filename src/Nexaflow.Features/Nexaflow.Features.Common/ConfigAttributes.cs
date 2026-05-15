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
/// Implemented by a custom Options control to participate in the Options panel Save flow.
/// <see cref="Apply"/> is called when the user clicks Save in the Options panel.
/// </summary>
public interface ICustomConfigApply
{
    void Apply();
}
