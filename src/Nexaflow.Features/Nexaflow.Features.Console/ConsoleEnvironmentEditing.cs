using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Features.Console;

/// <summary>
/// The rules the environments editor enforces while the user is rearranging their shell setups.
/// <para>
/// A folder binding pins a location to an environment <i>by name</i>, so a rename is the moment every
/// pin someone made can quietly stop resolving — the folder still opens, just with the wrong shell, and
/// nothing announces it. These are pulled out of the editor's code-behind because that is where the
/// consequence is least visible and the logic is otherwise unreachable without standing up the control.
/// </para>
/// </summary>
public static class ConsoleEnvironmentEditing
{
    /// <summary>
    /// Whether a rename from <paramref name="oldName"/> to <paramref name="newName"/> should carry that
    /// environment's pinned locations across. It should — unless another environment still answers to the
    /// old name, in which case the pins already belong to that one and moving them would steal them.
    /// </summary>
    public static bool ShouldMigrateBindings(string? oldName, string? newName, IEnumerable<string> otherEnvNames)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) return false;
        if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase)) return false;
        return !otherEnvNames.Any(n => string.Equals(n, oldName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A name no existing environment is using: the seed itself, else the seed with the lowest free
    /// suffix. Two environments sharing a name would make every binding to it ambiguous.
    /// </summary>
    public static string UniqueName(string seed, IEnumerable<string> existing)
    {
        var taken = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(seed)) return seed;
        for (var i = 2; ; i++)
        {
            var candidate = $"{seed} {i}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    /// <summary>Whether the Remove button is live: a shell always needs at least one environment to launch
    /// with, so the last one cannot be deleted.</summary>
    public static bool CanRemoveEnvironment(int environmentCount) => environmentCount > 1;
}
