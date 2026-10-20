using System.Text.Json;

namespace NothingOnWindows.Core;

public static class SimpleEqVerification
{
    private static readonly JsonSerializerOptions json = new() { WriteIndented = true };
    private static readonly ushort[] settings = [0xc01e, 0xc04f, 0xc04e, 0xc01f, 0xc04c, 0xc041, 0xc018, 0xc04d, 0xc044];

    public static async Task RunAsync(HeadphoneClient client, PairedHeadphone device, string reportPath)
    {
        async Task<byte[]> Read(ushort command) => (await client.RequestAsync(command, command == 0xc04d ? [255] : [])).Payload;
        var before = new Dictionary<string, string>();
        foreach (ushort command in settings) before[$"{command:X4}"] = Convert.ToHexString(await Read(command));
        if (!client.State.Readable.Contains(0xc044) || !client.State.Preset.HasValue || !client.State.Advanced.HasValue)
            throw new IOException("The original EQ settings could not be read. No test writes were performed.");

        byte preset = client.State.Preset.Value;
        bool advanced = client.State.Advanced.Value;
        byte[] originalSimple = Convert.FromHexString(before["C044"]);
        var stages = new List<object>();
        var restorationErrors = new List<string>();
        Dictionary<string, string>? after = null;
        string? failure = null;
        bool restored = false;
        void Save() => File.WriteAllText(reportPath + ".details.json", JsonSerializer.Serialize(new { before, after, stages, restorationErrors, restored, failure }, json));

        // Persist the complete settings snapshot before the first device mutation.
        File.WriteAllText(reportPath + ".backup.json", JsonSerializer.Serialize(new { device = device.Name, firmware = client.State.Firmware, preset, advanced, before }, json));
        Save();
        try
        {
            if (advanced)
                await client.SetVerifiedAsync(0xf04f, [0, 0], 0xc04c, s => s.Advanced == false);
            await client.SetVerifiedAsync(0xf010, [5, 0], 0xc01f, s => s.Preset == 5);
            foreach (float[] gains in new[] { new float[] { 1, -.5f, .5f }, new float[] { -1, .5f, -.5f } })
            {
                await client.SetVerifiedAsync(0xf041, Protocol.SimplePayload(gains[0], gains[1], gains[2]), 0xc044,
                    s => s.SimpleGains != null && s.SimpleGains.Zip(gains).All(pair => Math.Abs(pair.First - pair.Second) < .05));
                stages.Add(new { operation = "custom-eq-write-readback", requested = gains, actual = client.State.SimpleGains!.ToArray(), payload = Convert.ToHexString(await Read(0xc044)) });
                Save();
            }
        }
        catch (Exception ex) { failure = ex.ToString(); }
        finally
        {
            // Restore every mutated field independently, even when a previous restore step fails.
            async Task Restore(string name, ushort write, byte[] payload, ushort read, Func<byte[], bool> matches)
            {
                Exception? last = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        if (!client.Connected) await client.ConnectAsync(device);
                        try { await client.RequestAsync(write, payload); } catch (TimeoutException) { }
                        if (!matches(await Read(read))) throw new IOException("Readback does not match the original bytes.");
                        stages.Add(new { operation = "restore", field = name, verified = true }); Save(); return;
                    }
                    catch (Exception ex) { last = ex; }
                }
                restorationErrors.Add(name + ": " + last?.Message); Save();
            }
            await Restore("Simple custom EQ", 0xf041, originalSimple, 0xc044, p => p.SequenceEqual(originalSimple));
            await Restore("EQ preset", 0xf010, [preset, 0], 0xc01f, p => Convert.ToHexString(p) == before["C01F"]);
            await Restore("Advanced EQ enabled", 0xf04f, [(byte)(advanced ? 1 : 0), 0], 0xc04c, p => Convert.ToHexString(p) == before["C04C"]);
            try
            {
                after = new();
                foreach (ushort command in settings) after[$"{command:X4}"] = Convert.ToHexString(await Read(command));
                restored = before.All(entry => after.TryGetValue(entry.Key, out var actual) && entry.Value == actual);
            }
            catch (Exception ex) { restorationErrors.Add("Final snapshot: " + ex.Message); }
            Save();
        }
        if (!restored || restorationErrors.Count > 0) throw new IOException("Original settings could not be fully verified. Inspect the saved backup and detailed test report.");
        if (failure != null) throw new IOException("The simple EQ test failed, but all original setting bytes were restored and verified. " + failure);
    }
}
