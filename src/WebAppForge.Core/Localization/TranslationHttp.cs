using System.Net;
using System.Text.Json;

namespace WebAppForge.Core.Localization;

/// <summary>Shared HTTP handling of the translation providers: retry with backoff, error messages without secrets.</summary>
internal static class TranslationHttp
{
    public const int MaxAttempts = 3;

    /// <summary>
    /// Sends the request built by <paramref name="createRequest"/> up to <see cref="MaxAttempts"/> times. 429 and 5xx responses
    /// (and network failures) are retried after <paramref name="baseDelay"/> * 2^(attempt-1). Returns the body of a success response.
    /// </summary>
    public static async Task<string> SendAsync(
        HttpClient client, Func<HttpRequestMessage> createRequest, string apiKey, string service, TimeSpan baseDelay, CancellationToken cancellationToken)
    {
        string lastProblem = CoreMessages.Text("Translation_NoResponse");
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var request = createRequest();
                using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return body;
                }

                var status = (int)response.StatusCode;
                lastProblem = $"HTTP {status} {response.ReasonPhrase}".TrimEnd() + ApiMessage(body, apiKey);
                var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || status >= 500;
                if (!retryable)
                {
                    throw new TranslationException(CoreMessages.Format("Translation_Failed", service, lastProblem));
                }
            }
            catch (HttpRequestException ex)
            {
                lastProblem = CoreMessages.Format("Translation_NetworkError", Redact(ex.Message, apiKey));
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastProblem = CoreMessages.Text("Translation_TimedOut");
            }

            if (attempt < MaxAttempts && baseDelay > TimeSpan.Zero)
            {
                await Task.Delay(TimeSpan.FromTicks(baseDelay.Ticks << (attempt - 1)), cancellationToken).ConfigureAwait(false);
            }
        }

        throw new TranslationException(CoreMessages.Format("Translation_FailedAfter", service, MaxAttempts, lastProblem));
    }

    /// <summary>Removes the key from a text that may echo it.</summary>
    public static string Redact(string text, string apiKey) =>
        string.IsNullOrEmpty(apiKey) ? text : text.Replace(apiKey, "***", StringComparison.Ordinal);

    /// <summary>The error message of a JSON error body (both services use error.message), shortened and redacted; empty when there is none.</summary>
    private static string ApiMessage(string body, string apiKey)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                var message = error.ValueKind == JsonValueKind.String
                    ? error.GetString()
                    : error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                if (!string.IsNullOrWhiteSpace(message))
                {
                    var shortened = message.Length > 200 ? message[..200] : message;
                    return ": " + Redact(shortened, apiKey);
                }
            }
        }
        catch (JsonException)
        {
        }

        return string.Empty;
    }

    /// <summary>The key, or a TranslationException that names the environment variable but never a value.</summary>
    public static string RequireKey(IApiKeyProvider keys, WebAppForge.Core.Model.TranslationProviderKind provider, string service)
    {
        var key = keys.GetApiKey(provider);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new TranslationException(CoreMessages.Format("Translation_NoKey", service, EnvironmentApiKeyProvider.VariableFor(provider)));
        }

        return key.Trim();
    }
}
