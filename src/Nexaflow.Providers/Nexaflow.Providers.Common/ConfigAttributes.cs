namespace Nexaflow.Providers.Common;

[AttributeUsage(AttributeTargets.Property)]
public sealed class ConfigDisplayNameAttribute(string displayName) : Attribute
{
    public string DisplayName { get; } = displayName;
}

/// <summary>
/// Applied to an <see cref="IProviderConfig"/> class to replace the default property-grid
/// editor with a custom WPF UserControl. The control's DataContext is set to the config instance.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CustomControlAttribute(Type controlType) : Attribute
{
    public Type ControlType { get; } = controlType;
}

/// <summary>
/// Implemented by a custom provider-config control to participate in the Apply flow.
/// <see cref="Apply"/> is called when the user clicks Apply in the Manage AI panel.
/// </summary>
public interface ICustomConfigApply
{
    void Apply();
}
