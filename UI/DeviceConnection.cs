using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NothingOnWindows.Core;

namespace NothingOnWindows;

public partial class MainWindow
{
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
        await client.ConnectAsync(new PairedHeadphone(chosen.Id,chosen.Name,profile));await client.RefreshAsync();
        draft=client.State.Bands?.ToArray()??EqBand.Default;draftDirty=false;lastBatteryRead=Environment.TickCount64;
    },"Connected. Available device settings have been read.");
}
