namespace Nexaflow.Providers.Common;

[AttributeUsage(AttributeTargets.Property)]
public sealed class ConfigDisplayNameAttribute(string displayName) : Attribute
{
    public string DisplayName { get; } = displayName;
}
