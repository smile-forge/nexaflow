using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Nexaflow.IO.Location.Wsl;

/// <summary>One installed distro, as the registry lists it.</summary>
/// <param name="Version">1 or 2 — the WSL that runs it.</param>
/// <param name="IsDefault">The distro <c>wsl.exe</c> starts when none is named.</param>
internal sealed record WslDistro(string Name, int Version, bool IsDefault);

/// <summary>
/// The distros WSL has installed for this user, read from <c>HKCU\…\Lxss</c> — fast, and without starting
/// <c>wsl.exe</c> or any distro. A distro still being installed or removed is not listed.
/// </summary>
internal static class WslRegistry
{
    public const string LxssKey = @"Software\Microsoft\Windows\CurrentVersion\Lxss";

    // The value an installed distro's State holds; installing and uninstalling are others.
    private const int Installed = 1;

    /// <summary>The distros installed now, the default first; none when WSL is not installed.</summary>
    public static IReadOnlyList<WslDistro> Read()
    {
        using var lxss = Registry.CurrentUser.OpenSubKey(LxssKey);
        if (lxss is null) return [];

        var keys = new List<(string Id, Func<string, object?> Value)>();
        var open = new List<RegistryKey>();
        try
        {
            foreach (var id in lxss.GetSubKeyNames())
            {
                if (lxss.OpenSubKey(id) is not { } key) continue;
                open.Add(key);
                keys.Add((id, key.GetValue));
            }
            return Parse(lxss.GetValue("DefaultDistribution") as string, keys);
        }
        finally
        {
            foreach (var key in open) key.Dispose();
        }
    }

    /// <summary>The distros a set of Lxss subkeys describes: each key's id, and how to read one of its values.</summary>
    public static IReadOnlyList<WslDistro> Parse(string? defaultId, IEnumerable<(string Id, Func<string, object?> Value)> keys)
    {
        var found = new List<WslDistro>();
        foreach (var (id, value) in keys)
        {
            if (value("DistributionName") is not string name || name.Length == 0) continue;
            if (value("State") is int state && state != Installed) continue;
            found.Add(new WslDistro(name, value("Version") is int version ? version : 2,
                                    string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase)));
        }
        return [.. found.OrderByDescending(d => d.IsDefault)];
    }
}

/// <summary>
/// Raises <see cref="Changed"/> whenever the list of distros may have changed — one installed, removed or made the
/// default — until disposed. Before WSL has made its key it watches the key's parent, which learns when it appears.
/// </summary>
internal sealed class WslRegistryWatch : IDisposable
{
    private const uint ChangeName = 0x1, ChangeLastSet = 0x4, ThreadAgnostic = 0x10000000;

    private readonly AutoResetEvent _signal = new(false);
    private readonly Lock _gate = new();
    private RegisteredWaitHandle? _wait;
    private RegistryKey? _key;
    private bool _disposed;

    /// <summary>Raised on a thread-pool thread.</summary>
    public event Action? Changed;

    public void Start()
    {
        Arm();
        _wait = ThreadPool.RegisterWaitForSingleObject(_signal, (_, _) =>
        {
            Arm();
            Changed?.Invoke();
        }, null, Timeout.Infinite, executeOnlyOnce: false);
    }

    // A notification belongs to the key handle it was asked on, and fires once: each one is asked afresh on a fresh handle.
    private void Arm()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _key?.Dispose();
            var lxss = Registry.CurrentUser.OpenSubKey(WslRegistry.LxssKey);
            _key = lxss ?? Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion");
            if (_key is null) return;
            RegNotifyChangeKeyValue(_key.Handle, watchSubtree: lxss is not null, ChangeName | ChangeLastSet | ThreadAgnostic,
                                    _signal.SafeWaitHandle, asynchronous: true);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _wait?.Unregister(null);
            _key?.Dispose();
        }
        _signal.Dispose();
    }

    [DllImport("advapi32.dll")]
    private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key, bool watchSubtree, uint filter,
                                                      SafeWaitHandle @event, bool asynchronous);
}
