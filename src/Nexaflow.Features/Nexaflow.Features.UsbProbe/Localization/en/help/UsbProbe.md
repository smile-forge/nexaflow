# USB

Every USB bus, hub, port and device on this PC, and everything each one says about itself — down to a single byte of a descriptor.

---

## Getting around
- The tree on the left holds the buses and the devices plugged in, a hub opening onto what hangs off it; what a device is made of is in the list and on its own page. [Show me](locate:Usb_Tree)
- The list shows what the open place holds. Double-click a row, or press Enter, to open it; click its blank space to clear the selection. [Show me](locate:Usb_Listing)
- Right-click a place in the tree or a row in the list for what it offers: open it, open its device page, read it again, or copy its USB path, its device instance ID or its VID:PID.
- [Up](locate:Usb_Up) (or Backspace) goes to the place that holds this one; the breadcrumbs above the page go anywhere along the way.
- Type `>` in the AI bar followed by a place to go there: `> 3-10.2`, `> 046d:0843`, `> webcam`, `> COM5`.
- [Open device](locate:Usb_OpenDevice) opens the selected device on its own page, with its live tools: storage, phone files, audio, HID and serial.

## Places and paths
- `3` is bus 3: one host controller and the root hub on it. Its rows are its ports, empty or not.
- `3-10.2` is the device on port 2 of the hub on port 10 of bus 3 — the port path Windows shows nowhere else.
- Inside a device: `config1` is its first configuration, `if0.1` interface 0 in alternate setting 1, `ep0x81` an endpoint, `bos` its BOS descriptors. An interface association groups the interfaces of one function — a camera's video, a headset's audio.
- The tab remembers where it is: a restored session opens at the same place.

## What a device says
- Select a row to see everything about it below the list. [Show me](locate:Usb_Detail)
- Overview has the device's identity, where it is plugged in and how fast, its power, what Windows bound to it — service, INF, version, provider, problem code, instance and hardware IDs — and a plain-language summary of what it is. [Show me](locate:Usb_Tab_Overview)
- An interface's Overview shows the Windows function that owns it: the driver, the COM port of a serial adapter, the devices Windows made from it.
- Microsoft OS descriptors say whether the device declares itself WinUSB-ready, which means raw access needs no driver change.

## Descriptors, byte by byte
- Descriptor shows the selected descriptor's fields — offset, name, size, value and what the value means — beside the bytes they came from. [Show me](locate:Usb_Tab_Descriptor,Usb_Fields)
- Point at a field and its bytes light up in the hex view; point at a byte and the field it belongs to is named below. [Show me](locate:Usb_Hex)
- Audio, video (UVC), HID, CDC serial and network, and DFU class descriptors are decoded; anything else is shown as bytes with its header named.
- A descriptor shorter or longer than its standard says is shown as it is, with the problem named, rather than hidden.

## Live
- Plug a device in and it appears at once — the tree opens onto it — marked NEW until you look at it. Unplug one and it goes; if you were inside it, the page moves to the nearest place that still exists.
- A drive Windows has ejected ("Safe to remove") stays in its port, shown as ejected, until it is unplugged.
- Driver changes show at once too, and so does what Windows makes of a device: a phone unlocked into file transfer.
- Reading the tree needs no administrator rights and sends nothing to any device: Windows already holds everything shown.
- A device that was asleep may not have given its strings. [Read device again](locate:Usb_ReRead) asks it, waking it — the only thing this page ever sends a device, and only standard GET_DESCRIPTOR requests.

## Device names
- Vendor, product and class names come from usb.ids. Options → USB checks linux-usb.org for a newer list when the page opens, at most once a month, and can check at once.

## Asking the assistant
- The assistant sees where you are and what is listed, and can explore on its own: list any place, describe a device with every decoded field, find devices by name or VID:PID, and open a place on the page.
