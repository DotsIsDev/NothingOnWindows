namespace NothingOnWindows.Core;

public sealed record PairedHeadphone(string Id,string Name,DeviceProfile Profile)
{
    public override string ToString()=>Name;
}

// Discovery must return only devices Windows already considers connected.
// The delegates keep timing/selection testable without opening a Bluetooth socket.
public sealed class AutomaticConnection(
    Func<Task<IReadOnlyList<PairedHeadphone>>> discover,
    Func<PairedHeadphone,Task> connect,
    Func<bool> available,
    Func<string?> preferredId,
    Func<long>? clock=null)
{
    private readonly Func<long> now=clock??(()=>Environment.TickCount64);
    private long nextCheck=long.MinValue;
    private bool checking;
    public bool Suspended {get;private set;}
    public void Suspend()=>Suspended=true;
    public void Resume(){Suspended=false;nextCheck=long.MinValue;}

    public static PairedHeadphone? Select(IReadOnlyList<PairedHeadphone> connected,string? preferredId)=>
        connected.FirstOrDefault(d=>d.Id==preferredId)??(connected.Count==1?connected[0]:null);

    public async Task TickAsync()
    {
        if(Suspended||checking||!available()||now()<nextCheck)return;
        checking=true;nextCheck=now()+5000;
        try
        {
            var devices=await discover();
            if(Suspended||!available())return;
            var selected=Select(devices,preferredId());
            if(selected==null)return;
            nextCheck=now()+30000; // Avoid repeatedly opening a busy/unavailable control service.
            await connect(selected);
        }
        finally{checking=false;}
    }
}
