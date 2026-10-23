using System.Net;
using System.Text;
using System.Text.Json;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Localization;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

public class LocalizationTests
{
    private const string SecretKey = "sk-test-SECRET-1234567890";

    // ---- helpers --------------------------------------------------------------------------------

    private sealed class FixedKeys(string? key) : IApiKeyProvider
    {
        public string? GetApiKey(TranslationProviderKind provider) => key;
    }

    private sealed record Seen(HttpMethod Method, Uri? Uri, Dictionary<string, string> Headers, string Body);

    private sealed class FakeHandler(Func<Seen, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var seen = new Seen(request.Method, request.RequestUri, headers, body);
            Requests.Add(seen);
            return respond(seen, Requests.Count);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string GoogleReply(Seen seen)
    {
        var q = JsonDocument.Parse(seen.Body).RootElement.GetProperty("q").EnumerateArray().Select(e => e.GetString()!).ToList();
        return JsonSerializer.Serialize(new { data = new { translations = q.Select(t => new { translatedText = "hu:" + t }) } });
    }

    private static string ClaudeReply(params string[] translated) =>
        JsonSerializer.Serialize(new { content = new[] { new { type = "text", text = JsonSerializer.Serialize(translated) } } });

    private static List<string> Texts(int count) => Enumerable.Range(1, count).Select(i => "text " + i).ToList();

    private static Project SampleProject(bool enabled)
    {
        var project = new Project
        {
            Name = "shop",
            Database = new DatabaseSettings { Provider = "sqlite", Path = "shop.db" },
        };
        project.Tables.Add(new TableInfo
        {
            Name = "customers",
            Caption = "Customers",
            Fields =
            {
                new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true, IsAutoIncrement = true, IsRequired = true },
                new FieldInfo { Name = "name", Caption = "Name", Type = FieldType.Text, IsRequired = true },
                new FieldInfo { Name = "active", Type = FieldType.Boolean },
            },
        });
        if (enabled)
        {
            project.Localization.Enabled = true;
            project.Localization.Languages.Add("hu");
            project.Localization.Languages.Add("ar");
            project.Localization.SetManual("table.customers", "hu", "Ügyfelek");
            project.Localization.SetManual("field.customers.name", "hu", "Név");
        }

        return project;
    }

    private static string GenerateTo(Project project, out string output)
    {
        output = Path.Combine(Path.GetTempPath(), $"gen-{Guid.NewGuid():N}");
        new ProjectGenerator().Generate(project, output, output);
        return output;
    }

    // ---- model and XML --------------------------------------------------------------------------

    [Fact]
    public void DefaultsAreOffAndEnglish()
    {
        var settings = new Project().Localization;

        Assert.False(settings.Enabled);
        Assert.Equal("en", settings.DefaultLanguage);
        Assert.False(settings.AutoTranslate);
        Assert.Equal(TranslationProviderKind.None, settings.TranslationProvider);
        Assert.Equal(string.Empty, settings.TranslationNotes);
        Assert.Empty(settings.Entries);
        Assert.Equal(["en"], settings.AllLanguages);
    }

    [Fact]
    public void AllLanguagesAlwaysContainsTheDefaultFirstWithoutDuplicates()
    {
        var settings = new LocalizationSettings { DefaultLanguage = "hu" };
        settings.Languages.AddRange(["en", "HU", "en"]);

        Assert.Equal(["hu", "en"], settings.AllLanguages);
    }

