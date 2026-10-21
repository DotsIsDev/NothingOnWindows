using System.IO;
using System.Text.Json;
using System.Windows;
using NothingOnWindows.Core;
using NothingOnWindows.UI;

namespace NothingOnWindows;

public sealed class App : Application
{
    [STAThread]
    public static void Main(string[] args)
    {
        var app = new App();
        if(args.Contains("--ui-smoke"))
        {
            app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            string output=Path.GetFullPath(args.SkipWhile(a=>a!="--ui-smoke").Skip(1).FirstOrDefault()??"artifacts/ui-smoke.json");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            string preferencePath=output+".settings.json";
            new PreferencesStore(preferencePath).Save(new AppPreferences());
            var window=new MainWindow(preferencePath,new SmokeStartupRegistration());
            window.Loaded+=async(_,_)=>{
                try{await window.RunUiSmokeAsync(output);app.Shutdown(0);}
                catch(Exception ex){File.WriteAllText(output,JsonSerializer.Serialize(new{error=ex.ToString()},new JsonSerializerOptions{WriteIndented=true}));window.ExitApplication();app.Shutdown(1);}
            };
            app.Run(window);return;
        }
        if (args.Contains("--snapshot"))
        {
            var window = new MainWindow();
            window.Loaded += async (_, _) =>
            {
                string output = Path.GetFullPath(args.SkipWhile(a => a != "--snapshot").Skip(1).FirstOrDefault() ?? "artifacts/app-preview.png");
                try
                {
                    if (args.Contains("--live")) await window.ConnectForPreviewAsync();
                    if (args.Contains("--auto"))
                    {
                        await window.StartAutomaticConnection();
                        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                        File.WriteAllText(output+".connection.json",JsonSerializer.Serialize(new{connected=window.AutomaticPreviewConnected,readOnly=true,status=window.AutomaticPreviewStatus,details=window.AutomaticPreviewDetails},new JsonSerializerOptions{WriteIndented=true}));
                    }
                    if (args.Contains("--catalog")) await window.LoadCatalogForPreviewAsync();
                    foreach (string page in new[] { "Sound", "Equalizer", "Advanced", "Controls", "Explore", "Connection" })
                    {
                        window.PreviewPage(page);
                        await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                        bitmap.Render(window);
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        string destination = Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + "-" + page.ToLowerInvariant() + ".png");
                        Directory.CreateDirectory(Path.GetDirectoryName(output)!); using (var stream = File.Create(destination)) encoder.Save(stream);
                    }
                }
                catch (Exception ex) { Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.WriteAllText(output + ".error.txt", ex.ToString()); }
                finally { window.Close(); }
            };
            app.Run(window); return;
        }
        if (args.Contains("--probe") || args.Contains("--verify") || args.Contains("--verify-simple"))
        {
            app.Startup += async (_, _) =>
            {
                string mode = args.Contains("--verify-simple") ? "--verify-simple" : args.Contains("--verify") ? "--verify" : "--probe";
                string output = Path.GetFullPath(args.SkipWhile(a => a != mode).Skip(1).FirstOrDefault() ?? "artifacts/probe.json");
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                var log = new List<string>();
                using var client = new HeadphoneClient();
                client.Log += s => { lock (log) log.Add(s); };
                try
                {
                    var devices = await HeadphoneClient.DiscoverAsync();
                    if (devices.Count != 1) throw new IOException($"Found {devices.Count} recognized Nothing audio devices. Use the app to choose a device.");
                    if(mode!="--probe"&&devices[0].Profile.Id!="B186")throw new InvalidOperationException("The write/restore hardware test is verified for Headphone (a) only. Use a read-only probe for other models.");
                    await client.ConnectAsync(devices[0]); await client.RefreshAsync();
                    if (mode == "--verify") await HardwareVerification.RunAsync(client);
                    if (mode == "--verify-simple") await SimpleEqVerification.RunAsync(client, devices[0], output);
                    await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { devices = devices.Select(d => d.Name), state = client.State, log }, new JsonSerializerOptions { WriteIndented = true }));
                    app.Shutdown(0);
                }
                catch (Exception ex) { await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { error = ex.ToString(), log }, new JsonSerializerOptions { WriteIndented = true })); app.Shutdown(1); }
            };
            app.Run(); return;
        }
        using var instance=new SingleInstanceSession();
        if(!instance.IsPrimary){instance.SignalPrimary();return;}
        var main=new MainWindow();
        bool windowsStartup=args.Contains("--startup");
        main.ShowActivated=!windowsStartup;
        app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        main.Closed+=(_,_)=>app.Shutdown();
        main.Loaded+=async(_,_)=>{
            try{main.InitializeTray();}
            catch(Exception ex){System.Windows.MessageBox.Show(main,"The tray icon is unavailable. Closing the window will exit the app.\n\n"+ex.Message,"Nothing on Windows");}
            if(windowsStartup)main.HideForWindowsStartup();
            await main.StartAutomaticConnection();
        };
        instance.Listen(()=>app.Dispatcher.BeginInvoke(main.ShowFromTray));
        app.Run(main);
    }
}
