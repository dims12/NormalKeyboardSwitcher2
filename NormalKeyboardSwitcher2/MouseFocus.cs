using System;
using System.Runtime.InteropServices;
using System.Threading;
namespace NormalKeyboardSwitcher2;
internal sealed record ClickedEditor(nint Foreground,Native.Point Point);
internal sealed class MouseFocus : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct MouseData
    { public Native.Point Point; public uint Data,Flags,Time; public nuint Extra; }
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window,uint flags);
    private readonly Native.Hook callback;
    private readonly nint hook;
    private ClickedEditor? clicked;
    internal MouseFocus(Action wake)
    {
        callback=(code,message,data)=>
        {
            if(code>=0 && message==0x0201)
            {
                var mouse=Marshal.PtrToStructure<MouseData>(data);
                if((mouse.Flags & 3)==0)
                {
                    nint window=GetAncestor(AccessibleTextCaret.WindowFromPoint(mouse.Point),2);
                    Native.GetWindowThreadProcessId(window,out uint process);
                    if(process!=Environment.ProcessId)
                        Volatile.Write(ref clicked,new(window,mouse.Point));
                    wake();
                }
            }
            return Native.CallNextHookEx(hook,code,message,data);
        };
        hook=Native.SetWindowsHookEx(14,callback,Native.GetModuleHandle(null),0);
    }
    internal ClickedEditor? Read(nint foreground)
    {
        var value=Volatile.Read(ref clicked);
        return value?.Foreground==foreground ? value:null;
    }
    internal void Clear() => Volatile.Write(ref clicked,null);
    public void Dispose() { if(hook!=0) Native.UnhookWindowsHookEx(hook); }
}
