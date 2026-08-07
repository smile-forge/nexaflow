using System.Text.RegularExpressions;
using Nexaflow.Features.OneDrive.Models;

namespace Nexaflow.Features.OneDrive.Services;

/// <summary>
/// Finds the OneDrive sync folders already configured on this machine. Detection only — nothing here
/// talks to Microsoft, so there is no account to sign into and nothing to expire.
/// <para>
/// The source of truth is <c>HKCU\Software\Microsoft\OneDrive\Accounts</c>, one subkey per signed-in
/// account. It is messier than it looks: alongside <c>Personal</c> and <c>Business1</c> the key holds
/// <c>FileCoAuth</c>, a co-authoring bookkeeping entry with every value blank, and a real account can
/// have an empty <c>DisplayName</c>. Both occur on an ordinary machine, so both are handled rather than
/// assumed away.
/// </para>
/// </summary>
public sealed partial class OneDriveDetector(IRegistryView hkcu, Func<string, string?>? environment = null)
{
    private const string AccountsKey = @"Software\Microsoft\OneDrive\Accounts";

    private readonly Func<string, string?> _env = environment ?? Environment.GetEnvironmentVariable;

    /// <summary>Only <c>Personal</c> and <c>Business&lt;n&gt;</c> are accounts; anything else under the
    /// key is bookkeeping.</summary>
    [GeneratedRegex(@"^(Personal|Business\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex AccountKeyPattern { get; }

    /// <summary>
    /// Every account with a usable local sync folder, in registry order. Empty is the normal answer on a
    /// machine without OneDrive and must never be an error.
    /// </summary>
    public IReadOnlyList<SyncAccount> Detect()
    {
        var found = new List<SyncAccount>();

        foreach (var subKey in hkcu.SubKeyNames(AccountsKey))
        {
            if (!AccountKeyPattern.IsMatch(subKey)) continue;

            var path = $@"{AccountsKey}\{subKey}";

            // No folder, no row: an account can be present in the registry yet not syncing anywhere,
            // and FileCoAuth has nothing at all.
            var folder = hkcu.GetString(path, "UserFolder");
            if (string.IsNullOrWhiteSpace(folder)) continue;

            bool isBusiness = subKey.StartsWith("Business", StringComparison.OrdinalIgnoreCase);
            found.Add(new SyncAccount(
                Id:         IdFor(subKey),
                Label:      LabelFor(hkcu.GetString(path, "DisplayName"),
                                     hkcu.GetString(path, "UserEmail"),
                                     isBusiness, subKey),
                FolderPath: folder.Trim(),
                IsBusiness: isBusiness));
        }

        // Only when the registry told us nothing: the environment variables OneDrive sets are a coarser
        // view (no account names, and they can linger after a sign-out), so they are a fallback rather
        // than a supplement — mixing them in would double up every account it did find.
        if (found.Count == 0) found.AddRange(FromEnvironment());

        return found;
    }

    /// <summary>Stable id for an account's registry key. Kept path-segment-safe because it forms the
    /// virtual root <c>::onedrive.Business1</c>.</summary>
    private static string IdFor(string subKey)
        => "onedrive." + new string([.. subKey.Where(char.IsLetterOrDigit)]);

    /// <summary>
    /// Names the row the way Explorer does: the personal account is simply "OneDrive" (there can only be
    /// one, so qualifying it with an email would be noise), and a work account is "OneDrive - Contoso".
    /// Display name, then email, then the account key — a real account frequently has a blank
    /// DisplayName, and two unnamed work accounts must still be told apart.
    /// </summary>
    private static string LabelFor(string? displayName, string? email, bool isBusiness, string subKey)
    {
        if (!isBusiness) return "OneDrive";

        var name = Pick(displayName) ?? Pick(email) ?? subKey;
        return $"OneDrive - {name}";

        static string? Pick(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private IEnumerable<SyncAccount> FromEnvironment()
    {
        // Distinct paths only: %OneDrive% usually duplicates one of the two specific variables.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (variable, id, business) in new[]
        {
            ("OneDriveConsumer",   "onedrive.env.personal", false),
            ("OneDriveCommercial", "onedrive.env.business", true),
            ("OneDrive",           "onedrive.env",          false),
        })
        {
            var path = _env(variable);
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (!seen.Add(path.TrimEnd('\\', '/'))) continue;

            yield return new SyncAccount(id, business ? "OneDrive - Business" : "OneDrive",
                                         path.Trim(), business);
        }
    }
}
