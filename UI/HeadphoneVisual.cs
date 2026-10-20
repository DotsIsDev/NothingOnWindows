using System.Windows;
using System.Windows.Media;
namespace NothingOnWindows.UI;
// Original vector illustration. No vendor artwork is bundled.
public sealed class HeadphoneVisual : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        dc.PushTransform(new ScaleTransform(ActualWidth/230,ActualHeight/267));
        var band=Geometry.Parse("M 40,174 L 40,103 C 40,16 190,16 190,103 L 190,174");
        dc.DrawGeometry(null,new Pen(new SolidColorBrush(Color.FromRgb(48,48,48)),23),band);
        dc.DrawGeometry(null,new Pen(new LinearGradientBrush(Color.FromRgb(231,231,231),Color.FromRgb(92,92,92),0),14),band);
        dc.DrawGeometry(null,new Pen(new SolidColorBrush(Color.FromRgb(22,22,22)),7),Geometry.Parse("M 49,136 L 49,104 C 49,32 181,32 181,104 L 181,136"));
        foreach(bool right in new[]{false,true}) {
            double x=right?164:10; dc.PushTransform(new RotateTransform(right?-13:13,x+30,192));
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(16,16,16)),new Pen(new SolidColorBrush(Color.FromRgb(70,70,70)),1),new Rect(x,143,52,99),24,24);
            dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromRgb(42,42,42),Color.FromRgb(9,9,9),0),null,new Rect(x+7,147,41,91),19,19);
            dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromRgb(203,203,203),Color.FromRgb(80,80,80),0),new Pen(new SolidColorBrush(Color.FromRgb(167,167,167)),.8),new Rect(x+(right?19:-1),138,34,108),15,15);
            dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromRgb(67,67,67),Color.FromRgb(23,23,23),0),null,new Rect(x+(right?24:4),145,24,94),11,11);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(95,95,95)),new Pen(new SolidColorBrush(Color.FromRgb(130,130,130)),.8),new Point(x+(right?36:16),190),9,30);
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(177,177,177)),2),new Point(x+26,141),new Point(x+26,123)); dc.Pop();
        }
        dc.Pop();
    }
}
