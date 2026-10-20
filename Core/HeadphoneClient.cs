using System.Collections.Concurrent;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace NothingOnWindows.Core;

public sealed record PairedHeadphone(string Id,string Name,DeviceProfile Profile) { public override string ToString() => Name; }

public sealed class HeadphoneClient : IDisposable
{
    public static readonly Guid ServiceId = Guid.Parse("aeac4a03-dff5-498f-843a-34487cf133eb");
    private BluetoothDevice? device;
    private RfcommDeviceService? service;
    private StreamSocket? socket;
    private CancellationTokenSource? stop;
    private readonly SemaphoreSlim queue = new(1, 1);
    private sealed record PendingRequest(ushort Command,TaskCompletionSource<Frame> Response);
    private readonly ConcurrentDictionary<byte, PendingRequest> pending = new();
    private byte sequence;
    public HeadphoneState State { get; private set; } = new();
    public DeviceProfile Profile=>State.Profile;
    public bool Connected => socket != null;
    public event Action<StateChanges>? Changed;
    public event Action<string>? Log;

    public static async Task<List<PairedHeadphone>> DiscoverAsync()
    {
        var all = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
        return all.Select(d=>(Device:d,Profile:DeviceCatalog.Detect(d.Name))).Where(p=>p.Profile!=null).Select(p=>new PairedHeadphone(p.Device.Id,p.Device.Name,p.Profile!)).ToList();
    }

