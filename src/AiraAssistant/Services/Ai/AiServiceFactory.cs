using System;
using System.Net.Http;
using AiraAssistant.Infrastructure;
using AiraAssistant.Infrastructure.Logging;
using AiraAssistant.Models;
using Microsoft.Extensions.DependencyInjection;

namespace AiraAssistant.Services.Ai;

/// <summary>
/// Resolves the configured AI provider from <see cref="IAppConfig"/>, reads
/// the API key from appsettings.json (prefer) or the DPAPI <see cref="SecretStore"/>
/// (fallback), and returns the matching <see cref="IAiService"/>. A missing
/// key yields the graceful <see cref="UnconfiguredAiService"/> instead of
/// crashing the app.
/// </summary>
public static class AiServiceFactory
{
    public static IAiService Create(IServiceProvider sp)
    {
        var cfg = sp.GetRequiredService<IAppConfig>();
        var toolCatalog = sp.GetRequiredService<IToolCatalog>();
        var logger = sp.GetRequiredService<Infrastructure.Logging.ILogger>();
        var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
        var secretStore = sp.GetRequiredService<SecretStore>();

        var providerText = cfg.Ai.Provider?.Trim() ?? "OpenAI";
        if (!Enum.TryParse<AiProvider>(providerText, true, out var provider))
        {
            provider = AiProvider.OpenAI;
        }

        var model = string.IsNullOrWhiteSpace(cfg.Ai.Model) ? DefaultModel(provider) : cfg.Ai.Model;

        var openAiKey = EffectiveKey(cfg.Ai.OpenAiApiKey, secretStore, SecretStore.OpenAiKeyName);
        var anthropicKey = EffectiveKey(cfg.Ai.AnthropicApiKey, secretStore, SecretStore.AnthropicKeyName);

        return provider switch
        {
            AiProvider.Anthropic when anthropicKey is not null =>
                new AnthropicAiService(
                    apiKey: anthropicKey,
                    model: model,
                    systemPrompt: cfg.Ai.SystemPrompt,
                    temperature: cfg.Ai.Temperature,
                    toolCatalog: toolCatalog,
                    logger: logger,
                    httpFactory: httpFactory),

            AiProvider.Anthropic =>
                new UnconfiguredAiService("Anthropic", logger),

            _ when openAiKey is not null =>
                new OpenAiChatService(
                    apiKey: openAiKey,
                    model: model,
                    systemPrompt: cfg.Ai.SystemPrompt,
                    temperature: cfg.Ai.Temperature,
                    toolCatalog: toolCatalog,
                    logger: logger,
                    httpFactory: httpFactory),

            _ => new UnconfiguredAiService("OpenAI", logger)
        };
    }

    /// <summary>Explicit JSON key wins; otherwise the DPAPI-protected store.</summary>
    private static string? EffectiveKey(string jsonKey, SecretStore store, string storeName)
    {
        if (!string.IsNullOrWhiteSpace(jsonKey))
        {
            return jsonKey;
        }
        var stored = store.Get(storeName);
        return string.IsNullOrWhiteSpace(stored) ? null : stored;
    }

    private static string DefaultModel(AiProvider provider) => provider switch
    {
        AiProvider.Anthropic => "claude-3-5-sonnet-20241022",
        _ => "gpt-4o-mini"
    };
}
