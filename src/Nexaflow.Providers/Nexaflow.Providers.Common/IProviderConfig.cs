namespace Nexaflow.Providers.Common;

/// <summary>
/// Marks a plain POCO as a provider configuration section.
/// Implement this on config classes for LLM providers so they can be discovered
/// and displayed in the Options panel.
/// </summary>
public interface IProviderConfig
{
    string ConfigName   { get; }
    string FriendlyName { get; }
}
