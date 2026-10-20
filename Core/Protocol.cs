using System.Buffers.Binary;
using System.Text;

namespace NothingOnWindows.Core;

public sealed record Frame(ushort Command, byte Sequence, byte[] Payload);

public static class Protocol
{
    public static bool IsResponseTo(ushort request,ushort response)=>response==(request&0x7fff);

    public static ushort Crc(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0xffff;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++) crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xa001 : crc >> 1);
        }
        return crc;
    }

    public static byte[] Encode(ushort command, byte sequence, byte[] payload)
    {
        if (payload.Length > 1024) throw new ArgumentOutOfRangeException(nameof(payload));
        var bytes = new byte[10 + payload.Length];
        bytes[0] = 0x55; bytes[1] = 0x60; bytes[2] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(3), command);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(5), (ushort)payload.Length);
        bytes[7] = sequence;
        payload.CopyTo(bytes, 8);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - 2), Crc(bytes.AsSpan(0, bytes.Length - 2)));
        return bytes;
    }

    public static byte[] AdvancedPayload(EqBand[] bands)
    {
        if (bands.Length != 8 || bands.Any(b => !b.IsValid)) throw new ArgumentException("Use eight bands with gains from -6 to +6 dB, frequencies from 20 to 20000 Hz, and Q from 0.1 to 10.");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)0); writer.Write((byte)bands.Length);
        writer.Write(-Math.Max(0, bands.Max(b => b.Gain)));
        foreach (var b in bands) { writer.Write(b.Filter); writer.Write(b.Gain); writer.Write(b.Frequency); writer.Write(b.Q); }
        return stream.ToArray();
    }

    public static EqBand[] ParseBands(byte[] p)
    {
        if (p.Length < 6 || p[1] != 8 || p.Length != 110) throw new InvalidDataException("The headphones returned an unfamiliar EQ profile.");
        var bands = Enumerable.Range(0, 8).Select(i => {
            int o = 6 + i * 13;
            return new EqBand(BitConverter.ToSingle(p, o + 5), BitConverter.ToSingle(p, o + 1), BitConverter.ToSingle(p, o + 9), p[o]);
        }).ToArray();
        if (bands.Any(b => !b.IsValid)) throw new InvalidDataException("The headphones returned invalid EQ values.");
        return bands;
    }

    public static byte[] SimplePayload(float bass, float mid, float treble,DeviceProfile? profile=null)
    {
        if (new[] { bass, mid, treble }.Any(g => !float.IsFinite(g) || g < -6 || g > 6)) throw new ArgumentOutOfRangeException(nameof(bass));
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write((byte)3); writer.Write(-Math.Max(0, Math.Max(bass, Math.Max(mid, treble))));
        profile??=DeviceCatalog.HeadphoneA;
        foreach (var b in new[] { new EqBand(980, mid, profile.CustomMidQ, 1), new EqBand(profile.CustomTrebleFrequency, treble, 1, 2), new EqBand(140, bass, .8f, 0) })
        { writer.Write(b.Filter); writer.Write(b.Gain); writer.Write(b.Frequency); writer.Write(b.Q); }
        writer.Write(new byte[9]);
        return stream.ToArray();
    }
}

public sealed class FrameParser
{
    private readonly List<byte> buffer = [];
    public IEnumerable<Frame> Push(byte[] bytes)
    {
        buffer.AddRange(bytes);
        var frames = new List<Frame>();
        while (buffer.Count >= 10)
        {
            if (buffer[0] != 0x55 || buffer[1] != 0x60 || buffer[2] != 1) { buffer.RemoveAt(0); continue; }
            int length = buffer[5] | buffer[6] << 8;
            if (length > 1024) { buffer.RemoveAt(0); continue; }
            if (buffer.Count < length + 10) break;
            var packet = buffer.Take(length + 10).ToArray();
            ushort expected = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(packet.Length - 2));
            if (expected != Protocol.Crc(packet.AsSpan(0, packet.Length - 2)) && expected != Protocol.Crc(packet.AsSpan(8, length)))
            { buffer.RemoveAt(0); continue; }
            frames.Add(new Frame(BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(3)), packet[7], packet.AsSpan(8, length).ToArray()));
            buffer.RemoveRange(0, packet.Length);
        }
        return frames;
    }
}

