using System.Windows;
using System.Windows.Media;
namespace NothingOnWindows.UI;
public sealed class ModeIcon(string kind, Brush foreground) : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        dc.PushTransform(new ScaleTransform(ActualWidth/28,ActualHeight/28));
        var pen=new Pen(foreground,1.35){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
        if(kind=="Off") {dc.DrawLine(pen,new Point(6,6),new Point(22,22));dc.DrawGeometry(null,pen,Geometry.Parse("M 8,4 A 11,11 0 0 1 24,19 M 20,24 A 11,11 0 0 1 4,9"));}
        else if(kind=="Noise cancellation"||kind=="On") {dc.DrawGeometry(null,pen,Geometry.Parse("M 7,20 A 9,9 0 1 1 21,20 M 10,17 A 5,5 0 1 1 18,17"));dc.DrawEllipse(foreground,null,new Point(14,15),1.5,1.5);}
        else if(kind=="Transparency") {dc.DrawEllipse(null,pen,new Point(14,14),4,4);for(int i=0;i<8;i++){double a=i*Math.PI/4;dc.DrawLine(pen,new Point(14+8*Math.Cos(a),14+8*Math.Sin(a)),new Point(14+10*Math.Cos(a),14+10*Math.Sin(a)));}}
        else if(kind.Contains("Concert")){foreach(int i in new[]{0,1,2,3,4})dc.DrawLine(pen,new Point(6+i*4,14-(i%2==0?4:8)),new Point(6+i*4,14+(i%2==0?4:8)));}
        else {dc.DrawRoundedRectangle(null,pen,new Rect(5,6,18,14),2,2);dc.DrawLine(pen,new Point(10,24),new Point(18,24));dc.DrawEllipse(foreground,null,new Point(14,13),2,2);}
        dc.Pop();
    }
}
