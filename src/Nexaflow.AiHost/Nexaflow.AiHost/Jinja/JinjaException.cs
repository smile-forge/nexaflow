namespace Nexaflow.AiHost.Jinja;

/// <summary>A template that cannot be read, or that fails as it renders — its own <c>raise_exception</c> included.</summary>
internal sealed class JinjaException(string message, int line = 0) : Exception(line > 0 ? $"{message} (line {line})" : message)
{
    /// <summary>The template line it happened on, or 0 when no line applies.</summary>
    public int Line { get; } = line;
}
