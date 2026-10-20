namespace NothingOnWindows.Core;

public static class HardwareVerification
{
    // Re-send the exact current values, then prove one reversible change and restore it.
    // This path is explicit; normal startup never writes settings.
    public static async Task RunAsync(HeadphoneClient client)
    {
        var s = client.State;
        if (s.Readable.Count < 11) throw new IOException("All settings must be readable before hardware verification.");
        byte anc = s.Anc!.Value, spatial = s.Spatial!.Value, preset = s.Preset!.Value;
        bool advanced = s.Advanced!.Value, lowLag = s.LowLag!.Value;
        int bass = s.Bass!.Value;
        byte[] rawBands = (await client.RequestAsync(0xc04d, [255])).Payload;
        byte[] rawSimple = (await client.RequestAsync(0xc044)).Payload;
        var gestures = s.Gestures.ToArray();
        await client.SetVerifiedAsync(0xf00f, [1, anc, 0], 0xc01e, x => x.Anc == anc);
        await client.SetVerifiedAsync(0xf052, [spatial, 0], 0xc04f, x => x.Spatial == spatial);
        var bassRead = await client.RequestAsync(0xc04e);
        await client.SetVerifiedAsync(0xf051, bassRead.Payload, 0xc04e, x => x.Bass == bass);
        // Preserve exact on-device profile bytes and preamp, including firmware-specific padding.
        await client.SetVerifiedAsync(0xf050, rawBands, 0xc04d, x => x.Bands != null);
        if (rawSimple.Length > 0 && rawSimple[0] == 3)
            await client.SetVerifiedAsync(0xf041, rawSimple, 0xc044, x => x.SimpleGains != null);
        await client.SetVerifiedAsync(0xf010, [preset, 0], 0xc01f, x => x.Preset == preset);
        await client.SetVerifiedAsync(0xf04f, [(byte)(advanced ? 1 : 0), 0], 0xc04c, x => x.Advanced == advanced);
        foreach (var g in gestures)
            await client.SetVerifiedAsync(0xf003, [1, g.Device, g.Control, g.Type, g.Action], 0xc018, x => x.Gestures.Contains(g));
        try
        {
            await client.SetVerifiedAsync(0xf040, [(byte)(lowLag ? 2 : 1), 0], 0xc041, x => x.LowLag != lowLag);
        }
        finally
        {
            await client.SetVerifiedAsync(0xf040, [(byte)(lowLag ? 1 : 2), 0], 0xc041, x => x.LowLag == lowLag);
        }
        await client.RefreshAsync();
        if (s.Anc != anc || s.Spatial != spatial || s.Preset != preset || s.Advanced != advanced || s.LowLag != lowLag || s.Bass != bass || !s.Gestures.SequenceEqual(gestures))
            throw new IOException("The final device settings differ from the initial snapshot. Inspect the connection report.");
        if (!(await client.RequestAsync(0xc04d, [255])).Payload.SequenceEqual(rawBands))
            throw new IOException("The advanced EQ profile differs from the initial snapshot.");
    }
}
