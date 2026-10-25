using AiraAssistant.Models;

namespace AiraAssistant.Infrastructure;

/// <summary>
/// Marker interface for the strongly-typed configuration object so services
/// can depend on it without pulling the concrete <see cref="AppConfig"/>.
/// </summary>
public interface IAppConfig
{
    AiConfig Ai { get; }
    SpeechConfig Speech { get; }
    AudioConfig Audio { get; }
    UiConfig Ui { get; }
    ToolsConfig Tools { get; }
    LoggingConfig Logging { get; }
}
