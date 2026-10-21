using System;
using System.Threading;
using System.Windows.Media;

namespace NormalKeyboardSwitcher2;
internal sealed record InkRequest(nint Window,PixelRect Caret,PixelRect Overlay);
internal sealed record CachedInk(nint Window,Color Color);
internal sealed class InkSampler : IDisposable
{
    private InkRequest? request;
    private CachedInk? result;
    private volatile bool stopped;
    private readonly Thread thread;
    internal InkSampler()
    {
        thread=new Thread(Work) { IsBackground=true,Name="Caret color sampling" };
        thread.Start();
    }
    internal Color Read(PixelRect caret,PixelRect overlay)
    {
        nint window=Native.GetForegroundWindow();
        // A provider's one-pixel placeholder is not a text caret. Sampling it
        // mistakes grid lines or our own nearby label for a blinking stroke.
        Volatile.Write(ref request,caret.Height>=4 ? new(window,caret,overlay) : null);
        var cached=Volatile.Read(ref result);
        return cached?.Window==window ? cached.Color : Colors.Black;
    }
    private void Work()
    {
        var ink=new CaretInk();
        while(!stopped)
        {
            var target=Volatile.Read(ref request);
            if(target!=null)
            {
                try
                {
                    var sample=ink.Read(target.Caret,target.Overlay);
                    if(sample.Visible==true && Native.GetForegroundWindow()==target.Window)
                        Volatile.Write(ref result,new(target.Window,sample.Color));
                }
                catch(Exception ex) when(ex is System.Runtime.InteropServices.ExternalException or ArgumentException) { }
            }
            Thread.Sleep(60);
        }
    }
    public void Dispose() => stopped=true;
}

