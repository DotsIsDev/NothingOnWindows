using NothingOnWindows.Core;
using System.Text;
using System.Text.Json;

if(args.Length==2&&args[0]=="--update-result"){File.WriteAllText(args[1]+".restarted","restarted");return;}
if(args.Length==3&&args[0]=="--apply-update-test"){await UpdateInstaller.ApplyAsync(args[1],true,args[2]);return;}

int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
void Reject(Action action, string name) { try { action(); } catch (ArgumentException) { checks++; return; } catch (InvalidDataException) { checks++; return; } throw new Exception(name); }
Check(Protocol.Crc(Encoding.ASCII.GetBytes("123456789")) == 0x4b37, "CRC known vector");
byte[] packet = Protocol.Encode(0xc007, 1, []);
Check(Convert.ToHexString(packet) == "55600107C0000001ACDF", "Battery query wire fixture");
for (int split = 0; split <= packet.Length; split++)
{
    var parser = new FrameParser();
    var frames = parser.Push(packet[..split]).Concat(parser.Push(packet[split..])).ToArray();
    Check(frames.Length == 1 && frames[0].Command == 0xc007 && frames[0].Sequence == 1, "Fragmented frame");
}
Check(new FrameParser().Push(packet.Concat(packet).ToArray()).Count() == 2, "Coalesced frames");
byte[] bad = packet.ToArray(); bad[^1] ^= 1;
Check(new FrameParser().Push(bad.Concat(packet).ToArray()).Count() == 1, "Corruption and resynchronization");
Check(new FrameParser().Push(new byte[] { 0x55, 0x60, 1, 0, 0, 255, 255, 0, 1, 2 }.Concat(packet).ToArray()).Count() == 1, "Oversized length resynchronization");
var bands = EqBand.Default; bands[0] = bands[0] with { Gain = 4.5f };
byte[] payload = Protocol.AdvancedPayload(bands);
Check(payload.Length == 110 && BitConverter.ToSingle(payload, 2) == -4.5f, "EQ preamp and length");
Check(Protocol.ParseBands(payload).SequenceEqual(bands), "Advanced EQ roundtrip");
Reject(() => Protocol.AdvancedPayload([new EqBand(100, float.NaN)]), "Reject malformed bands");
Reject(() => new EqProfile("", bands).Validate(), "Reject empty name");
Reject(() => new EqProfile("bad", [new EqBand(100, 100)]).Validate(), "Reject dangerous gain");
var profile = new EqProfile("Test", bands);
var decoded = JsonSerializer.Deserialize<EqProfile>(JsonSerializer.Serialize(profile))!;
decoded.Validate(); Check(decoded.Name == "Test" && decoded.Bands.SequenceEqual(bands), "Profile JSON roundtrip");
var state = new HeadphoneState();
state.Apply(new Frame(0x4007, 1, [1, 6, 0x80])); Check(state.Battery == 0 && state.Charging, "Zero percent battery is valid");
state.Apply(new Frame(0x404e, 1, [1, 5])); Check(state.Bass == 1, "B186 level one is wire value five");
state.Apply(new Frame(0x404e, 1, [1, 10])); Check(state.Bass == 2, "B186 level two is wire value ten");
state.Apply(new Frame(0x404e, 1, [1, 4])); Check(state.Bass == 2, "Reject other-model bass values");
state.Apply(new Frame(0x4018, 1, [1, 6, 10, 1, 11])); Check(state.Gestures.Single() == new Gesture(6, 10, 1, 11), "Headphone gesture discriminators");
byte[] simple = Protocol.SimplePayload(2, -1, 3);
state.Apply(new Frame(0x4044, 1, simple)); Check(state.SimpleGains!.SequenceEqual(new float[] { 2, -1, 3 }), "Simple custom EQ filters");
state.Apply(new Frame(0x4044, 1, new byte[110])); Check(state.SimpleGains!.SequenceEqual(new float[] { 0, 0, 0 }), "Uninitialized custom EQ is neutral");
Console.WriteLine($"Passed {checks} protocol checks.");
const string playgroundFixture = "H4sIAAAAAAAA/2NIYGBwOMDA4OHEwNBgD2GfgLJBIMYZwT4BYwPVVLlA2AcOMDRYu0LYE4Ds3UC2ggMDwwKQuFta2jJ7Rm6nxJzEvOTUFAXXQCZRv/ySjMy8dAWP1MSUgoz8vFQFjURNAAvAB3uGAAAA";
var shared = NothingProfileCodec.Decode(playgroundFixture);
Check(shared.Model == "Nothing Headphone (a)" && shared.Bands.Length == 8, "Official Playground profile format");
Check(shared.Bands[0].Frequency == 50 && shared.Bands[0].Gain == -3, "Official profile binary values");
Reject(() => NothingProfileCodec.Decode(Convert.ToBase64String(new byte[5])), "Reject invalid gzip");
if (args.Length > 0 && args[0] == "--catalog") { var online = await PlaygroundCatalog.LoadAsync(); Check(online.Count > 0, "Live public catalog"); Console.WriteLine($"Public catalog: {online.Count} usable profiles, {online.Count(p => p.Model.Length > 0)} explicitly for Headphone (a)."); }
var shapes=new byte[]{0,1,2,3}.Select(p=>EqPresentation.Preview(p,[1,2,3])).ToArray();
Check(shapes.Select(p=>string.Join(",",p)).Distinct().Count()==4,"Distinct built-in preset previews");
Check(shapes[1][1]>shapes[1][0]&&shapes[1][1]>shapes[1][2],"Voice preview emphasizes mid");
Check(shapes[2][2]>shapes[2][0]&&shapes[2][2]>shapes[2][1],"Treble preview emphasizes treble");
Check(shapes[3][0]>shapes[3][1]&&shapes[3][0]>shapes[3][2],"Bass preview emphasizes bass");
float[] originalCustom=[1,-2,3];var customPreview=EqPresentation.Preview(5,originalCustom);
Check(customPreview.SequenceEqual(originalCustom),"Custom preview retains actual gains");
customPreview[0]=6;Check(originalCustom[0]==1,"Preview does not mutate saved custom gains");
Check(EqPresentation.Preview(null,originalCustom).All(g=>g==0),"Unknown preset uses neutral preview");
string settingsPath=Path.Combine(Directory.GetCurrentDirectory(),"artifacts","nothing-settings-"+Guid.NewGuid()+".json");
try
{
    var settingsStore=new PreferencesStore(settingsPath);
    Check(!settingsStore.Load().CloseToTray,"Close-to-tray defaults off");
    settingsStore.Save(new AppPreferences{CloseToTray=true});Check(settingsStore.Load().CloseToTray,"Settings enabled persistence");
    settingsStore.Save(new AppPreferences{CloseToTray=false});Check(!settingsStore.Load().CloseToTray,"Settings disabled persistence");
}
finally{if(File.Exists(settingsPath))File.Delete(settingsPath);}
for(int edge=1;edge<=8;edge++)
{
    var proposed=new SizingBounds(100,80,628,960);
    var sized=AspectRatioSizing.Constrain(proposed,edge,.55,330,600,440,800);
    Check(Math.Abs(sized.Width/(double)sized.Height-.55)<.001,"Portrait ratio on native resize edge "+edge);
    Check((edge is 1 or 4 or 7?sized.Right==proposed.Right:sized.Left==proposed.Left)&&(edge is 3 or 4 or 5?sized.Bottom==proposed.Bottom:sized.Top==proposed.Top),"Opposite edge anchored on resize "+edge);
}
var minimum=AspectRatioSizing.Constrain(new(0,0,100,100),8,.55,330,600,440,800);
Check(minimum.Width==330&&minimum.Height==600,"Resize minimum preserves aspect");
var maximum=AspectRatioSizing.Constrain(new(0,0,1200,2000),8,.55,330,600,440,800,1000);
Check(maximum.Height==1000&&maximum.Width==550,"Work area resize maximum preserves aspect");
var scaled=AspectRatioSizing.Constrain(new(0,0,800,1600),2,.55,495,900,660,1200,1800);
Check(scaled.Width>=495&&scaled.Height>=900&&Math.Abs(scaled.Width/(double)scaled.Height-.55)<.001,"DPI scaled resize bounds");
Reject(()=>AspectRatioSizing.Constrain(new(),2,double.NaN,330,600,440,800),"Reject invalid aspect ratio");
var liveState=new HeadphoneState();
var change=liveState.Apply(new Frame(0xe003,9,[1,7]));
Check(liveState.Anc==7&&change.HasFlag(StateChanges.Sound),"Physical ANC notification updates sound state");
Check(!change.HasFlag(StateChanges.Eq),"ANC notification does not invalidate EQ editing");
Check(liveState.Apply(new Frame(0xe003,9,[1,7]))==StateChanges.None,"Repeated device value does not refresh controls");
Check(liveState.Apply(new Frame(0x6003,9,[1,5])).HasFlag(StateChanges.Sound)&&liveState.Anc==5,"Alternate ANC notification updates state");
Check(liveState.Apply(new Frame(0xe003,9,[1,99]))==StateChanges.None&&liveState.Anc==5,"Malformed notification preserves known state");
Check(liveState.Apply(new Frame(0x401e,10,[1,4,0,2,4,0])).HasFlag(StateChanges.Sound)&&liveState.Anc==4,"Read-only fallback detects ANC without push event");
Check(liveState.Apply(new Frame(0x401f,10,[3])).HasFlag(StateChanges.Eq)&&liveState.Preset==3,"Read-only fallback detects hardware EQ change");
Check(liveState.Apply(new Frame(0x404c,10,[1])).HasFlag(StateChanges.Eq)&&liveState.Advanced==true,"Advanced EQ status changes invalidate EQ display");
Check(liveState.Apply(new Frame(0x4044,10,Protocol.SimplePayload(1,-1,2))).HasFlag(StateChanges.Eq),"Custom EQ changes invalidate graph");
Check(liveState.Apply(new Frame(0x4044,10,Protocol.SimplePayload(1,-1,2)))==StateChanges.None,"Unchanged custom EQ does not reset editor");
Check(liveState.Apply(new Frame(0x404d,10,payload)).HasFlag(StateChanges.Bands),"Advanced profile change detected");
Check(liveState.Apply(new Frame(0x404d,10,payload))==StateChanges.None,"Equivalent advanced profile does not reset editor");
liveState.Readable.UnionWith(new ushort[]{0xc04f,0xc04e,0xc041,0xc007});
var reads=LiveStateSync.ReadCommands(liveState,true,true,true);
Check(reads.Contains((ushort)0xc01e)&&reads.Contains((ushort)0xc01f)&&reads.Contains((ushort)0xc04c),"Automatic sync checks ANC and EQ modes");
Check(reads.Length==9&&reads.All(c=>(c&0xf000)==0xc000),"Automatic sync sends only read commands");
Check(!reads.Contains((ushort)0xc042)&&!reads.Contains((ushort)0xc018),"Fast sync avoids static firmware and gesture requests");
Check(LiveStateSync.ReadCommands(new HeadphoneState(),true,true,true).Length==0,"Unsupported capabilities are not polled");
Check(LiveStateSync.ReadCommands(liveState,false,false,true).SequenceEqual(new ushort[]{0xc007}),"Hidden window only polls battery");
Check(LiveStateSync.SettingsInterval<=TimeSpan.FromSeconds(2),"Hardware changes have short fallback polling interval");
var collisionBytes=Protocol.Encode(0xe003,7,[1,7]).Concat(Protocol.Encode(0x401f,7,[3])).ToArray();
var collisionFrames=new FrameParser().Push(collisionBytes).ToArray();
var matching=collisionFrames.Where(f=>f.Sequence==7&&Protocol.IsResponseTo(0xc01f,f.Command)).ToArray();
Check(matching.Length==1&&matching[0].Command==0x401f,"Unsolicited ANC event cannot consume EQ request with same sequence");
Check(Protocol.IsResponseTo(0xf00f,0x700f)&&!Protocol.IsResponseTo(0xf00f,0xe003),"Write acknowledgement is distinct from physical button event");
Check(DeviceCatalog.All.Count==11&&DeviceCatalog.All.Select(p=>p.Id).Distinct().Count()==11,"All eleven Nothing audio models have distinct profiles");
Check(DeviceCatalog.Detect("CMF Buds 2")==null&&DeviceCatalog.Detect("Nothing Phone (3)")==null,"CMF and phones are excluded from Nothing audio discovery");
Check(DeviceCatalog.Detect("Nothing Headphone (1) Pro")?.Id=="B192"&&DeviceCatalog.Detect("Nothing Headphone (1)")?.Id=="B170","Headphone Pro detection cannot fall back to standard Headphone");
foreach(var model in DeviceCatalog.All)
{
    Check(DeviceCatalog.Detect(model.Name)?.Id==model.Id&&DeviceCatalog.Detect(model.Id)?.Id==model.Id,"Model identity: "+model.Name);
    Check(model.Aliases.All(a=>DeviceCatalog.Detect(a)?.Id==model.Id),"Bluetooth aliases: "+model.Name);
    var modelState=new HeadphoneState(model);
    byte[] batteryFixture=model.Headphones?[1,6,0x80]:[3,2,75,3,0x80,4,90];
    modelState.Apply(new Frame(0x4007,1,batteryFixture));
    Check(modelState.Battery==0&&modelState.Charging&&modelState.Batteries.Length==(model.Headphones?1:3),"Multi-part battery and zero percent: "+model.Name);
    if(!model.Headphones)Check(modelState.Batteries.Any(b=>b.Device==4&&b.Level==90),"Charging case battery: "+model.Name);
    foreach(var mode in model.Spatial)
    {
        modelState.Apply(new Frame(0x404f,1,model.SpatialPayload(mode.Value)));
        Check(modelState.Spatial==mode.Value,"Spatial payload/readback: "+model.Name+" "+mode.Label);
    }
    if(model.Bass)
    {
        for(int level=0;level<=model.BassLevels;level++)
        {modelState.Apply(new Frame(0x404e,1,[(byte)(level==0?0:1),(byte)(level*model.BassStep)]));Check(modelState.Bass==level,"Bass mapping: "+model.Name+" level "+level);}
        Check(model.BassPayload(0,modelState.BassWireValue).SequenceEqual(new byte[]{0,(byte)(model.BassLevels*model.BassStep)}),"Turning bass off preserves last on-device level: "+model.Name);
        Check(model.BassPayload(0)[1]==model.BassStep,"Uninitialized bass slot uses a valid model level: "+model.Name);
    }
    if(model.CustomEq)
    {
        var customBytes=Protocol.SimplePayload(1,-2,3,model);
        Check(BitConverter.ToSingle(customBytes,23)==model.CustomTrebleFrequency&&BitConverter.ToSingle(customBytes,14)==model.CustomMidQ,"Model-specific custom EQ filters: "+model.Name);
        modelState.Apply(new Frame(0x4044,1,customBytes));Check(modelState.SimpleGains!.SequenceEqual(new float[]{1,-2,3}),"Custom EQ roundtrip: "+model.Name);
    }
    foreach(var preset in model.Presets)
    {modelState.Apply(new Frame(0x401f,1,[preset.Value]));Check(modelState.Preset==preset.Value,"Preset code: "+model.Name+" "+preset.Label);}
    var controls=model.Controls("Unknown");
    Check(controls.Any(c=>c.Custom)&&controls.Where(c=>c.Custom).All(c=>c.Type is >=0 and <=255&&c.Actions.Length>0),"Mapped configurable controls: "+model.Name);
    Check(controls.Where(c=>c.Custom).Select(c=>(c.Device,c.Control,c.Type)).Distinct().Count()==controls.Count(c=>c.Custom),"Unique configurable gesture addresses: "+model.Name);
    Check(model.Headphones?controls.All(c=>c.Device==6):controls.Any(c=>c.Device==2)&&controls.Any(c=>c.Device==3),"Correct headphone/earbud gesture devices: "+model.Name);
    var modelReads=model.ReadCommands("1.0.2.0");
    Check(modelReads.All(c=>(c&0xf000)==0xc000)&&modelReads.Contains((ushort)0xc007)&&modelReads.Contains((ushort)0xc01f),"Capability-based read commands: "+model.Name);
    Check(model.Verified==(model.Id=="B186"),"Hardware verification remains honest: "+model.Name);
}
var ear1=DeviceCatalog.All.Single(p=>p.Id=="B181");
Check(ear1.AncLevels.SequenceEqual(new byte[]{3,1})&&!ear1.CustomEq&&!ear1.HasAdvancedEq("Unknown"),"Ear (1) exposes only low/high ANC and built-in EQ");
var earA=DeviceCatalog.All.Single(p=>p.Id=="B162");
Check(!earA.HasAdvancedEq("Unknown")&&!earA.ReadCommands("Unknown").Contains((ushort)0xc04c),"Ear (a) does not require Advanced EQ for preset control");
var ear2=DeviceCatalog.All.Single(p=>p.Id=="B155");
Check(!ear2.HasAdvancedEq("1.0.1.94")&&ear2.HasAdvancedEq("1.0.1.95"),"Ear (2) Advanced EQ firmware boundary");
var stick=DeviceCatalog.All.Single(p=>p.Id=="B157");
Check(stick.ActiveAncLevels("1.0.1.99").Length==0&&stick.ActiveAncLevels("1.0.2.0").SequenceEqual(new byte[]{1}),"Ear (stick) firmware noise reduction boundary");
Check(!stick.HasAdvancedEq("1.0.1.79")&&stick.HasAdvancedEq("1.0.1.80"),"Ear (stick) Advanced EQ firmware boundary");
Check(stick.CustomTrebleFrequency==3500&&stick.CustomMidQ==.66f,"Ear (stick) has its documented 3500 Hz treble and 0.66 mid Q");
Check(!ear1.Supports(0xc041,"1.0.1.74")&&ear1.Supports(0xc041,"1.0.1.75"),"Ear (1) low-lag firmware boundary");
Check(!DeviceCatalog.All.Single(p=>p.Id=="B174").ReadCommands("Unknown").Contains((ushort)0xc01e),"Ear (open) never queries ANC");
var pro=DeviceCatalog.All.Single(p=>p.Id=="B192");
Check(pro.Spatial.Length==0&&pro.Presets.Any(p=>p.Value==7)&&pro.ReadCommands("Unknown").Contains((ushort)0xc068),"Headphone Pro maps New Instrument and Flat EQ without guessing Audiodo writes");
var proState=new HeadphoneState(pro);proState.Apply(new Frame(0x4068,1,[1]));Check(proState.FlatEq==true,"Headphone Pro Flat EQ switch readback");
proState.Apply(new Frame(0xe01c,0,[0]));Check(proState.FlatEq==false,"Headphone Pro Flat EQ switch notification");
var unsupportedState=new HeadphoneState(ear1);
Check(unsupportedState.Apply(new Frame(0x404c,1,[1]))==StateChanges.None&&unsupportedState.Advanced==null,"Unsupported Advanced EQ responses cannot enable Ear (1) editing");
unsupportedState.Apply(new Frame(0x4007,1,[1,2,80]));
Check(unsupportedState.Apply(new Frame(0xe001,0,[1,2,127]))==StateChanges.None&&unsupportedState.Battery==80,"Malformed earbud battery cannot erase a valid reading");
Check(ear2.HasAdvancedEq("0.0.1.95")&&!ear2.HasAdvancedEq("2.0.1.94"),"Firmware suffix boundaries match upstream wildcard prefixes");
var earACaptured=new HeadphoneState(earA);
earACaptured.Apply(new Frame(0x4007,1,[3,4,45,2,65,3,188]));
Check(earACaptured.Battery==60&&earACaptured.Charging&&earACaptured.Batteries.Single(b=>b.Device==4).Level==45,"Community Ear (a) battery fixture identifies right bud and case independently");
earACaptured.Apply(new Frame(0x400e,1,[1,1,0]));Check(earACaptured.InEar==false,"Community Ear (a) wearing detection response fixture");
earACaptured.Apply(new Frame(0x400e,1,[2,7,0,1,1]));Check(earACaptured.InEar==true,"Wearing detection locates feature type one in a multi-feature response");
Check(Convert.ToHexString(Protocol.Encode(0xf004,1,[1,1,0]))=="55600104F0030001010100B864","Community wearing detection write wire fixture");
var autoA=new PairedHeadphone("synthetic-a","Nothing Headphone (a)",DeviceCatalog.HeadphoneA);
var autoB=new PairedHeadphone("synthetic-b","Nothing Ear (a)",earA);
Check(AutomaticConnection.Select([],null)==null,"No automatic connection without a connected device");
Check(AutomaticConnection.Select([autoA],null)==autoA,"A single connected device is selected automatically");
Check(AutomaticConnection.Select([autoA,autoB],autoB.Id)==autoB,"Multiple connected devices prefer the last selected one");
Check(AutomaticConnection.Select([autoA,autoB],"absent")==null,"Ambiguous connected devices require manual selection");
Check(AutomaticConnection.Select([autoA],autoB.Id)==autoA,"A stale remembered device does not block the sole connected device");
long autoClock=0;int discoveries=0,connections=0;bool available=true;
IReadOnlyList<PairedHeadphone> candidates=[autoA];
var automatic=new AutomaticConnection(()=>{discoveries++;return Task.FromResult(candidates);},_=>{connections++;return Task.CompletedTask;},()=>available,()=>null,()=>autoClock);
await automatic.TickAsync();Check(discoveries==1&&connections==1,"Automatic startup opens the control connection for an eligible device");
await automatic.TickAsync();Check(discoveries==1,"Automatic retries respect the cooldown");
autoClock=30000;await automatic.TickAsync();Check(connections==2,"Automatic connection retries after its cooldown");
available=false;autoClock=60000;await automatic.TickAsync();Check(discoveries==2,"Connected, busy, or closed app does not rediscover devices");
available=true;automatic.Suspend();await automatic.TickAsync();Check(connections==2,"Manual disconnect suspends automatic connection");
automatic.Resume();await automatic.TickAsync();Check(connections==3,"Explicit reconnect resumes automatic connection");
candidates=[];autoClock=90000;await automatic.TickAsync();Check(connections==3,"An offline device is never opened by automatic connection");
autoClock=94999;await automatic.TickAsync();Check(discoveries==4,"Empty discovery uses a five-second interval");
candidates=[autoA];autoClock=95000;await automatic.TickAsync();Check(connections==4,"A later Bluetooth connection is picked up without restarting");
var discoveryGate=new TaskCompletionSource<IReadOnlyList<PairedHeadphone>>(TaskCreationOptions.RunContinuationsAsynchronously);
int concurrentScans=0,concurrentOpens=0;bool canOpen=true;
var guarded=new AutomaticConnection(()=>{concurrentScans++;return discoveryGate.Task;},_=>{concurrentOpens++;return Task.CompletedTask;},()=>canOpen,()=>null);
var firstScan=guarded.TickAsync();await guarded.TickAsync();Check(concurrentScans==1,"Overlapping timer ticks share one discovery attempt");
canOpen=false;discoveryGate.SetResult([autoA]);await firstScan;Check(concurrentOpens==0,"Manual activity or closing during discovery prevents a late automatic open");
long failureClock=0;int failures=0;
var failing=new AutomaticConnection(()=>Task.FromResult<IReadOnlyList<PairedHeadphone>>([autoA]),_=>{failures++;throw new IOException("Busy service");},()=>true,()=>null,()=>failureClock);
try{await failing.TickAsync();}catch(IOException){}
failureClock=29999;await failing.TickAsync();Check(failures==1,"Failed connection is not immediately retried");
failureClock=30000;try{await failing.TickAsync();}catch(IOException){}Check(failures==2,"Failed connection becomes eligible after backoff");
string rememberedPath=Path.Combine(Directory.GetCurrentDirectory(),"artifacts","remembered-device-"+Guid.NewGuid()+".json");
try
{
    var store=new PreferencesStore(rememberedPath);
    store.Save(new AppPreferences{CloseToTray=true,LastDeviceId=autoA.Id,LastDeviceModel=autoA.Profile.Id});
    var remembered=store.Load();Check(remembered.CloseToTray&&remembered.LastDeviceId==autoA.Id&&remembered.LastDeviceModel==autoA.Profile.Id,"Remembered device and model persist with existing tray preference");
    File.WriteAllText(rememberedPath,"{\"CloseToTray\":true}");Check(store.Load().LastDeviceId==null&&store.Load().CloseToTray,"Earlier settings migrate without a remembered device");
}
finally{if(File.Exists(rememberedPath))File.Delete(rememberedPath);}
await UpdateChecks.RunAsync(Check);
Console.WriteLine($"Passed {checks} total checks.");
