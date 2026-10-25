namespace AiraAssistant.Models;

/// <summary>
/// Available AI providers. <see cref="AiServiceFactory"/> inspects the configured
/// provider and instantiates the corresponding implementation.
/// </summary>
public enum AiProvider
{
    OpenAI,
    Anthropic
}
