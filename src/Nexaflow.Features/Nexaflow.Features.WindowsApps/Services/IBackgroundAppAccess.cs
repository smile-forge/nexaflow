using Nexaflow.Features.WindowsApps.Models;

namespace Nexaflow.Features.WindowsApps.Services;

/// <summary>
/// Reads and writes a Store app's background-execution policy — the "Let this app run in background"
/// choice. Behind an interface because the only real implementation writes to the live user hive:
/// tests substitute an in-memory one rather than retuning the machine they run on.
/// </summary>
public interface IBackgroundAppAccess
{
    /// <summary>The current policy for <paramref name="packageFamilyName"/>; the default when unset.</summary>
    BackgroundAppMode Get(string packageFamilyName);

    /// <summary>Applies <paramref name="mode"/>. False when the policy store couldn't be written.</summary>
    bool Set(string packageFamilyName, BackgroundAppMode mode);
}
