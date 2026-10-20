using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NothingOnWindows.Core;
using NothingOnWindows.UI;

namespace NothingOnWindows;

public partial class MainWindow
{
    internal async Task RunUiSmokeAsync(string reportPath)
    {
        var checks=new List<string>();
        void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);checks.Add(name);}
        async Task Idle()=>await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        T Find<T>(DependencyObject root,string? name=null) where T:DependencyObject
        {
            if(root is T found&&(name==null||root is FrameworkElement element&&element.Name==name))return found;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            {try{return Find<T>(VisualTreeHelper.GetChild(root,i),name);}catch(KeyNotFoundException){}}
            throw new KeyNotFoundException(typeof(T).Name);
        }
        Check(Width<=440&&Height<=800&&MinWidth==330&&Math.Abs(Width/Height-.55)<.001,"Compact portrait defaults");
        Check(Icon!=null&&Icon.Width>0,"Pixel headphone icon loaded");
        SetConnectionIndicator(true);Check(ConnectionBadge.Text=="Connected"&&ConnectionDot.Fill is SolidColorBrush green&&green.Color.G>green.Color.R&&green.Color.G>green.Color.B,"Connected status has green dot");
        SetConnectionIndicator(false);Check(ConnectionBadge.Text=="Offline"&&ConnectionDot.Effect==null,"Offline status remains neutral");
        Check(WindowStyle==WindowStyle.None,"Native title bar removed");
        Check(System.Windows.Shell.WindowChrome.GetWindowChrome(this)!=null,"Native window drag and resize chrome present");
        var hwnd=new WindowInteropHelper(this).Handle;
        var dpi=VisualTreeHelper.GetDpi(this);
        NativeSmoke.SendMessage(hwnd,0x0231,IntPtr.Zero,IntPtr.Zero);
        for(int edge=1;edge<=8;edge++)
        {
            IntPtr bounds=Marshal.AllocHGlobal(Marshal.SizeOf<NativeSmoke.Rect>());
            try
            {
                Marshal.StructureToPtr(new NativeSmoke.Rect{Left=100,Top=80,Right=100+(int)(385*dpi.DpiScaleX),Bottom=80+(int)(700*dpi.DpiScaleY)},bounds,false);
                NativeSmoke.SendMessage(hwnd,0x0214,new IntPtr(edge),bounds);
                var result=Marshal.PtrToStructure<NativeSmoke.Rect>(bounds);
                Check(Math.Abs((result.Right-result.Left)/(double)(result.Bottom-result.Top)-.55*dpi.DpiScaleX/dpi.DpiScaleY)<.002,"Native resize hook preserves ratio: edge "+edge);
            }
            finally{Marshal.FreeHGlobal(bounds);}
        }
        NativeSmoke.SendMessage(hwnd,0x0232,IntPtr.Zero,IntPtr.Zero);
        NativeSmoke.SendMessage(hwnd,0x0112,new IntPtr(0xf030),IntPtr.Zero);await Idle();
        Check(WindowState==WindowState.Normal,"Maximize cannot break portrait ratio");
        string instanceName="NothingOnWindows.Test."+Guid.NewGuid().ToString("N");
        using(var primary=new SingleInstanceSession(instanceName))
        using(var secondary=new SingleInstanceSession(instanceName))
        {
            Check(primary.IsPrimary&&!secondary.IsPrimary,"Duplicate launch recognizes existing instance");
            var signaled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);primary.Listen(()=>signaled.TrySetResult());
            Check(secondary.SignalPrimary(),"Duplicate launch can signal primary");
            await signaled.Task.WaitAsync(TimeSpan.FromSeconds(3));Check(true,"Activation reaches primary instance");
        }
        var hashes=new HashSet<string>();
        foreach(byte preset in new byte[]{0,3,2,1})
        {
            client.State.Preset=preset;client.State.Advanced=false;PreviewPage("Equalizer");await Idle();
            var dial=Find<SimpleEqDial>(PageContent);
            var bitmap=new RenderTargetBitmap((int)dial.ActualWidth,(int)dial.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(dial);
            var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
            hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels)));
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using(var imageFile=File.Create(reportPath+".preset-"+preset+".png"))encoder.Save(imageFile);
        }
        Check(hashes.Count==4,"All four preset graphs render differently");
        IEnumerable<T> All<T>(DependencyObject root) where T:DependencyObject
        {
            if(root is T match)yield return match;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
                foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;
        }
        Button Choice(string text)=>All<Button>(PageContent).First(b=>b.Content is string label&&label==text);
        bool ModeSelected(string label)=>All<Button>(PageContent).Any(b=>System.Windows.Automation.AutomationProperties.GetName(b)==label+", selected");
        void Receive(ushort command,byte[] bytes)
        {foreach(var frame in new FrameParser().Push(Protocol.Encode(command,17,bytes)))client.ProcessFrame(frame);}
        async Task Updated(){await Task.Delay(200);await Idle();}
        Check(poll.Interval<=TimeSpan.FromSeconds(2),"Fast read-only setting synchronization enabled");
        PreviewPage("Sound");Receive(0x401e,[1,4,0,2,4,0]);await Updated();
        Check(ModeSelected("Noise cancellation"),"Device read selects current ANC mode");
        Receive(0xe003,[1,7]);await Updated();
        Check(ModeSelected("Transparency")&&!ModeSelected("Noise cancellation"),"Physical ANC push updates visible Sound controls");
        Receive(0x6003,[1,5]);await Updated();Check(ModeSelected("Off"),"Alternate ANC push updates Off selection");
        Receive(0x401e,[1,1,0,2,1,0]);await Updated();
        Check(ModeSelected("Noise cancellation")&&Choice("High").Background==lime,"Fallback query updates both ANC mode and strength");
        Receive(0x404f,[2,0]);await Updated();Check(ModeSelected("Concert mode"),"External spatial audio change updates Concert selection");
        Receive(0x404f,[3,0]);await Updated();Check(ModeSelected("Cinema mode")&&!ModeSelected("Concert mode"),"External spatial audio change updates Cinema selection");
        Receive(0x404e,[1,5]);await Updated();Check(Choice("Level 1").Background==lime,"External bass setting updates level one selection");
        Receive(0x404e,[1,10]);await Updated();Check(Choice("Level 2").Background==lime&&Choice("Level 1").Background!=lime,"External bass setting replaces previous selection");
        Receive(0x4041,[1]);await Updated();Check(All<ToggleButton>(PageContent).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Low lag mode").IsChecked==true,"External low lag change turns toggle on");
        Receive(0x4041,[2]);await Updated();Check(All<ToggleButton>(PageContent).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Low lag mode").IsChecked==false,"External low lag change turns toggle off");
        var soundCard=PageContent.Children[0];Receive(0x401e,[1,1,0,2,1,0]);await Updated();
        Check(ReferenceEquals(soundCard,PageContent.Children[0]),"Unchanged polling result does not recreate Sound controls");
        Receive(0x4007,[1,6,80]);await Updated();soundCard=PageContent.Children[0];
        Receive(0xe001,[1,6,79]);await Updated();Check(ReferenceEquals(soundCard,PageContent.Children[0]),"Battery update leaves sound controls intact");
        PreviewPage("Equalizer");Receive(0x404c,[0]);Receive(0x401f,[0]);await Updated();
        var oldGraph=Find<SimpleEqDial>(PageContent);Receive(0x401f,[3]);await Updated();
        Check(Choice("More bass").Background==lime&&!ReferenceEquals(oldGraph,Find<SimpleEqDial>(PageContent)),"Device EQ preset updates selection and graph on open EQ tab");
        oldGraph=Find<SimpleEqDial>(PageContent);Receive(0xe003,[1,7]);await Updated();
        Check(ReferenceEquals(oldGraph,Find<SimpleEqDial>(PageContent)),"ANC change preserves the current EQ editor");
        PreviewPage("Advanced");Receive(0x404c,[1]);await Updated();
        Check(All<TextBlock>(PageContent).Any(t=>t.Text=="Advanced EQ · On"),"External Advanced EQ toggle updates visible status");
        var liveBands=EqBand.Default;liveBands[0]=liveBands[0] with{Gain=2};Receive(0x404d,Protocol.AdvancedPayload(liveBands));await Updated();
        Check(draft[0].Gain==2&&All<Slider>(PageContent).First().Value==2,"External advanced profile updates a clean editor");
        All<Slider>(PageContent).First().Value=4;
        liveBands[0]=liveBands[0] with{Gain=-2};Receive(0x404d,Protocol.AdvancedPayload(liveBands));await Updated();
        Check(client.State.Bands![0].Gain==-2&&draft[0].Gain==4&&All<Slider>(PageContent).First().Value==4,"Automatic sync preserves an unsaved advanced EQ draft");
        draftDirty=false;PreviewPage("Sound");busy=true;Receive(0xe003,[1,5]);await Updated();
        Check(!ModeSelected("Off"),"Device update is deferred during an app operation");
        busy=false;await Updated();Check(ModeSelected("Off"),"Deferred device update appears after app operation finishes");
        void RejectWrite(ushort write,byte[] payload,ushort read,string label)
        {
            try{client.ValidateWrite(write,payload,read);}
            catch(InvalidOperationException){Check(true,label);return;}
            throw new InvalidOperationException(label);
        }
        void CaptureWindow(string suffix)
        {
            var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output=File.Create(reportPath+suffix+".png");encoder.Save(output);
        }
        var photoHashes=new HashSet<string>();
        Width=385;await Idle();Height=800;await Idle();
        foreach(var model in DeviceCatalog.All)
        {
            client.SelectProfile(model);draft=EqBand.Default;draftDirty=false;
            Receive(0x4042,System.Text.Encoding.UTF8.GetBytes("1.0.2.0"));
            Receive(0x4007,model.Headphones?[1,6,75]:[3,2,75,3,80,4,90]);
            Receive(0x401f,[0]);Receive(0x4041,[2]);
            if(model.HasAdvancedEq(client.State.Firmware))Receive(0x404c,[0]);
            if(model.Supports(0xc01e,client.State.Firmware))Receive(0x401e,[1,model.ActiveAncLevels(client.State.Firmware)[0]]);
            if(model.Spatial.Length>0)Receive(0x404f,model.SpatialPayload(0));
            if(model.Bass)Receive(0x404e,[0,0]);
            var configured=model.Controls(client.State.Firmware).Where(c=>c.Custom).ToArray();
            var gestures=new List<byte>{(byte)configured.Length};
            foreach(var c in configured)gestures.AddRange(new byte[]{c.Device,c.Control,(byte)c.Type,c.Actions.OrderByDescending(a=>a.Name.Length).First().Code});
            Receive(0x4018,gestures.ToArray());await Updated();PreviewPage("Sound");await Idle();
            Check(DeviceName.Text==model.ShortName&&ProductPhoto.Source is BitmapImage photo&&photo.PixelWidth>0&&photo.UriSource.ToString().Contains(model.Id),"Correct model photo and name: "+model.Name);
            using(var resource=Application.GetResourceStream(new Uri("pack://application:,,,/"+model.ImageResource))!.Stream)
                photoHashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(resource)));
            Check(All<TextBlock>(PageContent).Any(t=>t.Text=="Bass enhancement")==model.Bass,"Model-specific bass card: "+model.Name);
            Check(All<TextBlock>(PageContent).Any(t=>t.Text=="Wearing detection")==model.InEar,"Model-specific wearing detection: "+model.Name);
            Check(model.Headphones?BatteryText(client.State).Contains("75%"):BatteryText(client.State).Contains("L 75%")&&BatteryText(client.State).Contains("R 80%")&&BatteryText(client.State).Contains("Case 90%"),"Correct single/stereo battery display: "+model.Name);
            CaptureWindow("."+model.Id+"-sound");
            PreviewPage("Equalizer");await Idle();
            Check(All<Button>(PageContent).Any(b=>b.Content as string=="Custom")==model.CustomEq,"Custom EQ button follows profile: "+model.Name);
            Check(All<Button>(PageContent).Any(b=>b.Content as string=="Advanced")==model.HasAdvancedEq(client.State.Firmware),"Advanced EQ tab follows profile: "+model.Name);
            PreviewPage("Controls");await Idle();CaptureWindow("."+model.Id+"-controls");
            Check(All<ComboBox>(PageContent).Count()==configured.Length,"All mapped controls render: "+model.Name);
            foreach(var c in configured)
                client.ValidateWrite(0xf003,[1,c.Device,c.Control,(byte)c.Type,c.Actions[0].Code],0xc018);
            Check(true,"Mapped gesture writes validate: "+model.Name);
            RejectWrite(0xf003,[1,7,1,2,1],0xc018,"Unmapped gesture writes rejected: "+model.Name);
            if(model.Bass)client.ValidateWrite(0xf051,[1,(byte)(model.BassLevels*model.BassStep)],0xc04e);
            else RejectWrite(0xf051,[1,5],0xc04e,"Unsupported bass writes rejected: "+model.Name);
            if(!model.CustomEq)RejectWrite(0xf041,Protocol.SimplePayload(1,0,0,model),0xc044,"Unsupported custom EQ writes rejected: "+model.Name);
            if(!model.HasAdvancedEq(client.State.Firmware))RejectWrite(0xf04f,[1,0],0xc04c,"Unsupported Advanced EQ writes rejected: "+model.Name);
            if(model.Id=="B192")
            {
                Receive(0x4068,[1]);await Updated();RejectWrite(0xf010,[0,0],0xc01f,"Flat EQ switch prevents sound writes");
                PreviewPage("Equalizer");await Idle();Check(All<TextBlock>(PageContent).Any(t=>t.Text=="Flat EQ is on")&&Choice("Balanced").Background!=lime,"Flat EQ bypass is visible on EQ page");
                Receive(0x4068,[0]);await Updated();
            }
            if(model.SpatialEqExclusive)
            {
                Receive(0x404f,model.SpatialPayload(1));await Updated();
                RejectWrite(0xf04f,[1,0],0xc04c,"Spatial/Advanced EQ interlock: "+model.Name);
                if(model.Bass)RejectWrite(0xf051,[1,2],0xc04e,"Spatial/bass interlock: "+model.Name);
                Receive(0x404f,model.SpatialPayload(0));await Updated();
            }
            foreach(double height in new[]{800.0,600.0})
            {
                Height=height;await Idle();
                foreach(string view in new[]{"Sound","Equalizer","Advanced","Controls","Explore","Connection"})
                {PreviewPage(view);await Idle();Check(PageScroll.ExtentWidth<=PageScroll.ViewportWidth+1,$"No horizontal overflow: {model.Name} / {view} / {height}");}
            }
            Height=800;await Idle();
        }
        Check(photoHashes.Count==DeviceCatalog.All.Count,"Every model has its own distinct embedded product photo");
        client.SelectProfile(DeviceCatalog.HeadphoneA);draft=EqBand.Default;draftDirty=false;await Updated();
        PreviewPage("Connection");await Idle();
        var profileSelector=Find<ComboBox>(PageContent,"DeviceProfileSelector");
        Check(profileSelector.Items.Count==11,"Settings lists all eleven Nothing audio profiles");
        profileSelector.SelectedItem=DeviceCatalog.All.Single(p=>p.Id=="B162");await Updated();
        Check(client.Profile.Id=="B162"&&DeviceName.Text=="Ear (a)","Settings profile selection updates device preview");
        PreviewPage("Connection");await Idle();Find<ComboBox>(PageContent,"DeviceProfileSelector").SelectedItem=DeviceCatalog.HeadphoneA;await Updated();
        foreach(string view in new[]{"Sound","Equalizer","Advanced","Controls","Explore","Connection"})
        {PreviewPage(view);await Idle();Check(PageScroll.ExtentWidth<=PageScroll.ViewportWidth+1,"No horizontal overflow: "+view);}
        Check(Find<GlassPanel>(PageContent).Effect!=null,"Frosted glass panels render with depth");
        Width=385;await Idle();Check(Math.Abs(ActualWidth/ActualHeight-.55)<.001&&Math.Abs(ActualHeight-700)<2,"Width resize preserves portrait ratio");
        Height=760;await Idle();Check(Math.Abs(ActualWidth/ActualHeight-.55)<.001&&Math.Abs(ActualWidth-418)<2,"Height resize preserves portrait ratio");
        Height=600;await Idle();
        foreach(string view in new[]{"Sound","Equalizer","Advanced","Controls","Explore","Connection"})
        {PreviewPage(view);await Idle();Check(PageScroll.ExtentWidth<=PageScroll.ViewportWidth+1,"No horizontal overflow at minimum size: "+view);}
        Check(Math.Abs(ActualWidth-330)<2&&Math.Abs(ActualHeight-600)<2,"Smallest window keeps portrait ratio");
        Height=800;await Idle();
        InitializeTray();Check(tray?.Visible==true,"Tray icon created");
        MinimizeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Idle();
        Check(WindowState==WindowState.Minimized,"Custom minimize button works");
        ShowFromTray();await Idle();Check(IsVisible&&ShowInTaskbar&&WindowState==WindowState.Normal,"Restore from tray works");
        PreviewPage("Connection");await Idle();
        var checkbox=Find<ToggleButton>(PageContent,"CloseToTrayToggle");
        Check(checkbox is not CheckBox&&checkbox.Style==FindResource("GlassSwitch"),"Close-to-tray uses accessible glass toggle");
        checkbox.IsChecked=true;checkbox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(preferences.CloseToTray&&preferencesStore.Load().CloseToTray,"Close-to-tray preference persists");
        CloseButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Idle();
        Check(!IsVisible&&!ShowInTaskbar&&tray?.Visible==true,"Custom close hides to tray when enabled");
        tray!.InvokeMenu(0);await Idle();Check(IsVisible&&ShowInTaskbar,"Tray Open menu reopens hidden app");
        tray.InvokeMenu(1);await Idle();Check(page=="Connection","Tray Settings menu opens settings");
        PreviewPage("Connection");await Idle();checkbox=Find<ToggleButton>(PageContent,"CloseToTrayToggle");checkbox.IsChecked=false;checkbox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(!preferences.CloseToTray&&!preferencesStore.Load().CloseToTray,"Close-to-tray preference can be disabled");
        checkbox.IsChecked=true;checkbox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        bool closed=false;Closed+=(_,_)=>closed=true;tray!.InvokeMenu(3);await Idle();
        Check(closed&&tray==null,"Explicit Exit closes and disposes tray even when close-to-tray is enabled");
        string defaultSettings=reportPath+".default-settings.json";new PreferencesStore(defaultSettings).Save(new AppPreferences());
        var normalClose=new MainWindow(defaultSettings);bool normalClosed=false;normalClose.Closed+=(_,_)=>normalClosed=true;normalClose.Show();await Idle();normalClose.InitializeTray();
        normalClose.CloseButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Idle();
        Check(normalClosed&&normalClose.tray==null,"Custom close exits and disposes tray when preference is disabled");
        File.WriteAllText(reportPath,JsonSerializer.Serialize(new{passed=checks.Count,checks,syntheticPresetData=true,syntheticDeviceUpdates=true,deviceProfiles=DeviceCatalog.All.Count,modelImages=photoHashes.Count,headphoneWrites=false},new JsonSerializerOptions{WriteIndented=true}));
    }
    private static class NativeSmoke
    {
        [StructLayout(LayoutKind.Sequential)]internal struct Rect{public int Left,Top,Right,Bottom;}
        [DllImport("user32.dll")]internal static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam);
    }
}
