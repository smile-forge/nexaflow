namespace Nexaflow.Providers.Common;

[AttributeUsage(AttributeTargets.Property)]
public sealed class ConfigDisplayNameAttribute(string displayName) : Attribute
{
    public string DisplayName { get; } = displayName;
}

/// <summary>
/// Applied to a property to grey out its editor whenever the named sibling property is "set"
/// (a true bool, a non-zero number, or a non-empty string). Use two of these — one on each property —
/// to make a pair mutually exclusive (e.g. Ollama's "keep loaded while running" vs "keep-alive minutes").
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

/// <summary>Marks a config class to appear as a step in the first-run / Add-Workspace setup wizard.
/// Mirrors <c>Nexaflow.Features.Common.MandatorySetupAttribute</c> for provider-side configs.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class MandatorySetupAttribute : Attribute { }

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
/// <see cref="Apply"/> is called when the user clicks Apply in the per-workspace Configure panel.
/// </summary>
public interface ICustomConfigApply
{
    void Apply();
}
