namespace NothingOnWindows.Core;

[Flags]
public enum StateChanges
{
    None=0, Battery=1, Device=2, Sound=4, Eq=8, Bands=16, Controls=32, Availability=64, Connection=128
}

public static class LiveStateSync
{
    public static readonly TimeSpan SettingsInterval=TimeSpan.FromSeconds(2);
    public static readonly TimeSpan BatteryInterval=TimeSpan.FromSeconds(30);

    // Only query capabilities that this connection has successfully read.
    public static ushort[] ReadCommands(HeadphoneState state,bool settings,bool equalizer,bool battery)
    {
        var commands=new List<ushort>();
        if(settings)commands.AddRange(new ushort[]{0xc068,0xc01e,0xc04f,0xc04e,0xc041,0xc00e,0xc01f,0xc04c});
        if(equalizer)commands.AddRange(new ushort[]{0xc044,0xc04d});
        if(battery)commands.Add(0xc007);
        return commands.Where(c=>state.Profile.Supports(c,state.Firmware)&&state.Readable.Contains(c)).ToArray();
    }
}
