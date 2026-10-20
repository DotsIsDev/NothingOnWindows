using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using NothingOnWindows.Core;

namespace NothingOnWindows;

public partial class MainWindow
{
    private readonly DispatcherTimer liveUpdates=new(){Interval=TimeSpan.FromMilliseconds(120)};
    private StateChanges pendingChanges;
    private bool polling;
    private bool windowClosed;
    private bool draftDirty;
    private long lastBatteryRead=Environment.TickCount64;

    private void QueueDeviceChanges(StateChanges changes)
    {
        if(Dispatcher.HasShutdownStarted)return;
        Dispatcher.BeginInvoke(()=>
        {
            if(windowClosed)return;
            pendingChanges|=changes;
            liveUpdates.Start();
        });
    }

    private void ApplyDeviceChanges()
    {
        if(windowClosed){liveUpdates.Stop();return;}
        UpdateHeader();
        if(busy)return;
        // Keep a drag or text edit intact; apply the queued state afterward.
        if((pendingChanges&StateChanges.Connection)==0)
        {
            if(Mouse.Captured is DependencyObject captured&&PageContent.IsAncestorOf(captured))return;
            if(Keyboard.FocusedElement is TextBox text&&PageContent.IsAncestorOf(text))return;
            if(Keyboard.FocusedElement is ComboBox combo&&combo.IsDropDownOpen)return;
        }
        var changes=pendingChanges;pendingChanges=StateChanges.None;liveUpdates.Stop();
        if((changes&StateChanges.Bands)!=0&&!draftDirty)draft=client.State.Bands?.ToArray()??draft;
        StateChanges relevant=page switch
        {
            "Sound"=>StateChanges.Sound,
            "Equalizer"=>StateChanges.Eq|StateChanges.Bands,
            "Controls"=>StateChanges.Controls,
            "Connection"=>StateChanges.Device,
            _=>StateChanges.None
        };
        if((changes&(relevant|StateChanges.Connection|StateChanges.Availability))!=0)Render();
    }

    private async Task SynchronizeDeviceAsync()
    {
        if(windowClosed||busy||polling||!client.Connected)return;
        polling=true;
        try
        {
            bool visible=IsVisible&&WindowState==WindowState.Normal;
            bool settings=visible&&page is "Sound" or "Equalizer";
            bool battery=Environment.TickCount64-lastBatteryRead>=LiveStateSync.BatteryInterval.TotalMilliseconds;
            foreach(ushort command in LiveStateSync.ReadCommands(client.State,settings,visible&&page=="Equalizer",battery))
            {
                if(windowClosed||busy||!client.Connected)break;
                await client.RequestAsync(command,command==0xc04d?[255]:[]);
                if(command==0xc007)lastBatteryRead=Environment.TickCount64;
            }
        }
        catch(Exception ex)
        {
            // Background reads never disable controls or replace the user's status message.
            lock(log){log.Add($"{DateTime.Now:HH:mm:ss} Automatic sync: {ex.Message}");if(log.Count>600)log.RemoveAt(0);}
        }
        finally{polling=false;}
    }
}
