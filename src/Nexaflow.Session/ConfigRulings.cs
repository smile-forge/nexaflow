namespace Nexaflow.Session;

/// <summary>
/// A setting decided outside the app while it runs — by the machine's AI host service, say — which Settings shows as decided there: its
/// value, read-only, and why (<see cref="Reason"/>). The app's own value stays in the config, for when the app decides it again.
/// </summary>
public sealed record ConfigRuling(object? Value, string Reason);

/// <summary>Rules on settings the app does not decide alone, asked through <see cref="ConfigManager.RulingFor"/>.</summary>
public interface IConfigRulings
{
    /// <summary>The ruling on <paramref name="propertyName"/> of <paramref name="config"/> now; null where the app decides it.</summary>
    ConfigRuling? RulingFor(object config, string propertyName);
}
