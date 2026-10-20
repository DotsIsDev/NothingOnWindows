namespace NothingOnWindows.Core;

public readonly record struct SizingBounds(int Left,int Top,int Right,int Bottom)
{
    public int Width=>Right-Left;
    public int Height=>Bottom-Top;
}

public static class AspectRatioSizing
{
    public const double PortraitRatio=440.0/800;
    public static SizingBounds Constrain(SizingBounds proposed,int edge,double ratio,int minWidth,int minHeight,int startWidth,int startHeight,int maxHeight=int.MaxValue)
    {
        if(ratio<=0||!double.IsFinite(ratio))throw new ArgumentOutOfRangeException(nameof(ratio));
        bool horizontal=edge is 1 or 2;
        if(edge is 4 or 5 or 7 or 8)
            horizontal=Math.Abs(proposed.Width-startWidth)/(double)Math.Max(1,startWidth)>=Math.Abs(proposed.Height-startHeight)/(double)Math.Max(1,startHeight);
        double height=horizontal?proposed.Width/ratio:proposed.Height;
        height=Math.Clamp(height,Math.Max(minHeight,minWidth/ratio),Math.Max(maxHeight,Math.Max(minHeight,minWidth/ratio)));
        int h=(int)Math.Round(height),w=(int)Math.Round(h*ratio);
        int left=edge is 1 or 4 or 7?proposed.Right-w:proposed.Left;
        int top=edge is 3 or 4 or 5?proposed.Bottom-h:proposed.Top;
        return new SizingBounds(left,top,left+w,top+h);
    }
}
