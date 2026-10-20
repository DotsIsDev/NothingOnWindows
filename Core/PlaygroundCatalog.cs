using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace NothingOnWindows.Core;

public sealed record CatalogProfile(string Id, string Name, string Creator, string Model, EqBand[] Bands)
{
    public string Url => "https://playground.nothing.tech/detail/eq/" + Uri.EscapeDataString(Id);
}

public static class NothingProfileCodec
{
    public static (EqBand[] Bands, string Model) Decode(string encoded)
    {
        if (encoded.Length > 8192) throw new InvalidDataException("The profile is too large.");
        using var compressed = new MemoryStream(Convert.FromBase64String(encoded));
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var decoded = new MemoryStream();
        var chunk = new byte[512]; int read;
        while ((read = gzip.Read(chunk)) > 0) { if (decoded.Length + read > 4096) throw new InvalidDataException("The profile expands beyond the supported size."); decoded.Write(chunk, 0, read); }
        byte[] b = decoded.ToArray();
        if (b.Length < 98 || b[0] != 0 || b[1] != 96) throw new InvalidDataException("Only standard eight-band Nothing profiles are supported.");
        var bands = Enumerable.Range(0, 8).Select(i => new EqBand(BitConverter.ToSingle(b, 6 + i*12), BitConverter.ToSingle(b, 2 + i*12), BitConverter.ToSingle(b, 10 + i*12))).ToArray();
        if (bands.Any(band => !band.IsValid)) throw new InvalidDataException("The profile contains unsupported EQ values.");
        string model = "";
        for (int i = 98; i < b.Length;)
        {
            if (i + 2 > b.Length) throw new InvalidDataException("Truncated profile metadata.");
            byte tag = b[i++], length = b[i++];
            if (i + length > b.Length) throw new InvalidDataException("Truncated profile metadata.");
            if (tag == 2) model = Encoding.UTF8.GetString(b, i, length);
            i += length;
        }
        return (bands, model);
    }
}

public static class PlaygroundCatalog
{
    private static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 4 * 1024 * 1024 };
    public static List<CatalogProfile> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("code").GetInt32() != 200) throw new IOException("Nothing Playground could not return the catalog.");
        var profiles = new List<CatalogProfile>();
        foreach (var item in root.GetProperty("data").GetProperty("items").EnumerateArray())
        {
            try
            {
                var (bands, model) = NothingProfileCodec.Decode(item.GetProperty("data").GetString()!);
                string name = item.GetProperty("name").GetString() ?? "Untitled";
                if (name.Length > 80) name = name[..80];
                profiles.Add(new CatalogProfile(item.GetProperty("id").GetString()!, name, item.GetProperty("publisher").GetProperty("name").GetString() ?? "Community", model, bands));
            }
            catch (Exception ex) when (ex is InvalidDataException or FormatException or KeyNotFoundException) { }
        }
        return profiles;
    }
    public static async Task<List<CatalogProfile>> LoadAsync()
    {
        var all = new List<CatalogProfile>();
        for (int page = 1; page <= 4; page++)
        {
            string json = await http.GetStringAsync($"https://playground.nothing.tech/eq-profiles?limit=50&sort=newest&page={page}");
            all.AddRange(Parse(json));
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("data").GetProperty("pagination").GetProperty("hasNext").GetBoolean()) break;
        }
        return all.DistinctBy(p => p.Id).ToList();
    }
}
