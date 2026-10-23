using WebAppForge.Core.Model;

namespace WebAppForge.Core.Localization;

/// <summary>A machine translation service. Implementations batch, retry and fail with <see cref="TranslationException"/>.</summary>
public interface ITranslationProvider
{
    /// <summary>The value stored in <see cref="TranslationEntry.Source"/> for texts this provider produced.</summary>
    TranslationSource Source { get; }

    /// <summary>
    /// Translates <paramref name="texts"/> and returns one result per input, in the same order.
    /// <paramref name="notes"/> is optional free text (style, glossary) for the translator.
    /// </summary>
    Task<IReadOnlyList<string>> TranslateAsync(
        IReadOnlyList<string> texts, string fromLanguage, string toLanguage, string? notes, CancellationToken cancellationToken);
}

/// <summary>A translation request failed. The message never contains an API key.</summary>
public sealed class TranslationException : Exception
{
    public TranslationException(string message) : base(message)
    {
    }

    public TranslationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
