using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NothingOnWindows.Core;

public sealed record AppRelease(string Tag,Version Version,string AssetName,Uri DownloadUrl,string Sha256,long Size)
{
    public string PageUrl=>$"https://github.com/DotsIsDev/NothingOnWindows/releases/tag/{Tag}";
}
public sealed record PreparedUpdate(string SessionDirectory,string Version);
public interface IAppUpdates
{
    Task<AppRelease?> CheckAsync(Version current,CancellationToken cancellationToken);
    Task<PreparedUpdate> DownloadAsync(AppRelease release,IProgress<int> progress,CancellationToken cancellationToken);
    void BeginInstall(PreparedUpdate update,string targetDirectory);
}

public sealed class GitHubAppUpdates(HttpClient? client=null,string? cacheDirectory=null):IAppUpdates
{
    public const string Repository="DotsIsDev/NothingOnWindows";
    public static string DefaultCache=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NothingOnWindows","Updates");
    private readonly HttpClient http=client??new HttpClient{Timeout=TimeSpan.FromMinutes(10),MaxResponseContentBufferSize=1024*1024};
    private readonly string cache=cacheDirectory??DefaultCache;
    public static Version ReleaseVersion(string tag)
    {
        var match=Regex.Match(tag,@"^v?(\d+\.\d+\.\d+)$",RegexOptions.CultureInvariant);
        if(!match.Success||!Version.TryParse(match.Groups[1].Value,out var version))throw new InvalidDataException("The release has an unsupported version tag.");
        return version;
    }
    public static AppRelease? ParseRelease(string json,Version current)
    {
        using var document=JsonDocument.Parse(json);var root=document.RootElement;
        if(root.GetProperty("draft").GetBoolean()||root.GetProperty("prerelease").GetBoolean())return null;
        string tag=root.GetProperty("tag_name").GetString()!;var version=ReleaseVersion(tag);
        if(version<=new Version(current.Major,current.Minor,Math.Max(0,current.Build)))return null;
        string name=$"NothingOnWindows-v{version}-win-x64.zip";
        var assets=root.GetProperty("assets").EnumerateArray().Where(a=>a.GetProperty("name").GetString()==name).ToArray();
        if(assets.Length!=1)throw new InvalidDataException("This release does not contain a Windows x64 update package.");
        var asset=assets[0];
        string expected=$"https://github.com/{Repository}/releases/download/{tag}/{name}";
        if(asset.GetProperty("browser_download_url").GetString()!=expected||asset.GetProperty("state").GetString()!="uploaded")throw new InvalidDataException("The release download is not a published asset from this project.");
        string digest=asset.GetProperty("digest").GetString()??"";
        if(!Regex.IsMatch(digest,@"^sha256:[a-fA-F0-9]{64}$"))throw new InvalidDataException("GitHub did not provide a SHA-256 digest for this update.");
        long size=asset.GetProperty("size").GetInt64();
        if(size<=0||size>256L*1024*1024)throw new InvalidDataException("The update package size is unsupported.");
        return new AppRelease(tag,version,name,new Uri(expected),digest[7..].ToLowerInvariant(),size);
    }
    private async Task<HttpResponseMessage> SendAsync(Uri url,CancellationToken token)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,url);
        request.Headers.UserAgent.ParseAdd("NothingOnWindows-Updater/1.0");
        if(url.Host=="api.github.com")request.Headers.Accept.ParseAdd("application/vnd.github+json");
        var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests){response.Dispose();throw new IOException("GitHub is limiting update checks. Try again later.");}
        if(!response.IsSuccessStatusCode){int code=(int)response.StatusCode;response.Dispose();throw new IOException($"GitHub could not return the update (HTTP {code}). Try again later.");}
        if(response.RequestMessage?.RequestUri?.Scheme!="https"){response.Dispose();throw new IOException("The update download requires HTTPS.");}
        return response;
    }
    public async Task<AppRelease?> CheckAsync(Version current,CancellationToken cancellationToken)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response=await SendAsync(new Uri($"https://api.github.com/repos/{Repository}/releases/latest"),timeout.Token);
        await response.Content.LoadIntoBufferAsync(1024*1024,timeout.Token);
        return ParseRelease(await response.Content.ReadAsStringAsync(timeout.Token),current);
    }
    public async Task<PreparedUpdate> DownloadAsync(AppRelease release,IProgress<int> progress,CancellationToken cancellationToken)
    {
        // Validate the download descriptor again rather than accepting arbitrary URLs.
        if(release.DownloadUrl.AbsoluteUri!=$"https://github.com/{Repository}/releases/download/{release.Tag}/{release.AssetName}"||ReleaseVersion(release.Tag)!=release.Version||release.AssetName!=$"NothingOnWindows-v{release.Version}-win-x64.zip"||!Regex.IsMatch(release.Sha256,@"^[a-fA-F0-9]{64}$")||release.Size<=0||release.Size>256L*1024*1024)throw new InvalidDataException("Invalid update descriptor.");
        string session=Path.Combine(cache,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(session);
        string archive=Path.Combine(session,"download.zip");
        using(var response=await SendAsync(release.DownloadUrl,cancellationToken))
        await using(var input=await response.Content.ReadAsStreamAsync(cancellationToken))
        await using(var output=new FileStream(archive,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))
        {
            var buffer=new byte[81920];long total=0;int count;
            while((count=await input.ReadAsync(buffer,cancellationToken))>0)
            {
                total+=count;if(total>release.Size)throw new InvalidDataException("The downloaded update exceeds its published size.");
                await output.WriteAsync(buffer.AsMemory(0,count),cancellationToken);progress.Report((int)(total*100/release.Size));
            }
            if(total!=release.Size)throw new InvalidDataException("The update download was incomplete. Try again.");
        }
        await using(var stream=File.OpenRead(archive))
        {
            string hash=Convert.ToHexString(await SHA256.HashDataAsync(stream,cancellationToken));
            if(!hash.Equals(release.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The update failed its SHA-256 integrity check. It was not installed.");
        }
        await Task.Run(()=>UpdatePackage.Extract(archive,Path.Combine(session,"package"),release.Version.ToString(),cancellationToken),cancellationToken);
        return new PreparedUpdate(session,release.Version.ToString());
    }
    public void BeginInstall(PreparedUpdate update,string targetDirectory)
    {
        UpdateInstaller.ValidateTarget(targetDirectory);
        string staged=Path.Combine(update.SessionDirectory,"package","NothingOnWindows");
        UpdatePackage.Validate(staged,update.Version);
        using var process=Process.GetCurrentProcess();
        var plan=new UpdatePlan(Path.GetFullPath(targetDirectory),update.Version,process.Id,process.StartTime.ToUniversalTime().Ticks);
        string planPath=Path.Combine(update.SessionDirectory,"plan.json");File.WriteAllText(planPath,JsonSerializer.Serialize(plan));
        var start=new ProcessStartInfo(Path.Combine(staged,"NothingOnWindows.exe")){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=update.SessionDirectory};
        start.ArgumentList.Add("--apply-update");start.ArgumentList.Add(planPath);
        _=Process.Start(start)??throw new IOException("Windows could not start the updater.");
    }
}

public sealed record UpdateManifest(string Version,string Platform,Dictionary<string,string> Files);
public static class UpdatePackage
{
    public const string ManifestName="release-manifest.json";
    public static string SafeFile(string root,string relative)
    {
        if(string.IsNullOrWhiteSpace(relative)||relative.Contains('\\')||relative.Split('/').Any(part=>part.Length==0||part is "." or ".."||part.EndsWith('.')||part.EndsWith(' ')||part.IndexOfAny("<>:\"|?*".ToCharArray())>=0||part.Any(char.IsControl)||Regex.IsMatch(part,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)",RegexOptions.IgnoreCase)))throw new InvalidDataException("The update contains an unsafe file path.");
        string full=Path.GetFullPath(Path.Combine(root,relative));
        if(!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The update path leaves its application folder.");
        return full;
    }
    public static void NoLinks(string path)
    {
        for(string? current=Path.GetFullPath(path);current!=null;current=Path.GetDirectoryName(current))
        {
            try{if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Updating through a linked folder is not supported. Use a regular extracted app folder.");}
            catch(FileNotFoundException){}
            catch(DirectoryNotFoundException){}
        }
    }
    public static UpdateManifest ReadManifest(string root,string expectedVersion)
    {
        string path=Path.Combine(root,ManifestName);
        if(!File.Exists(path)||new FileInfo(path).Length>1024*1024)throw new InvalidDataException("This package has no supported update manifest.");
        var manifest=JsonSerializer.Deserialize<UpdateManifest>(File.ReadAllText(path))??throw new InvalidDataException("Invalid update manifest.");
        if(manifest.Version!=expectedVersion||manifest.Platform!="win-x64"||manifest.Files==null||manifest.Files.Count is 0 or >2000||!manifest.Files.ContainsKey("NothingOnWindows.exe"))throw new InvalidDataException("The update manifest does not match this release or platform.");
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in manifest.Files)
            if(file.Key==ManifestName||!names.Add(file.Key)||!Regex.IsMatch(file.Value??"",@"^[a-fA-F0-9]{64}$"))throw new InvalidDataException("The update manifest contains invalid or duplicate files.");
            else SafeFile(root,file.Key);
        return manifest;
    }
    public static UpdateManifest ValidateManagedFiles(string root,string version)
    {
        NoLinks(root);var manifest=ReadManifest(root,version);
        foreach(var entry in manifest.Files)
        {
            string path=SafeFile(root,entry.Key);NoLinks(path);
            using var stream=File.OpenRead(path);
            if(!Convert.ToHexString(SHA256.HashData(stream)).Equals(entry.Value,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("An extracted update file failed its integrity check.");
        }
        return manifest;
    }
    public static UpdateManifest Validate(string root,string version)
    {
        var manifest=ValidateManagedFiles(root,version);
        foreach(string path in Directory.EnumerateFileSystemEntries(root,"*",SearchOption.AllDirectories))
        {
            NoLinks(path);
            if(File.Exists(path)&&Path.GetRelativePath(root,path).Replace('\\','/')!=ManifestName&&!manifest.Files.ContainsKey(Path.GetRelativePath(root,path).Replace('\\','/')))throw new InvalidDataException("The update contains an unlisted file.");
        }
        return manifest;
    }
    public static void Extract(string archive,string destination,string version,CancellationToken cancellationToken=default)
    {
        if(Directory.Exists(destination))throw new IOException("The update staging folder already exists.");
        NoLinks(destination);Directory.CreateDirectory(destination);
        using var zip=ZipFile.OpenRead(archive);long total=0;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(zip.Entries.Count>2200)throw new InvalidDataException("The update contains too many files.");
        foreach(var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Windows PowerShell's Compress-Archive uses backslashes in ZIP names.
            // Normalize before the same traversal/root/duplicate checks.
            string name=entry.FullName.Replace('\\','/');
            if(name=="NothingOnWindows/")continue;
            if(!name.StartsWith("NothingOnWindows/",StringComparison.Ordinal)||((entry.ExternalAttributes>>16)&0xf000)==0xa000||(entry.ExternalAttributes&(int)FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("The update contains an unexpected root or linked file.");
            string relative=name.EndsWith('/')?name[..^1]:name;string path=SafeFile(destination,relative);
            if(!names.Add(relative))throw new InvalidDataException("The update contains duplicate paths.");
            if(name.EndsWith('/')){Directory.CreateDirectory(path);continue;}
            total+=entry.Length;if(entry.Length>256L*1024*1024||total>1024L*1024*1024)throw new InvalidDataException("The extracted update is too large.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);entry.ExtractToFile(path,false);
        }
        Validate(Path.Combine(destination,"NothingOnWindows"),version);
    }
}
