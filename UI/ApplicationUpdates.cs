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
    private bool startupUpdateCheckStarted;
    private readonly HashSet<string> notifiedUpdates=[];
    private Action<string,Action>? updateNotificationOverride;

    private void RenderUpdates()
    {
        var card=Card("Updates",$"Version {AppVersionLabel} · Windows x64");
        var row=new Grid{Margin=new Thickness(0,0,0,16)};
        row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var details=new StackPanel{Margin=new Thickness(0,0,16,0)};
        var title=Text("Check for updates at startup",14);title.Margin=new Thickness(0,0,0,7);details.Children.Add(title);
        var hint=Text("Notify me when a new release is available. Downloads stay manual.",11,muted);hint.Margin=new Thickness(0);details.Children.Add(hint);row.Children.Add(details);
        var automatic=new System.Windows.Controls.Primitives.ToggleButton{Name="AutomaticUpdatesToggle",Style=(Style)FindResource("GlassSwitch"),IsChecked=preferences.CheckUpdatesAtStartup,VerticalAlignment=VerticalAlignment.Center};
        System.Windows.Automation.AutomationProperties.SetName(automatic,"Check for updates at startup");Grid.SetColumn(automatic,1);row.Children.Add(automatic);
        automatic.Click+=async(_,_)=>
        {
            try{var next=preferences with{CheckUpdatesAtStartup=automatic.IsChecked==true};preferencesStore.Save(next);preferences=next;}
            catch(Exception ex){automatic.IsChecked=preferences.CheckUpdatesAtStartup;updateStatus="Could not save update preference: "+ex.Message;RefreshUpdateControls();return;}
            if(preferences.CheckUpdatesAtStartup)await CheckForUpdatesAsync(true);
        };
        card.Children.Add(row);
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
        UpdateBanner.Visibility=availableUpdate==null?Visibility.Collapsed:Visibility.Visible;
        if(availableUpdate!=null)UpdateBannerText.Text=$"Update {availableUpdate.Version} available · View update";
    }
    internal Task CheckForUpdatesAtStartupAsync()
    {
        if(startupUpdateCheckStarted||windowClosed)return Task.CompletedTask;
        startupUpdateCheckStarted=true;
        return preferences.CheckUpdatesAtStartup?CheckForUpdatesAsync(true):Task.CompletedTask;
    }
    private void OpenUpdateSettings()
    {
        if(windowClosed)return;
        page="Connection";ShowFromTray();Render();updateAction?.BringIntoView();
    }
    private void UpdateBanner_Click(object sender,RoutedEventArgs e)=>OpenUpdateSettings();
    private void NotifyAvailableUpdate()
    {
        if(availableUpdate==null||!notifiedUpdates.Add(availableUpdate.Version.ToString()))return;
        try
        {
            if(updateNotificationOverride!=null)updateNotificationOverride(availableUpdate.Version.ToString(),OpenUpdateSettings);
            else tray?.ShowUpdateAvailable(availableUpdate.Version.ToString(),()=>Dispatcher.Invoke(OpenUpdateSettings));
        }
        catch(Exception ex){lock(log){log.Add("Update notification unavailable: "+ex.Message);if(log.Count>600)log.RemoveAt(0);}}
    }
    private async Task CheckForUpdatesAsync(bool automatic=false)
    {
        if(updateBusy||windowClosed)return;
        updateBusy=true;updateStatus="Checking GitHub…";RefreshUpdateControls();
        try
        {
            availableUpdate=await appUpdates.CheckAsync(AppVersion,windowLifetime.Token);
            if(windowClosed)return;
            updateStatus=availableUpdate==null?$"You're up to date ({AppVersionLabel}).":$"Version {availableUpdate.Version} is available. Downloading will restart the app.";
            if(automatic&&preferences.CheckUpdatesAtStartup)NotifyAvailableUpdate();
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
