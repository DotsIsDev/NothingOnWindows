using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NothingOnWindows.Core;
using NothingOnWindows.UI;

namespace NothingOnWindows;

public partial class MainWindow : Window
{
    private readonly HeadphoneClient client = new();
    private readonly List<string> log = [];
    private readonly Dictionary<string, Button> navigation = [];
    private readonly System.Windows.Threading.DispatcherTimer poll = new() { Interval = LiveStateSync.SettingsInterval };
    private List<EqProfile> profiles = [];
    private List<CatalogProfile>? catalog;
    private bool modelProfilesOnly = true;
    private EqBand[] draft = EqBand.Default;
    private string page = "Sound";
    private string eqTab = "Simple";
    private bool busy;
    private readonly PreferencesStore preferencesStore;
    private readonly IStartupRegistration startupRegistration;
    private readonly CancellationTokenSource windowLifetime=new();
    private AppPreferences preferences = new();
    private TrayController? tray;
    private bool exitRequested;
    private readonly AspectRatioWindow aspectRatio;
    private readonly Brush lime = Brushes.White;
    private readonly Brush muted = new SolidColorBrush(Color.FromRgb(170, 170, 170));
    private string? imageModel;

    public MainWindow(string? preferencesPath = null):this(preferencesPath,new WindowsStartupRegistration()) { }

    internal MainWindow(string? preferencesPath,IStartupRegistration startupRegistration,IAppUpdates? appUpdates=null)
    {
        this.appUpdates=appUpdates??new GitHubAppUpdates();
        this.startupRegistration=startupRegistration;
        InitializeComponent();
        MaxHeight=Math.Max(MinHeight,SystemParameters.WorkArea.Height-32);
        MaxWidth=MaxHeight*AspectRatioSizing.PortraitRatio;
        Height=Math.Min(Height,MaxHeight);Width=Height*AspectRatioSizing.PortraitRatio;
        aspectRatio=new AspectRatioWindow(this);
        preferencesStore = new PreferencesStore(preferencesPath);
        try { preferences = preferencesStore.Load(); } catch(Exception ex) { StatusLabel.Text="Could not load app preferences: "+ex.Message; }
        foreach (string name in new[] { "Sound", "Equalizer", "Controls", "Explore", "Connection" })
        {
            var b = new Button { Content = name == "Connection" ? "Settings" : name == "Equalizer" ? "EQ" : name, Padding = new Thickness(3,9,3,9), FontSize = 10, Margin = new Thickness(1,0,1,0), HorizontalAlignment=HorizontalAlignment.Stretch };
            b.Click += (_, _) => { page = name; if (name == "Equalizer"&&!draftDirty) draft = client.State.Bands?.ToArray() ?? draft; PageScroll.ScrollToTop(); Render(); _=SynchronizeDeviceAsync(); if (SystemParameters.ClientAreaAnimation) PageContent.BeginAnimation(OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(.65,1,TimeSpan.FromMilliseconds(140))); };
            navigation[name] = b; Navigation.Children.Add(b);
        }
        client.Log += s => { lock (log) { log.Add($"{DateTime.Now:HH:mm:ss} {s}"); if (log.Count > 600) log.RemoveAt(0); } };
        client.Changed += QueueDeviceChanges;
        liveUpdates.Tick+=(_,_)=>ApplyDeviceChanges();
        Closed += (_, _) => { windowClosed=true;automaticConnections.Stop();automaticConnection?.Suspend();windowLifetime.Cancel();poll.Stop();liveUpdates.Stop();client.Changed-=QueueDeviceChanges; client.Dispose(); tray?.Dispose(); tray=null; aspectRatio.Dispose(); };
        poll.Tick += async (_, _) => await SynchronizeDeviceAsync();
        Activated+=async(_,_)=>await SynchronizeDeviceAsync();
        poll.Start();
        try { profiles = ProfileStore.Load(); } catch (Exception ex) { StatusLabel.Text = "Could not load saved profiles: " + ex.Message; }
        Render();
    }

