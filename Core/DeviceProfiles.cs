using System.Text.Json;
using System.Text.RegularExpressions;

namespace NothingOnWindows.Core;

public sealed record DeviceAction(string Name,byte Code);
public sealed record DeviceControl(string Group,string Label,byte Device,byte Control,int Type,bool Custom,DeviceAction[] Actions);
public sealed record ControlVariant(string? Minimum,string? Maximum,DeviceControl[] Controls);
public sealed record SpatialMode(string Label,byte Value,byte Mode,byte Head=0);

public sealed record DeviceProfile(string Id,string Name,string Slug,bool Headphones,byte[] AncLevels,bool Bass,bool CustomEq,bool AdvancedEq,bool InEar,SpatialMode[] Spatial,string[] Aliases)
{
    public string ShortName=>Name.Replace("Nothing ","");
    public string ImageResource=>$"Assets/Devices/{Id}.png";
    public bool Verified=>Id=="B186";
    public int BassStep=>Id=="B186"?5:2;
    public int BassLevels=>Id=="B186"?2:5;
    public float CustomTrebleFrequency=>Headphones||Id=="B157"?3500:3400;
    public float CustomMidQ=>Id=="B157"?.66f:.7f;
    public bool SpatialEqExclusive=>Id is "B173" or "B190";
    public string Notes=>Id=="B192"?"Dynamic spatial scenes, Personal Sound, Auracast and driver-specific EQ need additional protocol support. Flat EQ follows the physical switch.":Id=="B173"?"Super Mic and Personal Sound setup remain in Nothing X.":Id=="B190"?"Audio Snapshot and call recording setup remain in Nothing X.":"";
    public override string ToString()=>Name;
    public byte[] ActiveAncLevels(string firmware)=>Id=="B157"?(FirmwareAtLeast(firmware,"1.0.2.0")?[1]:[]):AncLevels;
    public bool HasAdvancedEq(string firmware)=>AdvancedEq&&(Id switch{"B155"=>FirmwareAtLeast(firmware,"1.0.1.95"),"B157"=>FirmwareAtLeast(firmware,"1.0.1.80"),_=>true});
    public bool Supports(ushort read,string firmware)=>read switch
    {
        0xc007 or 0xc042 or 0xc01f or 0xc018=>true,
        0xc041=>Id!="B181"||FirmwareAtLeast(firmware,"1.0.1.75"),
        0xc01e=>ActiveAncLevels(firmware).Length>0,
        0xc04f=>Spatial.Length>0,
        0xc04e=>Bass,
        0xc044=>CustomEq,
        0xc04c or 0xc04d=>HasAdvancedEq(firmware),
        0xc00e=>InEar,
        0xc068=>Id=="B192",
        _=>false
    };
    public ushort[] ReadCommands(string firmware)=>new ushort[]{0xc007,0xc042,0xc068,0xc01e,0xc04f,0xc04e,0xc01f,0xc04c,0xc041,0xc00e,0xc018,0xc04d,0xc044}.Where(c=>Supports(c,firmware)).ToArray();
    public (string Label,byte Value)[] Presets=>new (string,byte)[]{("Balanced",0),("More bass",3),("More treble",2),("Voice",1)}.Concat(Id=="B192"?new[]{("New Instrument",(byte)7)}:[]).Concat(CustomEq?new[]{("Custom",(byte)5)}:[]).ToArray();
    public byte[] SpatialPayload(byte value)
    {var mode=Spatial.SingleOrDefault(m=>m.Value==value)??throw new ArgumentException("Spatial mode is unavailable for this device.");return [mode.Mode,mode.Head];}
    public byte[] BassPayload(int level,byte storedWire=0)
    {
        if(!Bass||level<0||level>BassLevels)throw new ArgumentException("Bass level is unavailable for this device.");
        int wire=level>0?level*BassStep:storedWire>0&&storedWire<=BassLevels*BassStep&&storedWire%BassStep==0?storedWire:BassStep;
        return [(byte)(level==0?0:1),(byte)wire];
    }
    public DeviceControl[] Controls(string firmware)
    {
        var variants=DeviceCatalog.ControlsByModel[Id];
        var known=Version.TryParse(firmware,out _);
        var match=variants.OrderByDescending(v=>PatternVersion(v.Minimum)).FirstOrDefault(v=>!known||Matches(firmware,v.Minimum,true)&&Matches(firmware,v.Maximum,false));
        return (match??variants[^1]).Controls;
    }
    internal static bool FirmwareAtLeast(string firmware,string minimum)
    {
        if(!Version.TryParse(firmware,out var actual))return true;
        var target=Version.Parse(minimum);
        // The upstream boundaries are *.*.build.revision, independent of prefix.
        return actual.Build>target.Build||actual.Build==target.Build&&Math.Max(0,actual.Revision)>=target.Revision;
    }
    private static Version PatternVersion(string? pattern)=>Version.Parse((pattern??"0.0.0.0").Replace("*","0"));
    private static bool Matches(string firmware,string? pattern,bool minimum)
    {
        if(pattern==null)return true;
        var actual=firmware.Split('.');var target=pattern.Split('.');
        for(int i=0;i<Math.Min(actual.Length,target.Length);i++)
        {if(target[i]=="*")continue;int comparison=int.Parse(actual[i]).CompareTo(int.Parse(target[i]));if(comparison!=0)return minimum?comparison>0:comparison<0;}
        return true;
    }
}

