namespace Nexaflow.Providers.Local.ServerTools;

/// <summary>A tool invocation parsed from the model's native output.</summary>
public sealed class ServerToolCall
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Argument map as parsed from the model output (string/number/bool values).</summary>
    public Dictionary<string, object?> Arguments { get; init; } = [];
}
