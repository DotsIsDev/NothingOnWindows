using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace NothingOnWindows.UI;

public sealed class SimpleEqDial(float[] gains, bool presetPreview = false) : FrameworkElement
{
    private static readonly Vector[] axes = [new(-.866,.5),new(0,-1),new(.866,.5)];
    private int selected=1;
    private bool dragging;
    public event Action? GainsChanged;
    private Point Center => new(ActualWidth/2,ActualHeight/2);
    private double Radius => Math.Min(ActualWidth,ActualHeight)*.38;
    private Point Handle(int i) => Center+axes[i]*(Radius*(.57+gains[i]/6*.29));
    protected override void OnRender(DrawingContext dc)
    {
        double r=Radius; var red=new SolidColorBrush(Color.FromRgb(235,38,46)); bool active=IsEnabled||presetPreview;
        var glass=new LinearGradientBrush(Color.FromArgb(58,155,155,155),Color.FromArgb(65,12,12,12),new Point(0,0),new Point(.8,1));
        var edge=new LinearGradientBrush(Color.FromArgb(105,225,225,225),Color.FromArgb(28,125,125,125),new Point(0,0),new Point(1,1));
        dc.DrawEllipse(glass,new Pen(edge,1),Center,r,r);
        dc.DrawEllipse(null,new Pen(new SolidColorBrush(Color.FromArgb(18,240,240,240)),1),Center,r-5,r-5);
        foreach(var axis in axes)dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(85,175,175,175)),1),Center+axis*r*.2,Center+axis*r*.91);
        var shape=new StreamGeometry();using(var context=shape.Open()){context.BeginFigure(Handle(0),true,true);context.LineTo(Handle(1),true,false);context.LineTo(Handle(2),true,false);}
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(active?(byte)48:(byte)12,235,38,46)),new Pen(active?red:new SolidColorBrush(Color.FromRgb(88,88,88)),1),shape);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(22,22,22)),new Pen(new SolidColorBrush(Color.FromArgb(80,220,220,220)),1),Center,4,4);
        for(int i=0;i<3;i++)
        {
            Brush color=active&&i==selected?red:Brushes.White;
            dc.DrawEllipse(color,null,Handle(i),5,5);
            string label=new[]{"Bass","Mid","Treble"}[i]+(IsEnabled?$" {gains[i]:+0.#;-0.#;0}":"");
            var text=new FormattedText(label,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,IsEnabled&&i==selected?red:new SolidColorBrush(Color.FromRgb(170,170,170)),VisualTreeHelper.GetDpi(this).PixelsPerDip);
            Point p=Center+axes[i]*(r+16);dc.DrawText(text,new Point(p.X-text.Width/2,p.Y-text.Height/2));
        }
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if(!IsEnabled)return;Point p=e.GetPosition(this);
        int i=Enumerable.Range(0,3).OrderBy(n=>(Handle(n)-p).Length).First();
        if((Handle(i)-p).Length>25)return;
        selected=i;dragging=true;CaptureMouse();InvalidateVisual();e.Handled=true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if(!dragging)return;Vector offset=e.GetPosition(this)-Center;
        double position=Vector.Multiply(offset,axes[selected])/Radius;
        gains[selected]=(float)(Math.Round(Math.Clamp((position-.57)/.29*6,-6,6)*2)/2);
        InvalidateVisual();GainsChanged?.Invoke();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e){if(dragging){dragging=false;ReleaseMouseCapture();e.Handled=true;}}
    protected override void OnLostMouseCapture(MouseEventArgs e){dragging=false;base.OnLostMouseCapture(e);}
}
