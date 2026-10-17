namespace Nexaflow.IO.Usb.Browsing;

/// <summary>
/// Device Manager's problem codes (CM_PROB_*) as a sentence: the ones a USB device actually shows, and the number for
/// the rest.
/// </summary>
public static class UsbProblemCodes
{
    /// <summary>"28 — no driver installed"; null for 0, which is no problem.</summary>
    public static string? Describe(uint code) => code switch
    {
        0 => null,
        1 => "1 — not configured correctly",
        3 => "3 — the driver may be corrupted, or the system is short of memory",
        10 => "10 — the device cannot start",
        12 => "12 — not enough free resources",
        14 => "14 — needs a restart to work",
        18 => "18 — its drivers need reinstalling",
        19 => "19 — its registry configuration is damaged",
        21 => "21 — Windows is removing it",
        22 => "22 — disabled",
        24 => "24 — not present, or not working properly",
        28 => "28 — no driver installed",
        29 => "29 — disabled by its firmware",
        31 => "31 — not working properly: Windows could not load its drivers",
        32 => "32 — its driver is disabled",
        37 => "37 — its driver failed to initialise",
        38 => "38 — a previous instance of its driver is still in memory",
        39 => "39 — its driver is corrupted or missing",
        43 => "43 — Windows stopped it because it reported problems (on USB, often a failed descriptor request)",
        45 => "45 — not connected",
        47 => "47 — prepared for safe removal",
        48 => "48 — its driver is blocked from starting",
        52 => "52 — Windows cannot verify its driver's signature",
        _ => code.ToString(),
    };
}
