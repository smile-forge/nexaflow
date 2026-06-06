using System.Collections;
using Nexaflow.Features.SystemInfo.Models;

namespace Nexaflow.Features.SystemInfo.Services;

/// <summary>
/// Reads User and Machine environment variables. Read-only — User edits happen in-process and Machine
/// edits go through the privilege bridge. Values are kept raw (registry targets return them unexpanded,
/// preserving <c>%VAR%</c> indirection). Synchronous: call off the UI thread.
/// </summary>
public sealed class EnvVarsCollector
{
    public EnvVarsBundle CollectAll() => new(
        Collect(EnvScope.User),
        Collect(EnvScope.Machine));

    public IReadOnlyList<EnvVarRow> Collect(EnvScope scope)
    {
        var target = scope == EnvScope.User
            ? EnvironmentVariableTarget.User
            : EnvironmentVariableTarget.Machine;

        var rows = new List<EnvVarRow>();
        foreach (DictionaryEntry kv in Environment.GetEnvironmentVariables(target))
        {
            if (kv.Key is not string name) continue;
            rows.Add(new EnvVarRow(name, kv.Value?.ToString() ?? "", scope));
        }
        return rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
