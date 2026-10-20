# ADR-001: Raw USB access on Windows

| | |
|---|---|
| **Status** | Proposed — Phase 0 of [usb-probe-spec.md](../usb-probe-spec.md), run on config W2; W3, replug, reboot and crash runs open |
| **Spike** | `spikes/UsbDriverBinding/` (not in `Nexaflow.slnx`; `dotnet build` it on its own) |
| **Machine** | Windows 11 Pro build 26300.9711, Secure Boot on, Memory Integrity (HVCI) running, Smart App Control off, admin account behind a UAC-filtered token |

## Decision

1. **Take-over is Strategy A**: select Windows' own `winusb.inf` for the one chosen interface and install it with
   `DiInstallDevice`; restore with `DiRollbackDriver`. No generated INF, no certificate, nothing in the driver store.
2. **Strategy D is always on**: interfaces already on WinUSB (WCID devices) open with no privilege and no change.
3. **Strategies B (self-signed catalog) and C (UsbDk) are not pursued.** A meets every exit criterion tested; B
   adds a root certificate per bind and C a filter driver on every hub — both larger, riskier system changes for no
   gain. Revisit only if A fails on a device class A has not yet been run against.
4. **Privilege is P1 through the existing `PrivilegeBridge`**, not a new broker exe. Bind and restore become two
   `IElevatedOperation`s; the bridge already gives one UAC prompt per request, a per-call pipe with a one-time
   token, and a compiled-in verb allow-list ([Architecture.md](../Architecture.md) — elevated operations).
5. **`IRawUsbTransport` on Windows is WinUSB P/Invoke**; libusb-1.0 is an optional second backend for later
   (cross-platform, libusbK/UsbDk devices).

## Results

### Strategy A — LG monitor CDC serial interface (`VID_043E&PID_9A39&MI_01`, `usbser`, COM5)

| Exit criterion (spec §5.1) | Result |
|---|---|
| Binds one interface of a composite without affecting siblings | **Pass** — MI_01 → `WINUSB`, MI_00 stayed `HidUsb`, problem 0 |
| libusb-1.0 opens it and completes GET_DESCRIPTOR | **Pass** — from the *unelevated* process: open, claim interface 1, GET_DESCRIPTOR, GET_STATUS. WinUSB P/Invoke likewise |
| Restore returns the original driver, device works | **Pass** — `DiRollbackDriver` put `usbser` back under the same driver key; COM5 present |
| No residue | **Pass** — `oem*.inf` 109 → 109; LocalMachine Root 71 → 71, TrustedPublisher 2 → 2; added `DeviceInterfaceGUIDs` removed; no orphan key under the USBDevice class |
| No reboot | **Pass** — neither install nor rollback asked for one |
| Crash mid-operation recoverable from the ledger | **Not run** — ledger writes intent before and outcome after every change |
| At most one UAC prompt per user action | **Pass** — one for bind (3.6 s round trip incl. prompt; `DiInstallDevice` 258 ms), one for restore (3.3 s) |

What Strategy A has to do beyond `DiInstallDevice`:

- **Change the device class first.** `winusb.inf` is `Class=USBDevice`; with the device still in `Ports`, both the
  compatible and the class driver list built from that single INF are empty. After setting `SPDRP_CLASSGUID` to
  USBDevice the class list offers *WinUsb Device*, *Billboard Device* and *ADB Device*. The original class comes back
  with the rollback; the ledger records it anyway.
- **Write `DeviceInterfaceGUIDs`** (a fresh GUID per binding) into the hardware key before installing. Without it
  `winusb.sys` registers no device interface and neither `CreateFile` nor libusb can reach the device.

### Strategy D — devices already on WinUSB, no privilege

| Device | WinUSB P/Invoke | libusb-1.0 |
|---|---|---|
| ASUS Motherboard LCD Panel `0B05:1C83` MI_00 (WCID, vendor code 0xA0) | open, 2 bulk pipes, GET_DESCRIPTOR + GET_STATUS ok | open, claim, transfers ok |
| ASUS AURA LED Controller `0B05:1AA6` MI_00 (WCID, vendor code 0x87) | open, GET_DESCRIPTOR + GET_STATUS ok | open, claim, transfers ok |

No vendor lighting service (Armoury Crate) was running, so nothing else held either interface; how an open behaves
while a vendor app holds the device is untested, and the UI must expect it to fail.

