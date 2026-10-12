namespace Nexaflow.Providers.Aria;

/// <summary>A <c>#focustab</c> navigation instruction the Aria service may embed in a reply.</summary>
public record FocusTabInstruction(string TabName, string Params);
