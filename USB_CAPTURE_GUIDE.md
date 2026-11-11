# USB capture guide for DS2 Logger

## Capture MS4X USB traffic (without opening its COM port)
1. Install Wireshark for Windows and select the USBPcap component during installation (or install USBPcap separately if offered).
2. Run Wireshark as Administrator.
3. Start capture on the relevant `USBPcapN` interface (the USB host controller where K+DCAN/KKL is connected).
4. In MS4X Flasher, perform the operation you want to observe.
5. Stop capture and save as `.pcapng`.
6. Import the capture in DS2 Logger (planned GUI integration) or inspect it in Wireshark.

## Identify the adapter
- Unplug the adapter, note which USBPcap interface/device disappears or changes.
- Plug it back in and identify its USB address in Wireshark.
- Filter by `usb.device_address == N` (field availability depends on Wireshark version).
- For USB serial adapters, inspect bulk endpoints and URB direction. USBPcap captures USB requests/transfers; the raw packet bytes include USBPcap metadata, not a plain UART byte stream.

## Current project status
The included `UsbPcapImport.cs` reads PCAPNG Enhanced Packet Blocks and exposes captured packet bytes/timestamps. Full USBPcap pseudo-header parsing, URB/endpoint direction, FTDI/CH340/CP210x payload extraction and DS2 stream reconstruction remain to be implemented and validated against a sample capture from your adapter.
