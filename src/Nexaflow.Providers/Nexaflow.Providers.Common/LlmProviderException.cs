namespace Nexaflow.Providers.Common;

/// <summary>
/// Base exception for provider-level failures (connection lost, timeout, auth, etc.).
/// </summary>
public class LlmProviderException : Exception
{
    public LlmProviderException(string message) : base(message) { }
    public LlmProviderException(string message, Exception inner) : base(message, inner) { }
}
