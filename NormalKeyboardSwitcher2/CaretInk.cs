using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace NormalKeyboardSwitcher2;
internal readonly record struct InkSample(Color Color,bool? Visible);
internal sealed class CaretInk
{
    private uint? lastInk;
    private nint lastWindow;
    private bool sawStroke;
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window,nint dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc,int x,int y);

    internal InkSample Read(PixelRect caret,PixelRect? overlay=null)
    {
        nint window=Native.GetForegroundWindow();
        if(window!=lastWindow) { lastWindow=window; lastInk=null; sawStroke=false; }

        try
        {
            var leftBackground=new List<uint>();
            var rightBackground=new List<uint>();
            var columns=new List<uint[]>();
            int x=(int)Math.Round(caret.X), y=(int)Math.Round(caret.Y);
            int width=Math.Max(1,(int)Math.Round(caret.Width));
            int height=Math.Max(1,(int)Math.Ceiling(caret.Height));
            using var bitmap=new System.Drawing.Bitmap(width+9,height+1);
            using(var graphics=System.Drawing.Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(x-4,y,0,0,bitmap.Size,System.Drawing.CopyPixelOperation.SourceCopy);
            uint Pixel(int px,int py)
            {
                if(overlay is { } excluded && CaretGeometry.Contains(excluded,px,py)) return uint.MaxValue;
                var pixel=bitmap.GetPixel(px-(x-4),py-y);
                return (uint)(pixel.R | pixel.G<<8 | pixel.B<<16);
            }
            for(int col=0;col<Math.Min(width+1,4);col++)
            {
                var pixels=new List<uint>();
                for(int row=0;row<height;row++)
                {
                    int yy=y+row;
                    uint value=Pixel(x+col,yy);
                    if(value!=uint.MaxValue) pixels.Add(value);
                    if(col==0)
                    {
                        uint left=Pixel(x-4,yy),right=Pixel(x+width+4,yy);
                        if(left!=uint.MaxValue) leftBackground.Add(left);
                        if(right!=uint.MaxValue) rightBackground.Add(right);
                    }
                }
                // A partially occluded column cannot establish a full-height
                // stroke: otherwise the remaining text pixels gain extra weight.
                if(pixels.Count==height) columns.Add(pixels.ToArray());
            }
            var samples=columns.ToArray();
            var surrounding=CaretInkPolicy.Background(leftBackground.ToArray(),rightBackground.ToArray());
            bool stroke=CaretInkPolicy.HasStroke(samples,surrounding);
            sawStroke |= stroke;
            uint ink=CaretInkPolicy.Choose(samples,surrounding,lastInk);
            if(stroke) lastInk=ink;
            return new(Color.FromRgb((byte)ink,(byte)(ink>>8),(byte)(ink>>16)),sawStroke ? stroke : null);
        }
        finally { }
    }
}



