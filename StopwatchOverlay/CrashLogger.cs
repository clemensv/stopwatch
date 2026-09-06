using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;

namespace StopwatchOverlay;

internal static class CrashLogger
{
    private const int MaximumEntryCharacters = 60_000;
    private const int RetainedLogCount = 10;
    private static int _writeInProgress;
    private static string _lastAction = "Application startup";
    private static string _lastSettingsCategory = "Not open";
    private static string _lastOpenWindowTypes = "Unavailable";

    internal static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StopwatchOverlay",
        "Logs");

    internal static void RecordUiAction(string action, string? settingsCategory = null)
    {
        Interlocked.Exchange(ref _lastAction, NormalizeContextToken(action, "Unavailable"));
        if (!string.IsNullOrWhiteSpace(settingsCategory))
        {
            Interlocked.Exchange(
                ref _lastSettingsCategory,
                NormalizeContextToken(settingsCategory, "Not open"));
        }
    }

    internal static void LogFatal(Exception exception, string origin, bool isTerminating)
        => TryWrite(exception, origin, isTerminating);

    internal static void LogUnhandledObject(object? exceptionObject, string origin, bool isTerminating)
    {
        Exception exception = exceptionObject as Exception
            ?? new InvalidOperationException(
                $"Unhandled runtime object of type {exceptionObject?.GetType().FullName ?? "null"}.");
        TryWrite(exception, origin, isTerminating);
    }

    internal static void LogRecoverable(Exception exception, string origin)
        => TryWrite(exception, origin, isTerminating: false);

    internal static bool TryWrite(
        Exception exception,
        string origin,
        bool isTerminating,
        string? directoryOverride = null,
        int retainedLogCount = RetainedLogCount)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Interlocked.CompareExchange(ref _writeInProgress, 1, 0) != 0)
            return false;

        try
        {
            RefreshWindowSnapshot();
            string directory = directoryOverride ?? LogDirectory;
            Directory.CreateDirectory(directory);

            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss.fffffff");
            string fileName = $"crash-{timestamp}-p{Environment.ProcessId}-t{Environment.CurrentManagedThreadId}-{Guid.NewGuid():N}.log";
            string path = Path.Combine(directory, fileName);
            string content = BuildEntry(exception, origin, isTerminating);
            if (content.Length > MaximumEntryCharacters)
            {
                content = content[..MaximumEntryCharacters]
                    + Environment.NewLine
                    + "[entry truncated]"
                    + Environment.NewLine;
            }

            using (var stream = new FileStream(
                       path,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.Read,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            RetainNewestLogs(directory, Math.Max(1, retainedLogCount));
            return true;
        }
        catch
        {
            // Logging is diagnostic and must never replace the original failure.
            return false;
        }
        finally
        {
            Volatile.Write(ref _writeInProgress, 0);
        }
    }

    private static string BuildEntry(Exception exception, string origin, bool isTerminating)
    {
        var text = new StringBuilder();
        text.AppendLine("Stopwatch Overlay diagnostic event");
        text.AppendLine($"TimestampUtc: {DateTime.UtcNow:O}");
        text.AppendLine($"AppVersion: {typeof(App).Assembly.GetName().Version}");
        text.AppendLine($"Origin: {NormalizeContextToken(origin, "Unknown")}");
        text.AppendLine($"Terminating: {isTerminating}");
        text.AppendLine($"Process: {Process.GetCurrentProcess().ProcessName} ({Environment.ProcessId})");
        text.AppendLine($"ManagedThreadId: {Environment.CurrentManagedThreadId}");
        text.AppendLine($"IsThreadPoolThread: {Thread.CurrentThread.IsThreadPoolThread}");
        text.AppendLine($"Theme: {AppThemeManager.CurrentTheme}");
        text.AppendLine($"OpenWindowTypes: {Volatile.Read(ref _lastOpenWindowTypes)}");
        text.AppendLine($"SettingsCategory: {Volatile.Read(ref _lastSettingsCategory)}");
        text.AppendLine($"LastUiAction: {Volatile.Read(ref _lastAction)}");

        int depth = 0;
        for (Exception? current = exception; current != null && depth < 8; current = current.InnerException)
        {
            text.AppendLine();
            text.AppendLine($"Exception[{depth}].Type: {current.GetType().FullName}");
            text.AppendLine($"Exception[{depth}].Message: {current.Message}");
            if (!string.IsNullOrWhiteSpace(current.StackTrace))
            {
                text.AppendLine($"Exception[{depth}].StackTrace:");
                text.AppendLine(current.StackTrace);
            }
            depth++;
        }
        if (depth == 8 && exception.InnerException != null)
            text.AppendLine("[inner exception chain truncated]");

        return RedactPrivatePaths(text.ToString());
    }

    private static void RefreshWindowSnapshot()
    {
        try
        {
            Application? application = Application.Current;
            if (application == null || !application.Dispatcher.CheckAccess())
                return;

            string[] windowTypes = application.Windows
                .OfType<Window>()
                .Where(window => window.IsLoaded)
                .Select(window => window.GetType().Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Interlocked.Exchange(
                ref _lastOpenWindowTypes,
                windowTypes.Length == 0 ? "None" : string.Join(",", windowTypes));
        }
        catch
        {
            // Keep the last safe UI-thread snapshot.
        }
    }

    private static string NormalizeContextToken(string? value, string fallback)
    {
        string normalized = new((value ?? "")
            .Where(character => !char.IsControl(character))
            .ToArray());
        normalized = normalized.Trim();
        if (normalized.Length == 0)
            return fallback;
        return normalized[..Math.Min(normalized.Length, 160)];
    }

    private static string RedactPrivatePaths(string value)
    {
        (string Path, string Token)[] roots =
        [
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%")
        ];

        string redacted = value;
        foreach ((string root, string token) in roots
                     .Where(item => !string.IsNullOrWhiteSpace(item.Path))
                     .OrderByDescending(item => item.Path.Length))
        {
            redacted = redacted.Replace(root, token, StringComparison.OrdinalIgnoreCase);
        }
        redacted = Regex.Replace(
            redacted,
            @"%(?:LOCALAPPDATA|APPDATA|USERPROFILE)%[\\/][^\r\n]+",
            match => match.Value[..match.Value.IndexOfAny(['\\', '/'])] + @"\[redacted]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        redacted = Regex.Replace(
            redacted,
            @"(?<![A-Za-z0-9_])[A-Za-z]:[\\/][^\r\n]+",
            "[redacted path]",
            RegexOptions.CultureInvariant);
        redacted = Regex.Replace(
            redacted,
            @"\\\\[^\r\n]+",
            "[redacted network path]",
            RegexOptions.CultureInvariant);
        return redacted;
    }

    private static void RetainNewestLogs(string directory, int retainedLogCount)
    {
        FileInfo[] obsolete = new DirectoryInfo(directory)
            .GetFiles("crash-*.log", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .Skip(retainedLogCount)
            .ToArray();
        foreach (FileInfo file in obsolete)
        {
            try
            {
                file.Delete();
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // Rotation failure must not prevent the diagnostic event itself.
            }
        }
    }
}
