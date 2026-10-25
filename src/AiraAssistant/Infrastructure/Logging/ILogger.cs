using System;

namespace AiraAssistant.Infrastructure.Logging;

/// <summary>
/// Lightweight application logger abstraction. Implementations are free to
/// fan out to file sinks, debug streams, or the in-app status panel.
/// </summary>
public interface ILogger
{
    void Log(Models.LogLevel level, string category, string message, Exception? exception = null);
    void Trace(string message) => Log(Models.LogLevel.Trace, "app", message);
    void Debug(string message) => Log(Models.LogLevel.Debug, "app", message);
    void Information(string message) => Log(Models.LogLevel.Information, "app", message);
    void Warning(string message) => Log(Models.LogLevel.Warning, "app", message);
    void Error(string message, Exception? ex = null) => Log(Models.LogLevel.Error, "app", message, ex);
    void Critical(string message, Exception? ex = null) => Log(Models.LogLevel.Critical, "app", message, ex);
}
