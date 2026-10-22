using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
namespace NormalKeyboardSwitcher2;
internal sealed record AccessibleFocus(nint Window,int ObjectId,int ChildId,nint Foreground,uint Kind=0);
internal sealed class AccessibilityEvents : IDisposable
{
    private delegate void Callback(nint hook,uint kind,nint window,int objectId,int childId,uint thread,uint time);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint first,uint last,nint module,Callback callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    private readonly Callback callback;
    private nint focusHook,caretHook;
    private AccessibleFocus? focus;
    internal AccessibilityEvents(Action wake)
    {
        callback=(_,kind,window,obj,child,_,_)=>
        {
            if(window==0) return;
            var foreground=Native.GetForegroundWindow();
            Native.GetWindowThreadProcessId(window,out uint source);
            Native.GetWindowThreadProcessId(foreground,out uint active);
            if(source!=active) return;
            var target=new AccessibleFocus(window,obj,child,foreground,kind);
            Volatile.Write(ref focus,target);
            wake();
        };
        focusHook=SetWinEventHook(0x8005,0x8005,0,callback,0,0,2);
        caretHook=SetWinEventHook(0x11B,0x11B,0,callback,0,0,2);
        if(focusHook==0 || caretHook==0)
        {
            Dispose();
            throw new Win32Exception("Could not register caret accessibility events.");
        }
    }
    internal AccessibleFocus? Read(nint foreground)
    {
        var value=Volatile.Read(ref focus);
        return value?.Foreground==foreground ? value:null;
    }
    public void Dispose()
    {
        if(focusHook!=0) UnhookWinEvent(focusHook);
        if(caretHook!=0) UnhookWinEvent(caretHook);
        focusHook=caretHook=0;
        Volatile.Write(ref focus,null);
    }
}
