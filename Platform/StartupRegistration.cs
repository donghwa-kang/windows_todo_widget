using System;
using Microsoft.Win32;
namespace TerminalWidget.Platform;
public static class StartupRegistration
{
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("TerminalWidget", "\"" + Environment.ProcessPath + "\"");
        else key.DeleteValue("TerminalWidget", false);
    }
}
