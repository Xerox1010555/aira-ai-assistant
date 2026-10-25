using System;
using System.IO;
using System.Text.Json;
using AiraAssistant.Models;

namespace AiraAssistant.Infrastructure;

/// <summary>Wraps the raw <see cref="AppConfig"/> object graph behind <see cref="IAppConfig"/>.</summary>
public sealed class AppConfigLoader : IAppConfig
{
    private readonly Models.AppConfig _config;

    public AppConfigLoader(Models.AppConfig config) { _config = config; }

    public AiConfig Ai => _config.Ai;
    public SpeechConfig Speech => _config.Speech;
    public AudioConfig Audio => _config.Audio;
    public UiConfig Ui => _config.Ui;
    public ToolsConfig Tools => _config.Tools;
    public LoggingConfig Logging => _config.Logging;
}

public static class AppConfigExtensions
{
    public static string DefaultConfigPath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    /// <summary>Loads the raw strongly-typed config; falls back to defaults when the file is missing or malformed.</summary>
    public static Models.AppConfig LoadFromFile()
    {
        var path = DefaultConfigPath;
        if (!File.Exists(path))
        {
            return new Models.AppConfig();
        }
        try
        {
            using var stream = File.OpenRead(path);
            var cfg = JsonSerializer.Deserialize<Models.AppConfig>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Models.AppConfig();
            return cfg;
        }
        catch
        {
            return new Models.AppConfig();
        }
    }

    /// <summary>Persists the settings (used by the in-app Settings dialog).</summary>
    public static void SaveToFile(Models.AppConfig cfg, string? path = null)
    {
        path ??= DefaultConfigPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            WriteIndented = true
        });
        File.WriteAllText(path, json);
    }
}
