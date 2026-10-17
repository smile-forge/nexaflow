# USB device

One USB device on its own page: what it is, what you can do with it now, what it is made of, and what is wrong with it.

---

## Getting here
- On the USB page, select a device (or anything inside one) and choose [Open device](locate:Usb_OpenDevice).
- [Show in USB tree](locate:UsbDevice_ShowInTree) goes back to the device's place on the USB page.
- The page follows the device, not the port: unplug it and the page says so and stops its tools; plug it back in — the same port or another — and it picks it up again.

## Overview
- What the device is, in plain words, which live tools it has, and its identity, connection, power and driver facts. [Show me](locate:UsbDevice_Tab_Overview)

## Live tools
- Each thing the device can do gets a tile: open it to use it, shut it to stop what is live in it. Tiles sit side by side when the window is wide enough. [Show me](locate:UsbDevice_Tab_Tools,UsbDevice_Tiles)
- **Mass storage** lists the device's volumes and browses them. Copy files and folders to this PC, or onto the device — every copy is read back from where it landed and checked against the original's SHA-256, and a copy that does not match is removed. Copying onto the device asks first. [Show me](locate:UsbStorage_Entries)
- **Audio output** plays a tone, a 20 Hz – 20 kHz sweep or a WAV file on every channel or one, at a level you set. [Identify channels](locate:UsbAudio_Identify) sounds each channel in turn and names it as it plays.
- **Audio input** meters every channel live, and [Record…](locate:UsbAudio_Record) saves a few seconds to a WAV exactly as the device sent it.
- **HID** shows each collection's report layout as the HID class driver decoded it, [watches its input](locate:UsbHid_StartMonitor) live, and reads and sends feature and output reports. Sending a report asks first. Windows keeps keyboards and mice to itself, so their input cannot be watched here; their layout and feature reports can.
- **Serial port** names the COM port of a serial device and [opens the serial terminal](locate:UsbSerial_OpenTerminal) on it.
- Anything on the device no tool covers is listed last, with what it is and what a tool for it would use.

## Interfaces
- The device's configurations, interface associations, interfaces and endpoints, each with its facts — the Windows function that owns it and that function's driver — and its descriptor field by field beside its bytes. [Show me](locate:UsbDevice_Tab_Interfaces,UsbDevice_Structure)

## Diagnostics
- Problems Windows or the descriptors report: problem codes in plain words, descriptors that do not add up, strings the device did not answer for. [Show me](locate:UsbDevice_Tab_Diagnostics,UsbDevice_Problems)
- Everything the live tools did — copies, recordings, reports sent — with the time. [Show me](locate:UsbDevice_Log)
- [Read device again](locate:UsbDevice_ReRead) asks the device for its descriptors and strings, waking it if it slept.

## Asking the assistant
- The assistant sees the device and every live tool's state. It can run any tool's read-only operations by itself — list a folder, read a feature report, read the audio levels — and, with your approval each time, the ones that act: copying, playing, recording, sending a report.