    internal void InitializeTray()
    {
        if(tray!=null)return;
        tray=new TrayController(()=>Dispatcher.Invoke(ShowFromTray),()=>Dispatcher.Invoke(()=>{page="Connection";ShowFromTray();Render();}),()=>Dispatcher.Invoke(ExitApplication));
        UpdateHeader();
    }
    internal void ShowFromTray()
    {
        ShowInTaskbar=true;Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();
    }
    internal void ExitApplication(){exitRequested=true;Close();}
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if(!exitRequested && preferences.CloseToTray && tray?.Visible==true){e.Cancel=true;Hide();ShowInTaskbar=false;}
    }
    private void Minimize_Click(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();

    private TextBlock Text(string value, double size = 14, Brush? brush = null) => new() { Text = value, FontSize = size, Foreground = brush ?? Foreground, Margin = new Thickness(0, 0, 0, 12) };
    private StackPanel Card(string title, string subtitle, Panel? destination = null)
    {
        var content = new StackPanel(); if(!string.IsNullOrWhiteSpace(title)) content.Children.Add(Text(title, 16)); if (!string.IsNullOrWhiteSpace(subtitle)) content.Children.Add(Text(subtitle, 12, muted));
        (destination ?? PageContent).Children.Add(new GlassPanel(GlassBackdrop,content,new Thickness(15)){Margin=new Thickness(0,0,0,12)});
        return content;
    }
    private Button ActionButton(string label, Action action, bool enabled = true)
    {
        var button = new Button { Content = label, IsEnabled = enabled, HorizontalAlignment = HorizontalAlignment.Left }; button.Click += (_, _) => action(); return button;
    }
    private void Choices<T>(StackPanel parent, (string Label, T Value)[] options, T? current, bool enabled, Func<T, Task> apply, bool circular = false) where T : struct
    {
        Panel row = circular ? new System.Windows.Controls.Primitives.UniformGrid { Columns = options.Length, Margin = new Thickness(0,4,0,3) } : new WrapPanel();
        bool presetChoices=!circular&&options.Length>=4&&options[0].Label=="Balanced";
        if(presetChoices)row=new System.Windows.Controls.Primitives.UniformGrid{Columns=2};
        Button? customButton=null;
        foreach (var option in options)
        {
            bool selected = current.HasValue && EqualityComparer<T>.Default.Equals(current.Value, option.Value);
            var b = ActionButton(option.Label, async () => await Run(() => apply(option.Value), "Updated."), enabled && !busy);
            b.Padding=new Thickness(11,8,11,8);b.FontSize=11;
            if (selected) { b.Background = lime; b.Foreground = Brushes.Black; }
            if(presetChoices)
            {
                b.HorizontalAlignment=HorizontalAlignment.Stretch;b.Margin=new Thickness(2,0,2,6);
                if(option.Label=="Custom")
                {customButton=b;if(selected){b.Background=new SolidColorBrush(Color.FromRgb(224,35,45));b.Foreground=Brushes.White;}continue;}
            }
            if (circular)
            {
                b.Content = new ModeIcon(option.Label, selected ? Brushes.Black : Brushes.White) { Width=25,Height=25 };
                b.Width=50;b.Height=50;b.Padding=new Thickness(0);b.Margin=new Thickness(0,0,0,9);b.HorizontalAlignment=HorizontalAlignment.Center;
                b.Template = CircleTemplate();
                System.Windows.Automation.AutomationProperties.SetName(b,option.Label + (selected ? ", selected" : ""));
                var item = new StackPanel { HorizontalAlignment=HorizontalAlignment.Center };
                item.Children.Add(b); var label=Text(option.Label=="Noise cancellation"?"On":option.Label.Replace(" mode",""),11,selected?Brushes.White:muted);label.TextAlignment=TextAlignment.Center;item.Children.Add(label);row.Children.Add(item);
            }
            else row.Children.Add(b);
        }
        parent.Children.Add(row);
        if(customButton!=null)parent.Children.Add(customButton);
    }
    private bool Can(ushort read) => client.Connected && client.Profile.Supports(read,client.State.Firmware) && client.State.Readable.Contains(read) && !busy && (client.State.FlatEq!=true || read is not (0xc01e or 0xc04f or 0xc04e or 0xc01f or 0xc04c or 0xc04d or 0xc044));
    private static ControlTemplate CircleTemplate() => (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
        <Border x:Name="Circle" Background="{TemplateBinding Background}" CornerRadius="40" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1"><Border.Effect><DropShadowEffect BlurRadius="10" ShadowDepth="3" Opacity=".2"/></Border.Effect><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
        <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Circle" Property="Opacity" Value="0.75"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Circle" Property="BorderBrush" Value="#EB262E"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter TargetName="Circle" Property="Opacity" Value="0.4"/></Trigger></ControlTemplate.Triggers></ControlTemplate>
        """);

    private void UpdateHeader()
    {
        var s = client.State;
        var profile=client.Profile;
        DeviceName.Text=profile.ShortName;
        ProfileStatusLabel.Text=profile.Verified?"Hardware-tested model":"Experimental profile · device testing needed";
        if(imageModel!=profile.Id)
        {
            ProductPhoto.Source=new BitmapImage(new Uri("pack://application:,,,/"+profile.ImageResource));imageModel=profile.Id;
            System.Windows.Automation.AutomationProperties.SetName(ProductPhoto,profile.Name+" product photo");
        }
        SetConnectionIndicator(client.Connected);
        BatteryLabel.Text = client.Connected ? BatteryText(s) : "—  Battery";
        DeviceDetail.Text = client.Connected ? $"Firmware {s.Firmware}" : "Pair in Windows to get started";
        ConnectButton.Content = client.Connected ? "Disconnect" : "Connect";
        ConnectButton.IsEnabled = !busy; RefreshButton.IsEnabled = client.Connected && !busy;
        CloseButton.ToolTip=preferences.CloseToTray&&tray?.Visible==true?"Minimize to tray":"Close";
        DevicePanel.Visibility=page=="Sound"?Visibility.Visible:Visibility.Collapsed;
        tray?.Update("Nothing on Windows · "+(client.Connected?$"Connected{(s.Battery.HasValue?$" · {s.Battery}%":"")}":"Disconnected"));
    }
    private static string BatteryText(HeadphoneState state)
    {
        if(state.Profile.Headphones)return state.Battery.HasValue?$"▰  {state.Battery}%{(state.Charging?" · Charging":"")}":"—  Battery";
        return string.Join(" · ",new (byte Id,string Name)[]{(2,"L"),(3,"R"),(4,"Case")}.Select(item=>
        {var part=state.Batteries.FirstOrDefault(p=>p.Device==item.Id);return item.Name+" "+(part==null?"—":$"{part.Level}%{(part.Charging?" +":"")}");}));
    }
    private void SetConnectionIndicator(bool connected)
    {
        ConnectionBadge.Text=connected?"Connected":"Offline";
        ConnectionDot.Fill=new SolidColorBrush(connected?Color.FromRgb(83,232,158):Color.FromRgb(146,146,146));
        ConnectionDot.Effect=connected?new System.Windows.Media.Effects.DropShadowEffect{Color=Color.FromRgb(83,232,158),BlurRadius=6,ShadowDepth=0,Opacity=.6}:null;
    }
    private void Render()
    {
        UpdateHeader(); PageContent.Children.Clear();
        foreach (var n in navigation) { n.Value.Background = n.Key == page ? (Brush)FindResource("GlassButtonFill") : Brushes.Transparent; n.Value.BorderBrush=n.Key==page?(Brush)FindResource("GlassEdge"):Brushes.Transparent; n.Value.Foreground = n.Key == page ? Brushes.White : muted; }
        PageTitle.Text = page == "Connection" ? "Settings" : page;
        if (!client.Connected && page is not "Explore" and not "Connection") PageContent.Children.Add(Text("Connect your headphones to read and change their settings.", 13, lime));
        switch (page) { case "Sound": RenderSound(); break; case "Equalizer": RenderEq(); break; case "Controls": RenderControls(); break; case "Explore": RenderExplore(); break; default: RenderConnection(); break; }
    }
    private void RenderSound()
    {
        var s = client.State;
        var profile=client.Profile;var levels=profile.ActiveAncLevels(s.Firmware);
        if(levels.Length>0)
        {
        var anc = Card(profile.Id=="B157"?"Noise reduction":"Noise cancellation", "");
        byte active= s.Anc.HasValue&&levels.Contains(s.Anc.Value)?s.Anc.Value:levels[^1];
        var modes=new List<(string,byte)>{("Noise cancellation",active)};
        if(profile.Id!="B157")modes.Add(("Transparency",7));modes.Add(("Off",5));
        Choices(anc,modes.ToArray(),s.Anc,Can(0xc01e)&&s.FlatEq!=true,SetAnc,true);
        if(s.Anc.HasValue&&levels.Contains(s.Anc.Value)&&levels.Length>1)Choices(anc,new (string,byte)[]{("Low",3),("Mid",2),("High",1),("Adaptive",4)}.Where(v=>levels.Contains(v.Item2)).ToArray(),s.Anc,Can(0xc01e)&&s.FlatEq!=true,SetAnc);
        }
        if(profile.Spatial.Length>0)
        {
        var spatial = Card("Spatial audio", "");
        Choices(spatial,profile.Spatial.Select(m=>(m.Label,m.Value)).ToArray(),s.Spatial,Can(0xc04f),async v=>{
            await client.SetVerifiedAsync(0xf052,profile.SpatialPayload(v),0xc04f,state=>state.Spatial==v);
        }, true);
        }
        if(profile.Id=="B192")Card("Spatial audio","Dynamic spatial scenes are managed by Nothing X; this profile does not yet control the Audiodo processing.");
        if(s.FlatEq==true)Card("Flat EQ is on","Turn off the physical Flat EQ switch on your headphones to edit sound settings.");
        var compact = new StackPanel();
        PageContent.Children.Add(compact);
        if(profile.Bass){var bass = Card("Bass enhancement", "", compact);
        Choices(bass,Enumerable.Range(0,profile.BassLevels+1).Select(v=>(v==0?"Off":$"Level {v}",v)).ToArray(),s.Bass,Can(0xc04e),async v=>{
            await client.SetVerifiedAsync(0xf051,profile.BassPayload(v,s.BassWireValue),0xc04e,state=>state.Bass==v);
        });
        }
        if(profile.Supports(0xc041,s.Firmware)){
        var lag = Card("Low lag mode", "For games and video", compact);
        var lagRow=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,5,0,0) };
        var toggle=new System.Windows.Controls.Primitives.ToggleButton { Style=(Style)FindResource("GlassSwitch"),IsChecked=s.LowLag??false,IsEnabled=Can(0xc041) };
        System.Windows.Automation.AutomationProperties.SetName(toggle,"Low lag mode");
        toggle.Click+=async(_,_)=>{bool desired=toggle.IsChecked==true;await Run(()=>client.SetVerifiedAsync(0xf040,[(byte)(desired?1:2),0],0xc041,state=>state.LowLag==desired),"Updated.");};
        lagRow.Children.Add(toggle);var lagLabel=Text(s.LowLag.HasValue?(s.LowLag.Value?"On":"Off"):"—",12,muted);lagLabel.Margin=new Thickness(12,4,0,0);lagRow.Children.Add(lagLabel);lag.Children.Add(lagRow);
        }
        if(profile.InEar)
        {
            var wear=Card("Wearing detection","Pause automatically when you remove your device.",compact);
            var wearToggle=new System.Windows.Controls.Primitives.ToggleButton{Style=(Style)FindResource("GlassSwitch"),IsChecked=s.InEar??false,IsEnabled=Can(0xc00e),HorizontalAlignment=HorizontalAlignment.Left};
            System.Windows.Automation.AutomationProperties.SetName(wearToggle,"Wearing detection");
            wearToggle.Click+=async(_,_)=>{bool desired=wearToggle.IsChecked==true;await Run(()=>client.SetVerifiedAsync(0xf004,[1,1,(byte)(desired?1:0)],0xc00e,x=>x.InEar==desired),"Wearing detection updated.");};wear.Children.Add(wearToggle);
        }
    }
    private Task SetAnc(byte v) => client.SetVerifiedAsync(0xf00f, [1, v, 0], 0xc01e, state => state.Anc == v);

    private void RenderEq()
    {
        var s = client.State;
        bool advancedSupported=client.Profile.HasAdvancedEq(s.Firmware);
        if(!advancedSupported)eqTab="Simple";
        var tabs = new WrapPanel { Margin = new Thickness(0,0,0,12) };
        foreach(string tabName in advancedSupported?new[]{"Simple","Advanced","Explore"}:new[]{"Simple","Explore"})
        {
            var tab=ActionButton(tabName,()=>{if(tabName=="Explore")page="Explore";else eqTab=tabName;Render();});
            tab.Background=eqTab==tabName?Brushes.White:Brushes.Transparent;tab.Foreground=eqTab==tabName?Brushes.Black:muted;tabs.Children.Add(tab);
        }
        PageContent.Children.Add(tabs);
        if(s.FlatEq==true)Card("Flat EQ is on","Your saved EQ is bypassed. Turn off the physical Flat EQ switch to edit your sound.");
        if(eqTab=="Simple")
        {
        var simple = Card("", "");
        float[] editGains = s.SimpleGains?.ToArray() ?? new float[3];
        bool custom = s.Preset == 5 && s.Advanced != true && s.FlatEq!=true;
        var preview=custom?editGains:EqPresentation.Preview(s.Advanced==true?null:s.Preset,editGains);
        var dial = new SimpleEqDial(preview,s.Advanced!=true&&s.Preset is 0 or 1 or 2 or 3) { Name="SimpleEqGraph",Width=290,Height=235,HorizontalAlignment=HorizontalAlignment.Center,IsEnabled=custom && Can(0xc044),Margin=new Thickness(0,0,0,10),ToolTip=custom?"Drag a point to adjust its gain.":"Preset preview shows relative tonal balance, not measured frequency response." };
        simple.Children.Add(new Viewbox{MaxWidth=290,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,Child=dial,HorizontalAlignment=HorizontalAlignment.Center});
        if(!custom)simple.Children.Add(Text(s.FlatEq==true?"Flat EQ is active.":s.Advanced==true?"Advanced EQ is active.":(client.Profile.CustomEq?"Select Custom to fine-tune your sound.":"Choose a preset to change your sound."),11,muted));
        Choices(simple,client.Profile.Presets,s.Advanced==true||s.FlatEq==true?null:s.Preset,Can(0xc01f)&&(!advancedSupported||Can(0xc04c))&&s.FlatEq!=true,async v=>{
            if(advancedSupported)await client.SetVerifiedAsync(0xf04f,[0,0],0xc04c,state=>state.Advanced==false);
            await client.SetVerifiedAsync(0xf010, [v, 0], 0xc01f, state => state.Preset == v);
        });
        if (s.Preset == 5 && s.Advanced != true)
        {
            float[] gains = editGains;
            var keyboard = new Expander { Header="Adjust with sliders",Foreground=muted,Margin=new Thickness(0,8,0,12) };
            var sliderPanel=new StackPanel(); keyboard.Content=sliderPanel;simple.Children.Add(keyboard);
            for (int i = 0; i < 3; i++)
            {
                int band = i; string label = new[] { "Bass", "Mid", "Treble" }[i];
                var value = Text($"{label}: {gains[i]:+0.0;-0.0;0.0} dB", 12, muted);
                var slider = new Slider { Minimum = -6, Maximum = 6, Value = gains[i] };
                System.Windows.Automation.AutomationProperties.SetName(slider, label + " gain in decibels");
                slider.ValueChanged += (_, _) => { gains[band] = (float)slider.Value; value.Text = $"{label}: {gains[band]:+0.0;-0.0;0.0} dB"; };
                sliderPanel.Children.Add(value); sliderPanel.Children.Add(slider);
                slider.ValueChanged += (_,_)=>dial.InvalidateVisual();
                dial.GainsChanged += ()=> { slider.Value=gains[band]; };
            }
            simple.Children.Add(ActionButton("Apply custom EQ", async () => await Run(() => client.SetVerifiedAsync(0xf041, Protocol.SimplePayload(gains[0], gains[1], gains[2],client.Profile), 0xc044, state => state.SimpleGains != null && state.SimpleGains.Zip(gains).All(pair => Math.Abs(pair.First-pair.Second) < .05)), "Custom EQ confirmed."), Can(0xc044)));
        }
        return;
        }
        var advanced = Card("Custom profile", "Eight bands · ±6 dB");
        advanced.Children.Add(Text(s.FlatEq==true?"Advanced EQ · Bypassed by Flat EQ":s.Advanced==true?"Advanced EQ · On":"Advanced EQ · Off",12,muted));
        if(s.Advanced==true)advanced.Children.Add(ActionButton("Turn off Advanced EQ", async () => await Run(() => client.SetVerifiedAsync(0xf04f, [0, 0], 0xc04c, state => state.Advanced == false), "Advanced EQ disabled."), Can(0xc04c)));
        var sliders = new System.Windows.Controls.Primitives.UniformGrid { Columns = 8, Margin = new Thickness(0, 14, 0, 20) };
        for (int i = 0; i < 8; i++)
        {
            int band = i;
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var value = Text($"{draft[i].Gain:+0.0;-0.0;0.0}", 12, lime); value.HorizontalAlignment = HorizontalAlignment.Center;
            var slider = new Slider { Orientation = Orientation.Vertical, Height = 140, Minimum = -6, Maximum = 6, Value = draft[i].Gain, Width = 24, ToolTip = $"Band {i+1} gain in dB" };
            System.Windows.Automation.AutomationProperties.SetName(slider, $"Band {i+1} gain in decibels");
            slider.ValueChanged += (_, _) => { draft[band] = draft[band] with { Gain = (float)slider.Value };draftDirty=true; value.Text = $"{slider.Value:+0.0;-0.0;0.0}"; };
            string f = draft[i].Frequency >= 1000 ? $"{draft[i].Frequency/1000:0.#}k" : $"{draft[i].Frequency:0}";
            var freq = Text(f, 10, muted); freq.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(value); col.Children.Add(slider); col.Children.Add(freq); sliders.Children.Add(col);
        }
        advanced.Children.Add(sliders);
        var precision = new Expander { Header = "Frequency and Q", Margin = new Thickness(0, 0, 0, 18), Foreground = Foreground };
        var precisionPanel = new StackPanel();
        precisionPanel.Children.Add(Text("Select a band to fine-tune its frequency and width. Changes apply with the profile.", 12, muted));
        var selector = new ComboBox { ItemsSource = Enumerable.Range(1, 8).Select(n => $"Band {n}"), SelectedIndex = 0, Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
        var frequency = new TextBox { Text = draft[0].Frequency.ToString(System.Globalization.CultureInfo.InvariantCulture), Width = 120 };
        var quality = new TextBox { Text = draft[0].Q.ToString(System.Globalization.CultureInfo.InvariantCulture), Width = 120 };
        System.Windows.Automation.AutomationProperties.SetName(selector, "Band to edit");
        System.Windows.Automation.AutomationProperties.SetName(frequency, "Frequency in hertz");
        System.Windows.Automation.AutomationProperties.SetName(quality, "Q factor");
        selector.SelectionChanged += (_, _) => { if (selector.SelectedIndex >= 0) { var b = draft[selector.SelectedIndex]; frequency.Text = b.Frequency.ToString(System.Globalization.CultureInfo.InvariantCulture); quality.Text = b.Q.ToString(System.Globalization.CultureInfo.InvariantCulture); } };
        precisionPanel.Children.Add(selector);
        var precisionRow = new WrapPanel();
        precisionRow.Children.Add(Text("Hz  ", 12, muted)); precisionRow.Children.Add(frequency);
        precisionRow.Children.Add(Text("Q  ", 12, muted)); precisionRow.Children.Add(quality);
        precisionPanel.Children.Add(precisionRow);
        precisionPanel.Children.Add(ActionButton("Update draft band", () => {
            if (!float.TryParse(frequency.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) || !float.TryParse(quality.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float q)) { StatusLabel.Text = "Enter a frequency and Q using decimal numbers."; return; }
            int i = selector.SelectedIndex; var edited = draft[i] with { Frequency = f, Q = q };
            if (!edited.IsValid) { StatusLabel.Text = "Frequency must be 20–20,000 Hz; Q must be 0.1–10."; return; }
            draft[i] = edited;draftDirty=true; Render(); StatusLabel.Text = "Draft band updated. Apply the profile to send it to your headphones.";
        }));
        precision.Content = precisionPanel; advanced.Children.Add(precision);
        var buttons = new WrapPanel();
        var apply = ActionButton("Apply profile", async () => await Run(ApplyDraft, "Profile updated."), Can(0xc04d) && Can(0xc04c));
        apply.Background=new SolidColorBrush(Color.FromRgb(224,35,45));buttons.Children.Add(apply);
        buttons.Children.Add(ActionButton("Reset draft", () => { draft = EqBand.Default;draftDirty=true; Render(); }));
        advanced.Children.Add(buttons);
        var save = new WrapPanel(); var name = new TextBox { Width = 205, Text = "My sound", MaxLength = 80 };
        System.Windows.Automation.AutomationProperties.SetName(name, "Profile name");
        save.Children.Add(name); save.Children.Add(ActionButton("Save profile", () => SaveProfile(name.Text)));
        advanced.Children.Add(save);
        RenderProfiles();
    }
    private async Task ApplyDraft()
    {
        EqBand[] target = draft.ToArray();
        await client.SetVerifiedAsync(0xf050, Protocol.AdvancedPayload(target), 0xc04d, s => s.Bands != null && s.Bands.Zip(target).All(pair => Math.Abs(pair.First.Gain-pair.Second.Gain) < .05 && Math.Abs(pair.First.Frequency-pair.Second.Frequency) < 1 && Math.Abs(pair.First.Q-pair.Second.Q) < .01 && pair.First.Filter == pair.Second.Filter));
        await client.SetVerifiedAsync(0xf04f, [1, 0], 0xc04c, s => s.Advanced == true);
        draftDirty=false;
    }
    private void SaveProfile(string name)
    {
        try { var p = new EqProfile(name.Trim(), draft.ToArray()); p.Validate(); profiles.RemoveAll(x => x.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase)); profiles.Add(p); ProfileStore.Save(profiles); StatusLabel.Text = "Profile saved on this PC."; Render(); }
        catch (Exception ex) { StatusLabel.Text = ex.Message; }
    }
    internal async Task ConnectForPreviewAsync()
    {
        var devices = await HeadphoneClient.DiscoverAsync();
        if (devices.Count != 1) throw new IOException("Preview requires exactly one recognized Nothing audio device.");
        await client.ConnectAsync(devices[0]); await client.RefreshAsync();
        draft = client.State.Bands?.ToArray() ?? draft; Render(); StatusLabel.Text = "Connected. Settings read directly from your headphones.";
    }
    internal void PreviewPage(string name) { page = name == "Advanced" ? "Equalizer" : name; eqTab = name == "Advanced" ? "Advanced" : "Simple"; PageScroll.ScrollToTop(); Render(); UpdateLayout(); }
    internal async Task LoadCatalogForPreviewAsync() { catalog = await PlaygroundCatalog.LoadAsync(); }
    private void RenderProfiles()
    {
        var saved = Card("Custom profiles", "Saved on this PC. Load a profile into the editor before applying it.");
        if (profiles.Count == 0) saved.Children.Add(Text("Your saved profiles will appear here.", 12, muted));
        foreach (var p in profiles.ToArray())
        {
            var row = new WrapPanel(); row.Children.Add(Text(p.Name + "   ", 14));
            row.Children.Add(ActionButton("Load", () => { draft = p.Bands.ToArray();draftDirty=true; page = "Equalizer"; eqTab="Advanced"; Render(); StatusLabel.Text = "Profile loaded. Select Apply profile to use it."; },client.Profile.HasAdvancedEq(client.State.Firmware)));
            row.Children.Add(ActionButton("Export", () => ExportProfile(p)));
            row.Children.Add(ActionButton("Delete", () => { try { var updated = profiles.Where(x => x != p).ToList(); ProfileStore.Save(updated); profiles = updated; Render(); } catch (Exception ex) { StatusLabel.Text = ex.Message; } }));
            saved.Children.Add(row);
        }
        saved.Children.Add(ActionButton("Import profile…", ImportProfile));
    }
    private void ImportProfile()
    {
        var dialog = new OpenFileDialog { Filter = "EQ profile (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { if (new FileInfo(dialog.FileName).Length > 65536) throw new InvalidDataException("This profile file is too large."); var p = JsonSerializer.Deserialize<EqProfile>(File.ReadAllText(dialog.FileName)) ?? throw new InvalidDataException("Empty profile."); p.Validate(); draft = p.Bands;draftDirty=true; SaveProfile(p.Name); }
        catch (Exception ex) { StatusLabel.Text = "Could not import profile: " + ex.Message; }
    }
    private void ExportProfile(EqProfile p)
    {
        var dialog = new SaveFileDialog { Filter = "EQ profile (*.json)|*.json", FileName = "eq-profile.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(p, new JsonSerializerOptions { WriteIndented = true })); StatusLabel.Text = "Profile exported."; }
        catch (Exception ex) { StatusLabel.Text = ex.Message; }
    }

    private void RenderControls()
    {
        foreach(var group in client.Profile.Controls(client.State.Firmware).GroupBy(c=>c.Group))
        {
            var card=Card(group.Key,"");
            foreach(var control in group)
            {
                if(control.Custom)
                    GestureEditor(card,control.Control,checked((byte)control.Type),control.Actions.Select(a=>(a.Name,a.Code)).ToArray(),control.Label,control.Device);
                else
                {
                    string detail=control.Device==4?" (phone)":control.Label=="Single press"?" · Play / pause":control.Type==2?" · Play / pause / Calls":"";
                    card.Children.Add(Text(control.Label+detail,13));
                }
            }
        }
        var note=Text("Fixed actions stay on the device. Actions marked phone require Nothing X or a supported phone.",11,muted);PageContent.Children.Add(note);
    }
    private void GestureEditor(StackPanel card, byte control, byte type, (string Name, byte Code)[] actions, string label,byte device=6)
    {
        var gesture = client.State.Gestures.FirstOrDefault(g => g.Device == device && g.Control == control && g.Type == type);
        var combo = new ComboBox { ItemsSource = actions.Select(a => a.Name), HorizontalAlignment = HorizontalAlignment.Stretch };
        int current = gesture == null ? -1 : Array.FindIndex(actions, a => a.Code == gesture.Action);
        combo.SelectedIndex = current;
        System.Windows.Automation.AutomationProperties.SetName(combo, $"Action for control {control} gesture {type}");
        var row=new Grid{Margin=new Thickness(0,6,0,0)};
        row.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});row.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var title=Text(label,13);title.VerticalAlignment=VerticalAlignment.Center;title.Margin=new Thickness(0,0,10,8);row.Children.Add(title);
        Grid.SetColumnSpan(title,2);combo.Width=double.NaN;Grid.SetRow(combo,1);row.Children.Add(combo);
        var apply=ActionButton("Apply", async () => await Run(async () => {
            if (combo.SelectedIndex < 0) throw new InvalidOperationException("Choose an action first.");
            byte action = actions[combo.SelectedIndex].Code;
            await client.SetVerifiedAsync(0xf003, [1, device, control, type, action], 0xc018, state => state.Gestures.Any(g => g.Device == device && g.Control == control && g.Type == type && g.Action == action));
        }, "Control updated."), Can(0xc018) && gesture != null);
        Grid.SetColumn(apply,1);Grid.SetRow(apply,1);row.Children.Add(apply);card.Children.Add(row);
        if(gesture!=null&&current<0)combo.ToolTip="The current custom action is preserved until you choose a replacement.";
    }
    private void RenderExplore()
    {
        var curated = Card("Nothing Playground", "Community sound profiles");
        if(!client.Profile.HasAdvancedEq(client.State.Firmware))curated.Children.Add(Text("This model cannot apply eight-band profiles. You can still browse the library.",11,muted));
        curated.Children.Add(Text("Discover a new sound. Preview a profile in Advanced EQ before applying it.",12,muted));
        curated.ToolTip="Nothing's public library may differ from the curated selection in Nothing X.";
        curated.Children.Add(ActionButton(catalog == null ? "Load online profiles ↗" : "Refresh online profiles", async () => await Run(async () => { catalog = await PlaygroundCatalog.LoadAsync(); }, "Nothing Playground profiles loaded.")));
        curated.Children.Add(ActionButton("Browse Nothing Playground ↗", () => Process.Start(new ProcessStartInfo("https://playground.nothing.tech/eqs") { UseShellExecute = true })));
        if (catalog != null)
        {
            var filter = new CheckBox { Content = "Show only profiles tagged for "+client.Profile.ShortName, IsChecked = modelProfilesOnly, Foreground = Foreground, Margin = new Thickness(0, 10, 0, 16) };
            filter.Checked += (_, _) => { modelProfilesOnly = true; Render(); }; filter.Unchecked += (_, _) => { modelProfilesOnly = false; Render(); };
            curated.Children.Add(filter);
            var visible = catalog.Where(p => DeviceCatalog.Detect(p.Model)?.Id==client.Profile.Id||!modelProfilesOnly&&p.Model.Length==0).ToArray();
            curated.Children.Add(Text($"{visible.Length} profiles · explicitly incompatible device profiles are excluded", 12, muted));
            foreach (var profile in visible)
            {
                var entry = new StackPanel { Margin = new Thickness(0, 8, 0, 14) };
                entry.Children.Add(Text(profile.Name, 17));
                entry.Children.Add(Text($"By {profile.Creator} · {(profile.Model.Length > 0 ? profile.Model : "Target device not specified by creator")}", 12, muted));
                var actions = new WrapPanel();
                actions.Children.Add(ActionButton("Load profile", () => { draft = profile.Bands.ToArray();draftDirty=true; page = "Equalizer"; eqTab="Advanced"; PageScroll.ScrollToTop(); Render(); StatusLabel.Text = "Profile loaded. Select Apply profile to use it."; },client.Profile.HasAdvancedEq(client.State.Firmware)));
                actions.Children.Add(ActionButton("View source ↗", () => Process.Start(new ProcessStartInfo(profile.Url) { UseShellExecute = true })));
                entry.Children.Add(actions); curated.Children.Add(entry);
            }
        }
        RenderProfiles();
    }
    private void RenderConnection()
    {
        var app=Card("Application","");
        var row=new Grid{Margin=new Thickness(0,3,0,3)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var details=new StackPanel{Margin=new Thickness(0,0,18,0)};
        var caption=Text("BACKGROUND BEHAVIOR",9,muted);caption.Margin=new Thickness(0,0,0,9);details.Children.Add(caption);
        var title=Text("Closing app minimizes to tray",14);title.Margin=new Thickness(0,0,0,7);details.Children.Add(title);
        var hint=Text("Keep headphone controls ready. Use Exit in the tray menu to quit.",11,muted);hint.Margin=new Thickness(0);details.Children.Add(hint);row.Children.Add(details);
        var closeToTray=new System.Windows.Controls.Primitives.ToggleButton{Name="CloseToTrayToggle",Style=(Style)FindResource("GlassSwitch"),IsChecked=preferences.CloseToTray,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(closeToTray,1);row.Children.Add(closeToTray);
        System.Windows.Automation.AutomationProperties.SetName(closeToTray,"Closing app minimizes to tray");
        closeToTray.Click+=(_,_)=>{try{var next=preferences with{CloseToTray=closeToTray.IsChecked==true};preferencesStore.Save(next);preferences=next;UpdateHeader();StatusLabel.Text="App preference saved.";}catch(Exception ex){closeToTray.IsChecked=preferences.CloseToTray;StatusLabel.Text=ex.Message;}};
        app.Children.Add(row);
        var startupRow=new Grid{Margin=new Thickness(0,20,0,3)};
        startupRow.ColumnDefinitions.Add(new ColumnDefinition());startupRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var startupDetails=new StackPanel{Margin=new Thickness(0,0,18,0)};
        var startupTitle=Text("Start with Windows",14);startupTitle.Margin=new Thickness(0,0,0,7);startupDetails.Children.Add(startupTitle);
        var startupHint=Text("Open quietly in the tray when you sign in. Enable again after moving the app folder.",11,muted);startupHint.Margin=new Thickness(0);startupDetails.Children.Add(startupHint);startupRow.Children.Add(startupDetails);
        bool startupEnabled=false;string? startupError=null;
        try{startupEnabled=startupRegistration.Enabled;}catch(Exception ex){startupError=ex.Message;}
        var startWithWindows=new System.Windows.Controls.Primitives.ToggleButton{Name="StartWithWindowsToggle",Style=(Style)FindResource("GlassSwitch"),IsChecked=startupEnabled,VerticalAlignment=VerticalAlignment.Center,IsEnabled=startupError==null,ToolTip=startupError};
        System.Windows.Automation.AutomationProperties.SetName(startWithWindows,"Start with Windows");Grid.SetColumn(startWithWindows,1);startupRow.Children.Add(startWithWindows);
        startWithWindows.Click+=(_,_)=>
        {
            bool requested=startWithWindows.IsChecked==true;
            try{startupRegistration.SetEnabled(requested);startupEnabled=startupRegistration.Enabled;startWithWindows.IsChecked=startupEnabled;StatusLabel.Text=startupEnabled?"The app will start in the tray when you sign in.":"Start with Windows is off.";}
            catch(Exception ex){startWithWindows.IsChecked=startupEnabled;StatusLabel.Text="Could not change startup settings: "+ex.Message;}
        };
        app.Children.Add(startupRow);
        RenderUpdates();
        var deviceProfile=Card("Device profile","Recognized models are selected automatically when you connect. You can preview any model while disconnected.");
        var models=new ComboBox{Name="DeviceProfileSelector",ItemsSource=DeviceCatalog.All,SelectedItem=client.Profile,IsEnabled=!client.Connected&&!busy,HorizontalAlignment=HorizontalAlignment.Stretch};
        System.Windows.Automation.AutomationProperties.SetName(models,"Device profile");
        models.SelectionChanged+=(_,_)=>{if(models.SelectedItem is DeviceProfile selected&&selected.Id!=client.Profile.Id){client.SelectProfile(selected);draft=EqBand.Default;draftDirty=false;eqTab="Simple";StatusLabel.Text="Previewing "+selected.ShortName+". Connect to read actual settings.";}};
        deviceProfile.Children.Add(models);
        deviceProfile.Children.Add(Text(client.Profile.Verified?"Headphone (a) controls have been hardware-tested on firmware 1.0.1.51.":"Experimental support. Connection and individual features still need testing on this model and firmware.",11,muted));
        if(client.Profile.Notes.Length>0)deviceProfile.Children.Add(Text(client.Profile.Notes,11,muted));
        var setup = Card("Connection", "");
        var connectionActions=new WrapPanel();connectionActions.Children.Add(ActionButton(client.Connected?"Disconnect":"Connect",()=>Connect_Click(this,new RoutedEventArgs()),!busy));connectionActions.Children.Add(ActionButton("Refresh",()=>Refresh_Click(this,new RoutedEventArgs()),client.Connected&&!busy));setup.Children.Add(connectionActions);
        setup.Children.Add(Text("Connected Nothing devices are picked up automatically. With multiple devices, the last one you selected is preferred. Use Connect to choose a device or retry.\n\nPair in Windows first, then power on your device or take the earbuds out of their case. If connecting fails, close Nothing X on your phone.", 14));
        setup.Children.Add(ActionButton("Connect a renamed device…",async()=>await ConnectRenamedAsync(),!client.Connected&&!busy));
        setup.Children.Add(ActionButton("Open Bluetooth settings ↗", () => Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true })));
        var info = Card("About", "");
        info.Children.Add(Text($"Model: {client.Profile.Name}\nFirmware: {client.State.Firmware}\nFeatures read successfully: {client.State.Readable.Count} / {client.Profile.ReadCommands(client.State.Firmware).Length}", 14));
        info.Children.Add(ActionButton("Save connection report…", SaveReport));
        info.Children.Add(Text("Reports include firmware, settings, and Bluetooth command responses. Review before sharing.", 12, muted));
    }
    private void SaveReport()
    {
        var dialog = new SaveFileDialog { FileName = "nothing-connection-report.json", Filter = "JSON report|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { string[] entries; lock(log) entries = log.ToArray(); File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(new { connected = client.Connected, state = client.State, log = entries }, new JsonSerializerOptions { WriteIndented = true })); StatusLabel.Text = "Connection report saved."; }
        catch (Exception ex) { StatusLabel.Text = ex.Message; }
    }
    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if(busy)return;
        if (client.Connected) { automaticConnection?.Suspend();client.Dispose(); Render(); StatusLabel.Text = "Disconnected. Automatic connection is paused until you reconnect or restart the app."; return; }
        await Run(async () => {
            var devices = await HeadphoneClient.DiscoverAsync();
            if (devices.Count == 0) throw new IOException("No recognized Nothing audio device found. Pair your device in Windows Bluetooth settings, or use Connect a renamed device in Settings.");
            PairedHeadphone selected = devices[0];
            if (devices.Count > 1)
            {
                var chooser = new Window { Title = "Choose Nothing audio device", Width = 430, Height = 240, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,Background=Background,Foreground=Foreground,Resources=Resources };
                var panel = new StackPanel { Margin = new Thickness(24) }; var list = new ComboBox { ItemsSource = devices, SelectedIndex = 0 };
                panel.Children.Add(new TextBlock { Text = "Select the paired headphones to control." }); panel.Children.Add(list);
                var confirm = new Button { Content = "Connect" }; confirm.Click += (_, _) => { chooser.DialogResult = true; }; panel.Children.Add(confirm); chooser.Content = panel;
                if (chooser.ShowDialog() != true) return;
                selected = (PairedHeadphone)list.SelectedItem;
            }
            await ConnectDeviceAsync(selected);
        }, "Connected. Available settings have been read from your headphones.");
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await Run(() => client.RefreshAsync(), "Device settings refreshed.");
    private async Task Run(Func<Task> work, string success, bool render = true,string progress="Updating…")
    {
        if (busy) return;
        if(windowClosed)return;
        busy = true; StatusLabel.Text = progress; UpdateHeader(); PageContent.IsEnabled = false;
        try { await work(); StatusLabel.Text = success; }
        catch (Exception ex) { StatusLabel.Text = ex is TimeoutException ? "The headphones did not respond. Refresh or reconnect and try again." : ex.Message; }
        finally { busy = false; if(!windowClosed){PageContent.IsEnabled = true; if (render) Render(); else UpdateHeader();} }
    }
}
