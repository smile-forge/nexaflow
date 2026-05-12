namespace Nexaflow.Providers.Common;

/// <summary>Response returned by an <see cref="ILlmProvider"/> call.</summary>
public record LlmResponse(string RawText, FocusTabInstruction? FocusTab = null);

/// <summary>Parsed navigation instruction — any provider may embed this in a response.</summary>
public record FocusTabInstruction(string TabName, string Params);
