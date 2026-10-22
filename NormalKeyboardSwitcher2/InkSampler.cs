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
        Volatile.Write(ref request,new(window,caret,overlay));
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
                    // Publish a readable background fallback even when the caret
                    // is blinking off or only exposes placeholder geometry.
                    if(sample.Reliable && Native.GetForegroundWindow()==target.Window &&
                        target==Volatile.Read(ref request))
                        Volatile.Write(ref result,new(target.Window,sample.Color));
                }
                catch(Exception ex) when(ex is System.Runtime.InteropServices.ExternalException or ArgumentException) { }
            }
            Thread.Sleep(60);
        }
    }
    public void Dispose() => stopped=true;
}

