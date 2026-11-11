USB native capture correction (11.11.2026)
- USBPcap driver ReadFile returns concatenated USBPcap native packet records, not classic-PCAP per-record headers.
- Parser uses native headerLen (offset 0) and dataLength (offset 23) to delimit packets.
- OVERLAPPED.hEvent offset now comes from a correctly laid out native OVERLAPPED structure (x86/x64).
- Needs Windows compile and real adapter validation. USB bulk payloads may contain FTDI/CH340/CP210x protocol/status bytes; adapter-specific byte extraction and identification remain required.