public sealed record EqBand(float Frequency, float Gain = 0, float Q = 1, byte Filter = 1)
{
    public bool IsValid => float.IsFinite(Frequency) && Frequency is >= 20 and <= 20000 && float.IsFinite(Gain) && Gain is >= -6 and <= 6 && float.IsFinite(Q) && Q is >= 0.1f and <= 10 && Filter <= 2;
    public static EqBand[] Default => new float[] { 20, 50, 100, 500, 1000, 5000, 10000, 20000 }.Select(f => new EqBand(f)).ToArray();
}

public sealed record Gesture(byte Device, byte Control, byte Type, byte Action);

public sealed record BatteryPart(byte Device,int Level,bool Charging);

public sealed class HeadphoneState(DeviceProfile? profile=null)
{
    public DeviceProfile Profile {get;}=profile??DeviceCatalog.HeadphoneA;
    public int? Battery { get; set; }
    public bool Charging { get; set; }
    public string Firmware { get; set; } = "Unknown";
    public byte? Anc { get; set; }
    public byte? Spatial { get; set; }
    public int? Bass { get; set; }
    public byte BassWireValue {get;set;}
    public byte? Preset { get; set; }
    public bool? Advanced { get; set; }
    public bool? LowLag { get; set; }
    public bool? InEar {get;set;}
    public bool? FlatEq {get;set;}
    public BatteryPart[] Batteries {get;set;}=[];
    public EqBand[]? Bands { get; set; }
    public float[]? SimpleGains { get; set; }
    public Gesture[] Gestures { get; set; } = [];
    public HashSet<ushort> Readable { get; } = [];
    public StateChanges Apply(Frame frame)
    {
        var before=(Battery,Charging,Firmware,Anc,Spatial,Bass,Preset,Advanced,LowLag,InEar,FlatEq,Batteries,Bands,SimpleGains,Gestures,Available:Readable.Count);
        byte[] p = frame.Payload;
        switch (frame.Command)
        {
            case 0x4007: case 0xe001:
                if(p.Length>=1&&p[0]>0&&p[0]<=8&&p.Length>=1+p[0]*2)
                {
                    var parts=Enumerable.Range(0,p[0]).Select(i=>new BatteryPart(p[1+i*2],p[2+i*2]&127,(p[2+i*2]&128)!=0)).ToArray();
                    if(parts.Any(b=>b.Level>100)||parts.Select(b=>b.Device).Distinct().Count()!=parts.Length||!parts.Any(b=>Profile.Headphones?b.Device==6:b.Device is 2 or 3 or 4))break;
                    Batteries=parts.OrderBy(b=>b.Device).ToArray();
                    var primary=parts.Where(b=>Profile.Headphones?b.Device==6:b.Device is 2 or 3).ToArray();
                    Battery=primary.Length>0?primary.Min(b=>b.Level):null;Charging=primary.Any(b=>b.Charging);
                    Readable.Add(0xc007);
                }
                break;
            case 0x4042: Firmware = Encoding.UTF8.GetString(p).TrimEnd('\0'); Readable.Add(0xc042); break;
            case 0x401e: case 0xe003: case 0x6003: if (p.Length>=2&&Profile.Supports(0xc01e,Firmware)&&(Profile.ActiveAncLevels(Firmware).Contains(p[1])||p[1]==5||p[1]==7&&Profile.Id!="B157")) { Anc = p[1]; Readable.Add(0xc01e); } break;
            case 0x404f:
                if(p.Length>=2&&Profile.Spatial.FirstOrDefault(m=>m.Mode==p[0]&&m.Head==p[1]) is {} spatial){Spatial=spatial.Value;Readable.Add(0xc04f);}break;
            case 0x404e:
                if(Profile.Bass&&p.Length>=2&&p[0]<=1&&p[1]<=Profile.BassStep*Profile.BassLevels&&p[1]%Profile.BassStep==0){Bass=p[0]==0?0:p[1]/Profile.BassStep;BassWireValue=p[1];Readable.Add(0xc04e);}break;
            case 0x401f: if (p.Length > 0&&Profile.Presets.Any(v=>v.Value==p[0])) { Preset = p[0]; Readable.Add(0xc01f); } break;
            case 0x404c: if (Profile.HasAdvancedEq(Firmware)&&p.Length > 0 && p[0] <= 1) { Advanced = p[0] == 1; Readable.Add(0xc04c); } break;
            case 0x4041: if (Profile.Supports(0xc041,Firmware)&&p.Length > 0 && p[0] is 1 or 2) { LowLag = p[0] == 1; Readable.Add(0xc041); } break;
            case 0x400e:
                if(Profile.InEar&&p.Length>=3&&p.Length>=1+p[0]*2)
                    for(int i=0;i<p[0];i++)if(p[1+i*2]==1&&p[2+i*2]<=1){InEar=p[2+i*2]==1;Readable.Add(0xc00e);break;}
                break;
            case 0x4068: case 0xe01c: case 0x601c: if(Profile.Id=="B192"&&p.Length>0&&p[0]<=1){FlatEq=p[0]==1;Readable.Add(0xc068);}break;
            case 0x404d: if(Profile.HasAdvancedEq(Firmware))try { Bands = Protocol.ParseBands(p); Readable.Add(0xc04d); } catch (InvalidDataException) { } break;
            case 0x4044:
                if(!Profile.CustomEq)break;
                if (p.Length >= 44 && p.All(b => b == 0)) { SimpleGains = [0, 0, 0]; Readable.Add(0xc044); break; }
                if (p.Length >= 44 && p[0] == 3)
                {
                    var gains = new float[3]; var found = new HashSet<byte>();
                    for (int i = 0; i < 3; i++) { int o = 5 + i * 13; if (p[o] <= 2) { gains[p[o]] = BitConverter.ToSingle(p, o + 1); found.Add(p[o]); } }
                    if (found.Count == 3 && gains.All(g => float.IsFinite(g) && g >= -6 && g <= 6)) { SimpleGains = gains; Readable.Add(0xc044); }
                }
                break;
            case 0x4018:
                if (p.Length >= 1 && p.Length >= 1 + p[0] * 4)
                {
                    Gestures = Enumerable.Range(0, p[0]).Select(i => new Gesture(p[1+i*4], p[2+i*4], p[3+i*4], p[4+i*4])).ToArray();
                    Readable.Add(0xc018);
                }
                break;
        }
        StateChanges changes=StateChanges.None;
        if(before.Battery!=Battery||before.Charging!=Charging||!before.Batteries.SequenceEqual(Batteries))changes|=StateChanges.Battery;
        if(before.Firmware!=Firmware)changes|=StateChanges.Device;
        if(before.Anc!=Anc||before.Spatial!=Spatial||before.Bass!=Bass||before.LowLag!=LowLag||before.InEar!=InEar||before.FlatEq!=FlatEq)changes|=StateChanges.Sound;
        if(before.FlatEq!=FlatEq)changes|=StateChanges.Eq;
        if(before.Preset!=Preset||before.Advanced!=Advanced||!(before.SimpleGains??[]).SequenceEqual(SimpleGains??[]))changes|=StateChanges.Eq;
        if(!(before.Bands??[]).SequenceEqual(Bands??[]))changes|=StateChanges.Bands;
        if(!before.Gestures.SequenceEqual(Gestures))changes|=StateChanges.Controls;
        if(before.Available!=Readable.Count)changes|=StateChanges.Availability;
        return changes;
    }
}
