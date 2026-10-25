using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Localization;

/// <summary>
/// Translation through the Anthropic Messages API. The key goes in the x-api-key header only. At most
/// <see cref="MaxBatchSize"/> strings are sent per request; the model answers with a strict JSON array of strings.
/// </summary>
public sealed class ClaudeTranslationProvider : ITranslationProvider
{
    public const string Endpoint = "https://api.anthropic.com/v1/messages";
    public const string ApiVersion = "2023-06-01";
    public const string DefaultModel = "claude-haiku-5-5";
    public const int MaxBatchSize = 50;

    internal const string SystemPrompt =
        "You are a professional software localization translator. You receive numbered strings of a web application's user interface. "
        + "Translate each numbered string from the source language to the target language. Preserve placeholders such as {0}, "
        + "HTML tags and punctuation exactly, and do not add explanations. "
        + "Answer with a strict JSON array of strings only, with exactly one translated string per input, in the same order, and no other text.";

    private static readonly JsonSerializerOptions PromptJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly HttpClient _client;
    private readonly IApiKeyProvider _keys;
    private readonly string _model;
    private readonly TimeSpan _retryDelay;

    public ClaudeTranslationProvider(HttpClient client, IApiKeyProvider keys, string? model = null, TimeSpan? retryBaseDelay = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
        _retryDelay = retryBaseDelay ?? TimeSpan.FromMilliseconds(500);
    }

    public TranslationSource Source => TranslationSource.Claude;

    public async Task<IReadOnlyList<string>> TranslateAsync(
        IReadOnlyList<string> texts, string fromLanguage, string toLanguage, string? notes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var result = new List<string>(texts.Count);
        if (texts.Count == 0)
        {
            return result;
        }

        var apiKey = TranslationHttp.RequireKey(_keys, TranslationProviderKind.Claude, "Claude");
        for (var offset = 0; offset < texts.Count; offset += MaxBatchSize)
        {
            var batch = texts.Skip(offset).Take(MaxBatchSize).ToList();
            var prompt = BuildPrompt(batch, fromLanguage, toLanguage, notes);
            var body = await TranslationHttp.SendAsync(
                _client,
                () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
                    {
                        Content = JsonContent.Create(new
                        {
                            model = _model,
                            max_tokens = 8192,
                            system = SystemPrompt,
                            messages = new[] { new { role = "user", content = prompt } },
                        }),
                    };
                    request.Headers.Add("x-api-key", apiKey);
                    request.Headers.Add("anthropic-version", ApiVersion);
                    return request;
                },
                apiKey, "Claude", _retryDelay, cancellationToken).ConfigureAwait(false);

            result.AddRange(Parse(body, batch.Count));
        }

        return result;
    }

    internal static string BuildPrompt(IReadOnlyList<string> texts, string fromLanguage, string toLanguage, string? notes)
    {
        var sb = new StringBuilder();
        sb.Append("Source language: ").Append(fromLanguage).Append('\n');
        sb.Append("Target language: ").Append(toLanguage).Append('\n');
        if (!string.IsNullOrWhiteSpace(notes))
        {
            sb.Append("Notes from the project owner (style or glossary): ").Append(notes.Trim()).Append('\n');
        }

        sb.Append("Strings (each is a JSON string literal):\n");
        for (var i = 0; i < texts.Count; i++)
        {
            sb.Append(i + 1).Append(". ").Append(JsonSerializer.Serialize(texts[i], PromptJson)).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>Reads the model answer defensively: tolerates code fences and surrounding text, and demands the right count of strings.</summary>
    internal static List<string> Parse(string body, int expected)
    {
        string text;
        try
        {
            using var document = JsonDocument.Parse(body);
            var builder = new StringBuilder();
            foreach (var block in document.RootElement.GetProperty("content").EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) && type.GetString() == "text")
                {
                    builder.Append(block.GetProperty("text").GetString());
                }
            }

            text = builder.ToString();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new TranslationException(CoreMessages.Format("Translation_Unreadable", "Claude"), ex);
        }

        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            throw new TranslationException(CoreMessages.Format("Translation_NotArray", "Claude"));
        }

        List<string> list;
        try
        {
            using var array = JsonDocument.Parse(text[start..(end + 1)]);
            if (array.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new TranslationException(CoreMessages.Format("Translation_NotArray", "Claude"));
            }

            list = [];
            foreach (var item in array.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    throw new TranslationException(CoreMessages.Format("Translation_NotStrings", "Claude"));
                }

                list.Add(item.GetString() ?? string.Empty);
            }
        }
        catch (JsonException ex)
        {
            throw new TranslationException(CoreMessages.Format("Translation_InvalidJson", "Claude"), ex);
        }

        if (list.Count != expected)
        {
            throw new TranslationException(CoreMessages.Format("Translation_WrongCount", "Claude", list.Count, expected));
        }

        return list;
    }
}
