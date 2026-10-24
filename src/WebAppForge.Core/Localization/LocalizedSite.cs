using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Localization;

/// <summary>The extra files of a generated site with localization: the Strings helper and one resources file per language.</summary>
internal static partial class LocalizedSite
{
    [GeneratedRegex("^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$")]
    private static partial Regex LanguageCode();

    /// <summary>Throws when a language code is not a plain BCP-47 tag, because it becomes part of a file name and of generated code.</summary>
    public static void Validate(LocalizationSettings settings)
    {
        foreach (var code in settings.AllLanguages)
        {
            if (!LanguageCode().IsMatch(code))
            {
                throw new InvalidOperationException($"'{code}' is not a valid language code. Use a BCP-47 code such as 'en' or 'pt-BR'.");
            }
        }
    }

    /// <summary>Localization/Strings.cs and Localization/strings.&lt;lang&gt;.json for every configured language.</summary>
    public static Dictionary<string, string> Files(string ns, Project project)
    {
        var settings = project.Localization;
        Validate(settings);
        var languages = settings.AllLanguages;
        var files = new Dictionary<string, string>
        {
            ["Localization/Strings.cs"] = StringsHelper(ns, settings.DefaultLanguage, languages),
        };

        var sources = LocalizationKeys.Enumerate(project);
        foreach (var language in languages)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var source in sources)
            {
                var entry = settings.Find(source.Key, language);
                if (entry is not null && entry.Text.Length > 0)
                {
                    map[source.Key] = entry.Text;
                }
                else if (string.Equals(source.Language, language, StringComparison.OrdinalIgnoreCase))
                {
                    // The source text is the text of the language it is written in.
                    map[source.Key] = source.Text;
                }
            }

