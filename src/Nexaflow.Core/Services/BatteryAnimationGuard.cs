using System.Runtime.InteropServices;
using Microsoft.Win32;
using Nexaflow.Visuals.Common.Theming;

namespace Nexaflow.Core.Services;

/// <summary>
/// Bridges the shell's "disable background animations when on battery" setting and the machine's
/// AC/battery state onto <see cref="BackgroundAnimationPolicy"/>, which is what a
/// <c>ThemedRegion</c> consults before realising a theme's animated backdrop.
/// <para>
/// The two reasons live here rather than in the policy so the policy stays a plain switch: the
/// shell owns OS integration (as it does for the login Run key), and a test can flip scenes off
/// without a battery. Both inputs are re-evaluated together in <see cref="Reevaluate"/>, so a
/// setting change and a charger being unplugged take the same path.
/// </para>
/// <para>
/// Applies live - no restart - so unplugging the charger stops the scene, and plugging back in
/// rebuilds it. A machine with no battery never reports "on battery", so this is a no-op on a
/// desktop whatever the setting says.
/// </para>
/// </summary>
internal static class BatteryAnimationGuard
{
    /// <summary>ACLineStatus values from <c>SYSTEM_POWER_STATUS</c>; 255 means "unknown".</summary>
    private const byte AcOffline = 0;

    /// <summary>BatteryFlag bit meaning the machine has no system battery at all.</summary>
    private const byte NoSystemBattery = 128;

    private static bool _disableOnBattery;
    private static bool _hooked;

    /// <summary>
    /// Applies the setting and starts watching power-state changes. Called once at startup, and
    /// again whenever the Shell options are applied.
    /// </summary>
    public static void SetDisableOnBattery(bool disableOnBattery)
    {
        _disableOnBattery = disableOnBattery;

        // Only a machine that cares about the answer pays for the subscription. Never unhooked:
        // SystemEvents holds a process-lifetime listener thread either way, and re-toggling the
        // setting would otherwise churn it.
        if (_disableOnBattery && !_hooked)
        {
            SystemEvents.PowerModeChanged += (_, _) => Reevaluate();
            _hooked = true;
        }

        Reevaluate();
    }

    private static void Reevaluate()
        => BackgroundAnimationPolicy.ScenesEnabled = !(_disableOnBattery && IsOnBattery());

    /// <summary>
    /// True only when the machine has a battery and is actually running off it. An unknown or
    /// unreadable power state counts as mains: a scene that quietly refuses to render is a worse
    /// failure than one that keeps running on a desktop that misreports itself.
    /// </summary>
    private static bool IsOnBattery()
    {
        if (!GetSystemPowerStatus(out var status)) return false;
        if ((status.BatteryFlag & NoSystemBattery) != 0) return false;
        return status.ACLineStatus == AcOffline;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int  BatteryLifeTime;
        public int  BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
