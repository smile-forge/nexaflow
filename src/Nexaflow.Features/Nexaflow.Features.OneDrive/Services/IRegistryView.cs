namespace Nexaflow.Features.OneDrive.Services;

/// <summary>
/// The sliver of the registry detection needs, behind an interface so <see cref="OneDriveDetector"/>
/// can be exercised against real-world key shapes — a blank account, a pseudo-account, no OneDrive at
/// all — without a machine configured to match.
/// </summary>
public interface IRegistryView
{
    /// <summary>Child key names under <paramref name="path"/>, or empty when it doesn't exist.</summary>
    IReadOnlyList<string> SubKeyNames(string path);

    /// <summary>A string value, or null when the key or value is absent (or isn't a string).</summary>
    string? GetString(string path, string valueName);
}
