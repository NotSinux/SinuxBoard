using System;
using Microsoft.Win32;

namespace SinuxBoard;

/// <summary>
/// Manages SinuxBoard's per-user Windows startup registration.
///
/// Uses:
///   HKCU\Software\Microsoft\Windows\CurrentVersion\Run
///
/// and keeps Windows' StartupApproved state synchronized so the app
/// appears as Enabled in Task Manager / Windows Startup settings.
///
/// No administrator privileges are required.
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string StartupApprovedRunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private const string ValueName = "SinuxBoard";

    // Windows uses the StartupApproved binary value to determine
    // whether a Run entry is enabled in Task Manager.
    //
    // 02 00 00 00 ... = enabled.
    private static readonly byte[] EnabledStartupApprovedValue =
    {
        0x02, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00
    };

    public static bool IsEnabled()
    {
        using RegistryKey? runKey =
            Registry.CurrentUser.OpenSubKey(
                RunKeyPath,
                writable: false);

        object? value = runKey?.GetValue(ValueName);

        if (value is not string existingPath)
        {
            return false;
        }

        bool executableMatches = string.Equals(
            existingPath.Trim('"'),
            GetExecutablePath(),
            StringComparison.OrdinalIgnoreCase);

        if (!executableMatches)
        {
            return false;
        }

        // If Windows has an explicit StartupApproved entry and it is
        // not marked as enabled, consider startup disabled.
        using RegistryKey? approvedKey =
            Registry.CurrentUser.OpenSubKey(
                StartupApprovedRunKeyPath,
                writable: false);

        if (approvedKey is null)
        {
            // No explicit approval entry yet.
            // The Run entry itself is enough to consider it enabled.
            return true;
        }

        object? approvedValue =
            approvedKey.GetValue(ValueName);

        if (approvedValue is not byte[] bytes)
        {
            return true;
        }

        return IsStartupApproved(bytes);
    }

    public static void Enable()
    {
        string executablePath = GetExecutablePath();

        // 1. Register the application in the per-user Run key.
        using (RegistryKey runKey =
               Registry.CurrentUser.CreateSubKey(
                   RunKeyPath,
                   writable: true))
        {
            runKey.SetValue(
                ValueName,
                $"\"{executablePath}\"",
                RegistryValueKind.String);
        }

        // 2. Explicitly mark the application as enabled in
        // Windows StartupApproved.
        using (RegistryKey approvedKey =
               Registry.CurrentUser.CreateSubKey(
                   StartupApprovedRunKeyPath,
                   writable: true))
        {
            approvedKey.SetValue(
                ValueName,
                EnabledStartupApprovedValue,
                RegistryValueKind.Binary);
        }
    }

    public static void Disable()
    {
        // Remove the actual Run registration.
        using (RegistryKey? runKey =
               Registry.CurrentUser.OpenSubKey(
                   RunKeyPath,
                   writable: true))
        {
            runKey?.DeleteValue(
                ValueName,
                throwOnMissingValue: false);
        }

        // Remove our StartupApproved entry as well.
        //
        // Since the application is no longer registered in Run,
        // there is no reason to keep a stale StartupApproved value.
        using (RegistryKey? approvedKey =
               Registry.CurrentUser.OpenSubKey(
                   StartupApprovedRunKeyPath,
                   writable: true))
        {
            approvedKey?.DeleteValue(
                ValueName,
                throwOnMissingValue: false);
        }
    }

    private static bool IsStartupApproved(byte[] value)
    {
        // Windows commonly uses a 12-byte binary value here.
        //
        // The first DWORD indicates the enabled/disabled state.
        // 0x02 is the normal enabled state.
        return value.Length >= 4
               && value[0] == 0x02;
    }

    private static string GetExecutablePath()
    {
        return Environment.ProcessPath
               ?? System.Diagnostics.Process
                   .GetCurrentProcess()
                   .MainModule?
                   .FileName
               ?? throw new InvalidOperationException(
                   "Unable to determine the current executable path.");
    }
}