### libusb against class-driver devices (no binding)

Opens HID-backed devices (its HID backend) and serves GET_DESCRIPTOR(device) from the cache; `usbstor` →
*not supported*; `usbvideo`/`usbaudio` and `BTHUSB` → *entity not found*; a D3 Corsair hub was not enumerated at
all. libusb adds nothing over the class APIs for these — the live tools (spec §7) stay on the class APIs.

### Phase 1 discovery, unprivileged

The hub-IOCTL walk plus CfgMgr32 inventoried 9 hubs and 15 devices in ~220 ms as a standard token: device, every
configuration, BOS and string descriptors; speed and SuperSpeed capability (V2 IOCTL); port connector flags; driver
service, INF, section, matching ID, provider, problem code, hardware/compatible IDs; COM port; WinUSB interface
GUIDs; MS OS 1.0 support and vendor code from the `usbflags` cache (no request sent); MS OS 2.0 and WebUSB from BOS.

## Known Windows behaviours

- **The hub serves device and configuration descriptors from its cache**, so they are available for every device,
  even a suspended one. **String, BOS and class requests go to the wire** and fail with `ERROR_GEN_FAILURE` (31) when
  the device is selectively suspended (D2/D3 — two Corsair hubs and the Bluetooth radio here). Inventory must fall
  back to CfgMgr32's `BusReportedDeviceDesc` for names and show "asleep" rather than an error.
- **The hub IOCTL does not reliably forward interface-recipient requests.** A HID report descriptor
  (`bmRequestType 0x81`, type 0x22) came back for 2 of 15 HID interfaces, all awake devices included; the rest fail
  with error 31. The HID adapter gets its report layout from the HID stack (preparsed data / `HidP_*` caps — hidapi
  reconstructs a descriptor from it), not from the hub.
- **A composite function sees a rewritten device descriptor.** Through WinUSB on `MI_01`, `iProduct` read 5 where the
  hub's copy reads 3: `usbccgp` points the function's product string at its interface name. The hub copy is the
  device's own; the inventory and the USB page use it.
- **A force-selected driver is per devnode instance**, not per hardware ID: nothing is published to the driver store,
  so an identical device on another port keeps its Windows driver (the opposite of Strategy B). Replug on the same
  port and reboot behaviour are still to be confirmed.
- **Windows reports itself as "Windows 10 Pro" in `ProductName`** on this build; use the build number.
- `setupapi.dev.log` records both operations (`Install Device`, `Driver Rollback (DiRollbackDriver)`) — the place to
  look when a future Windows build breaks binding.

## Contract consequences

`IDeviceBroker` maps onto bridge operations; the bridge, not the client, owns the ledger (spec §9.2: no record,
path or INF comes from the client).

| `IDeviceBroker` | Bridge operation | Elevated |
|---|---|---|
| `RefusalReason(interfaceInstanceId)` | — the policy, read from the live devnode | no |
| `BindGenericAsync(interfaceInstanceId, kind)` | `usb.bind` `{usbInstanceId, usbDriverKind}` | yes |
| `RestoreAsync(interfaceInstanceId)` | `usb.restore` `{usbInstanceId}` — record read from the ledger | yes |
| `RestoreAllAsync()` | `usb.restoreAll` `{}` — the bridge restores every active record it finds; one prompt | yes |
| `GetBindings()` | read `%ProgramData%\Nexaflow\Devices\usb-bindings.json` directly | no |

These are `IUsbDeviceBroker` and `BridgeUsbDeviceBroker` in `Nexaflow.IO.Usb/Binding/`; the operations are
`Nexaflow.PrivilegeBridge/Operations/UsbBindingOperations.cs`. The policy runs on both sides, so a refused target
never costs a prompt and a forged request is still refused. The ledger's directory is created owned by
Administrators with Users read-only, and the bridge refuses a ledger in a directory anyone else owns.

`UsbBindingRecord` carries what Strategy A changes besides the driver:

```csharp
public sealed record UsbBindingRecord(
    Guid Id, string InterfaceInstanceId, string HardwareId,
    string? OriginalService, string? OriginalInfName, string? OriginalInfSection, string? OriginalMatchingId,
    Guid? OriginalClassGuid, string Strategy,     // "InboxWinUsb"
    string AddedInterfaceGuid,                    // DeviceInterfaceGUIDs value written for winusb.sys
    DateTimeOffset BoundAt)
{
    UsbBindingState State; bool ClassChanged; DateTimeOffset? RestoredAt; List<string> Log;
    string? PublishedInfName;                     // always null for InboxWinUsb
    string? CertificateThumbprint;                // always null for InboxWinUsb
}
```

