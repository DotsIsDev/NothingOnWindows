using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace NothingOnWindows.UI;

public sealed class GlassPanel : Grid
{
    private readonly VisualBrush sample;
    private readonly FrameworkElement backdrop;
    private readonly RectangleGeometry clip=new(){RadiusX=18,RadiusY=18};
    public GlassPanel(FrameworkElement backdrop,UIElement content,Thickness padding)
    {
        this.backdrop=backdrop;
        sample=new VisualBrush(backdrop){ViewboxUnits=BrushMappingMode.Absolute,Stretch=Stretch.Fill};
        var surface=new Grid{Clip=clip,IsHitTestVisible=false};
        surface.Children.Add(new Border{Background=sample,Effect=new BlurEffect{Radius=20,RenderingBias=RenderingBias.Performance}});
        surface.Children.Add(new Border{Background=new LinearGradientBrush(Color.FromArgb(30,235,235,235),Color.FromArgb(45,12,12,12),new Point(0,0),new Point(.8,1))});
        Children.Add(surface);
        Children.Add(new Border{CornerRadius=new CornerRadius(18),BorderThickness=new Thickness(1),BorderBrush=new LinearGradientBrush(Color.FromArgb(78,235,235,235),Color.FromArgb(18,160,160,160),new Point(0,0),new Point(.8,1)),Background=Brushes.Transparent,IsHitTestVisible=false});
        Children.Add(new Border{Padding=padding,Child=content});
        Effect=new DropShadowEffect{Color=Colors.Black,BlurRadius=18,ShadowDepth=7,Opacity=.24};
        Loaded+=(_,_)=>UpdateSample();SizeChanged+=(_,_)=>UpdateSample();LayoutUpdated+=(_,_)=>UpdateSample();
    }
    private void UpdateSample()
    {
        if(!IsLoaded||ActualWidth<=0||ActualHeight<=0)return;
        var clipBounds=new Rect(0,0,ActualWidth,ActualHeight);if(clip.Rect!=clipBounds)clip.Rect=clipBounds;
        try{var origin=TransformToVisual(backdrop).Transform(new Point());var bounds=new Rect(origin,new Size(ActualWidth,ActualHeight));if(sample.Viewbox!=bounds)sample.Viewbox=bounds;}
        catch(InvalidOperationException){}
    }
}
