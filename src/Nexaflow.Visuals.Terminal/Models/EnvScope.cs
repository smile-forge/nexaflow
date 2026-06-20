namespace Nexaflow.Visuals.Terminal.Models;

/// <summary>Where an environment-variable edit is written.</summary>
public enum EnvScope
{
    /// <summary>Only the running shell, via a <c>set</c>/<c>$env:</c> command. Lost when the tab closes.</summary>
    Session,

    /// <summary>The current user's persistent environment (HKCU). No elevation needed.</summary>
    User,

    /// <summary>The machine-wide environment (HKLM). Applied via the elevated privilege bridge.</summary>
    Machine
}