    [Fact]
    public void XmlRoundTripKeepsSettingsAndEntries()
    {
        var project = new Project { Name = "shop", ProjectId = Guid.NewGuid() };
        var settings = project.Localization;
        settings.Enabled = true;
        settings.DefaultLanguage = "en";
        settings.Languages.Add("hu");
        settings.AutoTranslate = true;
        settings.TranslationProvider = TranslationProviderKind.Claude;
        settings.TranslationNotes = "Formal tone & <short>";
        settings.SetManual("table.customers", "hu", "Ügyfelek <b>&</b>");
        settings.Entries.Add(new TranslationEntry
        {
            Key = "ui.add", Language = "hu", Text = "Hozzáad", Source = TranslationSource.Google, Reviewed = false, SourceHash = TranslationEntry.HashOf("Add"),
        });

        var xml = ProjectFile.Write(project);
        var entryElement = xml.Root!.Element("localization")!.Elements("entry").First();
        Assert.Equal("table.customers", entryElement.Attribute("key")!.Value);
        Assert.Equal("hu", entryElement.Attribute("language")!.Value);
        Assert.Equal("Manual", entryElement.Attribute("source")!.Value);
        Assert.Equal("true", entryElement.Attribute("reviewed")!.Value);

        var loaded = ProjectFile.Read(xml).Localization;
        Assert.True(loaded.Enabled);
        Assert.True(loaded.AutoTranslate);
        Assert.Equal(TranslationProviderKind.Claude, loaded.TranslationProvider);
        Assert.Equal("Formal tone & <short>", loaded.TranslationNotes);
        Assert.Equal(["en", "hu"], loaded.AllLanguages);
        Assert.Equal(2, loaded.Entries.Count);
        Assert.Equal("Ügyfelek <b>&</b>", loaded.Find("table.customers", "hu")!.Text);
        var google = loaded.Find("ui.add", "hu")!;
        Assert.Equal(TranslationSource.Google, google.Source);
        Assert.False(google.Reviewed);
        Assert.Equal(TranslationEntry.HashOf("Add"), google.SourceHash);
    }

    [Fact]
    public void DefaultSettingsWriteNoLocalizationElementAndOldFilesLoadWithDefaults()
    {
        var xml = ProjectFile.Write(new Project { Name = "shop" });
        Assert.Null(xml.Root!.Element("localization"));

        var loaded = ProjectFile.Read(xml);
        Assert.False(loaded.Localization.Enabled);
        Assert.Empty(loaded.Localization.Entries);
    }

