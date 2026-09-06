using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace StopwatchOverlay
{
    /// <summary>
    /// Manages the current user's Windows sign-in launch entry. HKCU is used so
    /// enabling the option never requires administrator privileges.
    /// </summary>
    public static class StartupRegistration
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "StopwatchOverlay";

        public static void SetEnabled(bool enabled)
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("Windows startup settings could not be opened.");

            if (enabled)
            {
                runKey.SetValue(ValueName, BuildLaunchCommand(), RegistryValueKind.String);
            }
            else
            {
                runKey.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }

        private static string BuildLaunchCommand()
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
                throw new InvalidOperationException("The application executable path is unavailable.");

            // Normally WPF runs through StopwatchOverlay.exe. Keep dotnet-hosted
            // development runs valid as well, without affecting single-file builds.
            var entryAssemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
            var entryAssemblyPath = string.IsNullOrWhiteSpace(entryAssemblyName)
                ? null
                : Path.Combine(AppContext.BaseDirectory, $"{entryAssemblyName}.dll");
            if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet",
                    StringComparison.OrdinalIgnoreCase)
                && entryAssemblyPath != null
                && File.Exists(entryAssemblyPath))
            {
                return $"\"{processPath}\" \"{entryAssemblyPath}\"";
            }

            return $"\"{processPath}\"";
        }
    }
}
