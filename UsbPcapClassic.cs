using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DS2Logger;

internal sealed record UsbPacket(ushort Device, byte Endpoint, byte Transfer, bool IsIn, byte[] Payload);

internal static class UsbPcapClassic
{
    public static List<UsbPacket> Read(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);
        if (fs.Length < 24) throw new InvalidDataException("Za krótki plik PCAP.");
        uint magic = br.ReadUInt32();
        bool swap = magic == 0xD4C3B2A1 || magic == 0x4D3CB2A1;
        bool nano = magic == 0xA1B23C4D || magic == 0x4D3CB2A1;
        if (magic != 0xA1B2C3D4 && magic != 0xD4C3B2A1 && magic != 0xA1B23C4D && magic != 0x4D3CB2A1)
            throw new InvalidDataException("To nie jest klasyczny PCAP.");
        ReadU16(br,swap); ReadU16(br,swap); ReadU32(br,swap); ReadU32(br,swap); ReadU32(br,swap);
        uint link = ReadU32(br,swap);
        if (link != 249) throw new InvalidDataException($"Link type {link}; oczekiwano USBPcap (249).");
        var result = new List<UsbPacket>();
        while (fs.Position + 16 <= fs.Length)
        {
            ReadU32(br,swap); ReadU32(br,swap);
            uint incl = ReadU32(br,swap); ReadU32(br,swap);
            if (incl > 16*1024*1024 || fs.Position + incl > fs.Length) throw new InvalidDataException("Nieprawidłowa długość rekordu.");
            byte[] b=br.ReadBytes((int)incl);
            if (b.Length < 27) continue;
            int h=U16(b,0); if(h<27 || h>b.Length) continue;
            byte info=b[16], ep=b[21], transfer=b[22];
            ushort dev=U16(b,19);
            uint dl=U32(b,23);
            int n=(int)Math.Min(dl,(uint)(b.Length-h));
            if(n<=0) continue;
            result.Add(new UsbPacket(dev,ep,transfer,(ep&0x80)!=0 || (info&1)!=0,b.Skip(h).Take(n).ToArray()));
        }
        return result;
    }

    public static List<UsbPacket> ReadNew(string path, ref long offset)
    {
        var result = new List<UsbPacket>();
        using var fs = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
        using var br = new BinaryReader(fs);
        if(offset==0)
        {
            if(fs.Length<24)return result;
            uint magic=br.ReadUInt32();
            if(magic!=0xA1B2C3D4 && magic!=0xD4C3B2A1 && magic!=0xA1B23C4D && magic!=0x4D3CB2A1)
                throw new InvalidDataException("PCAP header invalid.");
            bool swap=magic==0xD4C3B2A1 || magic==0x4D3CB2A1;
            ReadU16(br,swap);ReadU16(br,swap);ReadU32(br,swap);ReadU32(br,swap);ReadU32(br,swap);
            uint link=ReadU32(br,swap);
            if(link!=249)throw new InvalidDataException("PCAP linktype nie jest USBPcap (249).");
            offset=24;
        }
        fs.Position=offset;
        while(fs.Position+16<=fs.Length)
        {
            long start=fs.Position;
            ReadU32(br,false);ReadU32(br,false);
            uint incl=ReadU32(br,false);ReadU32(br,false);
            if(incl>16*1024*1024 || fs.Position+incl>fs.Length){fs.Position=start;break;}
            byte[] b=br.ReadBytes((int)incl);offset=fs.Position;
            if(b.Length<27)continue;
            int h=U16(b,0);if(h<27||h>b.Length)continue;
            byte ep=b[21],transfer=b[22];ushort dev=U16(b,19);uint dl=U32(b,23);
            int n=(int)Math.Min(dl,(uint)(b.Length-h));if(n<=0)continue;
            result.Add(new UsbPacket(dev,ep,transfer,(ep&0x80)!=0,b.Skip(h).Take(n).ToArray()));
        }
        return result;
    }

    public static IEnumerable<byte[]> ExtractDs2(byte[] data)
    {
        var frames=new List<byte[]>();
        for(int i=0;i<data.Length;)
        {
            if(data[i]!=0x12 || i+1>=data.Length){i++;continue;}
            int len=data[i+1];
            if(len<4 || i+len>data.Length){i++;continue;}
            frames.Add(data.Skip(i).Take(len).ToArray()); i+=len;
        }
        return frames;
    }
    private static ushort U16(byte[] b,int o)=> (ushort)(b[o]|b[o+1]<<8);
    private static uint U32(byte[] b,int o)=> (uint)(b[o]|b[o+1]<<8|b[o+2]<<16|b[o+3]<<24);
    private static ushort ReadU16(BinaryReader r,bool s){var b=r.ReadBytes(2);return s?(ushort)(b[0]<<8|b[1]):(ushort)(b[0]|b[1]<<8);}
    private static uint ReadU32(BinaryReader r,bool s){var b=r.ReadBytes(4);return s?(uint)(b[0]<<24|b[1]<<16|b[2]<<8|b[3]):(uint)(b[0]|b[1]<<8|b[2]<<16|b[3]<<24);}
}
