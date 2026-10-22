using System;
using System.IO;
using Microsoft.Win32;

namespace NormalKeyboardSwitcher2;

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NormalKeyboardSwitcher2";
    private static string Command => "\"" + (Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the application executable.")) + "\" --startup";

    internal static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, Command, RegistryValueKind.String);
            // Replace only the original application's entry, leaving unrelated startup items alone.
            if (key.GetValue("NormalKeyboardSwitcher") is string oldCommand)
            {
                string executable = oldCommand.Trim().Trim('"');
                if (string.Equals(Path.GetFileName(executable), "NormalKeyboardSwitcher.exe", StringComparison.OrdinalIgnoreCase))
                    key.DeleteValue("NormalKeyboardSwitcher", false);
            }
        }
        else key.DeleteValue(ValueName, false);
    }

    internal static void RemoveIfOwned()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable:true);
        if (string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase))
            key!.DeleteValue(ValueName, false);
    }
}
