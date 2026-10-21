using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace NormalKeyboardSwitcher2;

internal sealed record ConsolePosition(long Window,PixelRect? Bounds,int Error=0,string State="",long ThreadLayout=0);
internal static class ConsoleCaretWorker
{
    [StructLayout(LayoutKind.Sequential)] private struct Coord { public short X,Y; }
    [StructLayout(LayoutKind.Sequential)] private struct SmallRect { public short Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct BufferInfo {
        public Coord Size,Cursor; public ushort Attributes; public SmallRect Window; public Coord Maximum;
    }
    [StructLayout(LayoutKind.Sequential)] private struct FontInfo { public uint Index; public Coord Size; }
    [StructLayout(LayoutKind.Sequential)] private struct CursorInfo { public uint Size; [MarshalAs(UnmanagedType.Bool)] public bool Visible; }
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool AttachConsole(uint process);
    [DllImport("kernel32.dll")] private static extern bool FreeConsole();
    [DllImport("kernel32.dll")] private static extern bool SetConsoleCtrlHandler(nint handler,bool add);
    [DllImport("kernel32.dll")] private static extern nint GetConsoleWindow();
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFile(string name,uint access,uint sharing,nint security,uint creation,uint flags,nint template);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetConsoleScreenBufferInfo(SafeFileHandle handle,out BufferInfo info);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetConsoleCursorInfo(SafeFileHandle handle,out CursorInfo info);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetCurrentConsoleFont(SafeFileHandle handle,bool maximum,out FontInfo info);
    [DllImport("kernel32.dll")] private static extern Coord GetConsoleFontSize(SafeFileHandle handle,uint font);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window,ref Native.Point point);

    private static ConsolePosition Sample(SafeFileHandle handle) =>
        SampleCursor(handle) with { ThreadLayout=(long)Native.Layout(GetConsoleWindow()) };
    private static ConsolePosition SampleCursor(SafeFileHandle handle)
    {
        nint window=GetConsoleWindow();
        if(!GetConsoleScreenBufferInfo(handle,out var buffer))
            return new((long)window,null,Marshal.GetLastWin32Error(),"GetConsoleScreenBufferInfo failed");
        if(!GetConsoleCursorInfo(handle,out var cursor))
            return new((long)window,null,Marshal.GetLastWin32Error(),"GetConsoleCursorInfo failed");
        if(!GetCurrentConsoleFont(handle,false,out var font))
            return new((long)window,null,Marshal.GetLastWin32Error(),"GetCurrentConsoleFont failed");
        if(!cursor.Visible) return new((long)window,null,0,"Cursor hidden");
        var cell=GetConsoleFontSize(handle,font.Index);
        var origin=new Native.Point();
        if(cell.X<=0 || cell.Y<=0) return new((long)window,null,Marshal.GetLastWin32Error(),$"Invalid cell {cell.X}x{cell.Y}; font {font.Size.X}x{font.Size.Y}");
        if(!ClientToScreen(window,ref origin)) return new((long)window,null,Marshal.GetLastWin32Error(),"ClientToScreen failed");
        var rect=ConsoleGeometry.Caret(origin.X,origin.Y,buffer.Cursor.X,buffer.Cursor.Y,
            buffer.Window.Left,buffer.Window.Top,buffer.Window.Right,buffer.Window.Bottom,
            cell.X,cell.Y,(int)cursor.Size);
        return new((long)window,rect,0,rect==null ? "Outside viewport" : "Cursor located");
    }
    internal static void Run(uint process,string destination,bool probe)
    {
        // Only this isolated helper attaches. The main UI never joins another
        // application's console or receives its console-close/control events.
        FreeConsole();
        if(!AttachConsole(process))
        {
            if(probe) File.WriteAllText(destination,JsonSerializer.Serialize(new ConsolePosition(0,null,Marshal.GetLastWin32Error())));
            return;
        }
        SetConsoleCtrlHandler(0,true);
        try
        {
            // Cursor-info queries return ERROR_ACCESS_DENIED with a read-only
            // handle on this console host. Only metadata queries use this handle.
            using var output=CreateFile("CONOUT$",0xC0000000,3,0,3,0,0);
            if(probe) { File.WriteAllText(destination,JsonSerializer.Serialize(Sample(output))); return; }
            using var pipe=new NamedPipeClientStream(".",destination,PipeDirection.Out);
            pipe.Connect(2000);
            using var writer=new StreamWriter(pipe) { AutoFlush=true };
            while(true)
            {
                var sample=Sample(output);
                writer.WriteLine(JsonSerializer.Serialize(sample));
                Thread.Sleep(16);
            }
        }
        catch(IOException) { } catch(TimeoutException) { }
        finally { FreeConsole(); }
    }
}

internal sealed class ConsoleCaretSource : IDisposable
{
    private nint target;
    private NamedPipeServerStream? pipe;
    private ConsolePosition? current;
    private long received;

    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(nint window,StringBuilder name,int size);
    internal ConsolePosition? Read(nint window)
    {
        if(window!=target)
        {
            pipe?.Dispose(); pipe=null; current=null; target=window;
            var name=new StringBuilder(128); GetClassName(window,name,name.Capacity);
            if(name.ToString()=="ConsoleWindowClass") { Start(window); }
        }
        var value=Volatile.Read(ref current);
        return value!=null && value.Window==(long)window && Environment.TickCount64-Interlocked.Read(ref received)<250 ? value:null;
    }
    private void Start(nint window)
    {
        Native.GetWindowThreadProcessId(window,out uint process);
        string name="NKS2-caret-"+Guid.NewGuid().ToString("N");
        var server=new NamedPipeServerStream(name,PipeDirection.In,1,PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        pipe=server;
        _=ReadLoop(server,window);
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) {
                UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
                Arguments="--console-caret "+process+" "+name
            })?.Dispose();
        }
        catch(Exception ex) { server.Dispose(); AppLog.Write("Console helper: "+ex.Message); }
    }
    private async Task ReadLoop(NamedPipeServerStream server,nint window)
    {
        try
        {
            using var timeout=new CancellationTokenSource(2500);
            await server.WaitForConnectionAsync(timeout.Token);
            using var reader=new StreamReader(server);
            while(await reader.ReadLineAsync() is string line)
            {
                var value=JsonSerializer.Deserialize<ConsolePosition>(line);
                if(target!=window) return;
                Volatile.Write(ref current,value);
                Interlocked.Exchange(ref received,Environment.TickCount64);
            }
        }
        catch(Exception ex) when(ex is IOException or ObjectDisposedException or OperationCanceledException) { }
        finally { server.Dispose(); }
    }
    public void Dispose() { pipe?.Dispose(); }
}



