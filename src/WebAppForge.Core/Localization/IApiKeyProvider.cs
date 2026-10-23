using WebAppForge.Core.Model;

namespace WebAppForge.Core.Localization;

/// <summary>Supplies translation API keys. Keys are never stored in the project file and never logged.</summary>
public interface IApiKeyProvider
{
    /// <summary>The key for a provider, or null/empty when none is configured.</summary>
    string? GetApiKey(TranslationProviderKind provider);
}

/// <summary>Reads keys from the environment variables GOOGLE_TRANSLATE_API_KEY and ANTHROPIC_API_KEY.</summary>
public sealed class EnvironmentApiKeyProvider : IApiKeyProvider
{
    public const string GoogleVariable = "GOOGLE_TRANSLATE_API_KEY";
    public const string ClaudeVariable = "ANTHROPIC_API_KEY";

    public string? GetApiKey(TranslationProviderKind provider) => provider switch
    {
        TranslationProviderKind.Google => Environment.GetEnvironmentVariable(GoogleVariable),
        TranslationProviderKind.Claude => Environment.GetEnvironmentVariable(ClaudeVariable),
        _ => null,
    };

    /// <summary>Name of the environment variable for a provider, for messages.</summary>
    public static string VariableFor(TranslationProviderKind provider) => provider switch
    {
        TranslationProviderKind.Google => GoogleVariable,
        TranslationProviderKind.Claude => ClaudeVariable,
        _ => string.Empty,
    };
}
