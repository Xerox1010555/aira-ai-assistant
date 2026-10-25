using System;
using System.IO;
using System.Text;
using System.Text.Json;
using AiraAssistant.Models;

namespace AiraAssistant.Infrastructure.Logging;

/// <summary>
/// Plain, dependency-free logger. Writes rolling JSON lines to a file plus
/// a mirrored human-readable file. The configuration values are read from
/// <c>appsettings.json</c> at startup so log volume can be tuned without
/// recompiling.
/// </summary>
public sealed class FileLogger : ILogger
{
    private readonly string _logFilePath;
    private readonly string _textFilePath;
    private readonly object _writeLock = new();
    private readonly LogLevel _minimum;

    public FileLogger(string logDirectory, LogLevel minimum)
    {
        Directory.CreateDirectory(logDirectory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        _logFilePath = Path.Combine(logDirectory, $"aira-{stamp}.jsonl");
        _textFilePath = Path.Combine(logDirectory, $"aira-{stamp}.log");
        _minimum = minimum;
    }

    public void Log(LogLevel level, string category, string message, Exception? exception = null)
    {
        if (level < _minimum) return;
        var entry = new LogEntry(DateTimeOffset.UtcNow, level, category, message, exception);
        var line = JsonSerializer.Serialize(entry);
        var textLine = $"[{entry.Timestamp:yyyy-MM-ddTHH:mm:ss.fffZ}] [{level,-11}] {category} :: {message}" +
                       (exception is null ? string.Empty : $"{Environment.NewLine}{exception}");
        lock (_writeLock)
        {
            try
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
                File.AppendAllText(_textFilePath, textLine + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Logging must never throw; we silently drop if the disk is full or locked.
            }
        }
    }
}

/// <summary>
/// Convenience factory that reads the directory / level from <c>appsettings.json</c>.
/// Falls back to sane defaults if the file is missing or malformed.
/// v1.1: relative log directories resolve under %LOCALAPPDATA%\Aira (the
/// install folder is typically read-only now that the app runs without admin).
/// </summary>
public static class FileLoggerFactory
{
    public static string DefaultLogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Aira",
        "Logs");

    public static ILogger CreateFromAppConfig()
    {
        var logDir = DefaultLogDirectory;
        try
        {
            var cfg = AppConfigExtensions.LoadFromFile();
            if (!string.IsNullOrWhiteSpace(cfg.Logging.LogFileDirectory))
            {
                logDir = Path.IsPathRooted(cfg.Logging.LogFileDirectory)
                    ? cfg.Logging.LogFileDirectory
                    : Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Aira",
                        cfg.Logging.LogFileDirectory);
            }
            var level = Enum.TryParse<LogLevel>(cfg.Logging.MinimumLevel, true, out var parsed)
                ? parsed
                : LogLevel.Information;
            var logger = new FileLogger(logDir, level);
            _ = Task.Run(() => PruneOldLogs(logDir));
            return logger;
        }
        catch
        {
            return new FileLogger(logDir, LogLevel.Information);
        }
    }

    /// <summary>Best-effort housekeeping: drop log files older than 14 days, keep the folder lean forever.</summary>
    private static void PruneOldLogs(string logDir)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-14);
            foreach (var file in new DirectoryInfo(logDir).EnumerateFiles())
            {
                if (file.LastWriteTimeUtc < cutoff)
                {
                    file.Delete();
                }
            }
        }
        catch
        {
            // Never let housekeeping blow up.
        }
    }
}
