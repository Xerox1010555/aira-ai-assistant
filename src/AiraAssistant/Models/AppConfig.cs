namespace AiraAssistant.Models;

/// <summary>
/// Strongly-typed application settings. Populated by <c>AppConfigExtensions.LoadFromFile</c>
/// at startup. Anything that needs to be tweaked without recompiling lives here.
/// Every key in this file is actually read by the app (no dead config).
/// </summary>
public sealed class AppConfig
{
    public AiConfig Ai { get; set; } = new();
    public SpeechConfig Speech { get; set; } = new();
    public AudioConfig Audio { get; set; } = new();
    public UiConfig Ui { get; set; } = new();
    public ToolsConfig Tools { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();
}

public sealed class AiConfig
{
    public string Provider { get; set; } = "OpenAI";
    public string Model { get; set; } = "gpt-4o-mini";
    public double Temperature { get; set; } = 0.4;
    public string OpenAiApiKey { get; set; } = string.Empty;
    public string AnthropicApiKey { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
}

public sealed class SpeechConfig
{
    public string SttCulture { get; set; } = "en-US";
    public string TtsCulture { get; set; } = "en-US";
    public string TtsVoiceName { get; set; } = string.Empty;
    public int TtsRate { get; set; } = 0;          // -10..10 (System.Speech Rate)
    public int TtsVolume { get; set; } = 100;      // 0..100
    public bool BargeInEnabled { get; set; } = true;
}

public sealed class AudioConfig
{
    public string InputDeviceName { get; set; } = string.Empty;
    public int SampleRate { get; set; } = 16000;
    public int Channels { get; set; } = 1;
}

public sealed class UiConfig
{
    public string Theme { get; set; } = "Midnight Violet";
    public bool AlwaysOnTop { get; set; }
}

public sealed class ToolsConfig
{
    /// <summary>When true, destructive tools (shutdown/sleep/lock/type/clipboard-write) show a confirmation dialog first.</summary>
    public bool RequireConfirmation { get; set; } = true;
}

public sealed class LoggingConfig
{
    public string LogFileDirectory { get; set; } = "Logs";
    public string MinimumLevel { get; set; } = "Information";
}
