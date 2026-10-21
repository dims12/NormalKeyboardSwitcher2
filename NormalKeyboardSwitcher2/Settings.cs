using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NormalKeyboardSwitcher2;
internal sealed class Settings
{
    public Gesture Gesture { get; set; } = Gesture.CtrlShift;
    public bool ManageWindowsShortcut { get; set; } = true;
    public bool ShowIndicator { get; set; } = true;
    public bool BlinkIndicator { get; set; }
    public bool Configured { get; set; }
    internal static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NormalKeyboardSwitcher2");
    internal static Settings Load()
    {
        string path=Path.Combine(Folder,"settings.json");
        if (!File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException) { AppLog.Write(ex.Message); return new(); }
    }
    internal void Save()
    {
        Directory.CreateDirectory(Folder);
        string path=Path.Combine(Folder,"settings.json");
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(this));
        File.Move(path+".tmp",path,true);
    }
}
internal static class AppLog
{
    internal static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Settings.Folder);
            string path=Path.Combine(Settings.Folder,"app.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Move(path,path+".old",true);
            File.AppendAllText(path,DateTimeOffset.Now.ToString("O")+" "+message+Environment.NewLine);
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

// A durable journal is saved BEFORE registry changes. Restore uses compare-and-set
// so changes made later through Windows Settings are never overwritten.
internal static class WindowsShortcut
{
    private const string KeyPath = @"Keyboard Layout\Toggle";
    private static string BackupPath => Path.Combine(Settings.Folder,"windows-shortcut-backup.json");
    internal static bool HasBackup => File.Exists(BackupPath);
    internal static void DisableMatching(Gesture gesture)
    {
        Restore();
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        var backup=ShortcutPolicy.ToDisable(ReadValues(key),gesture);
        if (backup.Count == 0) return;
        Directory.CreateDirectory(Settings.Folder);
        File.WriteAllText(BackupPath+".tmp",JsonSerializer.Serialize(backup));
        File.Move(BackupPath+".tmp",BackupPath,true);
        try
        {
            foreach (string name in backup.Keys) key.SetValue(name,"3",RegistryValueKind.String);
            Reload();
        }
        catch { Restore(); throw; }
    }
    internal static void Restore()
    {
        if (!File.Exists(BackupPath)) return;
        var backup=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(BackupPath))
            ?? throw new InvalidDataException("Invalid Windows shortcut backup.");
        using var key=Registry.CurrentUser.CreateSubKey(KeyPath);
        foreach (var entry in ShortcutPolicy.ToRestore(ReadValues(key),backup))
            key.SetValue(entry.Key,entry.Value,RegistryValueKind.String);
        Reload();
        File.Delete(BackupPath);
    }
    private static Dictionary<string,string> ReadValues(RegistryKey key)
    {
        var values=new Dictionary<string,string>();
        foreach (string name in new[]{"Hotkey","Language Hotkey","Layout Hotkey"})
            if (key.GetValue(name) is string value) values[name]=value;
        return values;
    }
    private static void Reload()
    {
        if (!Native.SystemParametersInfo(0x005B,0,0,2))
            throw new Win32Exception(Marshal.GetLastWin32Error(),"Windows could not reload its language shortcut settings.");
    }
}