            files[$"Localization/strings.{language}.json"] = JsonSerializer.Serialize(map, JsonOptions) + "\n";
        }

        return files;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string StringsHelper(string ns, string defaultLanguage, IReadOnlyList<string> languages) => ("""
        using System.Collections.Concurrent;
        using System.Globalization;
        using System.Net;
        using System.Text.Json;
        using Microsoft.AspNetCore.Http;

        namespace @@NS@@.Localization;

        /// <summary>
        /// Texts of the pages in the visitor's language. The language is the "lang" cookie (set by /setlanguage), then the
        /// Accept-Language header, then the default language. A text missing in a language falls back to the default language
        /// and then to the English text written in the page, which is the fallback key.
        /// </summary>
        public static class Strings
        {
            public const string DefaultLanguage = @@DEFAULT@@;
            public const string CookieName = "lang";

            public static readonly string[] Languages = { @@LANGS@@ };

            private static readonly string[] RightToLeft = { "ar", "he", "fa", "ur" };
            private static readonly Dictionary<string, Dictionary<string, string>> Resources = new(StringComparer.OrdinalIgnoreCase);
            private static readonly AsyncLocal<RequestState?> Current = new();

            private sealed record RequestState(string Language, string PathAndQuery);

            /// <summary>Reads Localization/strings.&lt;lang&gt;.json for every configured language from the content root (or the app folder).</summary>
            public static void Load(string contentRoot)
            {
                foreach (var language in Languages)
                {
                    var map = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var root in new[] { contentRoot, AppContext.BaseDirectory })
                    {
                        var path = Path.Combine(root, "Localization", "strings." + language + ".json");
                        if (File.Exists(path))
                        {
                            map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? map;
                            break;
                        }
                    }

                    Resources[language] = map;
                }
            }

            /// <summary>Language of the current request; the default language outside a request.</summary>
            public static string CurrentLanguage => Current.Value?.Language ?? DefaultLanguage;

            /// <summary>The text for a key in the current language. <paramref name="fallback"/> is the English text of the page.</summary>
            public static string T(string key, string fallback)
            {
                if (Resources.TryGetValue(CurrentLanguage, out var current) && current.TryGetValue(key, out var text))
                {
                    return text;
                }

                if (Resources.TryGetValue(DefaultLanguage, out var fallbackMap) && fallbackMap.TryGetValue(key, out text))
                {
                    return text;
                }

                return fallback;
            }

            public static string T(string key) => T(key, key);

            /// <summary>Text for a format string key, such as "{0} is required.", with the arguments filled in.</summary>
            public static string F(string key, string fallback, params object[] args) => string.Format(T(key, fallback), args);

            private static readonly ConcurrentDictionary<string, CultureInfo> Cultures = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// The culture that formats dates and numbers for the visitor: the specific culture of the request language ("hu" is
            /// formatted as hu-HU), or the invariant culture for a language .NET does not know. Only display uses it. Form values,
            /// the API, exports and imports stay invariant, and CultureInfo.CurrentCulture is not changed, so nothing that formats
            /// implicitly (such as input values) can pick up a decimal comma.
            /// </summary>
            public static CultureInfo Culture => CultureOf(CurrentLanguage);

            public static CultureInfo CultureOf(string language) => Cultures.GetOrAdd(language, static tag =>
            {
                try
                {
                    return CultureInfo.CreateSpecificCulture(tag);
                }
                catch (CultureNotFoundException)
                {
                    return CultureInfo.InvariantCulture;
                }
            });

            /// <summary>
            /// Number format for currency values: the separators of the visitor's language with the currency symbol of the default
            /// language, because the amount is the same whatever language the page is read in.
            /// </summary>
            public static NumberFormatInfo CurrencyFormat
            {
                get
                {
                    var format = (NumberFormatInfo)Culture.NumberFormat.Clone();
                    format.CurrencySymbol = CultureOf(DefaultLanguage).NumberFormat.CurrencySymbol;
                    return format;
                }
            }

            /// <summary>A date and time as the visitor's language writes it (short date and short time).</summary>
            public static string FormatDateTime(DateTime value)
            {
                var culture = Culture;
                return ReferenceEquals(culture, CultureInfo.InvariantCulture)
                    ? value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                    : value.ToString(culture.DateTimeFormat.ShortDatePattern + " " + culture.DateTimeFormat.ShortTimePattern, culture);
            }

            /// <summary>
            /// A number as the visitor's language writes it. Decimals get the language's decimal and group separators and keep all
            /// their digits; whole numbers get no group separator, because they are often keys and codes.
            /// </summary>
            public static string FormatNumber(IFormattable value)
            {
                var culture = Culture;
                if (ReferenceEquals(culture, CultureInfo.InvariantCulture))
                {
                    return value.ToString(null, CultureInfo.InvariantCulture);
                }

                return value switch
                {
                    decimal or double or float => value.ToString("#,0.############################", culture),
                    _ => value.ToString(null, culture),
                };
            }

            /// <summary>
            /// Reads a decimal typed by the visitor: first with the decimal separator of the visitor's language, then with ".".
            /// Group separators are not accepted, so "1,5" can only mean one and a half (in a language with a decimal comma) and is
            /// refused where "," is not a decimal separator, instead of being read as 15.
            /// </summary>
            public static bool TryParseDecimal(string text, out decimal value)
            {
                const NumberStyles style = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
                return decimal.TryParse(text, style, Culture, out value) || decimal.TryParse(text, style, CultureInfo.InvariantCulture, out value);
            }

            public static bool IsRightToLeft(string language) =>
                RightToLeft.Contains(language.Split('-')[0], StringComparer.OrdinalIgnoreCase);

            /// <summary>Attribute for the html element: dir="rtl" for right-to-left languages, nothing otherwise.</summary>
            public static string DirAttribute => IsRightToLeft(CurrentLanguage) ? " dir=\"rtl\"" : string.Empty;

            /// <summary>Middleware that decides the language of each request.</summary>
            public static Task Middleware(HttpContext context, Func<Task> next)
            {
                Current.Value = new RequestState(Resolve(context), context.Request.Path + context.Request.QueryString.ToString());
                // Resource lookups of the framework follow the language; formatting does not (see Culture).
                CultureInfo.CurrentUICulture = Culture;
                return next();
            }

            private static string Resolve(HttpContext context)
            {
                if (context.Request.Cookies.TryGetValue(CookieName, out var cookie) && Match(cookie) is { } fromCookie)
                {
                    return fromCookie;
                }

                var header = context.Request.GetTypedHeaders().AcceptLanguage;
                foreach (var item in header.OrderByDescending(h => h.Quality ?? 1.0))
                {
                    if (item.Value.HasValue && Match(item.Value.Value) is { } fromHeader)
                    {
                        return fromHeader;
                    }
                }

                return DefaultLanguage;
            }

            /// <summary>The configured language for a tag: an exact match, then a match of the primary language ("hu-HU" for "hu").</summary>
            private static string? Match(string? tag)
            {
                if (string.IsNullOrWhiteSpace(tag))
                {
                    return null;
                }

                var exact = Languages.FirstOrDefault(l => string.Equals(l, tag, StringComparison.OrdinalIgnoreCase));
                if (exact is not null)
                {
                    return exact;
                }

                var primary = tag.Split('-')[0];
                return Languages.FirstOrDefault(l => string.Equals(l.Split('-')[0], primary, StringComparison.OrdinalIgnoreCase));
            }

            /// <summary>Language links for the page header. Empty when only one language is configured.</summary>
            public static string Switcher()
            {
                if (Languages.Length < 2)
                {
                    return string.Empty;
                }

                var back = Uri.EscapeDataString(Current.Value?.PathAndQuery is { Length: > 0 } p ? p : "/");
                var html = new System.Text.StringBuilder("<ul class=\"navbar-nav ms-auto flex-row gap-2\">");
                foreach (var language in Languages)
                {
                    var active = string.Equals(language, CurrentLanguage, StringComparison.OrdinalIgnoreCase);
                    html.Append("<li class=\"nav-item\"><a class=\"nav-link").Append(active ? " active fw-bold" : string.Empty)
                        .Append("\" hreflang=\"").Append(WebUtility.HtmlEncode(language))
                        .Append("\" href=\"/setlanguage?lang=").Append(Uri.EscapeDataString(language))
                        .Append("&amp;returnUrl=").Append(WebUtility.HtmlEncode(back)).Append("\">")
                        .Append(WebUtility.HtmlEncode(language.ToUpperInvariant())).Append("</a></li>");
                }

                return html.Append("</ul>").ToString();
            }

            /// <summary>
            /// GET /setlanguage?lang=xx[&amp;returnUrl=/path]. Only a configured language is accepted, and only a path on this site
            /// is a redirect target.
            /// </summary>
            public static IEndpointRouteBuilder MapSetLanguage(this IEndpointRouteBuilder app)
            {
                app.MapGet("/setlanguage", (HttpContext context) =>
                {
                    var language = Languages.FirstOrDefault(l => string.Equals(l, context.Request.Query["lang"].ToString(), StringComparison.OrdinalIgnoreCase));
                    if (language is null)
                    {
                        return Results.BadRequest("Unknown language.");
                    }

                    context.Response.Cookies.Append(CookieName, language, new CookieOptions
                    {
                        Path = "/",
                        MaxAge = TimeSpan.FromDays(365),
                        HttpOnly = true,
                        IsEssential = true,
                        SameSite = SameSiteMode.Lax,
                        Secure = context.Request.IsHttps,
                    });
                    var back = context.Request.Query["returnUrl"].ToString();
                    return Results.Redirect(IsLocalPath(back) ? back : "/");
                });
                return app;
            }

            // Only a path on this site may be a redirect target. "//" and "/\" start another host in browsers,
            // a scheme or a backslash is not a plain path, and control characters are stripped by browsers.
            private static bool IsLocalPath(string url) =>
                url.StartsWith("/", StringComparison.Ordinal)
                && !url.StartsWith("//", StringComparison.Ordinal)
                && !url.StartsWith("/\\", StringComparison.Ordinal)
                && url.IndexOf('\\') < 0
                && !url.Any(char.IsControl)
                && url.IndexOf("://", StringComparison.Ordinal) < 0;
        }
        """).Replace("@@NS@@", ns)
            .Replace("@@DEFAULT@@", Templates.Lit(defaultLanguage))
            .Replace("@@LANGS@@", string.Join(", ", languages.Select(Templates.Lit)));
}
