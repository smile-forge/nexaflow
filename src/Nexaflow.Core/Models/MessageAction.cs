using System.Windows.Input;

namespace Nexaflow.Core.Models;

/// <summary>Severity of a <see cref="NotificationItem"/>; drives the toast/inbox icon and colour.</summary>
public enum MessageSeverity { Info, Warning, Error, Update }

/// <summary>
/// An optional action a <see cref="NotificationItem"/> can carry, rendered as a button both in the
/// transient toast and in the persistent inbox list (e.g. "Update", "Retry").
/// </summary>
public sealed record MessageAction(string Label, ICommand Command, bool IsPrimary = false);
