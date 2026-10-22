using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NormalKeyboardSwitcher2;
internal sealed class KeyboardHook : IDisposable
{
    private readonly Native.Hook callback;
    private nint handle;
    internal GestureState State { get; } = new();
    internal event Action<GestureAction>? Action;
    internal event System.Action? Input;
    internal event System.Action? FocusNavigation;
    internal KeyboardHook()
    {
        callback = OnKey;
        Restart();
    }
    internal void Restart()
    {
        Dispose();
        handle = Native.SetWindowsHookEx(13, callback, Native.GetModuleHandle(null), 0);
        if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private nint OnKey(int code, nint msg, nint data)
    {
        if (code >= 0 && msg is 0x0100 or 0x0101 or 0x0104 or 0x0105)
        {
            Input?.Invoke();
            var key = Marshal.PtrToStructure<Native.KeyboardData>(data);
            if ((key.Flags & 0x10) == 0) // Ignore injected keystrokes.
            {
                if(msg is 0x0100 or 0x0104 && key.Key is 0x09 or 0x0D or 0x1B or 0x75)
                    FocusNavigation?.Invoke();
                var action = State.Feed(key.Key, msg is 0x0100 or 0x0104);
                if (action != GestureAction.None) Action?.Invoke(action);
            }
        }
        // Leave application shortcuts intact. Windows' matching language shortcut
        // is disabled separately, with a recoverable backup.
        return Native.CallNextHookEx(handle, code, msg, data);
    }
    public void Dispose()
    {
        if (handle != 0) Native.UnhookWindowsHookEx(handle);
        handle = 0;
        State.Reset();
    }
}


