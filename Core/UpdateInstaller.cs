using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NothingOnWindows.Core;

public sealed record UpdatePlan(string TargetDirectory,string Version,int ParentProcessId,long ParentStartTicks);
public sealed record UpdateResult(string State,string Version,string Message);

public static class UpdateInstaller
{
    public static void ValidateTarget(string directory)
    {
        string root=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        if(root.Length<4||root==Path.GetPathRoot(root)?.TrimEnd(Path.DirectorySeparatorChar)||!File.Exists(Path.Combine(root,"NothingOnWindows.exe")))throw new IOException("Use the extracted Nothing on Windows app folder to install updates.");
        UpdatePackage.NoLinks(root);
        string probe=Path.Combine(root,".update-write-test-"+Guid.NewGuid().ToString("N"));
        try{using(File.Create(probe)){}File.Delete(probe);}
        catch(Exception ex){throw new IOException("The app folder is not writable. Move the extracted app to a folder you own before updating.",ex);}
    }
    public static async Task<UpdateResult> ApplyAsync(string planPath,bool restart=true,string? allowedCacheRoot=null)
    {
        string cache=Path.GetFullPath(allowedCacheRoot??GitHubAppUpdates.DefaultCache).TrimEnd(Path.DirectorySeparatorChar);
        string fullPlan=Path.GetFullPath(planPath);string session=Path.GetDirectoryName(fullPlan)!;
        if(Path.GetFileName(fullPlan)!="plan.json"||Path.GetDirectoryName(session)!=cache||!Regex.IsMatch(Path.GetFileName(session),"^[a-f0-9]{32}$")||new FileInfo(fullPlan).Length>16384)throw new InvalidDataException("Invalid update session.");
        UpdatePackage.NoLinks(session);
        var plan=JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(fullPlan))??throw new InvalidDataException("Invalid update plan.");
        GitHubAppUpdates.ReleaseVersion(plan.Version);
        string target=Path.GetFullPath(plan.TargetDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if(session.StartsWith(target+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||target.StartsWith(session+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("The update cache must be separate from the app folder.");
        ValidateTarget(target);
        string staged=Path.Combine(session,"package","NothingOnWindows");
        var manifest=UpdatePackage.Validate(staged,plan.Version);
        if(plan.ParentProcessId>0)
        {
            Process? parent=null;
            try{parent=Process.GetProcessById(plan.ParentProcessId);}catch(ArgumentException){}
            using(parent)
                if(parent!=null&&parent.StartTime.ToUniversalTime().Ticks==plan.ParentStartTicks)
                    await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
        }
        ValidateTarget(target);UpdatePackage.Validate(staged,plan.Version);
        var newFiles=new HashSet<string>(manifest.Files.Keys,StringComparer.OrdinalIgnoreCase){UpdatePackage.ManifestName};
        var allFiles=new HashSet<string>(newFiles,StringComparer.OrdinalIgnoreCase);
        string previousManifest=Path.Combine(target,UpdatePackage.ManifestName);
        if(File.Exists(previousManifest))
        {
            if(new FileInfo(previousManifest).Length>1024*1024)throw new InvalidDataException("The installed update manifest is invalid.");
            var previous=JsonSerializer.Deserialize<UpdateManifest>(File.ReadAllText(previousManifest))??throw new InvalidDataException("The installed update manifest is invalid.");
            allFiles.UnionWith(UpdatePackage.ReadManifest(target,previous.Version).Files.Keys);
        }
        string backup=Path.Combine(session,"backup");Directory.CreateDirectory(backup);
        var changed=new List<string>();UpdateResult result;
        try
        {
            foreach(string relative in allFiles)
            {
                string old=UpdatePackage.SafeFile(target,relative);UpdatePackage.NoLinks(old);
                if(!File.Exists(old))continue;
                string saved=UpdatePackage.SafeFile(backup,relative);Directory.CreateDirectory(Path.GetDirectoryName(saved)!);File.Copy(old,saved,false);
            }
            foreach(string relative in allFiles.OrderBy(p=>p==UpdatePackage.ManifestName?1:0).ThenBy(p=>p,StringComparer.OrdinalIgnoreCase))
            {
                string destination=UpdatePackage.SafeFile(target,relative);UpdatePackage.NoLinks(destination);changed.Add(relative);
                if(newFiles.Contains(relative))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(UpdatePackage.SafeFile(staged,relative),destination,true);
                }
                else if(File.Exists(destination))File.Delete(destination);
            }
            UpdatePackage.ValidateManagedFiles(target,plan.Version);
            result=new("success",plan.Version,$"Updated to {plan.Version}. Your settings and saved profiles were kept.");
        }
        catch(Exception failure)
        {
            var rollbackErrors=new List<string>();
            foreach(string relative in changed.AsEnumerable().Reverse())
            {
                try
                {
                    string destination=UpdatePackage.SafeFile(target,relative);UpdatePackage.NoLinks(destination);
                    string saved=UpdatePackage.SafeFile(backup,relative);
                    if(File.Exists(saved))File.Copy(saved,destination,true);
                    else if(File.Exists(destination))File.Delete(destination);
                }
                catch(Exception ex){rollbackErrors.Add(ex.Message);}
            }
            result=rollbackErrors.Count==0
                ?new("failed",plan.Version,"The update could not be installed. Your previous app was restored. "+failure.Message)
                :new("rollback-failed",plan.Version,"The update could not be completed. A backup is saved at "+backup+". "+failure.Message);
        }
        string resultPath=Path.Combine(session,"result.json");File.WriteAllText(resultPath,JsonSerializer.Serialize(result));
        if(restart)
        {
            var start=new ProcessStartInfo(Path.Combine(target,"NothingOnWindows.exe")){UseShellExecute=false,WorkingDirectory=target};
            start.ArgumentList.Add("--update-result");start.ArgumentList.Add(resultPath);
            _=Process.Start(start)??throw new IOException("The update finished, but Windows could not reopen the app. Open NothingOnWindows.exe from its folder.");
        }
        return result;
    }
}
