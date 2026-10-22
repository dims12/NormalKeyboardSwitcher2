using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace NormalKeyboardSwitcher2;
internal sealed class App : Application
{
    private Mutex? instance;
    private bool ownsInstance;
    private KeyboardHook? keyboard;
    private CaretTracker? caret;
    private BadgeWindow? badge;
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? appIcon;
    private DispatcherTimer? timer;
    private readonly RecentLayouts recent=new();
    private Settings settings=new();
    private SettingsWindow? settingsWindow;
    private bool enabled, pending, endRequested;
    private int requestVersion;
    private nint sessionFocus, lastExternalWindow, lastExternalFocus;
    private uint lastExternalProcess;
    private long lastRefresh, lastStatus;
    private string switchStatus="Ready";

    [STAThread]
    public static int Main(string[] args)
    {
        if(args.Length==1 && args[0]=="--uninstall-cleanup")
        {
            using var guard=new Mutex(true,@"Local\NormalKeyboardSwitcher2",out bool acquired);
            if(!acquired) return 2;
            try
            {
                WindowsShortcut.Restore();
                StartupRegistration.RemoveIfOwned();
                return 0;
            }
            catch(Exception ex)
            {
                AppLog.Write("Uninstall cleanup: "+ex);
                return 1;
            }
            finally { guard.ReleaseMutex(); }
        }
        if(args.Length==3 && (args[0]=="--console-caret" || args[0]=="--console-probe") && uint.TryParse(args[1],out uint consoleProcess))
        {
            ConsoleCaretWorker.Run(consoleProcess,args[2],args[0]=="--console-probe");
            return 0;
        }
        if(args.Length==2 && args[0]=="--diagnose-pointer")
        {
            var trace=new System.Collections.Generic.List<string>();
            PixelRect? bounds=null;
            var window=Native.GetForegroundWindow();
            try { bounds=AccessibleTextCaret.Read(window,trace,new(window,0,0,window,uint.MaxValue)); }
            catch(Exception ex) { trace.Add("Accessibility error "+ex.HResult.ToString("X")); }
            File.WriteAllText(args[1],System.Text.Json.JsonSerializer.Serialize(new { Caret=bounds,Accessibility=trace }));
            return 0;
        }
        if(args.Length==5 && args[0]=="--diagnose-object" && long.TryParse(args[1],out long objectWindow) && int.TryParse(args[2],out int objectId) && int.TryParse(args[3],out int childId))
        {
            var trace=new System.Collections.Generic.List<string>();
            PixelRect? bounds=null;
            try { bounds=AccessibleTextCaret.Read((nint)objectWindow,trace,new((nint)objectWindow,objectId,childId,(nint)objectWindow)); }
            catch(Exception ex) { trace.Add("Accessibility error "+ex.HResult.ToString("X")); }
            File.WriteAllText(args[4],System.Text.Json.JsonSerializer.Serialize(new { Caret=bounds,Accessibility=trace }));
            return 0;
        }
        if(args.Length==3 && args[0]=="--diagnose-window" && long.TryParse(args[1],out long diagnosticWindow))
        {
            var trace=new System.Collections.Generic.List<string>();
            PixelRect? bounds=null;
            try { bounds=AccessibleTextCaret.Read((nint)diagnosticWindow,trace); }
            catch(Exception ex) { trace.Add("Accessibility error "+ex.HResult.ToString("X")); }
            File.WriteAllText(args[2],System.Text.Json.JsonSerializer.Serialize(new { Caret=bounds,Accessibility=trace }));
            return 0;
        }
        if (args.Length == 2 && args[0] == "--render-contrast-preview")
        {
            Preview.RenderContrast(args[1]);
            return 0;
        }
        if (args.Length == 2 && args[0] == "--render-preview")
        {
            Preview.Render(new App(),args[1]);
            return 0;
        }
        if (args.Length is 2 or 3 && args[0] == "--watch-caret")
        {
            using var tracker=new CaretTracker();
            var readings=new System.Collections.Generic.List<object>();
            int count=args.Length==3 && int.TryParse(args[2],out int requested) ? Math.Clamp(requested,1,600) : 60;
            for(int i=0;i<count;i++)
            {
                long until=Environment.TickCount64+500;
                while(Environment.TickCount64<until) { Forms.Application.DoEvents(); Thread.Sleep(10); }
                var sample=tracker.Read();
                Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out uint observedProcess);
                string process="";
                try { using var p=Process.GetProcessById((int)observedProcess); process=p.ProcessName; } catch(ArgumentException) { }
                readings.Add(new { Process=process, Method=sample?.Method, Caret=sample?.Bounds, Accessibility=tracker.Diagnostic });
                File.WriteAllText(args[1],System.Text.Json.JsonSerializer.Serialize(readings));
            }
            File.WriteAllText(args[1],System.Text.Json.JsonSerializer.Serialize(readings));
            return 0;
        }
        if (args.Length == 2 && args[0] == "--diagnose")
        {
            // Read-only diagnostic mode: no keyboard hook, registry change, or overlay.
            using var tracker=new CaretTracker();
            Thread.Sleep(800);
            var sample=tracker.Read();
            File.WriteAllText(args[1],System.Text.Json.JsonSerializer.Serialize(new {
                LayoutCount=Native.Layouts().Length,
                ActiveLayout=Native.Layout(Native.FocusWindow()).ToString("X"),
                CaretMethod=sample?.Method, Caret=sample?.Bounds, Accessibility=tracker.Diagnostic,
                Elevated=IsElevated(), PointerSize=IntPtr.Size
            }));
            return 0;
        }
        if (args.Length == 2 && args[0] == "--wait-for-pid" && int.TryParse(args[1],out int pid))
        {
            try { using var parent=Process.GetProcessById(pid); parent.WaitForExit(8000); }
            catch (ArgumentException) { }
        }
        var app=new App { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        app.Run();
        return 0;
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance=new Mutex(true,@"Local\NormalKeyboardSwitcher2",out bool created);
        if (!created) { if (!e.Args.Contains("--startup")) MessageBox.Show("NormalKeyboardSwitcher2 is already running. Open its settings from the tray."); Shutdown(); return; }
        ownsInstance=true;
        DispatcherUnhandledException += (_,args) => {
            AppLog.Write(args.Exception.ToString());
            MessageBox.Show(args.Exception.Message,"NormalKeyboardSwitcher2");
            args.Handled=true;
        };
        try
        {
            RememberExternalWindow();
            WindowsShortcut.Restore();
            settings=Settings.Load();
            caret=new CaretTracker();
            badge=new BadgeWindow();
            keyboard=new KeyboardHook();
            keyboard.State.Gesture=settings.Gesture;
            keyboard.Input += () => { caret.Wake(); };
            keyboard.FocusNavigation += () => caret.ClearPointerFocus();
            keyboard.Action += action => {
                bool released=keyboard.State.Released;
                Dispatcher.BeginInvoke(() => HandleGesture(action,released),DispatcherPriority.Input);
            };
            using (var iconStream=Application.GetResourceStream(new Uri("pack://application:,,,/Assets/AppIcon.ico")).Stream)
                appIcon=new System.Drawing.Icon(iconStream,Forms.SystemInformation.SmallIconSize);
            tray=new Forms.NotifyIcon { Icon=appIcon,
                Text="NormalKeyboardSwitcher2",Visible=true };
            var menu=new Forms.ContextMenuStrip();
            menu.Items.Add("Settings",null,(_,_)=>ShowSettings());
            menu.Items.Add("Exit",null,(_,_)=>Shutdown());
            tray.ContextMenuStrip=menu;
            tray.MouseClick += (_,args)=> { if (args.Button == Forms.MouseButtons.Left) ShowSettings(); };
            timer=new DispatcherTimer(DispatcherPriority.Render) { Interval=TimeSpan.FromMilliseconds(16) };
            timer.Tick += (_,_)=>Tick();
            timer.Start();
            if (settings.Configured) Apply(settings.Gesture,settings.ShowIndicator,settings.BlinkIndicator);
            else ShowSettings();
            if (settings.Configured && e.Args.Contains("--settings")) ShowSettings();
        }
        catch (Exception ex) { AppLog.Write(ex.ToString()); MessageBox.Show(ex.Message,"Cannot start NormalKeyboardSwitcher2"); Shutdown(1); }
    }
    private void Tick()
    {
        if (Environment.TickCount64-lastRefresh > 3000 && !recent.Cycling)
        {
            recent.Refresh(Native.Layouts()); lastRefresh=Environment.TickCount64;
        }
        RememberExternalWindow();
        var focus=Native.FocusWindow();
        var layout=Native.Layout(focus);
        Native.GetWindowThreadProcessId(focus,out uint focusedProcess);
        nint selectedLayout=focusedProcess == Environment.ProcessId
            ? Native.Layout(lastExternalFocus) : layout;
        if (recent.Cycling && sessionFocus != focus) Cancel();
        if (!pending && focusedProcess != Environment.ProcessId) recent.Observe(layout);
        if(layout!=0) badge?.UpdateLanguage(Label(layout));
        var location=caret?.Read();
        if (settings.ShowIndicator && layout != 0 && location != null)
            badge?.Update(Label(layout),location.Bounds,settings.BlinkIndicator);
        else badge?.Hide();
        if(settingsWindow?.IsVisible==true && Environment.TickCount64-lastStatus>=250)
        {
        lastStatus=Environment.TickCount64;
        settingsWindow.UpdateStatus(
            (enabled ? "Recently used switching enabled" : settings.Gesture == Gesture.System ? "Windows switching" : "Switching paused") +
            (IsElevated() ? " \u00b7 Administrator" : " \u00b7 Standard permissions") +
            "\n"+switchStatus+"\nCaret: "+(location?.Method ?? "not exposed by the focused application"),
            recent.Order.Select(layout=>new LanguageChoice(layout,Name(layout))).ToArray(), selectedLayout);
        }
    }
    private void HandleGesture(GestureAction action,bool released)
    {
        if (!enabled) return;
        if (action == GestureAction.Cancel) { Cancel(); return; }
        if (action == GestureAction.Commit)
        {
            endRequested=true;
            if (!pending) recent.End(Native.Layout(sessionFocus));
            return;
        }
        var focus=Native.FocusWindow();
        if (focus == 0) return;
        if (!recent.Cycling) { sessionFocus=focus; recent.Refresh(Native.Layouts()); }
        if (focus != sessionFocus) { Cancel(); return; }
        nint target=recent.Next(Native.Layout(focus));
        if (target == 0) { Cancel(); return; }
        endRequested=released;
        _ = SwitchAndVerify(focus,target,++requestVersion);
    }
    private async Task SwitchAndVerify(nint focus,nint target,int version,bool requireFocus=true)
    {
        pending=true;
        try
        {
            Native.RequestLayout(focus,target);
            for (int i=0;i<24;i++)
            {
                await Task.Delay(25);
                if (version != requestVersion) return;
                if (requireFocus && Native.FocusWindow()!=focus) { Cancel(); return; }
                if (Native.Layout(focus)==target)
                {
                    pending=false;
                    if(Native.FocusWindow()==focus) badge?.UpdateLanguage(Label(target));
                    switchStatus="Selected "+Name(target);
                    if (endRequested) recent.End(target);
                    return;
                }
            }
            switchStatus="The focused application did not confirm the language change.";
            AppLog.Write(switchStatus+" Requested HKL="+target.ToString("X"));
        }
        catch (Exception ex) { switchStatus="Switch failed: "+ex.Message; AppLog.Write(switchStatus); }
        if (version == requestVersion) { pending=false; recent.End(Native.Layout(focus)); }
    }
    private void Cancel()
    {
        requestVersion++; pending=false; endRequested=false;
        recent.End(Native.Layout(Native.FocusWindow()));
    }
    internal void Apply(Gesture gesture,bool show,bool blink,bool? startWithWindows=null)
    {
        if (gesture != Gesture.System && Process.GetProcessesByName("NormalKeyboardSwitcher").Length != 0)
            throw new InvalidOperationException("Exit the original NormalKeyboardSwitcher before enabling its replacement.");
        if (startWithWindows.HasValue) StartupRegistration.SetEnabled(startWithWindows.Value);
        enabled=false; Cancel(); keyboard!.State.Reset();
        WindowsShortcut.Restore();
        if (gesture != Gesture.System) WindowsShortcut.DisableMatching(gesture);
        settings.Gesture=gesture;
        settings.ManageWindowsShortcut=gesture != Gesture.System;
        settings.ShowIndicator=show; settings.BlinkIndicator=blink; settings.Configured=true; settings.Save();
        keyboard.State.Gesture=gesture; enabled=gesture != Gesture.System;
        switchStatus=gesture == Gesture.System
            ? "Windows handles keyboard switching. The caret indicator remains available."
            : "Hold "+(gesture==Gesture.CtrlShift ? "Ctrl" : "left Alt")+" and tap Shift; release both to keep the selection.";
    }
    private void RememberExternalWindow()
    {
        nint window=Native.GetForegroundWindow();
        if (window == 0) return;
        Native.GetWindowThreadProcessId(window,out uint process);
        if (process == 0 || process == Environment.ProcessId) return;
        lastExternalWindow=window; lastExternalProcess=process; lastExternalFocus=Native.FocusWindow();
    }
    internal async Task SelectLanguage(nint layout,bool close)
    {
        nint targetWindow=lastExternalWindow, focus=lastExternalFocus;
        Native.GetWindowThreadProcessId(targetWindow,out uint process);
        if (!Native.IsWindow(targetWindow) || !Native.IsWindow(focus) || process != lastExternalProcess)
            throw new InvalidOperationException("Activate the application you want to type in, then reopen this window to choose its language.");
        Cancel();
        if (close)
        {
            if (!Native.SetForegroundWindow(targetWindow))
                throw new InvalidOperationException("Windows could not return focus to the previous application.");
            for (int i=0;i<10 && Native.GetForegroundWindow()!=targetWindow;i++) await Task.Delay(25);
            if (Native.GetForegroundWindow()!=targetWindow)
                throw new InvalidOperationException("The previous application did not receive focus.");
            focus=Native.FocusWindow();
        }
        sessionFocus=focus; endRequested=true;
        int version=++requestVersion;
        await SwitchAndVerify(focus,layout,version,requireFocus:close);
        if (version != requestVersion) return;
        if (Native.Layout(focus)!=layout)
            throw new InvalidOperationException(switchStatus);
    }
    private void ShowSettings()
    {
        RememberExternalWindow();
        if (settingsWindow == null)
        {
            settingsWindow=new SettingsWindow(this,settings);
            settingsWindow.Closed += (_,_)=>settingsWindow=null;
        }
        // Reopening always starts from saved settings; cancelled edits do not linger.
        settingsWindow.LoadSettings(settings);
        settingsWindow.Show(); settingsWindow.WindowState=WindowState.Normal; settingsWindow.Activate();
    }
    internal static bool IsElevated()
    {
        using var identity=WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
    internal static string Label(nint layout)
    {
        try { return CultureInfo.GetCultureInfo((int)((long)layout & 0xffff)).TwoLetterISOLanguageName.ToUpperInvariant(); }
        catch (CultureNotFoundException) { return "??"; }
    }
    internal static string Name(nint layout)
    {
        foreach (Forms.InputLanguage language in Forms.InputLanguage.InstalledInputLanguages)
            if (language.Handle == layout) return language.Culture.EnglishName+" ("+language.LayoutName+")";
        return Label(layout)+" ["+layout.ToString("X")+"]";
    }
    internal void RestartElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) {
                UseShellExecute=true, Verb="runas",
                Arguments="--wait-for-pid "+Environment.ProcessId
            });
            Shutdown();
        }
        catch (System.ComponentModel.Win32Exception ex) { MessageBox.Show(ex.Message,"Restart as administrator"); }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        timer?.Stop(); keyboard?.Dispose(); caret?.Dispose(); badge?.Close();
        if (tray != null) { tray.Visible=false; tray.Dispose(); }
        appIcon?.Dispose();
        // Restore on normal exit; the journal also survives crashes for next startup.
        try { if (ownsInstance && WindowsShortcut.HasBackup) WindowsShortcut.Restore(); }
        catch (Exception ex) { AppLog.Write("Restore on exit: "+ex); }
        instance?.Dispose();
        base.OnExit(e);
    }
}
