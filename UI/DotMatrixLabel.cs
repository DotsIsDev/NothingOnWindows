using System.Windows;
using System.Windows.Media;
namespace NothingOnWindows.UI;
public sealed class DotMatrixLabel : FrameworkElement
{
    public string Text { get; set; } = "NOTHING";
    protected override void OnRender(DrawingContext dc)
    {
        var glyphs = new Dictionary<char, string[]> {
            ['N']=["10001","11001","10101","10011","10001","10001","10001"], ['O']=["01110","10001","10001","10001","10001","10001","01110"], ['T']=["11111","00100","00100","00100","00100","00100","00100"], ['H']=["10001","10001","10001","11111","10001","10001","10001"], ['I']=["111","010","010","010","010","010","111"], ['G']=["01110","10001","10000","10111","10001","10001","01110"] };
        double units = Text.Sum(c => glyphs.TryGetValue(c, out var glyph) ? glyph[0].Length + 1 : 4);
        double step = Math.Min(ActualWidth / units, ActualHeight / 7), x = 0;
        foreach (char c in Text) { if (!glyphs.TryGetValue(c, out var glyph)) { x += 4*step; continue; } for(int y=0;y<glyph.Length;y++) for(int col=0;col<glyph[y].Length;col++) if(glyph[y][col]=='1') dc.DrawEllipse(Brushes.White,null,new Point(x+col*step+step/2,y*step+step/2),step*.30,step*.30); x+=(glyph[0].Length+1)*step; }
    }
}
