using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.IO.Usb.Naming;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.UsbProbe.ViewModels;

/// <summary>
/// The Options → USB editor: the monthly-check switch, which reaches the config on Save, and the database's state
/// with a check that runs at once — a download is not a setting, so it does not wait for Save.
/// </summary>
public sealed partial class UsbProbeOptionsModel : ObservableObject
{
    readonly UsbProbeConfig _config;
    readonly UsbIdsStore _store;

    public UsbProbeOptionsModel(UsbProbeConfig config, UsbIdsStore store)
    {
        _config = config;
        _store = store;
        _checkMonthly = config.CheckDeviceNamesMonthly;
        Describe();
    }

    public event EventHandler? Changed;

    [ObservableProperty] private bool _checkMonthly;
    [ObservableProperty] private string _inUse = "";
    [ObservableProperty] private string _lastChecked = "";
    [ObservableProperty] private string _checkResult = "";

    public bool HasChanges => CheckMonthly != _config.CheckDeviceNamesMonthly;

    partial void OnCheckMonthlyChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);

    public void Apply() => _config.CheckDeviceNamesMonthly = CheckMonthly;

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        CheckResult = Str.Get("UsbProbe.Options.Checking");
        // Awaited from the button's click, so this resumes on the thread the Options panel runs on.
        var result = await Task.Run(() => _store.UpdateAsync(CancellationToken.None));
        CheckResult = result.Message;
        Describe();
    }

    void Describe()
    {
        var current = _store.Current;
        InUse = current is null ? Str.Get("UsbProbe.Options.NoDatabase")
            : _store.CurrentIsDownloaded ? Str.Format("UsbProbe.Options.InUseDownloaded", current.Version ?? "?", current.VendorCount, current.ProductCount)
            : Str.Format("UsbProbe.Options.InUseBundled", current.Version ?? "?", current.VendorCount, current.ProductCount);

        var state = _store.State;
        LastChecked = state.LastChecked is { } at
            ? Str.Format("UsbProbe.Options.LastChecked", at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
            : Str.Get("UsbProbe.Options.NeverChecked");
        if (state.LastFailure is { } failed && (state.LastChecked is null || failed > state.LastChecked))
            LastChecked += " " + Str.Format("UsbProbe.Options.LastFailed", failed.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), state.FailureMessage ?? "");
    }
}