public static class DeviceCatalog
{
    private static SpatialMode[] StaticSpatial=>[new("Fixed",1,1),new("Off",0,0)];
    public static IReadOnlyList<DeviceProfile> All {get;}=new DeviceProfile[]
    {
        new("B181","Nothing Ear (1)","ear-1",false,[3,1],false,false,false,true,[],["Nothing ear (1)","Ear (1)"]),
        new("B155","Nothing Ear (2)","ear-2",false,[3,2,1,4],false,true,true,true,[],["Ear (2)"]),
        new("B171","Nothing Ear","ear",false,[3,2,1,4],true,true,true,true,[],["Nothing Ear (2024)","Ear (2024)"]),
        new("B162","Nothing Ear (a)","ear-a",false,[3,2,1,4],true,true,false,true,[],["Ear (a)"]),
        new("B173","Nothing Ear (3)","ear-3",false,[3,2,1,4],true,true,true,true,StaticSpatial,["Ear (3)"]),
        new("B190","Nothing Ear (3a)","ear-3a",false,[3,2,1,4],false,true,true,true,StaticSpatial,["Ear (3a)"]),
        new("B157","Nothing Ear (stick)","ear-stick",false,[],false,true,true,true,[],["Ear (stick)","Ear (Stick)"]),
        new("B174","Nothing Ear (open)","ear-open",false,[],false,true,true,false,[],["Ear (open)"]),
        new("B170","Nothing Headphone (1)","headphone-1",true,[3,2,1,4],true,true,true,true,[new("Fixed",1,1),new("Head tracking",4,1,1),new("Off",0,0)],["Headphone (1)"]),
        new("B192","Nothing Headphone (1) Pro","headphone-1-pro",true,[3,2,1,4],false,true,true,true,[],["Headphone (1) Pro"]),
        new("B186","Nothing Headphone (a)","headphone-a",true,[3,2,1,4],true,true,true,false,[new("Concert mode",2,2),new("Cinema mode",3,3),new("Off",0,0)],["Headphone (a)"])
    };
    public static DeviceProfile HeadphoneA=>All.Single(p=>p.Id=="B186");
    public static DeviceProfile? Detect(string name)
    {
        static string Normalize(string s)=>Regex.Replace(s.Trim(),@"\s+", " ").ToLowerInvariant();
        string normalized=Normalize(name);
        return All.FirstOrDefault(p=>Normalize(p.Name)==normalized||Normalize(p.Id)==normalized||p.Aliases.Any(a=>Normalize(a)==normalized));
    }
    internal static Dictionary<string,ControlVariant[]> ControlsByModel {get;}=LoadControls();
    private static Dictionary<string,ControlVariant[]> LoadControls()
    {
        using var source=typeof(DeviceCatalog).Assembly.GetManifestResourceStream("NothingOnWindows.device-controls.json")??throw new InvalidDataException("Device control profiles are missing.");
        return JsonSerializer.Deserialize<Dictionary<string,ControlVariant[]>>(source)!;
    }
}
