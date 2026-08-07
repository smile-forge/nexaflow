using Nexaflow.Features.OneDrive.Services;

namespace Nexaflow.Tests.Features.OneDrive;

/// <summary>
/// A registry shaped by the test rather than by whatever this machine happens to have. Values are keyed
/// <c>path\value</c>; a missing key or value answers null, exactly as the real view does when OneDrive
/// isn't installed.
/// </summary>
internal sealed class FakeRegistryView : IRegistryView
{
    private const string Accounts = @"Software\Microsoft\OneDrive\Accounts";

    private readonly Dictionary<string, List<string>> _subKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string>       _values  = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds an account subkey with the values a real one carries. Pass null to leave a value
    /// absent, empty to reproduce the blank-but-present case that occurs in the wild.</summary>
    public FakeRegistryView WithAccount(string subKey, string? userFolder,
                                        string? displayName = null, string? userEmail = null)
    {
        if (!_subKeys.TryGetValue(Accounts, out var keys)) _subKeys[Accounts] = keys = [];
        keys.Add(subKey);

        Set(subKey, "UserFolder",  userFolder);
        Set(subKey, "DisplayName", displayName);
        Set(subKey, "UserEmail",   userEmail);
        return this;

        void Set(string key, string name, string? value)
        {
            if (value is not null) _values[$@"{Accounts}\{key}\{name}"] = value;
        }
    }

    public IReadOnlyList<string> SubKeyNames(string path)
        => _subKeys.TryGetValue(path, out var keys) ? keys : [];

    public string? GetString(string path, string valueName)
        => _values.TryGetValue($@"{path}\{valueName}", out var v) ? v : null;
}
