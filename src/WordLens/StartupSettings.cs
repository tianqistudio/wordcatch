using System;
using Microsoft.Win32;

namespace WordLens;

internal sealed class StartupSettings
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "TianqiStudio.WordLens";
    private readonly string keyPath;
    internal StartupSettings(string keyPath = RunKey) { this.keyPath = keyPath; }
    internal static string Command(string executable) => "\"" + executable + "\" --startup";
    public bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
    }
    public void Save(bool enabled)
    {
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定程序路径。");
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            key.SetValue(ValueName, Command(executable), RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
