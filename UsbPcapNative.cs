using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DS2Logger;

/// <summary>
/// Direct user-mode interface to the USBPcap filter driver. This replaces USBPcapCMD
/// as a process dependency. The driver itself must be installed by the DS2 Logger installer.
/// </summary>
internal sealed class UsbPcapNative : IDisposable
{
    private const uint FILE_DEVICE_UNKNOWN = 0x22;
    private const uint METHOD_BUFFERED = 0;
    private const uint FILE_READ_ACCESS = 1;
    private const uint FILE_WRITE_ACCESS = 2;
    private static uint CtlCode(uint fn, uint access) => (FILE_DEVICE_UNKNOWN << 16) | (access << 14) | (fn << 2) | METHOD_BUFFERED;
    private static readonly uint IOCTL_SETUP_BUFFER = CtlCode(0x800, FILE_READ_ACCESS);
    private static readonly uint IOCTL_START_FILTERING = CtlCode(0x801, FILE_READ_ACCESS | FILE_WRITE_ACCESS);
    private static readonly uint IOCTL_STOP_FILTERING = CtlCode(0x802, FILE_READ_ACCESS | FILE_WRITE_ACCESS);
    private static readonly uint IOCTL_SET_SNAPLEN = CtlCode(0x804, FILE_READ_ACCESS);

    private SafeFileHandle? handle;
    private bool filtering;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct AddressFilter { public uint A0, A1, A2, A3; public byte FilterAll; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle h, uint code, IntPtr input, uint inputSize,
        IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle h, byte[] buffer, uint count, IntPtr read, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr overlapped, out uint transferred, bool wait);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(SafeFileHandle h, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateEvent(IntPtr attributes, bool manualReset, bool initialState, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeOverlapped
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public uint Offset;
        public uint OffsetHigh;
        public IntPtr Event;
    }

    public void Start(string rootHubName = "USBPcap1")
    {
        Stop();
        handle = CreateFile("\\\\.\\" + rootHubName, 0x80000000u | 0x40000000u, 0,
            IntPtr.Zero, 3, 0x40000000, IntPtr.Zero); // FILE_FLAG_OVERLAPPED
        if (handle.IsInvalid) { var e = Marshal.GetLastWin32Error(); handle.Dispose(); handle = null; throw new Win32Exception(e, "Nie można otworzyć urządzenia USBPcap."); }
        // Driver allocates its capture ring. Size is passed as a 32-bit value.
        IntPtr sizePtr = Marshal.AllocHGlobal(4);
        try {
            Marshal.WriteInt32(sizePtr, 65535);
            if (!DeviceIoControl(handle, IOCTL_SET_SNAPLEN, sizePtr, 4, IntPtr.Zero, 0, out _, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(), "USBPcap SET_SNAPLEN nie powiódł się.");
            Marshal.WriteInt32(sizePtr, 4 * 1024 * 1024);
            if (!DeviceIoControl(handle, IOCTL_SETUP_BUFFER, sizePtr, 4, IntPtr.Zero, 0, out _, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(), "USBPcap SETUP_BUFFER nie powiódł się.");
        }
        finally { Marshal.FreeHGlobal(sizePtr); }
        var f = new AddressFilter { FilterAll = 1 };
        IntPtr filterPtr = Marshal.AllocHGlobal(Marshal.SizeOf<AddressFilter>());
        try { Marshal.StructureToPtr(f, filterPtr, false); if (!DeviceIoControl(handle, IOCTL_START_FILTERING, filterPtr, (uint)Marshal.SizeOf<AddressFilter>(), IntPtr.Zero, 0, out _, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(), "USBPcap START_FILTERING nie powiódł się."); filtering = true; }
        finally { Marshal.FreeHGlobal(filterPtr); }
    }

    /// <summary>Reads a driver buffer. USBPcap packet records are returned in native capture format.</summary>
    public byte[] ReadBuffer(int maxBytes = 1024 * 1024, int waitMilliseconds = 250)
    {
        if (handle == null || handle.IsInvalid) throw new InvalidOperationException("Capture nie jest uruchomiony.");
        byte[] buffer = new byte[maxBytes];
        IntPtr ov = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
        IntPtr evt = CreateEvent(IntPtr.Zero, true, false, null);
        if (evt == IntPtr.Zero) { Marshal.FreeHGlobal(ov); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        try
        {
            Marshal.StructureToPtr(new NativeOverlapped { Event = evt }, ov, false);
            bool ok = ReadFile(handle, buffer, (uint)buffer.Length, IntPtr.Zero, ov);
            int err = ok ? 0 : Marshal.GetLastWin32Error();
            if (!ok && err == 997) // ERROR_IO_PENDING
            {
                uint wait = WaitForSingleObject(evt, (uint)Math.Max(1, waitMilliseconds));
                if (wait == 258) // WAIT_TIMEOUT
                {
                    CancelIoEx(handle, ov); WaitForSingleObject(evt, 1000);
                    return Array.Empty<byte>();
                }
                if (wait != 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Wait USBPcap failed.");
            }
            else if (!ok)
            {
                if (err == 259) return Array.Empty<byte>();
                throw new Win32Exception(err, "USBPcap ReadFile failed.");
            }
            if (!GetOverlappedResult(handle, ov, out uint got, false))
            {
                int e = Marshal.GetLastWin32Error();
                if (e == 995 || e == 259) return Array.Empty<byte>();
                throw new Win32Exception(e, "USBPcap GetOverlappedResult failed.");
            }
            if (got == 0) return Array.Empty<byte>();
            Array.Resize(ref buffer, (int)got); return buffer;
        }
        finally { CloseHandle(evt); Marshal.FreeHGlobal(ov); }
    }

    public void Stop()
    {
        if (handle != null && !handle.IsInvalid)
        {
            if (filtering) DeviceIoControl(handle, IOCTL_STOP_FILTERING, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
            filtering = false; handle.Dispose();
        }
        handle = null;
    }
    public void Dispose() => Stop();
}
