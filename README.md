# DS2 Logger — Windows

## Important: passive capture of MS4X traffic
The COM logger in this project opens a serial port itself. It cannot read a COM port already exclusively opened by MS4X Flasher.

For passive capture while MS4X is running, use **USBPcap + Wireshark** (USB-level capture). This requires USBPcap installed and usually administrator rights. Select the USB host controller and capture the USB device corresponding to K+DCAN/KKL. USB capture records transfer payloads, not automatically decoded DS2 messages; FTDI/USB serial transfers must be extracted/reassembled and then passed to the DS2 parser.

A second possible method is a COM API monitor/hook, but it is a separate Windows component and is not implemented in this starter project.

## Build
- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022 with .NET desktop development

Open `DS2Logger.csproj` and build Release, or run:
`dotnet publish -c Release -r win-x64 --self-contained true`

## Features in this starter
- COM selection and baud rate
- UART RX/TX color display
- RAW mode
- DS2 framing (`0x12`, second byte = total frame length)
- manual HEX TX
- text log export

## Roadmap
1. USBPcap/Wireshark capture import (PCAP/PCAPNG)
2. FTDI/USB bulk payload extraction and endpoint direction classification
3. DS2 parser and timestamped RX/TX frame view
4. Logic analyzer capture import (PulseView `.sr` / VCD) and UART decode


## Samodzielne przechwytywanie USB (bez Wiresharka)
W GUI dodano start/stop USBPcapCMD:
1. Zainstaluj sam sterownik USBPcap (bez instalowania/uruchamiania Wiresharka).
2. Kliknij `Start USB capture`, wskaż `USBPcapCMD.exe` (z instalatora USBPcap), wybierz plik `.pcap` i root hub USBPcap.
3. Program uruchamia USBPcapCMD z uprawnieniami administratora; MS4X Flasher może nadal pracować.
4. `Stop USB capture` kończy przechwytywanie.

**Uwaga:** to uruchamia samodzielny capture engine USBPcap, ale aktualny build nadal zapisuje surowy PCAP. Wyciąganie bajtów UART z FTDI/CH340/CP210x oraz prezentacja ich jako DS2 na żywo jest kolejnym etapem. To nie jest jeszcze gotowy dekoder DS2 z USB.


## Dekodowanie USBPcap PCAP (implementacja)
Dodano `Dekoduj USB PCAP`: odczytuje klasyczny PCAP z link type 249 (USBPcap), analizuje nagłówek USBPcap (27+ bajtów), wyciąga payload transferów BULK, pokazuje USB-IN/USB-OUT wraz z urządzeniem i endpointem oraz wyszukuje ramki DS2 (`0x12`, następny bajt = długość). To jest dekoder pliku PCAP, nie jeszcze strumieniowanie ramek DS2 z capture w czasie rzeczywistym. Dla adapterów USB-UART payload może być opakowany protokołem producenta (np. FTDI), więc w razie potrzeby trzeba odfiltrować jego nagłówek przed DS2.
