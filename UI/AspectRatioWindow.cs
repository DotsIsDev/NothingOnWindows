using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using NothingOnWindows.Core;

namespace NothingOnWindows.UI;

public sealed class AspectRatioWindow : IDisposable
{
    private readonly Window window;
    private HwndSource? source;
    private bool adjusting;
    private int startWidth,startHeight;
    public AspectRatioWindow(Window window)
    {
        this.window=window;
        window.SourceInitialized+=SourceInitialized;
        window.SizeChanged+=SizeChanged;
        window.StateChanged+=StateChanged;
    }
    private void SourceInitialized(object? sender,EventArgs e)
    {
        source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);source?.AddHook(Hook);
    }
    private IntPtr Hook(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        var dpi=VisualTreeHelper.GetDpi(window);
        if(message==0x0231){startWidth=(int)Math.Round(window.ActualWidth*dpi.DpiScaleX);startHeight=(int)Math.Round(window.ActualHeight*dpi.DpiScaleY);}
        if(message==0x0112&&((long)wParam&0xfff0)==0xf030){handled=true;return IntPtr.Zero;}
        if(message==0x00a3&&(long)wParam==2){handled=true;return IntPtr.Zero;}
        if(message==0x0214)
        {
            var r=Marshal.PtrToStructure<NativeRect>(lParam);
            var next=AspectRatioSizing.Constrain(new(r.Left,r.Top,r.Right,r.Bottom),(int)wParam,AspectRatioSizing.PortraitRatio*dpi.DpiScaleX/dpi.DpiScaleY,
                (int)Math.Ceiling(window.MinWidth*dpi.DpiScaleX),(int)Math.Ceiling(window.MinHeight*dpi.DpiScaleY),startWidth,startHeight,(int)Math.Floor(window.MaxHeight*dpi.DpiScaleY));
            Marshal.StructureToPtr(new NativeRect{Left=next.Left,Top=next.Top,Right=next.Right,Bottom=next.Bottom},lParam,false);handled=true;return new IntPtr(1);
        }
        return IntPtr.Zero;
    }
    private void SizeChanged(object sender,SizeChangedEventArgs e)
    {
        if(adjusting||window.WindowState!=WindowState.Normal||e.NewSize.Height<=0||Math.Abs(e.NewSize.Width/e.NewSize.Height-AspectRatioSizing.PortraitRatio)<.001)return;
        double height=e.WidthChanged&&!e.HeightChanged?e.NewSize.Width/AspectRatioSizing.PortraitRatio:e.NewSize.Height;
        if(e.WidthChanged&&e.HeightChanged)height=Math.Min(e.NewSize.Height,e.NewSize.Width/AspectRatioSizing.PortraitRatio);
        height=Math.Clamp(height,window.MinHeight,window.MaxHeight);
        adjusting=true;
        try{window.SetCurrentValue(Window.HeightProperty,height);window.SetCurrentValue(Window.WidthProperty,height*AspectRatioSizing.PortraitRatio);}
        finally{adjusting=false;}
    }
    private void StateChanged(object? sender,EventArgs e)
    {
        // WPF updates WindowState before later native message hooks run.
        if(window.WindowState==WindowState.Maximized)window.SetCurrentValue(Window.WindowStateProperty,WindowState.Normal);
    }
    public void Dispose(){window.SourceInitialized-=SourceInitialized;window.SizeChanged-=SizeChanged;window.StateChanged-=StateChanged;source?.RemoveHook(Hook);}
    [StructLayout(LayoutKind.Sequential)]private struct NativeRect{public int Left,Top,Right,Bottom;}
}
