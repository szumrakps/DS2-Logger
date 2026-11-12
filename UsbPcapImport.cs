using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DS2Logger;

/// <summary>
/// Minimal PCAPNG reader. It extracts Enhanced Packet Block packet byte arrays.
/// It does NOT yet decode USBPcap pseudo-header/URB direction and endpoints.
/// </summary>
public static class UsbPcapImport
{
    public sealed record Packet(long TimestampTicks, byte[] Data, uint OriginalLength);
    public static List<Packet> ReadPcapNg(string path)
    {
        var packets = new List<Packet>();
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);
        bool little = true;
        ulong tsResolution = 1_000_000; // default 10^-6 seconds
        var interfaces = new List<(ulong unitsPerSecond, int linkType)>();
        while (fs.Position + 12 <= fs.Length)
        {
            uint typeRaw = br.ReadUInt32();
            if (typeRaw == 0x0A0D0D0A)
            {
                uint blockLen = br.ReadUInt32();
                uint bom = br.ReadUInt32();
                little = bom == 0x1A2B3C4D;
                if (!little && bom != 0x4D3C2B1A) throw new InvalidDataException("Nieznany byte order magic.");
                uint major = ReadU32(br,little), minor=ReadU32(br,little);
                ulong sectionLen=ReadU64(br,little);
                if (blockLen < 28 || blockLen > fs.Length) throw new InvalidDataException("Błędny blok SHB.");
                fs.Position = (long)(blockLen - 4); ReadU32(br,little);
                continue;
            }
            uint len=ReadU32(br,little);
            if (len < 12 || fs.Position + len - 8 > fs.Length) throw new InvalidDataException("Błędna długość bloku PCAPNG.");
            long blockStart=fs.Position-8;
            if (typeRaw==1) // Interface Description Block
            {
                ushort link=ReadU16(br,little); // link type
                br.ReadUInt16(); // reserved
                uint snap=ReadU32(br,little);
                ulong resolution=1_000_000;
                long end=blockStart+len-4;
                while(fs.Position+4<=end)
                {
                    ushort code=ReadU16(br,little), optLen=ReadU16(br,little);
                    if(code==0) break;
                    byte[] val=br.ReadBytes(optLen);
                    if((optLen&3)!=0) fs.Position += 4-(optLen&3);
                    if(code==9 && val.Length>=1) resolution=(val[0]&0x80)!=0 ? Pow2(val[0]&0x7f) : Pow10(val[0]);
                }
                interfaces.Add((resolution,link));
            }
            else if(typeRaw==6) // Enhanced Packet Block
            {
                uint ifid=ReadU32(br,little), hi=ReadU32(br,little), lo=ReadU32(br,little), cap=ReadU32(br,little), orig=ReadU32(br,little);
                byte[] data=br.ReadBytes((int)cap);
                if((cap&3)!=0) fs.Position += 4-(cap&3);
                ulong units=ifid<interfaces.Count?interfaces[(int)ifid].unitsPerSecond:1_000_000;
                ulong ts=((ulong)hi<<32)|lo;
                long ticks=(long)(ts*(TimeSpan.TicksPerSecond/(double)units));
                packets.Add(new Packet(ticks,data,orig));
            }
            fs.Position=blockStart+len;
        }
        return packets;
    }
    private static uint ReadU32(BinaryReader br,bool le){var b=br.ReadBytes(4);return le?BitConverter.ToUInt32(b):BitConverter.ToUInt32(b.Reverse().ToArray());}
    private static ulong ReadU64(BinaryReader br,bool le){var b=br.ReadBytes(8);return le?BitConverter.ToUInt64(b):BitConverter.ToUInt64(b.Reverse().ToArray());}
    private static ushort ReadU16(BinaryReader br,bool le){var b=br.ReadBytes(2);return le?BitConverter.ToUInt16(b):BitConverter.ToUInt16(b.Reverse().ToArray());}
    private static ulong Pow10(byte n){ulong v=1;for(int i=0;i<n&&i<18;i++)v*=10;return v;}
    private static ulong Pow2(byte n)=>n<63?1UL<<n:1UL;
}