    public static async Task<List<(string Id,string Name)>> AllPairedAsync()=>
        (await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true))).Select(d=>(d.Id,d.Name)).ToList();

    internal void SelectProfile(DeviceProfile profile)
    {
        if(Connected)throw new InvalidOperationException("Disconnect before changing the device profile.");
        State=new HeadphoneState(profile);Changed?.Invoke(StateChanges.Connection|StateChanges.Availability);
    }

    public async Task ConnectAsync(PairedHeadphone selected)
    {
        Dispose(); State = new(selected.Profile);
        try
        {
            device = await BluetoothDevice.FromIdAsync(selected.Id).AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            if (device == null) throw new IOException("Windows could not access the headphones. Check Bluetooth permissions and pairing.");
            var services = await device.GetRfcommServicesForIdAsync(RfcommServiceId.FromUuid(ServiceId), BluetoothCacheMode.Uncached).AsTask().WaitAsync(TimeSpan.FromSeconds(20));
            if (services.Error != BluetoothError.Success || services.Services.Count == 0) throw new IOException("The Nothing control service is unavailable. Close Nothing X on your phone, check that the headphones are connected to Windows, then retry.");
            service = services.Services[0];
            socket = new StreamSocket();
            await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName, SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            stop = new();
            _ = ReadLoopAsync(socket, stop.Token);
            Log?.Invoke("Connected to Nothing's control service.");
            Changed?.Invoke(StateChanges.Connection);
        }
        catch { Dispose(); throw; }
    }

    private async Task ReadLoopAsync(StreamSocket activeSocket, CancellationToken token)
    {
        using var reader = new DataReader(activeSocket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
        var parser = new FrameParser();
        try
        {
            while (!token.IsCancellationRequested)
            {
                uint count = await reader.LoadAsync(2048).AsTask(token);
                if (count == 0) throw new IOException("The headphones disconnected.");
                var bytes = new byte[count]; reader.ReadBytes(bytes);
                foreach (var frame in parser.Push(bytes))
                {
                    Log?.Invoke($"RX {frame.Command:X4} / {frame.Sequence}: {Convert.ToHexString(frame.Payload)}");
                    ProcessFrame(frame);
                }
            }
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            Log?.Invoke(ex.Message);
            foreach (var waiter in pending.Values) waiter.Response.TrySetException(new IOException("The headphones disconnected.", ex));
            if (ReferenceEquals(socket, activeSocket)) Dispose();
        }
        catch (OperationCanceledException) { }
        finally { reader.DetachStream(); }
    }

    internal void ProcessFrame(Frame frame)
    {
        var changes=State.Apply(frame);
        // Push events can reuse a request's sequence number. They must update
        // state without completing a different command's pending response.
        if(pending.TryGetValue(frame.Sequence,out var waiter)&&Protocol.IsResponseTo(waiter.Command,frame.Command))waiter.Response.TrySetResult(frame);
        if(changes!=StateChanges.None)Changed?.Invoke(changes);
    }

    public async Task<Frame> RequestAsync(ushort command, byte[]? payload = null)
    {
        await queue.WaitAsync();
        byte id = 0;
        try
        {
            var activeSocket = socket ?? throw new IOException("Connect your headphones first.");
            id = sequence = (byte)(sequence % 250 + 1);
            var response = new TaskCompletionSource<Frame>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[id] = new PendingRequest(command,response);
            byte[] packet = Protocol.Encode(command, id, payload ?? []);
            using var writer = new DataWriter(activeSocket.OutputStream);
            try { writer.WriteBytes(packet); await writer.StoreAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { writer.DetachStream(); }
            Log?.Invoke($"TX {command:X4} / {id}: {Convert.ToHexString(payload ?? [])}");
            var frame = await response.Task.WaitAsync(TimeSpan.FromSeconds(4));
            if ((command & 0xf000) == 0xc000 && frame.Command != (command & 0x7fff)) throw new IOException($"Unexpected response {frame.Command:X4} to query {command:X4}.");
            return frame;
        }
        finally { pending.TryRemove(id, out _); queue.Release(); }
    }

    public async Task RefreshAsync()
    {
        // Firmware determines capability variants on Ear (2) and Ear (stick).
        if(Connected)
        {
            try{await RequestAsync(0xc042);}
            catch(TimeoutException){Log?.Invoke("Firmware did not respond; checking the profile's known capabilities.");}
        }
        foreach (ushort command in Profile.ReadCommands(State.Firmware).Where(c=>c!=0xc042))
        {
            if (!Connected) break;
            try { await RequestAsync(command, command == 0xc04d ? [255] : []); }
            catch (TimeoutException) { Log?.Invoke($"No response for {command:X4}; feature remains unavailable."); }
        }
    }

    public async Task SetVerifiedAsync(ushort write, byte[] payload, ushort read, Func<HeadphoneState, bool> matches)
    {
        ValidateWrite(write,payload,read);
        if (!State.Readable.Contains(read)) throw new InvalidOperationException("This feature has not been read from your headphones yet. Refresh the connection first.");
        try { await RequestAsync(write, payload); }
        catch (TimeoutException) { Log?.Invoke("No write acknowledgement; checking the device state."); }
        State.Readable.Remove(read);
        await RequestAsync(read, read == 0xc04d ? [255] : []);
        if (!State.Readable.Contains(read) || !matches(State)) throw new IOException("The headphones did not confirm this change. Their current setting is shown.");
    }

    internal void ValidateWrite(ushort write,byte[] payload,ushort read)
    {
        if(!Profile.Supports(read,State.Firmware))throw new InvalidOperationException("This setting is unavailable on this device or firmware.");
        if(State.FlatEq==true&&write is 0xf00f or 0xf010 or 0xf041 or 0xf04f or 0xf050 or 0xf051 or 0xf052)throw new InvalidOperationException("Turn off Flat EQ using the switch on the headphones before changing sound settings.");
        bool valid=write switch
        {
            0xf00f=>read==0xc01e&&payload.Length==3&&payload[0]==1&&payload[2]==0&&(Profile.ActiveAncLevels(State.Firmware).Contains(payload[1])||payload[1]==5||payload[1]==7&&Profile.Id!="B157"),
            0xf052=>read==0xc04f&&payload.Length==2&&Profile.Spatial.Any(m=>m.Mode==payload[0]&&m.Head==payload[1]),
            0xf051=>read==0xc04e&&payload.Length==2&&payload[0]<=1&&payload[1]%Profile.BassStep==0&&payload[1]<=Profile.BassLevels*Profile.BassStep,
            0xf010=>read==0xc01f&&payload.Length==2&&payload[1]==0&&Profile.Presets.Any(p=>p.Value==payload[0]),
            0xf04f=>read==0xc04c&&payload.Length==2&&payload[0]<=1&&payload[1]==0,
            0xf050=>read==0xc04d&&payload.Length==110,
            0xf041=>read==0xc044&&payload.Length>=44,
            0xf040=>read==0xc041&&payload.Length==2&&payload[0] is 1 or 2&&payload[1]==0,
            0xf004=>read==0xc00e&&payload.Length==3&&payload[0]==1&&payload[1]==1&&payload[2]<=1,
            0xf003=>read==0xc018&&payload.Length==5&&payload[0]==1&&Profile.Controls(State.Firmware).Any(c=>c.Custom&&c.Device==payload[1]&&c.Control==payload[2]&&c.Type==payload[3]&&c.Actions.Any(a=>a.Code==payload[4])),
            _=>false
        };
        if(!valid)throw new InvalidOperationException("The selected action is not mapped for this device profile.");
        if(Profile.SpatialEqExclusive)
        {
            if(write==0xf052&&payload[0]!=0&&(State.Advanced==true||State.Bass>0))throw new InvalidOperationException("Turn off Advanced EQ and Bass enhancement before enabling spatial audio.");
            if((write==0xf04f&&payload[0]==1||write==0xf051&&payload[0]==1)&&State.Spatial>0)throw new InvalidOperationException("Turn off spatial audio before enabling this setting.");
        }
    }

    public void Dispose()
    {
        bool wasConnected=Connected;
        stop?.Cancel(); stop?.Dispose(); stop = null;
        socket?.Dispose(); socket = null;
        service?.Dispose(); service = null;
        device?.Dispose(); device = null;
        if(wasConnected)
        {
            foreach(var waiter in pending.Values)waiter.Response.TrySetException(new IOException("The headphones disconnected."));
            Changed?.Invoke(StateChanges.Connection);
        }
    }
}
