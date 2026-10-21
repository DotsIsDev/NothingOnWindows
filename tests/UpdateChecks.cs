using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NothingOnWindows.Core;

internal static class UpdateChecks
{
    private static string Hash(byte[] data)=>Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    private static void WritePackage(string directory,string version,Dictionary<string,string> files)
    {
        Directory.CreateDirectory(directory);var hashes=new Dictionary<string,string>();
        foreach(var file in files)
        {
            string path=UpdatePackage.SafeFile(directory,file.Key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,file.Value);hashes[file.Key]=Hash(File.ReadAllBytes(path));
        }
        File.WriteAllText(Path.Combine(directory,UpdatePackage.ManifestName),JsonSerializer.Serialize(new UpdateManifest(version,"win-x64",hashes)));
    }
    public static async Task RunAsync(Action<bool,string> check)
    {
        string root=Path.Combine(Directory.GetCurrentDirectory(),"artifacts","updater-checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        string releaseJson(string tag="v0.2.0",string? url=null,string? digest=null,string name="NothingOnWindows-v0.2.0-win-x64.zip",bool prerelease=false)=>JsonSerializer.Serialize(new{draft=false,prerelease,tag_name=tag,assets=new[]{new{name,state="uploaded",browser_download_url=url??$"https://github.com/DotsIsDev/NothingOnWindows/releases/download/{tag}/{name}",digest=digest??"sha256:"+new string('a',64),size=100}}});
        void Reject(Action action,string label){bool rejected=false;try{action();}catch(Exception ex)when(ex is InvalidDataException or IOException or JsonException){rejected=true;}check(rejected,label);}
        check(GitHubAppUpdates.ParseRelease(releaseJson(),new Version(0,1,2))?.Version==new Version(0,2,0),"Updater recognizes a newer published Windows release");
        check(GitHubAppUpdates.ParseRelease(releaseJson(),new Version(0,2,0,0))==null,"Updater treats equal assembly/release versions as current");
        check(GitHubAppUpdates.ParseRelease(releaseJson(),new Version(0,3,0))==null,"Updater never offers a downgrade");
        check(GitHubAppUpdates.ParseRelease(releaseJson(prerelease:true),new Version(0,1,0))==null,"Updater excludes prereleases");
        Reject(()=>GitHubAppUpdates.ReleaseVersion("v0.2.0-beta"),"Updater rejects prerelease/malformed tags");
        Reject(()=>GitHubAppUpdates.ParseRelease(releaseJson(url:"https://example.com/app.zip"),new Version(0,1,0)),"Updater rejects downloads outside this repository");
        Reject(()=>GitHubAppUpdates.ParseRelease(releaseJson(digest:"sha256:bad"),new Version(0,1,0)),"Updater requires GitHub SHA-256 metadata");
        Reject(()=>GitHubAppUpdates.ParseRelease(releaseJson(name:"source.zip"),new Version(0,1,0)),"Updater rejects missing/wrong-platform packages");
        foreach(string path in new[]{"../escape","a/../../escape","C:/escape","/rooted","a\\b","a:stream","NUL.txt","CON/file","a./b","a /b","a//b","a/./b"})
            Reject(()=>UpdatePackage.SafeFile(root,path),"Updater rejects unsafe archive path: "+path);
        check(UpdatePackage.SafeFile(root,"docs/images/settings.png").StartsWith(root+Path.DirectorySeparatorChar),"Updater accepts nested package files");
        string stage=Path.Combine(root,"valid-package");WritePackage(stage,"0.2.0",new(){{"NothingOnWindows.exe","new exe"},{"data.txt","new data"}});
        check(UpdatePackage.Validate(stage,"0.2.0").Files.Count==2,"Updater verifies every package file against the manifest");
        Reject(()=>UpdatePackage.Validate(stage,"0.3.0"),"Updater rejects manifest version mismatches");
        File.WriteAllText(Path.Combine(stage,"data.txt"),"tampered");Reject(()=>UpdatePackage.Validate(stage,"0.2.0"),"Updater rejects altered extracted files");
        WritePackage(stage,"0.2.0",new(){{"NothingOnWindows.exe","new exe"},{"data.txt","new data"}});
        File.WriteAllText(Path.Combine(stage,"unexpected.txt"),"extra");Reject(()=>UpdatePackage.Validate(stage,"0.2.0"),"Updater rejects unlisted package files");File.Delete(Path.Combine(stage,"unexpected.txt"));
        string archive=Path.Combine(root,"package.zip");ZipFile.CreateFromDirectory(stage,archive,CompressionLevel.Fastest,false);
        // Release archives contain the fixed NothingOnWindows top-level folder.
        string packed=Path.Combine(root,"packed");Directory.CreateDirectory(packed);Directory.Move(stage,Path.Combine(packed,"NothingOnWindows"));
        string validZip=Path.Combine(root,"valid.zip");ZipFile.CreateFromDirectory(packed,validZip,CompressionLevel.Fastest,false);
        UpdatePackage.Extract(validZip,Path.Combine(root,"extracted"),"0.2.0");check(File.Exists(Path.Combine(root,"extracted","NothingOnWindows","NothingOnWindows.exe")),"Updater extracts a verified release layout");
        string windowsZip=Path.Combine(root,"windows.zip");
        using(var zip=ZipFile.Open(windowsZip,ZipArchiveMode.Create))
        foreach(string path in Directory.EnumerateFiles(packed,"*",SearchOption.AllDirectories))
        {var entry=zip.CreateEntry(Path.GetRelativePath(packed,path).Replace('/','\\'));using var input=File.OpenRead(path);using var output=entry.Open();input.CopyTo(output);}
        UpdatePackage.Extract(windowsZip,Path.Combine(root,"windows-extracted"),"0.2.0");check(File.Exists(Path.Combine(root,"windows-extracted","NothingOnWindows","NothingOnWindows.exe")),"Updater supports real Windows PowerShell backslash ZIP paths");
        string evil=Path.Combine(root,"traversal.zip");using(var zip=ZipFile.Open(evil,ZipArchiveMode.Create)){using var writer=new StreamWriter(zip.CreateEntry("NothingOnWindows/../../escape.txt").Open());writer.Write("bad");}
        Reject(()=>UpdatePackage.Extract(evil,Path.Combine(root,"bad-extract"),"0.2.0"),"Updater blocks ZIP traversal before writing outside staging");
        check(!File.Exists(Path.Combine(root,"escape.txt")),"Rejected archive creates no escaped file");
        string duplicateZip=Path.Combine(root,"duplicate.zip");File.Copy(validZip,duplicateZip);
        using(var zip=ZipFile.Open(duplicateZip,ZipArchiveMode.Update)){using var writer=new StreamWriter(zip.CreateEntry("NothingOnWindows/DATA.txt").Open());writer.Write("duplicate");}
        bool duplicateRejected=false;try{UpdatePackage.Extract(duplicateZip,Path.Combine(root,"duplicate-extract"),"0.2.0");}catch(InvalidDataException ex){duplicateRejected=ex.Message.Contains("duplicate");}check(duplicateRejected,"Updater rejects archive paths that collide on Windows case rules");
        string linkedZip=Path.Combine(root,"linked.zip");File.Copy(validZip,linkedZip);
        using(var zip=ZipFile.Open(linkedZip,ZipArchiveMode.Update)){var entry=zip.CreateEntry("NothingOnWindows/link");entry.ExternalAttributes=unchecked((int)0xa1ff0000);using var writer=new StreamWriter(entry.Open());writer.Write("../elsewhere");}
        bool linkRejected=false;try{UpdatePackage.Extract(linkedZip,Path.Combine(root,"linked-extract"),"0.2.0");}catch(InvalidDataException ex){linkRejected=ex.Message.Contains("linked");}check(linkRejected,"Updater rejects ZIP symbolic links");
        string validManifest=Path.Combine(root,"extracted","NothingOnWindows",UpdatePackage.ManifestName);string savedManifest=File.ReadAllText(validManifest);
        File.WriteAllText(validManifest,savedManifest.Replace("win-x64","win-arm64"));Reject(()=>UpdatePackage.Validate(Path.GetDirectoryName(validManifest)!,"0.2.0"),"Updater rejects mismatched manifest platforms");File.WriteAllText(validManifest,savedManifest);
        byte[] payload=File.ReadAllBytes(validZip);
        var release=new AppRelease("v0.2.0",new Version(0,2,0),"NothingOnWindows-v0.2.0-win-x64.zip",new Uri("https://github.com/DotsIsDev/NothingOnWindows/releases/download/v0.2.0/NothingOnWindows-v0.2.0-win-x64.zip"),Hash(payload),payload.Length);
        using var http=new HttpClient(new FakeHandler(_=>new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(payload)}));
        var downloader=new GitHubAppUpdates(http,Path.Combine(root,"downloads"));
        var downloaded=await downloader.DownloadAsync(release,new Progress<int>(),CancellationToken.None);
        check(File.Exists(Path.Combine(downloaded.SessionDirectory,"package","NothingOnWindows",UpdatePackage.ManifestName)),"Updater downloads, hashes, and stages a package with no installation");
        bool badHash=false;try{await downloader.DownloadAsync(release with{Sha256=new string('0',64)},new Progress<int>(),CancellationToken.None);}catch(InvalidDataException){badHash=true;}check(badHash,"Updater refuses a download with a mismatched archive hash");
        bool incomplete=false;try{await downloader.DownloadAsync(release with{Size=payload.Length+1},new Progress<int>(),CancellationToken.None);}catch(InvalidDataException){incomplete=true;}check(incomplete,"Updater refuses truncated downloads");
        using var rateHttp=new HttpClient(new FakeHandler(_=>new HttpResponseMessage(HttpStatusCode.Forbidden)));
        bool limited=false;try{await new GitHubAppUpdates(rateHttp).CheckAsync(new Version(0,1,0),CancellationToken.None);}catch(IOException ex){limited=ex.Message.Contains("limiting");}check(limited,"Updater explains GitHub rate limiting");

        async Task<(UpdateResult Result,string Target,string Session)> InstallFixture(bool conflict)
        {
            string cache=Path.Combine(root,conflict?"rollback":"install");Directory.CreateDirectory(cache);
            string session=Path.Combine(cache,Guid.NewGuid().ToString("N"));string package=Path.Combine(session,"package","NothingOnWindows");
            string target=Path.Combine(root,conflict?"rollback-target":"target");
            WritePackage(target,"0.1.0",new(){{"NothingOnWindows.exe","original exe"},{"a.txt","original"},{"obsolete.txt","old"}});
            File.WriteAllText(Path.Combine(target,"my-notes.txt"),"keep user file");
            var files=new Dictionary<string,string>{{"NothingOnWindows.exe","updated exe"},{"0-added.txt","added"},{"a.txt","updated"}};
            if(conflict){File.WriteAllText(Path.Combine(target,"blocked"),"keep blocking user file");files.Add("blocked/child.txt","cannot replace this parent file");}
            WritePackage(package,"0.2.0",files);
            string planPath=Path.Combine(session,"plan.json");File.WriteAllText(planPath,JsonSerializer.Serialize(new UpdatePlan(target,"0.2.0",0,0)));
            var result=await UpdateInstaller.ApplyAsync(planPath,false,cache);
            return(result,target,session);
        }
        var installed=await InstallFixture(false);
        check(installed.Result.State=="success"&&File.ReadAllText(Path.Combine(installed.Target,"a.txt"))=="updated","Installer replaces managed files successfully");
        check(!File.Exists(Path.Combine(installed.Target,"obsolete.txt")),"Installer removes obsolete previously managed files");
        check(File.ReadAllText(Path.Combine(installed.Target,"my-notes.txt"))=="keep user file","Installer preserves unknown user files");
        check(File.ReadAllText(Path.Combine(installed.Session,"backup","a.txt"))=="original","Installer keeps a backup of replaced files");
        var rolledBack=await InstallFixture(true);
        check(rolledBack.Result.State=="failed"&&File.ReadAllText(Path.Combine(rolledBack.Target,"a.txt"))=="original","Installer rolls back a partially applied update after an actual file conflict");
        check(!File.Exists(Path.Combine(rolledBack.Target,"0-added.txt")),"Rollback removes newly introduced files");
        check(File.ReadAllText(Path.Combine(rolledBack.Target,"NothingOnWindows.exe"))=="original exe"&&File.Exists(Path.Combine(rolledBack.Target,"obsolete.txt")),"Rollback preserves the previous application and its obsolete files");
        check(File.Exists(Path.Combine(rolledBack.Session,"result.json")),"Installer records a readable failure result");
        Reject(()=>UpdateInstaller.ValidateTarget(Path.GetPathRoot(root)!),"Installer refuses a drive-root destination");
        await CrossProcessCheck(root,check);
    }
    private static async Task CrossProcessCheck(string root,Action<bool,string> check)
    {
        string cache=Path.Combine(root,"cross-process");Directory.CreateDirectory(cache);
        string session=Path.Combine(cache,Guid.NewGuid().ToString("N"));string staged=Path.Combine(session,"package","NothingOnWindows");Directory.CreateDirectory(staged);
        string target=Path.Combine(root,"restart-target");Directory.CreateDirectory(target);
        var files=new Dictionary<string,string>();
        foreach(string file in new[]{"ProtocolChecks.exe","ProtocolChecks.dll","ProtocolChecks.runtimeconfig.json","ProtocolChecks.deps.json"})
        {
            string name=file=="ProtocolChecks.exe"?"NothingOnWindows.exe":file;
            File.Copy(Path.Combine(AppContext.BaseDirectory,file),Path.Combine(staged,name));files[name]=Hash(File.ReadAllBytes(Path.Combine(staged,name)));
        }
        File.Copy(Path.Combine(staged,"NothingOnWindows.exe"),Path.Combine(target,"NothingOnWindows.exe"));
        File.WriteAllText(Path.Combine(staged,UpdatePackage.ManifestName),JsonSerializer.Serialize(new UpdateManifest("0.2.0","win-x64",files)));
        using var parent=Process.Start(new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true,ArgumentList={"-NoProfile","-Command","Start-Sleep -Seconds 2"}})!;
        string planPath=Path.Combine(session,"plan.json");File.WriteAllText(planPath,JsonSerializer.Serialize(new UpdatePlan(target,"0.2.0",parent.Id,parent.StartTime.ToUniversalTime().Ticks)));
        var helperStart=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true};
        helperStart.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"ProtocolChecks.dll"));helperStart.ArgumentList.Add("--apply-update-test");helperStart.ArgumentList.Add(planPath);helperStart.ArgumentList.Add(cache);
        using var helper=Process.Start(helperStart)!;await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        string marker=Path.Combine(session,"result.json.restarted");
        for(int i=0;i<50&&!File.Exists(marker);i++)await Task.Delay(100);
        check(parent.HasExited&&helper.ExitCode==0&&File.Exists(marker),"Separate updater waits for the old process and restarts the installed executable");
    }
    private sealed class FakeHandler(Func<HttpRequestMessage,HttpResponseMessage> handler):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {cancellationToken.ThrowIfCancellationRequested();var response=handler(request);response.RequestMessage=request;return Task.FromResult(response);}
    }
}
