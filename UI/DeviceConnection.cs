using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NothingOnWindows.Core;

namespace NothingOnWindows;

public partial class MainWindow
{
    private readonly System.Windows.Threading.DispatcherTimer automaticConnections=new(){Interval=TimeSpan.FromSeconds(5)};
    private AutomaticConnection? automaticConnection;
    internal bool AutomaticPreviewConnected=>client.Connected;
    internal string AutomaticPreviewStatus=>StatusLabel.Text;
    internal string[] AutomaticPreviewDetails {get{lock(log)return log.Where(s=>s.Contains("Automatic connection:")).ToArray();}}

    internal Task StartAutomaticConnection()
    {
        if(automaticConnection!=null||windowClosed)return Task.CompletedTask;
        automaticConnection=new AutomaticConnection(
            async()=>
            {
                var devices=await HeadphoneClient.DiscoverConnectedAsync(preferences.LastDeviceId,preferences.LastDeviceModel,windowLifetime.Token,
                    detail=>{lock(log){log.Add($"{DateTime.Now:HH:mm:ss} Automatic connection: {detail}");if(log.Count>600)log.RemoveAt(0);}});
                lock(log){log.Add($"{DateTime.Now:HH:mm:ss} Automatic connection: {devices.Count} connected Nothing device(s) found.");if(log.Count>600)log.RemoveAt(0);}
                return devices;
            },
            selected=>Run(()=>ConnectDeviceAsync(selected),"Connected automatically. Device settings are ready.",progress:"Connecting automatically…"),
            ()=>!windowClosed&&!busy&&!polling&&!client.Connected,
            ()=>preferences.LastDeviceId);
        automaticConnections.Tick+=async(_,_)=>await TryAutomaticConnectionAsync();
        automaticConnections.Start();
        return TryAutomaticConnectionAsync();
    }

    private async Task TryAutomaticConnectionAsync()
    {
        try{if(automaticConnection!=null)await automaticConnection.TickAsync();}
        catch(OperationCanceledException)when(windowClosed){}
        catch(Exception ex){lock(log){log.Add($"{DateTime.Now:HH:mm:ss} Automatic connection: {ex.Message}");if(log.Count>600)log.RemoveAt(0);}}
    }

    private async Task ConnectDeviceAsync(PairedHeadphone selected)
    {
        await client.ConnectAsync(selected,windowLifetime.Token);
        if(windowClosed)return;
        // Remember only a successfully opened control connection; saving is not a headphone write.
        try
        {
            var next=preferences with{LastDeviceId=selected.Id,LastDeviceModel=selected.Profile.Id};
            preferencesStore.Save(next);preferences=next;
        }
        catch(Exception ex){lock(log)log.Add("Could not remember the selected device: "+ex.Message);}
        await client.RefreshAsync();
        if(!client.Connected)throw new IOException("The headphones disconnected while their settings were being read.");
        automaticConnection?.Resume();
        draft=client.State.Bands?.ToArray()??EqBand.Default;draftDirty=false;lastBatteryRead=Environment.TickCount64;
    }

    internal void HideForWindowsStartup()
    {
        if(tray?.Visible==true){Hide();ShowInTaskbar=false;}
    }

    private async Task ConnectRenamedAsync()=>await Run(async()=>
    {
        var paired=await HeadphoneClient.AllPairedAsync();
        if(paired.Count==0)throw new InvalidOperationException("Pair your Nothing audio device in Windows Bluetooth settings first.");
        var chooser=new Window{Title="Connect Nothing audio",Width=400,Height=330,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Background,Foreground=Foreground,Resources=Resources,ResizeMode=ResizeMode.NoResize};
        var panel=new StackPanel{Margin=new Thickness(22)};
        panel.Children.Add(Text("Paired Bluetooth device",13));
        var deviceList=new ComboBox{ItemsSource=paired.Select(p=>p.Name).ToArray(),SelectedIndex=0,Margin=new Thickness(0,0,0,18)};panel.Children.Add(deviceList);
        panel.Children.Add(Text("Nothing model",13));
        var models=new ComboBox{ItemsSource=DeviceCatalog.All,SelectedItem=client.Profile,Margin=new Thickness(0,0,0,18)};panel.Children.Add(models);
        void Match(){var known=DeviceCatalog.Detect(paired[deviceList.SelectedIndex].Name);models.IsEnabled=known==null;if(known!=null)models.SelectedItem=known;}
        deviceList.SelectionChanged+=(_,_)=>Match();Match();
        var connect=ActionButton("Connect",()=>chooser.DialogResult=true);panel.Children.Add(connect);chooser.Content=panel;
        if(chooser.ShowDialog()!=true)return;
        var chosen=paired[deviceList.SelectedIndex];var profile=(DeviceProfile)models.SelectedItem;
        await ConnectDeviceAsync(new PairedHeadphone(chosen.Id,chosen.Name,profile));
    },"Connected. Available device settings have been read.");
}
