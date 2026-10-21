using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;

namespace NormalKeyboardSwitcher2;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; public PixelRect Pixels => new(Left, Top, Right-Left, Bottom-Top); }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct GuiInfo {
        public int Size; public uint Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public Rect CaretRect;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardData { public int Key, Scan; public uint Flags, Time; public nuint Extra; }
    internal delegate nint Hook(int code, nint message, nint data);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [DllImport("user32.dll")] internal static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] internal static extern int GetKeyboardLayoutList(int count, [Out] nint[]? layouts);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
    [DllImport("user32.dll")] internal static extern int MapWindowPoints(nint from, nint to, ref Rect rect, uint count);
    [DllImport("user32.dll", SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError=true)] internal static extern nint SetWindowsHookEx(int type, Hook callback, nint module, uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] internal static extern nint MonitorFromRect(ref Rect rect, uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool SystemParametersInfo(uint action, uint param, nint value, uint flags);
    [DllImport("oleacc.dll")] internal static extern int AccessibleObjectFromWindow(nint hwnd, uint objectId, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out Accessibility.IAccessible? accessible);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] internal static extern bool IsHungAppWindow(nint window);
    internal static GuiInfo Gui()
    {
        var info = new GuiInfo { Size = Marshal.SizeOf<GuiInfo>() };
        GetGUIThreadInfo(0, ref info);
        return info;
    }
    internal static nint FocusWindow()
    {
        var info = Gui();
        return info.Focus != 0 ? info.Focus : GetForegroundWindow();
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(nint hwnd,StringBuilder name,int size);
    private delegate bool EnumWindow(nint window,nint data);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread,EnumWindow callback,nint data);
    private static nint consoleWindow;
    private static uint consoleThread;
    private static long lastConsoleLookup;
    internal static nint Layout(nint hwnd)
    {
        if(hwnd==0) return 0;
        var name=new StringBuilder(128);
        GetClassName(hwnd,name,name.Capacity);
        if(name.ToString()!="ConsoleWindowClass")
            return GetKeyboardLayout(GetWindowThreadProcessId(hwnd,out _));
        // ConsoleWindowClass reports a compatibility client thread, whose HKL
        // can remain unchanged. Resolve the host GUI thread by its window instead.
        bool OwnsWindow(uint thread)
        {
            bool found=false;
            EnumThreadWindows(thread,(window,_)=> { if(window==hwnd) found=true; return !found; },0);
            return found;
        }
        if(consoleWindow==hwnd && consoleThread!=0 && OwnsWindow(consoleThread))
            return GetKeyboardLayout(consoleThread);
        if(consoleWindow==hwnd && Environment.TickCount64-lastConsoleLookup<1000) return 0;
        consoleWindow=hwnd; consoleThread=0; lastConsoleLookup=Environment.TickCount64;
        foreach(string host in new[]{"conhost","OpenConsole"})
            foreach(var process in Process.GetProcessesByName(host))
            {
                using(process)
                try
                {
                    foreach(ProcessThread thread in process.Threads)
                    {
                        using(thread)
                        if(OwnsWindow((uint)thread.Id))
                        { consoleThread=(uint)thread.Id; return GetKeyboardLayout(consoleThread); }
                    }
                }
                catch(Exception ex) when(ex is Win32Exception or InvalidOperationException) { }
            }
        // Unknown is safer than reporting the stale console-client layout.
        return 0;
    }
    internal static nint[] Layouts()
    {
        var result = new nint[GetKeyboardLayoutList(0, null)];
        int count = GetKeyboardLayoutList(result.Length, result);
        return result[..Math.Min(count, result.Length)];
    }
    internal static void RequestLayout(nint focus, nint layout)
    {
        if (focus == 0 || layout == 0) return;
        // Preserve the complete HKL, including layout variants and IMEs.
        if (!PostMessage(focus, 0x0050, 0, layout))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}


