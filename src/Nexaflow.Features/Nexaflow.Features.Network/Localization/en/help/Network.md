# Network

Lists the devices on your network and what is known about each one.

---

## Finding devices
- Press [Discover](locate:Net_Discover) to search on every adapter it can use.
- Two ways of finding devices ship. One reads the address table Windows already keeps of the machines this PC has recently exchanged traffic with: it sends nothing, needs no elevation, and is the only one that gives you a MAC address. On a quiet network it is a short list.
- The other listens for devices that announce themselves — one small multicast search per adapter, then a listen for the replies. This is what turns up TVs, printers, routers, smart plugs and NAS boxes.
- Each is a switch. Turn one off and it does not run. Hover it to read what it does and what it puts on the network.
- The line under the button says how many observations came back and how many devices they turned out to be.
- If one way of finding devices fails, the other carries on. The log says what each did and why one found nothing.

## What it sends
- There is no address sweep and no port scan. A run reads the table, sends one multicast search per adapter, and fetches a description only from an address a device has just given.
- It sends only to your own networks: a device on a locally attached network, or a multicast group that cannot leave it. Loopback, this machine's own addresses and a broadcast aimed at a remote network are refused.
- A run is bounded — a ceiling on packets, on bytes, on how long it may take, and a rate limit per device.
- Fetching a device's description sends no cookies and no credentials, and uses no proxy.
- Discovery never asks for administrator rights. Raw IP and Ethernet sends are refused rather than elevated.

## The device list
- Both ways of finding a device produce one row, not two. [Show me](locate:Net_Devices)
- Found by names which of them found it. A device that announces itself sends no MAC, so what it reports is joined to the table entry through the address they share.
- A device is named by the name it published; where it published none, by its model or its address.
- IPv4 and IPv6 addresses are listed in full, one per line, not just the first.
- Your own machine is left out. Windows answers its own search, and those replies are dropped.

## Device details
- Select a device to open a panel of its facts beside the list. [Show me](locate:Net_Devices,Net_PanelTabs)
- Under each value is what found it, how confident it was, and how long ago.
- Contested marks a fact the two ways of finding devices disagree about.
- Drag the panel wider; the width you choose stays as you move from device to device. Click the selected row again to close the panel.

## Actions on a device
- Only the actions that apply to the device in hand are offered.
- Ping reports how long the round trip took. No answer is recorded as a result too, since some devices refuse pings by policy.
- Web page, Model, Maker and Service URL open an address the device published, as an ordinary tab. Each appears only where the device published that address.
- Each run gets its own tab in the panel, which you can close. Running the same action again replaces its tab.
- What an action learns stays with the device through the next discovery, and the panel stays on the device you were reading.

## Elsewhere in Nexaflow
- Addresses open as a Nexaflow tab, not in another browser.
- Add Network to your ribbon from the ribbon editor's list of pages.