    [Fact]
    public void ProjectFileNeverContainsApiKeys()
    {
        var project = new Project { Name = "shop" };
        project.Localization.Enabled = true;
        project.Localization.AutoTranslate = true;
        project.Localization.TranslationProvider = TranslationProviderKind.Google;

        var text = ProjectFile.Write(project).ToString();

        Assert.DoesNotContain("apikey", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api_key", text, StringComparison.OrdinalIgnoreCase);
    }

    // ---- generation -----------------------------------------------------------------------------

    [Fact]
    public void DisabledLocalizationGeneratesTheClassicSite()
    {
        GenerateTo(SampleProject(enabled: false), out var output);
        try
        {
            Assert.False(Directory.Exists(Path.Combine(output, "Localization")));
            var pages = File.ReadAllText(Path.Combine(output, "Pages", "CustomersPages.cs"));
            Assert.Contains("private const string Caption = \"Customers\";", pages);
            Assert.Contains("Html.Link(Route + \"/add\", \"Add\", \"btn btn-sm btn-primary\")", pages);
            Assert.DoesNotContain("Strings.", pages);
            var html = File.ReadAllText(Path.Combine(output, "Web", "Html.cs"));
            Assert.Contains("<html lang=\\\"en\\\">", html);
            Assert.DoesNotContain("Strings.", html);
            Assert.DoesNotContain("MapSetLanguage", File.ReadAllText(Path.Combine(output, "Program.cs")));
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void EnabledLocalizationWritesResourcesHelperAndLocalizedPages()
    {
        GenerateTo(SampleProject(enabled: true), out var output);
        try
        {
            var en = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "Localization", "strings.en.json"))).RootElement;
            var hu = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "Localization", "strings.hu.json"))).RootElement;
            Assert.True(File.Exists(Path.Combine(output, "Localization", "strings.ar.json")));
            Assert.Equal("Customers", en.GetProperty("table.customers").GetString());
            Assert.Equal("Add", en.GetProperty("ui.add").GetString());
            Assert.Equal("Ügyfelek", hu.GetProperty("table.customers").GetString());
            Assert.Equal("Név", hu.GetProperty("field.customers.name").GetString());
            // Not translated yet: the key is absent, so the page falls back to the English text.
            Assert.False(hu.TryGetProperty("ui.add", out _));
            Assert.Equal("Delete this record? This cannot be undone.", en.GetProperty("ui.confirmDelete").GetString());

            var helper = File.ReadAllText(Path.Combine(output, "Localization", "Strings.cs"));
            Assert.Contains("public static string T(string key, string fallback)", helper);
            Assert.Contains("CookieName = \"lang\"", helper);
            Assert.Contains("\"/setlanguage\"", helper);
            Assert.Contains("GetTypedHeaders().AcceptLanguage", helper);
            Assert.Contains("{ \"en\", \"hu\", \"ar\" }", helper);
            Assert.Contains("{ \"ar\", \"he\", \"fa\", \"ur\" }", helper);
            Assert.Contains("dir=\\\"rtl\\\"", helper);

            var pages = File.ReadAllText(Path.Combine(output, "Pages", "CustomersPages.cs"));
            Assert.Contains("private static string Caption => Strings.T(\"table.customers\", \"Customers\");", pages);
            Assert.Contains("Strings.T(\"field.customers.name\", \"Name\")", pages);
            Assert.Contains("Strings.T(\"ui.add\", \"Add\")", pages);
            Assert.Contains("Strings.F(\"ui.required\"", pages);

            var html = File.ReadAllText(Path.Combine(output, "Web", "Html.cs"));
            Assert.Contains("Strings.Switcher()", html);
            Assert.Contains("Strings.DirAttribute", html);

            var program = File.ReadAllText(Path.Combine(output, "Program.cs"));
            Assert.Contains("app.Use(Strings.Middleware);", program);
            Assert.Contains("app.MapSetLanguage();", program);
            Assert.Contains("Strings.T(\"table.customers\", \"Customers\")", program);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void GeneratingAfterALocalizedSiteDoesNotLeakIntoTheNextRun()
    {
        GenerateTo(SampleProject(enabled: true), out var first);
        GenerateTo(SampleProject(enabled: false), out var second);
        try
        {
            Assert.DoesNotContain("Strings.", File.ReadAllText(Path.Combine(second, "Pages", "CustomersPages.cs")));
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }

    [Fact]
    public void InvalidLanguageCodeIsRejected()
    {
        var project = SampleProject(enabled: true);
        project.Localization.Languages.Add("../evil");
        var output = Path.Combine(Path.GetTempPath(), $"gen-{Guid.NewGuid():N}");

        Assert.Throws<InvalidOperationException>(() => new ProjectGenerator().Generate(project, output, output));
        Assert.False(Directory.Exists(output));
    }

    // ---- translation service --------------------------------------------------------------------

    private sealed class CountingProvider : ITranslationProvider
    {
        public List<(IReadOnlyList<string> Texts, string From, string To, string? Notes)> Calls { get; } = [];

        public TranslationSource Source => TranslationSource.Google;

        public Task<IReadOnlyList<string>> TranslateAsync(
            IReadOnlyList<string> texts, string fromLanguage, string toLanguage, string? notes, CancellationToken cancellationToken)
        {
            Calls.Add((texts, fromLanguage, toLanguage, notes));
            return Task.FromResult<IReadOnlyList<string>>(texts.Select(t => toLanguage + ":" + t).ToList());
        }
    }

    [Fact]
    public async Task ServiceRefusesToRunUnlessAutoTranslateIsOn()
    {
        var provider = new CountingProvider();
        var project = SampleProject(enabled: true);
        project.Localization.AutoTranslate = false;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new TranslationService(provider).TranslateAsync(project, null, CancellationToken.None));
        Assert.Empty(provider.Calls);
    }

    [Fact]
    public async Task ServiceTranslatesMissingEntriesAndSkipsReviewedAndManualOnes()
    {
        var provider = new CountingProvider();
        var project = SampleProject(enabled: true);
        var settings = project.Localization;
        settings.AutoTranslate = true;
        settings.TranslationNotes = "Use formal tone";
        settings.Languages.Remove("ar");
        // A reviewed provider entry whose source also changed: still never overwritten.
        settings.Entries.Add(new TranslationEntry
        {
            Key = "ui.add", Language = "hu", Text = "Hozzáad (jóváhagyott)", Source = TranslationSource.Google, Reviewed = true, SourceHash = "old",
        });

        var result = await new TranslationService(provider).TranslateAsync(project, ["hu"], CancellationToken.None);

        Assert.Equal("Hozzáad (jóváhagyott)", settings.Find("ui.add", "hu")!.Text);
        Assert.Equal("Ügyfelek", settings.Find("table.customers", "hu")!.Text);
        Assert.Equal(TranslationSource.Manual, settings.Find("table.customers", "hu")!.Source);
        Assert.Equal(3, result.Protected);
        Assert.Equal("hu:Edit", settings.Find("ui.edit", "hu")!.Text);
        var made = settings.Find("ui.edit", "hu")!;
        Assert.Equal(TranslationSource.Google, made.Source);
        Assert.False(made.Reviewed);
        Assert.Equal(TranslationEntry.HashOf("Edit"), made.SourceHash);

        // UI strings are English; table and field captions are in the default language (also English here). One call per source language.
        Assert.All(provider.Calls, call => Assert.Equal("en", call.From));
        Assert.All(provider.Calls, call => Assert.Equal("Use formal tone", call.Notes));
        Assert.DoesNotContain(provider.Calls.SelectMany(c => c.Texts), t => t == "Customers" || t == "Name");
        Assert.DoesNotContain(provider.Calls.SelectMany(c => c.Texts), t => t == "Add");
        Assert.Contains(provider.Calls.SelectMany(c => c.Texts), t => t == "active");
    }

    [Fact]
    public async Task ServiceRetranslatesOnlyWhenTheSourceChanged()
    {
        var provider = new CountingProvider();
        var project = SampleProject(enabled: true);
        var settings = project.Localization;
        settings.AutoTranslate = true;
        settings.Languages.Remove("ar");
        var service = new TranslationService(provider);

        await service.TranslateAsync(project, ["hu"], CancellationToken.None);
        var firstCalls = provider.Calls.Count;
        var again = await service.TranslateAsync(project, ["hu"], CancellationToken.None);

        Assert.Equal(firstCalls, provider.Calls.Count);
        Assert.Equal(0, again.Translated);
        Assert.True(again.UpToDate > 0);

        // Edit the source caption of a field that was machine translated: the translation becomes stale and is redone.
        var active = project.Tables[0].Fields.First(f => f.Name == "active");
        Assert.Equal("hu:active", settings.Find("field.customers.active", "hu")!.Text);
        active.Caption = "Is active";
        var third = await service.TranslateAsync(project, ["hu"], CancellationToken.None);

        Assert.Equal(1, third.Translated);
        Assert.Equal("hu:Is active", settings.Find("field.customers.active", "hu")!.Text);
        Assert.Equal(TranslationEntry.HashOf("Is active"), settings.Find("field.customers.active", "hu")!.SourceHash);

        // A stale but reviewed translation is left alone.
        settings.Find("field.customers.active", "hu")!.Reviewed = true;
        active.Caption = "Enabled";
        var fourth = await service.TranslateAsync(project, ["hu"], CancellationToken.None);

        Assert.Equal(0, fourth.Translated);
        Assert.Equal("hu:Is active", settings.Find("field.customers.active", "hu")!.Text);
    }

    [Fact]
    public async Task ServiceTranslatesDefaultLanguageCaptionsFromTheDefaultLanguage()
    {
        var provider = new CountingProvider();
        var project = SampleProject(enabled: false);
        var settings = project.Localization;
        settings.Enabled = true;
        settings.DefaultLanguage = "hu";
        settings.Languages.Add("en");
        settings.AutoTranslate = true;

        await new TranslationService(provider).TranslateAsync(project, ["en"], CancellationToken.None);

        // Captions are written in Hungarian here, so they are translated from hu; the UI strings are already English.
        var call = Assert.Single(provider.Calls);
        Assert.Equal("hu", call.From);
        Assert.Equal("en", call.To);
        Assert.Contains("Customers", call.Texts);
        Assert.Equal("en:Customers", settings.Find("table.customers", "en")!.Text);
        Assert.Null(settings.Find("ui.add", "en"));
    }

    // ---- providers ------------------------------------------------------------------------------

    [Fact]
    public async Task GoogleRequestAndResponseShape()
    {
        var handler = new FakeHandler((seen, _) => Json(HttpStatusCode.OK, GoogleReply(seen)));
        var provider = new GoogleTranslationProvider(new HttpClient(handler), new FixedKeys(SecretKey), TimeSpan.Zero);

        var result = await provider.TranslateAsync(["Add", "Save"], "en", "hu", "ignored", CancellationToken.None);

        Assert.Equal(["hu:Add", "hu:Save"], result);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://translation.googleapis.com/language/translate/v2", request.Uri!.ToString());
        Assert.Equal(SecretKey, request.Headers["X-goog-api-key"]);
        var body = JsonDocument.Parse(request.Body).RootElement;
        Assert.Equal(["Add", "Save"], body.GetProperty("q").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("en", body.GetProperty("source").GetString());
        Assert.Equal("hu", body.GetProperty("target").GetString());
        Assert.Equal("text", body.GetProperty("format").GetString());
        Assert.DoesNotContain(SecretKey, request.Body);
        Assert.DoesNotContain(SecretKey, request.Uri.ToString());
        Assert.Equal(TranslationSource.Google, provider.Source);
    }

    [Fact]
    public async Task GoogleSendsAtMostOneHundredStringsPerRequest()
    {
        var handler = new FakeHandler((seen, _) => Json(HttpStatusCode.OK, GoogleReply(seen)));
        var provider = new GoogleTranslationProvider(new HttpClient(handler), new FixedKeys(SecretKey), TimeSpan.Zero);

        var result = await provider.TranslateAsync(Texts(250), "en", "hu", null, CancellationToken.None);

        Assert.Equal(250, result.Count);
        Assert.Equal("hu:text 250", result[249]);
        var sizes = handler.Requests.Select(r => JsonDocument.Parse(r.Body).RootElement.GetProperty("q").GetArrayLength()).ToList();
        Assert.Equal([100, 100, 50], sizes);
    }

    [Fact]
    public async Task ClaudeRequestAndResponseShape()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, ClaudeReply("Hozzáad {0}", "<b>Ment</b>")));
        var provider = new ClaudeTranslationProvider(new HttpClient(handler), new FixedKeys(SecretKey), retryBaseDelay: TimeSpan.Zero);

        var result = await provider.TranslateAsync(["Add {0}", "<b>Save</b>"], "en", "hu", "Formal", CancellationToken.None);

        Assert.Equal(["Hozzáad {0}", "<b>Ment</b>"], result);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.anthropic.com/v1/messages", request.Uri!.ToString());
        Assert.Equal(SecretKey, request.Headers["x-api-key"]);
        Assert.Equal("2023-06-01", request.Headers["anthropic-version"]);
        var body = JsonDocument.Parse(request.Body).RootElement;
        Assert.Equal("claude-haiku-5-5", body.GetProperty("model").GetString());
        var system = body.GetProperty("system").GetString()!;
        Assert.Contains("placeholders", system);
        Assert.Contains("JSON array", system);
        var message = body.GetProperty("messages")[0];
        Assert.Equal("user", message.GetProperty("role").GetString());
        var prompt = message.GetProperty("content").GetString()!;
        Assert.Contains("Formal", prompt);
        Assert.Contains("1. \"Add {0}\"", prompt);
        Assert.Contains("2. \"<b>Save</b>\"", prompt);
        Assert.DoesNotContain(SecretKey, request.Body);
        Assert.Equal(TranslationSource.Claude, provider.Source);
    }

    [Fact]
    public async Task ClaudeModelIsConfigurable()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, ClaudeReply("a")));
        var provider = new ClaudeTranslationProvider(new HttpClient(handler), new FixedKeys(SecretKey), "my-model", TimeSpan.Zero);

        await provider.TranslateAsync(["x"], "en", "hu", null, CancellationToken.None);

        Assert.Equal("my-model", JsonDocument.Parse(handler.Requests[0].Body).RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task ClaudeAnswerWrappedInACodeFenceIsParsed()
    {
        var text = "Here you go:\n```json\n[\"egy\", \"kettő\"]\n```";
        var reply = JsonSerializer.Serialize(new { content = new[] { new { type = "text", text } } });
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, reply));
        var provider = new ClaudeTranslationProvider(new HttpClient(handler), new FixedKeys(SecretKey), retryBaseDelay: TimeSpan.Zero);

        var result = await provider.TranslateAsync(["one", "two"], "en", "hu", null, CancellationToken.None);

        Assert.Equal(["egy", "kettő"], result);
    }

    [Fact]
    public async Task ClaudeCountMismatchFailsClearly()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, ClaudeReply("only one")));
        var provider = new ClaudeTranslationProvider(new HttpClient(handler), new FixedKeys(SecretKey), retryBaseDelay: TimeSpan.Zero);

        var ex = await Assert.ThrowsAsync<TranslationException>(
            () => provider.TranslateAsync(["one", "two", "three"], "en", "hu", null, CancellationToken.None));

        Assert.Contains("1 texts for 3 requested", ex.Message);
        Assert.DoesNotContain(SecretKey, ex.Message);
    }

    [Fact]
    public async Task ClaudeAnswerThatIsNotJsonFails()
    {
        var reply = JsonSerializer.Serialize(new { content = new[] { new { type = "text", text = "Sorry, I cannot do that." } } });
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, reply));
        var provider = new ClaudeTranslationProvider(new HttpClient(handler), new FixedKeys(SecretKey), retryBaseDelay: TimeSpan.Zero);

        await Assert.ThrowsAsync<TranslationException>(() => provider.TranslateAsync(["one"], "en", "hu", null, CancellationToken.None));
    }

    [Fact]
    public async Task GoogleCountMismatchFails()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, "{\"data\":{\"translations\":[{\"translatedText\":\"x\"}]}}"));
        var provider = new GoogleTranslationProvider(new HttpClient(handler), new FixedKeys(SecretKey), TimeSpan.Zero);

        await Assert.ThrowsAsync<TranslationException>(() => provider.TranslateAsync(["a", "b"], "en", "hu", null, CancellationToken.None));
    }

    [Fact]
    public async Task ClaudeSendsAtMostFiftyStringsPerRequest()
    {
        var handler = new FakeHandler((seen, _) =>
        {
            var count = JsonDocument.Parse(seen.Body).RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!
                .Split('\n').Count(line => line.Length > 1 && char.IsDigit(line[0]) && line.Contains(". \""));
            return Json(HttpStatusCode.OK, ClaudeReply(Enumerable.Range(0, count).Select(i => "t" + i).ToArray()));
        });
        var provider = new ClaudeTranslationProvider(new HttpClient(handler), new FixedKeys(SecretKey), retryBaseDelay: TimeSpan.Zero);

        var result = await provider.TranslateAsync(Texts(120), "en", "hu", null, CancellationToken.None);

        Assert.Equal(120, result.Count);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task RetriesOn429ThenSucceeds()
    {
        var handler = new FakeHandler((seen, n) => n < 3
            ? Json((HttpStatusCode)429, "{\"error\":{\"message\":\"slow down\"}}")
            : Json(HttpStatusCode.OK, GoogleReply(seen)));
        var provider = new GoogleTranslationProvider(new HttpClient(handler), new FixedKeys(SecretKey), TimeSpan.Zero);

        var result = await provider.TranslateAsync(["Add"], "en", "hu", null, CancellationToken.None);

        Assert.Equal(["hu:Add"], result);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task GivesUpAfterThreeAttemptsOnServerErrors()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.ServiceUnavailable, "{}"));
        var provider = new ClaudeTranslationProvider(new HttpClient(handler), new FixedKeys(SecretKey), retryBaseDelay: TimeSpan.Zero);

        var ex = await Assert.ThrowsAsync<TranslationException>(() => provider.TranslateAsync(["x"], "en", "hu", null, CancellationToken.None));

        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains("503", ex.Message);
        Assert.DoesNotContain(SecretKey, ex.Message);
    }

    [Fact]
    public async Task ClientErrorsAreNotRetriedAndNeverEchoTheKey()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.Unauthorized, $"{{\"error\":{{\"message\":\"Bad key {SecretKey} given\"}}}}"));
        var provider = new GoogleTranslationProvider(new HttpClient(handler), new FixedKeys(SecretKey), TimeSpan.Zero);

        var ex = await Assert.ThrowsAsync<TranslationException>(() => provider.TranslateAsync(["x"], "en", "hu", null, CancellationToken.None));

        Assert.Single(handler.Requests);
        Assert.Contains("401", ex.Message);
        Assert.DoesNotContain(SecretKey, ex.ToString());
    }

    [Fact]
    public async Task NetworkFailureMessageNeverContainsTheKey()
    {
        var handler = new FakeHandler((_, _) => throw new HttpRequestException($"connection failed for {SecretKey}"));
        var provider = new GoogleTranslationProvider(new HttpClient(handler), new FixedKeys(SecretKey), TimeSpan.Zero);

        var ex = await Assert.ThrowsAsync<TranslationException>(() => provider.TranslateAsync(["x"], "en", "hu", null, CancellationToken.None));

        Assert.Equal(3, handler.Requests.Count);
        Assert.DoesNotContain(SecretKey, ex.ToString());
    }

    [Fact]
    public async Task MissingKeyNamesTheEnvironmentVariableAndSendsNothing()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, "{}"));
        var google = new GoogleTranslationProvider(new HttpClient(handler), new FixedKeys(null), TimeSpan.Zero);
        var claude = new ClaudeTranslationProvider(new HttpClient(handler), new FixedKeys(" "), retryBaseDelay: TimeSpan.Zero);

        var g = await Assert.ThrowsAsync<TranslationException>(() => google.TranslateAsync(["x"], "en", "hu", null, CancellationToken.None));
        var c = await Assert.ThrowsAsync<TranslationException>(() => claude.TranslateAsync(["x"], "en", "hu", null, CancellationToken.None));

        Assert.Contains("GOOGLE_TRANSLATE_API_KEY", g.Message);
        Assert.Contains("ANTHROPIC_API_KEY", c.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void EnvironmentKeyProviderReadsTheDocumentedVariables()
    {
        var oldGoogle = Environment.GetEnvironmentVariable("GOOGLE_TRANSLATE_API_KEY");
        var oldClaude = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("GOOGLE_TRANSLATE_API_KEY", "g-key");
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "c-key");
            var keys = new EnvironmentApiKeyProvider();

            Assert.Equal("g-key", keys.GetApiKey(TranslationProviderKind.Google));
            Assert.Equal("c-key", keys.GetApiKey(TranslationProviderKind.Claude));
            Assert.Null(keys.GetApiKey(TranslationProviderKind.None));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOOGLE_TRANSLATE_API_KEY", oldGoogle);
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", oldClaude);
        }
    }

    [Fact]
    public void ProviderFactoryReturnsNullForNone()
    {
        using var client = new HttpClient(new FakeHandler((_, _) => Json(HttpStatusCode.OK, "{}")));

        Assert.Null(TranslationProviders.Create(TranslationProviderKind.None, client, new FixedKeys(null)));
        Assert.IsType<GoogleTranslationProvider>(TranslationProviders.Create(TranslationProviderKind.Google, client, new FixedKeys(null)));
        Assert.IsType<ClaudeTranslationProvider>(TranslationProviders.Create(TranslationProviderKind.Claude, client, new FixedKeys(null)));
    }

    [Fact]
    public void UiCatalogHasUniqueIdsAndTheRequiredTexts()
    {
        Assert.Equal(UiStrings.All.Count, UiStrings.All.Select(s => s.Id).Distinct().Count());
        var english = UiStrings.All.Select(s => s.English).ToList();
        foreach (var text in new[]
        {
            "Add", "Edit", "Delete", "Search", "Save", "Cancel", "View", "Copy", "Back to list", "Invalid username or password.",
            "Too many failed attempts. Try again later.", "Delete this record? This cannot be undone.",
        })
        {
            Assert.Contains(text, english);
        }
    }
}
