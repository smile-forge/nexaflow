using Nexaflow.Features.Common.ThisPc;
using Nexaflow.Features.OneDrive.Models;
using Nexaflow.Features.OneDrive.Services;

namespace Nexaflow.Features.OneDrive;

/// <summary>
/// Puts the machine's OneDrive sync folders in "This PC", so a user can click through to their files
/// without knowing where OneDrive put them. Each row is backed by a real local folder, published as a
/// pass-through mount by the file browser — no cloud API, no sign-in, nothing to expire.
/// </summary>
public sealed class OneDriveThisPcProvider : IThisPcItemProvider
{
    /// <summary>Detection is a handful of registry reads, but <c>GetItems</c> is called on the UI thread
    /// every time This PC is drawn. Memoising briefly keeps that free while still noticing a client that
    /// was installed, or an account signed in, since the window opened.</summary>
    private static readonly TimeSpan MemoFor = TimeSpan.FromSeconds(30);

    private readonly OneDriveConfig  _config;
    private readonly OneDriveDetector _detector;
    private readonly Lock _gate = new();

    private IReadOnlyList<ThisPcItem>? _memo;
    private DateTime _memoAt;

    public OneDriveThisPcProvider(OneDriveConfig config)
        : this(config, new OneDriveDetector(new HkcuRegistryView())) { }

    /// <summary>Test seam: the detector is built, not run, here — nothing touches the registry until
    /// <see cref="GetItems"/> is called.</summary>
    internal OneDriveThisPcProvider(OneDriveConfig config, OneDriveDetector detector)
    {
        _config   = config;
        _detector = detector;
        _config.Changed += Invalidate;
    }

    public string ProviderId => "onedrive";

    /// <summary>Ahead of the generic contributors: a cloud drive is a more familiar place than whatever
    /// else may end up on this seam.</summary>
    public int SortOrder => 20;

    public event Action? Changed;

    public IReadOnlyList<ThisPcItem> GetItems()
    {
        lock (_gate)
        {
            if (_memo is not null && DateTime.UtcNow - _memoAt < MemoFor) return _memo;
            _memo   = Build();
            _memoAt = DateTime.UtcNow;
            return _memo;
        }
    }

    private IReadOnlyList<ThisPcItem> Build()
    {
        var items = new List<ThisPcItem>();
        var order = 0;

        IReadOnlyList<SyncAccount> detected;
        try { detected = _detector.Detect(); }
        catch { detected = []; }   // no OneDrive is the normal case, not a failure

        foreach (var account in detected)
        {
            var over = _config.Overrides.FirstOrDefault(
                o => string.Equals(o.Id, account.Id, StringComparison.OrdinalIgnoreCase));
            if (over is { Hidden: true }) continue;

            items.Add(new ThisPcItem
            {
                Id         = account.Id,
                Label      = string.IsNullOrWhiteSpace(over?.Label) ? account.Label : over!.Label!,
                TargetPath = account.FolderPath,
                TypeLabel  = "OneDrive",
                Icon       = ThisPcItemIcon.Cloud,
                SortOrder  = order++,
            });
        }

        foreach (var entry in _config.Custom)
        {
            if (string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.FolderPath)) continue;

            items.Add(new ThisPcItem
            {
                Id         = entry.Id,
                Label      = string.IsNullOrWhiteSpace(entry.Label) ? entry.FolderPath : entry.Label,
                TargetPath = entry.FolderPath,
                TypeLabel  = "OneDrive",
                Icon       = ThisPcItemIcon.Cloud,
                SortOrder  = order++,
            });
        }

        return items;
    }

    private void Invalidate()
    {
        lock (_gate) _memo = null;
        Changed?.Invoke();
    }
}
