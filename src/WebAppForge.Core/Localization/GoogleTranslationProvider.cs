using System.Net.Http.Json;
using System.Text.Json;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Localization;

/// <summary>
/// Google Cloud Translation v2 (basic). The key goes in the X-goog-api-key header only. Free text notes are not supported by
/// this service and are ignored. At most <see cref="MaxBatchSize"/> strings are sent per request.
/// </summary>
public sealed class GoogleTranslationProvider : ITranslationProvider
{
    public const string Endpoint = "https://translation.googleapis.com/language/translate/v2";
    public const int MaxBatchSize = 100;

    private readonly HttpClient _client;
    private readonly IApiKeyProvider _keys;
    private readonly TimeSpan _retryDelay;

    public GoogleTranslationProvider(HttpClient client, IApiKeyProvider keys, TimeSpan? retryBaseDelay = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _retryDelay = retryBaseDelay ?? TimeSpan.FromMilliseconds(500);
    }

    public TranslationSource Source => TranslationSource.Google;

    public async Task<IReadOnlyList<string>> TranslateAsync(
        IReadOnlyList<string> texts, string fromLanguage, string toLanguage, string? notes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var result = new List<string>(texts.Count);
        if (texts.Count == 0)
        {
            return result;
        }

        var apiKey = TranslationHttp.RequireKey(_keys, TranslationProviderKind.Google, "Google");
        for (var offset = 0; offset < texts.Count; offset += MaxBatchSize)
        {
            var batch = texts.Skip(offset).Take(MaxBatchSize).ToList();
            var body = await TranslationHttp.SendAsync(
                _client,
                () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
                    {
                        Content = JsonContent.Create(new { q = batch, source = fromLanguage, target = toLanguage, format = "text" }),
                    };
                    request.Headers.Add("X-goog-api-key", apiKey);
                    return request;
                },
                apiKey, "Google", _retryDelay, cancellationToken).ConfigureAwait(false);

            result.AddRange(Parse(body, batch.Count));
        }

        return result;
    }

    private static List<string> Parse(string body, int expected)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var translations = document.RootElement.GetProperty("data").GetProperty("translations");
            var list = new List<string>();
            foreach (var item in translations.EnumerateArray())
            {
                list.Add(item.GetProperty("translatedText").GetString() ?? string.Empty);
            }

            if (list.Count != expected)
            {
                throw new TranslationException($"Google translation returned {list.Count} texts for {expected} requested.");
            }

            return list;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new TranslationException("Google translation returned a response that could not be read.", ex);
        }
    }
}