`UsbBindingState` is `BindIntent → Bound | BindFailed`, `RestoreIntent → Restored | RestoreFailed`. The bridge
saves the record after every step that changes the system (interface GUID written, class changed, driver
installed), so a record left in an `*Intent` state after a crash says exactly what to undo, and `usb.restore`
finishes it from whatever state it is in.

## usb.ids licence

`external/usbids` mirrors linux-usb.org's `usb.ids`, which linux-usb.org offers under GPL-2.0-or-later **or**
BSD-3-Clause; Nexaflow (Unlicense) embeds it under BSD-3-Clause. The upstream mirror's GPL-3.0 `LICENSE` is the
mirror's, not the database's; our fork's `LICENSE` replaces it with linux-usb.org's statement and the BSD-3-Clause
text. A newer copy is downloaded from linux-usb.org itself
(`UsbIdsStore`), under the same BSD-3-Clause option — at most monthly when the USB page opens, or from Options → USB.
linux-usb.org's HTTPS certificate names another host on the same server, so a handshake that fails on the host name
and nothing else falls back to `http://www.linux-usb.org/usb.ids`; any other certificate failure is a failure. The
download is validated the same way over either.

## Transport choice

| | WinUSB P/Invoke | libusb-1.0 via LibUsbDotNet 3.0.224 |
|---|---|---|
| Opens Strategy A and D interfaces | yes | yes |
| Dependency | `winusb.dll`, Microsoft-signed, inbox | LibUsbDotNet (LGPL-3.0-or-later) + a native `libusb-1.0.dll` |
| Native DLL source | — | LibUsbDotNet ships none; the only maintained package with a current win-x64 build is `UsbDotNet.LibUsbNative` 1.4.2 (MIT, libusb 1.0.30) — **unsigned**, which Smart App Control may block |
| Robustness seen | — | `UsbDevice.VendorId` reads and parses every configuration descriptor and **throws** for a device it cannot read; listing has to guard each device |
| Reaches libusbK / UsbDk / other OSes | no | yes |

WinUSB covers every device Nexaflow will bind itself. libusb becomes worth its weight only for devices already on
libusbK/libusb0, or a non-Windows build — the contract keeps room for it.

## Bearing on the rest of the spec

- **Names follow the repo:** the library is `Nexaflow.IO.Usb` (WPF-free leaf, like `IO.Network`), the tab is
  `Nexaflow.Features.Usb`, and bind/restore are PrivilegeBridge operations with their DTOs in `Elevation.Contracts` —
  there is no `Devices.*` / `Modules.*` convention and no separate broker exe.
- **There is no hierarchical state machine in the repo.** The binding lifecycle (§8.1) is the `BindingState` above,
  owned by the ledger; a UI state machine is a separate decision.
- **No report, no export, no "send to AI".** The USB page is a live explorer, not a document: the agent reads it the
  way it reads any page — `GetContext()` names the open place and its rows, and read-only tools (`usb_list`,
  `usb_describe`, `usb_find`, `usb_open`, `usb_reread`) walk the same `UsbBrowser` tree the user does, by the same
  location paths. The spec's §10 report schema is not implemented.
- **`IO.Network`'s guarded transport** (`IGuardedTransport` / `SendIntent`) is the model for the armed state on raw
  OUT transfers.

## Still to run

| Run | Needs |
|---|---|
| W3 — standard user with over-the-shoulder admin consent | a standard account |
| Replug and reboot while bound | someone at the machine |
| Crash between intent and outcome (kill the bridge mid-`DiInstallDevice`) | a recovery verb that reads `*Intent` records |
| A driverless device (problem 28, no class) — the main real-world target | a Pico flashed vendor-class, or any device with no Windows driver |
| A vendor-driver device (CH340/FTDI) and an audio interface, restore verified by use | the devices |
| HVCI off (W1) and Smart App Control on (W4) | config changes |

Re-run on any of them: `UsbDriverBinding all`, then `bind-inbox <MI_xx instance id>` and `restore <same id>`.
