using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using NothingOnWindows.Core;

namespace NothingOnWindows;

public partial class MainWindow
{
    private readonly IAppUpdates appUpdates;
    private static Version AppVersion=>typeof(App).Assembly.GetName().Version??new Version(0,0,0);
    private static string AppVersionLabel=>$"{AppVersion.Major}.{AppVersion.Minor}.{AppVersion.Build}";
    private AppRelease? availableUpdate;
    private bool updateBusy;
    private string updateStatus="Check GitHub for a newer release.";
    private TextBlock? updateStatusView;
    private Button? updateAction;
    private Button? updateNotes;

    private void RenderUpdates()
    {
        var card=Card("Updates",$"Version {AppVersionLabel} · Windows x64");
        updateStatusView=Text(updateStatus,12,muted);updateStatusView.Name="UpdateStatus";card.Children.Add(updateStatusView);
        updateAction=ActionButton("Check for updates",async()=>{if(availableUpdate==null)await CheckForUpdatesAsync();else await DownloadAndRestartAsync();});
        updateAction.Name="CheckForUpdatesButton";card.Children.Add(updateAction);
        updateNotes=ActionButton("Release notes ↗",()=>{if(availableUpdate!=null)Process.Start(new ProcessStartInfo(availableUpdate.PageUrl){UseShellExecute=true});});
        card.Children.Add(updateNotes);RefreshUpdateControls();
    }
    private void RefreshUpdateControls()
    {
        if(updateStatusView!=null)updateStatusView.Text=updateStatus;
        if(updateAction!=null)
        {
            updateAction.Content=updateBusy?"Please wait…":availableUpdate==null?"Check for updates":"Download and restart";
            updateAction.IsEnabled=!updateBusy&&!busy;
        }
        if(updateNotes!=null)updateNotes.Visibility=availableUpdate==null?Visibility.Collapsed:Visibility.Visible;
    }
    private async Task CheckForUpdatesAsync()
    {
        if(updateBusy||windowClosed)return;
        updateBusy=true;updateStatus="Checking GitHub…";RefreshUpdateControls();
        try
        {
            availableUpdate=await appUpdates.CheckAsync(AppVersion,windowLifetime.Token);
            updateStatus=availableUpdate==null?$"You're up to date ({AppVersionLabel}).":$"Version {availableUpdate.Version} is available. Downloading will restart the app.";
        }
        catch(OperationCanceledException)when(windowClosed){}
        catch(Exception ex){availableUpdate=null;updateStatus="Could not check for updates. "+(ex is OperationCanceledException?"The request timed out. Try again.":ex.Message);}
        finally{updateBusy=false;if(!windowClosed)RefreshUpdateControls();}
    }
    private async Task DownloadAndRestartAsync()
    {
        if(updateBusy||busy||windowClosed||availableUpdate==null)return;
        if(draftDirty&&System.Windows.MessageBox.Show(this,"Restart to install the update? Your current EQ draft will be discarded. Saved profiles are kept.","Unsaved EQ draft",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        updateBusy=true;updateStatus="Downloading update…";RefreshUpdateControls();
        try
        {
            var progress=new Progress<int>(percent=>{if(!windowClosed){updateStatus=$"Downloading update… {percent}%";RefreshUpdateControls();}});
            var prepared=await appUpdates.DownloadAsync(availableUpdate,progress,windowLifetime.Token);
            if(windowClosed)return;
            updateStatus="Verified. Restarting to install…";RefreshUpdateControls();
            appUpdates.BeginInstall(prepared,AppContext.BaseDirectory);
            ExitApplication(); // Explicit exit bypasses close-to-tray and releases Bluetooth/files.
        }
        catch(OperationCanceledException)when(windowClosed){}
        catch(Exception ex){updateStatus="The update was not installed. "+ex.Message;}
        finally{updateBusy=false;if(!windowClosed)RefreshUpdateControls();}
    }
    internal void ShowUpdateResult(string resultPath)
    {
        try
        {
            string path=Path.GetFullPath(resultPath);
            string cache=Path.GetFullPath(GitHubAppUpdates.DefaultCache).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!path.StartsWith(cache,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(path)!="result.json"||new FileInfo(path).Length>16384)return;
            var result=JsonSerializer.Deserialize<UpdateResult>(File.ReadAllText(path));
            if(result==null)return;
            updateStatus=result.Message;page="Connection";Render();
        }
        catch(Exception ex){updateStatus="Could not read the update result: "+ex.Message;}
    }
}
