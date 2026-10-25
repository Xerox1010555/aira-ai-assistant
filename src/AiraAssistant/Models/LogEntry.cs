using System;

namespace AiraAssistant.Models;

/// <summary>
/// A single entry written to the rolling log file. Immutable record so it's
/// safe to hand to background threads without locks.
/// </summary>
public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    Exception? Exception);
