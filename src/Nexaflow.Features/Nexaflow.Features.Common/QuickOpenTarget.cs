using System;

namespace Nexaflow.Features.Common;

/// <summary>
/// A page or ribbon shortcut the AI-input quick-open can jump to: its display <see cref="Label"/> (matched
/// by name) and the <see cref="Open"/> action that opens it. Supplied by
/// <see cref="Services.IShellServices.GetQuickOpenTargets"/> so a query handler can enumerate and open
/// targets without reaching into shell chrome.
/// </summary>
public sealed record QuickOpenTarget(string Label, Action Open);
