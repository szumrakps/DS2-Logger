using System;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace DS2Logger;

public sealed class MainForm : Form
{
    private readonly ComboBox ports = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly ComboBox baud = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly Button connect = new() { Text = "Połącz", Width = 90 };
    private readonly CheckBox echo = new() { Text = "Pokaż echo adaptera", Checked = false, AutoSize = true };
    private readonly CheckBox filter = new() { Text = "Filtr ramek DS2 (12 + długość)", Checked = true, AutoSize = true };
    private readonly CheckBox raw = new() { Text = "RAW", Checked = false, AutoSize = true };
    private readonly CheckBox logRx = new() { Text = "RX", Checked = true, AutoSize = true };
    private readonly CheckBox logTx = new() { Text = "TX", Checked = true, AutoSize = true };
    private readonly RichTextBox output = new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 10), BackColor = Color.FromArgb(22,24,28), ForeColor = Color.Gainsboro };
    private readonly TextBox sendBox = new() { Width = 500, PlaceholderText = "HEX do wysłania, np. 12 04 00 16" };
    private readonly Button send = new() { Text = "Wyślij HEX" };
    private readonly Button save = new() { Text = "Zapisz log" };
    private readonly Button clear = new() { Text = "Wyczyść" };
    private readonly Button importUsb = new() { Text = "Import USB PCAPNG" };
    private readonly Button decodePcap = new() { Text = "Dekoduj USB PCAP" };
    private readonly ComboBox usbRoot = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 115 };
    private readonly Button usbStart = new() { Text = "Start USB capture" };
    private readonly Button usbStop = new() { Text = "Stop USB capture", Enabled = false };
    private Process? usbCaptureProcess;
    private UsbPcapNative? nativeCapture;
    private CancellationTokenSource? captureCts;
    private readonly List<byte> capturePending = new();
    private readonly Timer usbLiveTimer = new() { Interval = 500 };
    private long usbReadOffset;
    private string? usbCapturePath;
    private readonly Label status = new() { Text = "Rozłączono", AutoSize = true, ForeColor = Color.Firebrick };
    private SerialPort? serial;
    private readonly List<byte> rxBuffer = new();
    private readonly List<byte> pendingEcho = new();
    private readonly StringBuilder logText = new();
    private bool suppressEcho;
    private readonly Timer refreshTimer = new() { Interval = 1000 };

    public MainForm()
    {
        Text = "DS2 Logger";
        Width = 1150; Height = 760; MinimumSize = new Size(850, 550);
        StartPosition = FormStartPosition.CenterScreen;
        baud.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200", "230400" });
        baud.SelectedItem = "9600";
        RefreshPorts();
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8), WrapContents = false };
        top.Controls.Add(new Label { Text = "COM:", AutoSize = true, Padding = new Padding(0,8,0,0) });
        top.Controls.Add(ports);
        var refresh = new Button { Text = "Odśwież COM" }; refresh.Click += (_,__) => RefreshPorts(); top.Controls.Add(refresh);
        top.Controls.Add(new Label { Text = "Baud:", AutoSize = true, Padding = new Padding(8,8,0,0) }); top.Controls.Add(baud);
        connect.Click += (_,__) => ToggleConnection(); top.Controls.Add(connect); top.Controls.Add(status);
        var options = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(8), WrapContents = false };
        options.Controls.Add(logRx); options.Controls.Add(logTx); options.Controls.Add(echo); options.Controls.Add(filter); options.Controls.Add(raw);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8), WrapContents = false };
        bottom.Controls.Add(sendBox); send.Click += (_,__) => SendHex(); bottom.Controls.Add(send); save.Click += (_,__) => SaveLog(); bottom.Controls.Add(save); clear.Click += (_,__) => { output.Clear(); logText.Clear(); }; bottom.Controls.Add(clear); importUsb.Click += (_,__) => ImportUsbCapture(); bottom.Controls.Add(importUsb); decodePcap.Click += (_,__) => DecodeUsbPcap(); bottom.Controls.Add(decodePcap);
        bottom.Controls.Add(new Label { Text = "USB:", AutoSize = true, Padding = new Padding(4,8,0,0) });
        usbRoot.Items.AddRange(new object[] { "USBPcap1", "USBPcap2", "USBPcap3", "USBPcap4", "USBPcap5", "USBPcap6" });
        usbRoot.SelectedIndex = 0; bottom.Controls.Add(usbRoot);
        usbStart.Click += (_,__) => StartUsbCapture(); bottom.Controls.Add(usbStart);
        usbStop.Click += (_,__) => StopUsbCapture(); bottom.Controls.Add(usbStop);
        Controls.Add(output); Controls.Add(bottom); Controls.Add(options); Controls.Add(top);
        refreshTimer.Tick += (_,__) => { if (serial?.IsOpen == true) status.Text = $"Połączono • {serial.PortName} • {serial.BaudRate}"; };
        refreshTimer.Start();
        usbLiveTimer.Tick += (_,__) => PollUsbCapture();
        FormClosing += (_,__) => { try { serial?.Close(); } catch {} StopUsbCapture(); };
    }

    private void RefreshPorts()
    {
        var selected = ports.SelectedItem?.ToString();
        ports.Items.Clear(); ports.Items.AddRange(SerialPort.GetPortNames().OrderBy(x=>x).Cast<object>().ToArray());
        if (selected != null && ports.Items.Contains(selected)) ports.SelectedItem = selected;
        else if (ports.Items.Count > 0) ports.SelectedIndex = 0;
    }

    private void ToggleConnection()
    {
        if (serial?.IsOpen == true) { serial.Close(); connect.Text = "Połącz"; status.Text = "Rozłączono"; status.ForeColor = Color.Firebrick; return; }
        if (ports.SelectedItem == null) { MessageBox.Show("Wybierz port COM."); return; }
        try {
            serial = new SerialPort(ports.SelectedItem.ToString()!, int.Parse(baud.SelectedItem!.ToString()!), Parity.None, 8, StopBits.One) { ReadTimeout=500, WriteTimeout=1000, DtrEnable=false, RtsEnable=false };
            serial.DataReceived += SerialDataReceived; serial.Open();
            connect.Text = "Rozłącz"; status.Text = "Połączono"; status.ForeColor = Color.LightGreen;
            AddLine("INFO", $"Otwarto {serial.PortName} @ {serial.BaudRate}");
        } catch (Exception ex) { MessageBox.Show("Nie można otworzyć portu: " + ex.Message); }
    }

    private void SerialDataReceived(object? sender, SerialDataReceivedEventArgs e)
    {
        try {
            var sp = (SerialPort)sender!;
            var n = sp.BytesToRead; if (n <= 0) return;
            var bytes = new byte[n]; sp.Read(bytes,0,n);
            BeginInvoke(() => ProcessRx(bytes));
        } catch { }
    }

    private void ProcessRx(byte[] bytes)
    {
        // Gdy echo jest wyłączone, usuń z RX bajty odpowiadające ostatniemu TX.
        // Dopasowanie działa także wtedy, gdy echo przychodzi w kilku porcjach.
        if (!echo.Checked && pendingEcho.Count > 0)
        {
            var visible = new List<byte>();
            foreach (byte b in bytes)
            {
                if (pendingEcho.Count > 0 && pendingEcho[0] == b) pendingEcho.RemoveAt(0);
                else
                {
                    pendingEcho.Clear();
                    visible.Add(b);
                }
            }
            bytes = visible.ToArray();
            if (bytes.Length == 0) return;
        }
        if (raw.Checked) { if (logRx.Checked) AddLine("RX", Hex(bytes)); return; }
        if (!filter.Checked) { if (logRx.Checked) AddLine("RX", Hex(bytes)); return; }
        rxBuffer.AddRange(bytes);
        while (rxBuffer.Count > 0)
        {
            int start = rxBuffer.IndexOf(0x12);
            if (start < 0) { rxBuffer.Clear(); break; }
            if (start > 0) rxBuffer.RemoveRange(0,start);
            if (rxBuffer.Count < 2) break;
            int len = rxBuffer[1];
            if (len < 2 || len > 255) { rxBuffer.RemoveAt(0); continue; }
            if (rxBuffer.Count < len) break;
            var frame = rxBuffer.Take(len).ToArray(); rxBuffer.RemoveRange(0,len);
            if (logRx.Checked) AddLine("RX", Hex(frame));
        }
    }



    private void ImportUsbCapture()
    {
        using var dlg = new OpenFileDialog { Filter = "USB capture (*.pcapng;*.pcap)|*.pcapng;*.pcap|PCAPNG (*.pcapng)|*.pcapng|PCAP (*.pcap)|*.pcap|All files|*.*" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try
        {
            if (Path.GetExtension(dlg.FileName).Equals(".pcapng", StringComparison.OrdinalIgnoreCase))
            {
                var packets = UsbPcapImport.ReadPcapNg(dlg.FileName);
                int ds2 = 0, usb = 0;
                foreach (var item in packets)
                {
                    var b = item.Data;
                    if (b.Length < 27) continue;
                    int h = b[0] | b[1] << 8;
                    uint dl = (uint)(b[23] | b[24] << 8 | b[25] << 16 | b[26] << 24);
                    if (h < 27 || h > b.Length || dl > b.Length - h) continue;
                    byte ep = b[21], tr = b[22], info = b[16];
                    if (tr != 3 || dl == 0) continue;
                    bool incoming = (ep & 0x80) != 0 || (info & 1) != 0;
                    byte[] payload = b.Skip(h).Take((int)dl).ToArray();
                    AddLine(incoming ? "USB-IN" : "USB-OUT", $"dev={b[19] | b[20] << 8} ep=0x{ep:X2} {Hex(payload)}");
                    foreach (var frame in UsbPcapClassic.ExtractDs2(payload)) { AddLine(incoming ? "RX" : "TX", Hex(frame)); ds2++; }
                    usb++;
                }
                AddLine("INFO", $"PCAPNG: {packets.Count} pakietów; USB BULK={usb}; DS2 znalezione={ds2}");
            }
            else
            {
                var packets = UsbPcapClassic.Read(dlg.FileName);
                int ds2 = 0;
                foreach (var packet in packets.Where(x => x.Transfer == 3 && x.Payload.Length > 0))
                {
                    AddLine(packet.IsIn ? "USB-IN" : "USB-OUT", $"dev={packet.Device} ep=0x{packet.Endpoint:X2} {Hex(packet.Payload)}");
                    foreach (var frame in UsbPcapClassic.ExtractDs2(packet.Payload)) { AddLine(packet.IsIn ? "RX" : "TX", Hex(frame)); ds2++; }
                }
                AddLine("INFO", $"PCAP: {packets.Count} pakietów; DS2 znalezione={ds2}");
            }
        }
        catch (Exception ex) { MessageBox.Show("Import USB capture nie powiódł się: " + ex.Message); }
    }

    private void DecodeUsbPcap()
    {
        using var dlg = new OpenFileDialog { Filter = "USBPcap (*.pcap)|*.pcap|Wszystkie pliki|*.*" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try {
            var packets = UsbPcapClassic.Read(dlg.FileName);
            int count = 0;
            foreach (var packet in packets) {
                if (packet.Transfer != 3 || packet.Payload.Length == 0) continue; // BULK only
                string dir = packet.IsIn ? "USB-IN" : "USB-OUT";
                AddLine(dir, $"dev={packet.Device} ep=0x{packet.Endpoint:X2} {Hex(packet.Payload)}");
                foreach (var frame in UsbPcapClassic.ExtractDs2(packet.Payload)) {
                    AddLine(packet.IsIn ? "RX" : "TX", Hex(frame)); count++;
                }
            }
            AddLine("INFO", $"PCAP: {packets.Count} transferów, znalezione ramki DS2: {count}");
        } catch(Exception ex) { MessageBox.Show("Błąd dekodowania PCAP: " + ex.Message); }
    }

    private void StartUsbCapture()
    {
        if (nativeCapture != null) return;
        try
        {
            nativeCapture = new UsbPcapNative();
            nativeCapture.Start(usbRoot.SelectedItem?.ToString() ?? "USBPcap1");
            captureCts = new CancellationTokenSource();
            var token = captureCts.Token;
            Task.Run(() => NativeCaptureLoop(token), token);
            usbStart.Enabled = false; usbStop.Enabled = true;
            status.Text = "USB capture aktywny (sterownik)";
            AddLine("INFO", $"USB capture start: {usbRoot.SelectedItem}; bez USBPcapCMD");
        }
        catch (Exception ex)
        {
            nativeCapture?.Dispose(); nativeCapture = null;
            MessageBox.Show("Nie udało się uruchomić przechwytywania. Sterownik USBPcap musi być zainstalowany.\n" + ex.Message);
        }
    }

    private void NativeCaptureLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var chunk = nativeCapture?.ReadBuffer(1024 * 1024);
                if (chunk == null || chunk.Length == 0) { Thread.Sleep(20); continue; }
                capturePending.AddRange(chunk);
                ParseCaptureRecords();
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested) BeginInvoke(new Action(() => AddLine("ERR", "USB capture: " + ex.Message)));
                break;
            }
        }
    }

    // Native USBPcap device delivers concatenated USBPcap packet records (no PCAP record header).
    private void ParseCaptureRecords()
    {
        while (capturePending.Count >= 27)
        {
            int hdr = capturePending[0] | capturePending[1] << 8;
            uint dataLen = (uint)(capturePending[23] | capturePending[24] << 8 |
                                  capturePending[25] << 16 | capturePending[26] << 24);
            if (hdr < 27 || hdr > 4096 || dataLen > 2 * 1024 * 1024)
            {
                capturePending.Clear();
                BeginInvoke(new Action(() => AddLine("ERR", "Nieprawidłowy rekord USBPcap")));
                return;
            }
            int total = hdr + (int)dataLen;
            if (capturePending.Count < total) return;
            byte info = capturePending[16], ep = capturePending[21], transfer = capturePending[22];
            ushort dev = (ushort)(capturePending[19] | capturePending[20] << 8);
            byte[] payload = capturePending.Skip(hdr).Take((int)dataLen).ToArray();
            capturePending.RemoveRange(0, total);
            if (transfer != 3 || payload.Length == 0) continue;
            bool incoming = (ep & 0x80) != 0 || (info & 1) != 0;
            BeginInvoke(new Action(() =>
            {
                AddLine(incoming ? "USB-IN" : "USB-OUT", $"dev={dev} ep=0x{ep:X2} {Hex(payload)}");
                foreach (var frame in UsbPcapClassic.ExtractDs2(payload)) AddLine(incoming ? "RX" : "TX", Hex(frame));
            }));
        }
    }

    private void StopUsbCapture()
    {
        captureCts?.Cancel();
        try { nativeCapture?.Stop(); nativeCapture?.Dispose(); } catch { }
        nativeCapture = null; captureCts?.Dispose(); captureCts = null;
        usbLiveTimer.Stop();
        usbStart.Enabled = true; usbStop.Enabled = false;
        AddLine("INFO", "USB capture zatrzymany");
    }

    private void SendHex()
    {
        if (serial?.IsOpen != true) { MessageBox.Show("Najpierw połącz z portem COM."); return; }
        try {
            var clean = sendBox.Text.Replace("0x","",StringComparison.OrdinalIgnoreCase);
            var parts = clean.Split(new[] { ' ', '\t', '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            var data = parts.Select(p=>Convert.ToByte(p,16)).ToArray();
            serial.Write(data,0,data.Length);
            pendingEcho.AddRange(data);
            if (logTx.Checked) AddLine("TX",Hex(data));
        } catch(Exception ex) { MessageBox.Show("Nieprawidłowy HEX: "+ex.Message); }
    }

    private void AddLine(string direction, string data)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {direction}: {data}";
        logText.AppendLine(line);
        Color c = direction=="RX" ? Color.LightGreen : direction=="TX" ? Color.DeepSkyBlue : Color.DarkGray;
        output.SelectionStart=output.TextLength; output.SelectionColor=c; output.AppendText(line+Environment.NewLine); output.SelectionColor=output.ForeColor;
        output.ScrollToCaret();
    }
    private static string Hex(byte[] b) => BitConverter.ToString(b).Replace("-", " ");
    private void SaveLog()
    {
        using var dlg = new SaveFileDialog { Filter="Log tekstowy (*.txt)|*.txt|Wszystkie pliki (*.*)|*.*", FileName=$"DS2_{DateTime.Now:yyyyMMdd_HHmmss}.txt" };
        if (dlg.ShowDialog()==DialogResult.OK) File.WriteAllText(dlg.FileName,logText.ToString(),Encoding.UTF8);
    }
}
