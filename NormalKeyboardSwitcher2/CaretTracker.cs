using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;

namespace NormalKeyboardSwitcher2;
internal sealed record CaretSample(nint Foreground, nint Focus, PixelRect Bounds, long Time, string Method);

internal sealed class CaretTracker : IDisposable
{
    private readonly Thread worker;
    private readonly AccessibilityEvents events;
    private readonly MouseFocus mouse;
    internal void ClearPointerFocus() => mouse.Clear();
    private volatile bool stopped;
    private readonly object lifetime=new();
    private bool workerExited;
    private readonly AutoResetEvent wake=new(false);
    private readonly ConsoleCaretSource console=new();
    private long inputTime;
    internal void Wake()
    {
        lock(lifetime)
        {
            if(stopped || workerExited) return;
            Interlocked.Exchange(ref inputTime,Environment.TickCount64);
            wake.Set();
        }
    }
    private CaretSample? sample;
    private string diagnostic="Starting";
    internal string Diagnostic => Volatile.Read(ref diagnostic);
    internal CaretTracker()
    {
        events=new AccessibilityEvents(Wake);
        try { mouse=new MouseFocus(Wake); }
        catch { events.Dispose(); wake.Dispose(); throw; }
        worker = new Thread(Work) { IsBackground = true, Name = "Caret accessibility" };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
    }
    internal CaretSample? Read()
    {
        nint foreground = Native.GetForegroundWindow();
        if(Native.IsHungAppWindow(foreground)) return null;
        var gui = Native.Gui();
        if(console.Read(foreground) is { } consolePosition)
            return consolePosition.Bounds is { } bounds
                ? new(foreground,gui.Focus,bounds,Environment.TickCount64,"Console cursor") : null;
        // Track the existing caret through its blink-off phase; menu/move loops
        // suppress it, and a destroyed caret or changed focus invalidates it.
        if (gui.Caret != 0 && gui.Focus != 0 && (gui.Flags & 0x1E) == 0 && gui.CaretRect.Bottom > gui.CaretRect.Top)
        {
            var rect = gui.CaretRect;
            Native.MapWindowPoints(gui.Caret, 0, ref rect, 2);
            return new(foreground, gui.Focus, rect.Pixels, Environment.TickCount64, "Windows caret");
        }
        var cached = Volatile.Read(ref sample);
        return cached != null && cached.Foreground == foreground && cached.Focus == gui.Focus &&
            Environment.TickCount64 - cached.Time < 800 ? cached : null;
    }
    private void Work()
    {
        IAutomation? automation = null;
        try
        {
            automation = (IAutomation)Activator.CreateInstance(Type.GetTypeFromCLSID(
                new Guid("ff48dba4-60ef-4201-aa87-54103eef594e"))!)!;
            while (!stopped)
            {
                var foreground = Native.GetForegroundWindow();
                if(Native.IsHungAppWindow(foreground))
                {
                    Volatile.Write(ref sample,null);
                    Volatile.Write(ref diagnostic,"Focused application is not responding");
                    wake.WaitOne(250);
                    continue;
                }
                var gui = Native.Gui();
                PixelRect? rect = null;
                string method = "MSAA caret";
                var trace=new List<string>();
                // An explicit caret object is more precise than a text-range
                // approximation. Chrome's address bar can report its field origin
                // for a collapsed UIA selection even while the real caret moves.
                // A transient provider error must not kill tracking for all applications.
                try { rect = AccessibleCaret(gui.Focus); }
                catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
                { trace.Add("MSAA error "+ex.HResult.ToString("X")); }
                if (rect == null)
                {
                    method = "UI Automation";
                    try { rect = AutomationCaret(automation,trace); }
                    catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
                    { trace.Add("UIA error "+ex.HResult.ToString("X")); }
                }
                if(rect==null)
                {
                    method="IAccessible2 caret";
                    PixelRect? TryRead(Func<PixelRect?> read)
                    {
                        try { return read(); }
                        catch(Exception ex) when(ex is COMException or InvalidCastException or ArgumentException)
                        { trace.Add("IA2 error "+ex.HResult.ToString("X")); return null; }
                    }
                    if(events.Read(foreground) is { } candidate)
                        rect=TryRead(()=>AccessibleTextCaret.Read(candidate.Window,trace,candidate,requireFocused:true));
                    if(rect==null && mouse.Read(foreground) is { } clicked)
                        rect=TryRead(()=>AccessibleTextCaret.Read(foreground,trace,clickedPoint:clicked.Point));
                    if(rect==null && events.Read(foreground) is { } focusedEvent)
                        rect=TryRead(()=>AccessibleTextCaret.Read(focusedEvent.Window,trace,focusedEvent));
                    if(rect==null) rect=TryRead(()=>AccessibleTextCaret.Read(gui.Focus,trace));
                    if(rect==null && foreground!=gui.Focus) rect=TryRead(()=>AccessibleTextCaret.Read(foreground,trace));
                }
                Volatile.Write(ref diagnostic,string.Join("; ",trace));
                if (!stopped && foreground == Native.GetForegroundWindow() && gui.Focus == Native.Gui().Focus)
                    Volatile.Write(ref sample, rect is { } r
                        ? new(foreground, gui.Focus, r, Environment.TickCount64, method) : null);
                if (!stopped) wake.WaitOne(Environment.TickCount64-Interlocked.Read(ref inputTime)<500 ? 16 : 40);
            }
        }
        catch (Exception ex) { AppLog.Write("Caret worker: " + ex.Message); }
        finally
        {
            Release(automation);
            lock(lifetime) { workerExited=true; wake.Dispose(); }
        }
    }
    private static PixelRect? AutomationCaret(IAutomation automation,List<string> trace)
    {
        IAutomationElement? element = null;
        object? pattern = null;
        ITextRange? range = null;
        ITextRangeArray? selection=null;
        try
        {
            element = automation.GetFocusedElement();
            if (element == null) { trace.Add("No focused control"); return null; }
            // Only technical metadata; never query names, text, selections' contents, or values.
            trace.Add("ControlType="+element.GetCurrentPropertyValue(30003));
            trace.Add("Framework="+element.GetCurrentPropertyValue(30024));
            if (element.GetCurrentPropertyValue(30008) is not true)
            { trace.Add("Control lacks keyboard focus"); return null; }
            try
            {
                pattern = element.GetCurrentPattern(10024);
                if (pattern is ITextPattern2 text)
                {
                    range=text.GetCaretRange(out bool active);
                    trace.Add("TextPattern2 active="+active);
                    if (active && range != null)
                    {
                        var result=RangeBounds(range,trace);
                        if (result != null) return result;
                    }
                }
            }
            catch(COMException ex) { trace.Add("TextPattern2="+ex.HResult.ToString("X")); }
            Release(range); range=null; Release(pattern); pattern=null;
            try
            {
                pattern=element.GetCurrentPattern(10014);
                if (pattern is ITextPattern text)
                {
                    selection=text.GetSelection();
                    int count=selection.GetLength();
                    trace.Add("TextPattern ranges="+count);
                    if(count==1)
                    {
                        range=selection.GetElement(0);
                        // A non-empty selection does not tell us which endpoint is active.
                        if(range.CompareEndpoints(0,range,1)==0) return RangeBounds(range,trace);
                        trace.Add("Non-empty selection");
                    }
                }
            }
            catch(COMException ex) { trace.Add("TextPattern="+ex.HResult.ToString("X")); }
            return null;
        }
        finally { Release(selection); Release(range); Release(pattern); Release(element); }
    }
    private static PixelRect? RangeBounds(ITextRange range,List<string> trace)
    {
        double[] bounds=range.GetBoundingRectangles();
        trace.Add("Caret rectangles="+bounds.Length/4);
        if(bounds.Length>=4)
        {
            var direct=Valid(bounds[0],bounds[1],bounds[2],bounds[3]);
            if(direct!=null) return direct;
        }
        // A zero-length caret may legitimately have no rectangles. Extend a CLONE
        // by one character; this never selects, edits, or reads the user's text.
        foreach(bool forward in new[]{true,false})
        {
            ITextRange? adjacent=null;
            try
            {
                adjacent=range.Clone();
                int moved=adjacent.MoveEndpointByUnit(forward ? 1 : 0,0,forward ? 1 : -1);
                if(moved==0) continue;
                bounds=adjacent.GetBoundingRectangles();
                if(bounds.Length<4) continue;
                int i=forward ? 0 : bounds.Length-4;
                trace.Add(forward ? "Next-character edge" : "Previous-character edge");
                return Valid(bounds[i]+(forward?0:bounds[i+2]),bounds[i+1],1,bounds[i+3]);
            }
            finally { Release(adjacent); }
        }
        return null;
    }
    private static PixelRect? AccessibleCaret(nint focus)
    {
        var iid = new Guid("618736e0-3c3d-11cf-810c-00aa00389b71");
        Accessibility.IAccessible? accessible = null;
        try
        {
            if (focus == 0 || Native.AccessibleObjectFromWindow(focus, unchecked((uint)-8), ref iid, out accessible) < 0 ||
                accessible == null) return null;
            int state = Convert.ToInt32(accessible.get_accState(0));
            if ((state & (0x8000 | 0x10000)) != 0) return null;
            accessible.accLocation(out int x, out int y, out int width, out int height, 0);
            return Valid(x, y, width, height);
        }
        finally { Release(accessible); }
    }
    private static PixelRect? Valid(double x, double y, double width, double height) =>
        double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(width) && double.IsFinite(height) &&
        width >= 0 && width < 200 && height > 0 && height < 500 ? new(x,y,width,height) : null;
    private static void Release(object? value)
    {
        if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }
    public void Dispose()
    {
        lock(lifetime)
        {
            if(stopped) return;
            stopped=true;
            if(!workerExited) wake.Set();
        }
        events.Dispose(); mouse.Dispose();
        Volatile.Write(ref sample,null);
        console.Dispose();
        // The worker releases its own COM connection and wait handle when any
        // in-flight provider call returns; do not block the UI waiting for it.
    }
}


