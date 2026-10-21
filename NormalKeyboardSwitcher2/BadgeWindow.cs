using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace NormalKeyboardSwitcher2;
internal sealed class BadgeWindow : Window
{
    private readonly OutlinedLabel label;
    private nint hwnd;
    private readonly InkSampler ink = new();
    private readonly CaretBlink blink = new();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetCaretBlinkTime();
    internal BadgeWindow()
    {
        Width = 24; Height = 14;
        Closed += (_,_)=>ink.Dispose();
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; Focusable = false;
        IsHitTestVisible = false;
        label = new OutlinedLabel();
        Content = label;
        SourceInitialized += (_,_) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(hwnd, -20, Native.GetWindowLongPtr(hwnd,-20) | 0x08000000 | 0x20 | 0x80);
            HwndSource.FromHwnd(hwnd).AddHook((nint h, int msg, nint w, nint l, ref bool handled) =>
            {
                if (msg == 0x21) { handled = true; return 3; } // MA_NOACTIVATE
                if (msg == 0x84) { handled = true; return -1; } // HTTRANSPARENT
                return 0;
            });
        };
    }
    internal void UpdateLanguage(string text)
    {
        if(label.Text==text) return;
        label.Text=text;
        blink.Restart(Environment.TickCount64);
        Opacity=1;
    }
    internal void Update(string text, PixelRect caret,bool blinking)
    {
        UpdateLanguage(text);
        Opacity=!blinking || blink.Visible(Environment.TickCount64,GetCaretBlinkTime()) ? 1 : 0;

        var rect = new Native.Rect { Left=(int)caret.X,Top=(int)caret.Y,
            Right=(int)(caret.X+Math.Max(1,caret.Width)),Bottom=(int)(caret.Y+caret.Height) };
        nint monitor = Native.MonitorFromRect(ref rect,2);
        var info = new Native.MonitorInfo { Size=System.Runtime.InteropServices.Marshal.SizeOf<Native.MonitorInfo>() };
        if (!Native.GetMonitorInfo(monitor, ref info)) { Hide(); return; }
        Native.GetDpiForMonitor(monitor,0,out uint dpi,out _);
        double scale = (dpi == 0 ? 96 : dpi)/96d;
        int width=(int)Math.Ceiling(Width*scale), height=(int)Math.Ceiling(Height*scale);
        var position=BadgePlacement.Place(caret,info.Work.Pixels,width,height,Math.Max(1,(int)scale));
        if (!IsVisible) Show();
        Native.SetWindowPos(hwnd, -1, position.X, position.Y, width, height, 0x10);
        label.SetColor(ink.Read(caret,new(position.X,position.Y,width,height)));
    }
}










