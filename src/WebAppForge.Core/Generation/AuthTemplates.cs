using System.Text;
using WebAppForge.Core.Localization;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Generation;

/// <summary>Source text for the login files of a generated site (only written when <see cref="SecuritySettings.Enabled"/> is set).</summary>
internal static class AuthTemplates
{
    /// <summary>
    /// User Level helpers (only written when a user level field is set). The level is read at login and stored in the
    /// cookie as the "user_level" claim. A missing or non-numeric claim counts as level 0.
    /// </summary>
    public static string Permissions(string ns) => ("""
        using System.Security.Claims;

        namespace @@NS@@.Security;

        /// <summary>User Level security checks for the generated routes.</summary>
        public static class Permissions
        {
            public const string LevelClaimType = "user_level";

            /// <summary>The signed-in user's level, or 0 when the claim is missing or not a number.</summary>
            public static int LevelOf(ClaimsPrincipal user) =>
                int.TryParse(user.FindFirst(LevelClaimType)?.Value, out var level) ? level : 0;

            /// <summary>True when the user is signed in and has at least <paramref name="minLevel"/>. A minLevel of 0 admits any signed-in user.</summary>
            public static bool Allowed(ClaimsPrincipal user, int minLevel) =>
                user.Identity?.IsAuthenticated == true && LevelOf(user) >= minLevel;

            /// <summary>Requires <paramref name="minLevel"/> for a route. Other users get 403 with <paramref name="deniedMessage"/> as plain text.</summary>
            public static RouteHandlerBuilder RequireLevel(this RouteHandlerBuilder builder, int minLevel, string deniedMessage) =>
                builder.AddEndpointFilter(async (context, next) =>
                {
                    if (Allowed(context.HttpContext.User, minLevel))
                    {
                        return await next(context);
                    }

                    return Results.Text(deniedMessage, "text/plain; charset=utf-8", statusCode: StatusCodes.Status403Forbidden);
                });
        }
        """).Replace("@@NS@@", ns);

    public static string Passwords(string ns) => ("""
        using Microsoft.AspNetCore.Identity;

        namespace @@NS@@.Security;

        /// <summary>
        /// Password hashing for the user table. Uses ASP.NET Core's PasswordHasher (PBKDF2 with HMAC-SHA256, 128-bit salt,
        /// self-describing hash format). Store the output of Hash in the password column.
        /// </summary>
        public static class Passwords
        {
            // PasswordHasher does not use its user argument, so a fixed string stands in for it.
            private static readonly PasswordHasher<string> Hasher = new();

            public static string Hash(string password) => Hasher.HashPassword(string.Empty, password);

            /// <summary>True when <paramref name="password"/> matches the stored <paramref name="hash"/>. A missing or malformed hash never matches.</summary>
            public static bool Verify(string hash, string password)
            {
                if (string.IsNullOrEmpty(hash))
                {
                    return false;
                }

                var result = Hasher.VerifyHashedPassword(string.Empty, hash, password);
                return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
            }
        }
        """).Replace("@@NS@@", ns);

    public static string LoginAttempts(string ns) => ("""
        using System.Globalization;
        using Dapper;
        using Microsoft.Data.Sqlite;

        namespace @@NS@@.Security;

        /// <summary>
        /// Counts failed logins per user name in the login_attempts table of the site database. A user is locked out once the
        /// failures reach the limit inside one window. The window starts at the first failure and ends when it has run for its
        /// full length; the failure count then starts again.
        /// </summary>
        /// <remarks>
        /// The counts are stored in the database, so a restart does not clear a lockout and every process that uses the same
        /// database sees the same counts. Window start times are stored as UTC ISO 8601 text and compared with DateTime.UtcNow.
        /// Names are compared without case.
        /// </remarks>
        public sealed class LoginAttempts
        {
            private const string CreateTableSql =
                "CREATE TABLE IF NOT EXISTS login_attempts (username TEXT PRIMARY KEY, failures INTEGER NOT NULL, window_start TEXT NOT NULL)";
            private const string SelectSql = "SELECT failures, window_start FROM login_attempts WHERE username = @username";
            private const string UpsertSql =
                "INSERT INTO login_attempts (username, failures, window_start) VALUES (@username, @failures, @windowStart) "
                + "ON CONFLICT(username) DO UPDATE SET failures = excluded.failures, window_start = excluded.window_start";
            private const string DeleteSql = "DELETE FROM login_attempts WHERE username = @username";

            private readonly string _connectionString;
            private readonly int _maxFailures;
            private readonly TimeSpan _window;

            public LoginAttempts(string connectionString, int maxFailures, TimeSpan window)
            {
                _connectionString = connectionString;
                _maxFailures = maxFailures;
                _window = window;
            }

            /// <summary>Creates the login_attempts table when it does not exist. Called once at startup, before the site serves requests.</summary>
            public static async Task EnsureTableAsync(string connectionString)
            {
                await using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();
                await connection.ExecuteAsync(CreateTableSql);
            }

            /// <summary>True while the user name has reached the failure limit and the window has not expired.</summary>
            public async Task<bool> IsLocked(string userName)
            {
                await using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                var row = (await connection.QueryAsync<(long Failures, string WindowStart)>(SelectSql, new { username = Key(userName) })).AsList();
                return row.Count > 0 && row[0].Failures >= _maxFailures && !IsExpired(ParseUtc(row[0].WindowStart), DateTime.UtcNow);
            }

            /// <summary>Counts one failed login for the user name. The read and the write run in one transaction.</summary>
            public async Task RecordFailure(string userName)
            {
                await using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // BEGIN IMMEDIATE starts a transaction that takes SQLite's write lock before the read. SQLite serialises
                // writers: a second failure for the same user waits for this commit and then reads the count it wrote.
                // A deferred transaction would read first and could fail with SQLITE_BUSY when it tries to write.
                await Run(connection, "BEGIN IMMEDIATE");
                try
                {
                    var key = Key(userName);
                    var now = DateTime.UtcNow;
                    var failures = 1;
                    var windowStart = now;

                    var previous = (await connection.QueryAsync<(long Failures, string WindowStart)>(SelectSql, new { username = key })).AsList();
                    if (previous.Count > 0)
                    {
                        var previousStart = ParseUtc(previous[0].WindowStart);
                        if (!IsExpired(previousStart, now))
                        {
                            failures = (int)previous[0].Failures + 1;
                            windowStart = previousStart;
                        }
                    }

                    await connection.ExecuteAsync(UpsertSql, new { username = key, failures, windowStart = Format(windowStart) });

                    await Run(connection, "COMMIT");
                }
                catch
                {
                    await Run(connection, "ROLLBACK");
                    throw;
                }
            }

            /// <summary>Clears the count after a successful login.</summary>
            public async Task Reset(string userName)
            {
                await using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                await connection.ExecuteAsync(DeleteSql, new { username = Key(userName) });
            }

            private static string Key(string userName) => userName.ToLowerInvariant();

            private static string Format(DateTime utc) => utc.ToString("O", CultureInfo.InvariantCulture);

            private static DateTime ParseUtc(string text) =>
                DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

            private bool IsExpired(DateTime windowStart, DateTime now) => now - windowStart >= _window;

            private static async Task Run(SqliteConnection connection, string sql) => await connection.ExecuteAsync(sql);
        }
        """).Replace("@@NS@@", ns);

    /// <summary>
    /// Login and logout pages. The login compares the password hash from the user table and signs in with a cookie.
    /// Unknown users and wrong passwords get the same message.
    /// </summary>
    public static string AuthPages(string ns, string userTable, string userNameField, string passwordField, SecuritySettings security)
    {
        var track = security.TrackFailedAttempts;
        var maxFailures = Math.Max(1, security.MaxFailedAttempts);
        var windowMinutes = Math.Max(1, security.FailedAttemptsWindowMinutes);
        // With a user level field the lookup also reads the level column (second column of the row).
        var levelSecured = !string.IsNullOrEmpty(security.UserLevelField);
        var lookup = "SELECT " + Templates.QuoteIdent(passwordField)
            + (levelSecured ? ", " + Templates.QuoteIdent(security.UserLevelField) : string.Empty)
            + " FROM " + Templates.QuoteIdent(userTable)
            + " WHERE " + Templates.QuoteIdent(userNameField) + " = @username";

        var sb = new StringBuilder();
        sb.AppendLine("using System.Security.Claims;");
        sb.AppendLine("using System.Text;");
        sb.AppendLine("using Microsoft.AspNetCore.Antiforgery;");
        sb.AppendLine("using Microsoft.AspNetCore.Authentication;");
        sb.AppendLine("using Microsoft.AspNetCore.Authentication.Cookies;");
        sb.AppendLine("using Dapper;");
        sb.AppendLine("using Microsoft.Data.Sqlite;");
        sb.AppendLine($"using {ns}.Security;");
        if (Templates.Localized)
        {
            sb.AppendLine($"using {ns}.Localization;");
        }

        sb.AppendLine($"using {ns}.Web;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns}.Pages;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Login and logout pages. Login checks the password hash in the user table and signs the user in with a cookie.");
        sb.AppendLine("/// These are the only pages that do not require a signed-in user.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static class AuthPages");
        sb.AppendLine("{");
        sb.AppendLine("    private const string LoginRoute = \"/login\";");
        sb.AppendLine("    private const string LogoutRoute = \"/logout\";");
        sb.AppendLine($"    private const string LookupSql = {Templates.Lit(lookup)};");
        // Localized, the messages are read per request, so they cannot be constants.
        sb.AppendLine(Templates.Localized
            ? $"    private static string InvalidMessage => {Templates.Ui("invalidLogin")};"
            : "    private const string InvalidMessage = \"Invalid username or password.\";");
        if (track)
        {
            sb.AppendLine(Templates.Localized
                ? $"    private static string LockedMessage => {Templates.Ui("tooManyAttempts")};"
                : "    private const string LockedMessage = \"Too many failed attempts. Try again later.\";");
            sb.AppendLine("");
            sb.AppendLine("    // Failed-attempt counts are stored in the login_attempts table. See LoginAttempts.");
            sb.AppendLine($"    private const int MaxFailedAttempts = {maxFailures};");
            sb.AppendLine($"    private static readonly TimeSpan FailedAttemptsWindow = TimeSpan.FromMinutes({windowMinutes});");
        }

        sb.AppendLine();
        sb.AppendLine("    public static IEndpointRouteBuilder MapAuthPages(this IEndpointRouteBuilder app, string connectionString)");
        sb.AppendLine("    {");
        sb.AppendLine("        app.MapGet(LoginRoute, (HttpContext context, IAntiforgery antiforgery) => LoginForm(context, antiforgery, null, context.Request.Query[\"ReturnUrl\"].ToString()));");
        sb.AppendLine("        app.MapPost(LoginRoute, (HttpContext context, IAntiforgery antiforgery) => LoginPost(context, antiforgery, connectionString));");
        sb.AppendLine("        app.MapPost(LogoutRoute, (HttpContext context, IAntiforgery antiforgery) => LogoutPost(context, antiforgery));");
        sb.AppendLine("        return app;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Sign-out form for the home page. Signing out is a POST protected by the anti-forgery token.</summary>");
        sb.AppendLine("    public static string LogoutForm(HttpContext context, IAntiforgery antiforgery) =>");
        sb.AppendLine("        \"<form method=\\\"post\\\" action=\\\"\" + Html.Attr(LogoutRoute) + \"\\\">\"");
        sb.AppendLine("        + Html.AntiforgeryField(context, antiforgery)");
        sb.AppendLine($"        + \"<button type=\\\"submit\\\" class=\\\"btn btn-outline-secondary\\\">{InText("logOut")}</button></form>\";");
        sb.AppendLine();
        sb.AppendLine("    private static IResult LoginForm(HttpContext context, IAntiforgery antiforgery, string? message, string returnUrl)");
        sb.AppendLine("    {");
        sb.AppendLine("        var body = new StringBuilder();");
        sb.AppendLine("        if (message is not null)");
        sb.AppendLine("        {");
        sb.AppendLine("            body.Append(\"<div class=\\\"alert alert-danger\\\">\").Append(Html.Encode(message)).Append(\"</div>\");");
        sb.AppendLine("        }");
        sb.AppendLine();
        if (security.AllowRegistration)
        {
            // Shown after a successful sign-up, which redirects here with ?registered.
            AppendBlock(sb, """
                if (message is null && context.Request.Query.ContainsKey("registered"))
                {
                    body.Append("<div class=\"alert alert-success\">Registration complete. You can now log in.</div>");
                }

                """.Replace("Registration complete. You can now log in.", InText("registrationComplete")));
        }

        sb.AppendLine("        body.Append(\"<form method=\\\"post\\\" action=\\\"\").Append(Html.Attr(LoginRoute)).Append(\"\\\">\");");
        sb.AppendLine("        body.Append(Html.AntiforgeryField(context, antiforgery));");
        sb.AppendLine("        body.Append(\"<input type=\\\"hidden\\\" name=\\\"ReturnUrl\\\" value=\\\"\").Append(Html.Attr(returnUrl)).Append(\"\\\">\");");
        sb.AppendLine($"        body.Append(Html.Input(\"username\", {Templates.Ui("userName")}, \"text\", string.Empty, true));");
        sb.AppendLine($"        body.Append(Html.Input(\"password\", {Templates.Ui("password")}, \"password\", string.Empty, true));");
        sb.AppendLine($"        body.Append(\"<button type=\\\"submit\\\" class=\\\"btn btn-primary\\\">{InText("logIn")}</button></form>\");");
        if (security.AllowRegistration)
        {
            sb.AppendLine($"        body.Append(Html.Link(\"/register\", {Templates.Ui("register")}, \"btn btn-link\"));");
        }

        sb.AppendLine($"        return Html.Page({Templates.Ui("logIn")}, body.ToString());");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static async Task<IResult> LoginPost(HttpContext context, IAntiforgery antiforgery, string connectionString)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (!await Html.IsAntiforgeryValidAsync(context, antiforgery))");
        sb.AppendLine("        {");
        sb.AppendLine("            return Results.BadRequest();");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        var form = await context.Request.ReadFormAsync();");
        sb.AppendLine("        var userName = form[\"username\"].ToString().Trim();");
        sb.AppendLine("        var password = form[\"password\"].ToString();");
        sb.AppendLine("        var returnUrl = form[\"ReturnUrl\"].ToString();");
        sb.AppendLine("        if (userName.Length == 0 || password.Length == 0)");
        sb.AppendLine("        {");
        sb.AppendLine("            return LoginForm(context, antiforgery, InvalidMessage, returnUrl);");
        sb.AppendLine("        }");
        sb.AppendLine();
        if (track)
        {
            // Checked before the password, so a locked user is refused even with the right password.
            sb.AppendLine("        var attempts = new LoginAttempts(connectionString, MaxFailedAttempts, FailedAttemptsWindow);");
            sb.AppendLine("        if (await attempts.IsLocked(userName))");
            sb.AppendLine("        {");
            sb.AppendLine("            return LoginForm(context, antiforgery, LockedMessage, returnUrl);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        sb.AppendLine("        await using var connection = new SqliteConnection(connectionString);");
        sb.AppendLine("        await connection.OpenAsync();");
        if (levelSecured)
        {
            // The hash and the level, or nulls when there is no such user.
            sb.AppendLine("        var (storedHash, level) = await connection.QueryFirstOrDefaultAsync<(string? Hash, long? Level)>(LookupSql, new { username = userName });");
            sb.AppendLine("        var userLevel = (int)(level ?? 0);");
        }
        else
        {
            sb.AppendLine("        var storedHash = await connection.ExecuteScalarAsync<string?>(LookupSql, new { username = userName });");
        }

        sb.AppendLine();
        sb.AppendLine("        if (!Passwords.Verify(storedHash ?? string.Empty, password))");
        sb.AppendLine("        {");
        if (track)
        {
            sb.AppendLine("            await attempts.RecordFailure(userName);");
        }

        sb.AppendLine("            return LoginForm(context, antiforgery, InvalidMessage, returnUrl);");
        sb.AppendLine("        }");
        sb.AppendLine();
        if (track)
        {
            sb.AppendLine("        await attempts.Reset(userName);");
            sb.AppendLine();
        }

        sb.AppendLine("        var claims = new List<Claim>");
        sb.AppendLine("        {");
        sb.AppendLine("            new(ClaimTypes.Name, userName),");
        sb.AppendLine("            new(ClaimTypes.NameIdentifier, userName),");
        sb.AppendLine("        };");
        if (levelSecured)
        {
            sb.AppendLine("        claims.Add(new(Permissions.LevelClaimType, userLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)));");
        }

        sb.AppendLine("        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);");
        sb.AppendLine("        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));");
        sb.AppendLine("        return Results.Redirect(IsLocalPath(returnUrl) ? returnUrl : \"/\");");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static async Task<IResult> LogoutPost(HttpContext context, IAntiforgery antiforgery)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (!await Html.IsAntiforgeryValidAsync(context, antiforgery))");
        sb.AppendLine("        {");
        sb.AppendLine("            return Results.BadRequest();");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);");
        sb.AppendLine("        return Results.Redirect(LoginRoute);");
        sb.AppendLine("    }");
        sb.AppendLine();
        // Only a path on this site may be a login redirect target. "//" and "/\" start another host in browsers,
        // a scheme or a backslash is not a plain path, and control characters are stripped by browsers, so they are refused too.
        sb.Append(("""
                    private static bool IsLocalPath(string url) =>
                        url.StartsWith("/", StringComparison.Ordinal)
                        && !url.StartsWith("//", StringComparison.Ordinal)
                        && !url.StartsWith("/\\", StringComparison.Ordinal)
                        && url.IndexOf(':') < 0
                        && url.IndexOf('\\') < 0
                        && !url.Any(char.IsControl);
                """).Replace("\r\n", "\n") + "\n");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// A fixed UI text inside a string literal of the generated code. Localized, it closes the literal, appends the encoded text
    /// from the Strings helper and opens the literal again; otherwise it is the plain English text.
    /// </summary>
    private static string InText(string id) => Templates.Localized
        ? "\" + Html.Encode(" + Templates.Ui(id) + ") + \""
        : UiStrings.English(id);

    /// <summary>
    /// Localizes the member text of the account pages. Whole string literals ("User name") become Strings calls and the texts
    /// inside markup become encoded calls. Does nothing when the site is not localized.
    /// </summary>
    private static string Localize(string text)
    {
        if (!Templates.Localized)
        {
            return text;
        }

        // A constant cannot hold a per-request text.
        text = text.Replace(
            "private const string ManualSignUpMessage = \"" + UiStrings.English("manualSignUp") + "\";",
            "private static string ManualSignUpMessage => " + Templates.Ui("manualSignUp") + ";");

        foreach (var id in new[]
        {
            "userName", "password", "confirmPassword", "backToLogIn", "register", "changePassword", "currentPassword", "newPassword",
            "confirmNewPassword", "manualSignUp", "enterUserName", "userNameTaken", "enterPassword", "passwordsDiffer",
            "enterCurrentPassword", "currentPasswordWrong", "passwordChanged",
        })
        {
            text = text.Replace("\"" + UiStrings.English(id) + "\"", Templates.Ui(id));
        }

        // Texts inside markup.
        text = text.Replace(">Register</button>", ">" + InText("register") + "</button>");
        text = text.Replace(">Change password</button>", ">" + InText("changePassword") + "</button>");
        // The length rule has an argument.
        text = text.Replace(
            "$\"The password must be at least {MinimumPasswordLength} characters.\"",
            "Strings.F(" + Templates.Lit(UiStrings.KeyOf("passwordTooShort")) + ", " + Templates.Lit(UiStrings.English("passwordTooShort")) + ", MinimumPasswordLength)");
        return text;
    }

    /// <summary>Appends a method-body block written at column 0, indented to the body of a login method.</summary>
    private static void AppendBlock(StringBuilder sb, string text)
    {
        foreach (var line in text.TrimEnd('\n').Split('\n'))
        {
            sb.AppendLine(line.Length == 0 ? string.Empty : "        " + line);
        }
    }

    /// <summary>
    /// Sign-up (/register, anonymous) and change password (/changepassword, signed in) pages. Only written when one of the
    /// two options is set. Both use the same password rules and the Passwords hash that Login checks.
    /// </summary>
    public static string UserAccountPages(string ns, TableInfo users, string userNameField, string passwordField, SecuritySettings security)
    {
        var table = Templates.QuoteIdent(users.Name);
        var user = Templates.QuoteIdent(userNameField);
        var pass = Templates.QuoteIdent(passwordField);

        // Other required columns with no default cannot be filled by the sign-up form, so it cannot insert a row.
        var blocked = security.AllowRegistration
            && users.Fields.Any(f => !string.Equals(f.Name, userNameField, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(f.Name, passwordField, StringComparison.OrdinalIgnoreCase)
                && f.IsRequired && !f.IsAutoIncrement && !f.HasDefault);

        var sb = new StringBuilder();
        sb.Append(("""
            using System.Text;
            using Dapper;
            using Microsoft.AspNetCore.Antiforgery;
            using Microsoft.Data.Sqlite;
            using @@NS@@.Security;
            using @@NS@@.Web;

            namespace @@NS@@.Pages;

            /// <summary>
            /// Sign-up and password change for the user table. Passwords are stored with Passwords.Hash, the format Login checks.
            /// </summary>
            public static class UserAccountPages
            {
                private const int MinimumPasswordLength = 8;

            """).Replace("@@NS@@", ns).Replace("\r\n", "\n"));

        if (Templates.Localized)
        {
            sb.Replace($"using {ns}.Web;", $"using {ns}.Localization;\nusing {ns}.Web;");
        }

        // The members below are written at column 0 and indented into the class once they are all in.
        var headerLength = sb.Length;

        if (security.AllowRegistration)
        {
            if (blocked)
            {
                sb.Append(("""
                    private const string ManualSignUpMessage = "This site needs a manual sign-up for this table.";

                    public static IEndpointRouteBuilder MapRegistrationPages(this IEndpointRouteBuilder app, string connectionString)
                    {
                        app.MapGet("/register", () => Html.Page("Register", "<div class=\"alert alert-warning\">" + Html.Encode(ManualSignUpMessage) + "</div>"));
                        app.MapPost("/register", () => Html.Page("Register", "<div class=\"alert alert-warning\">" + Html.Encode(ManualSignUpMessage) + "</div>"));
                        return app;
                    }

                    """).Replace("\r\n", "\n"));
            }
            else
            {
                sb.Append(("""
                    private const string ExistsSql = @@EXISTS@@;
                    private const string InsertSql = @@INSERT@@;

                    public static IEndpointRouteBuilder MapRegistrationPages(this IEndpointRouteBuilder app, string connectionString)
                    {
                        app.MapGet("/register", (HttpContext context, IAntiforgery antiforgery) => RegisterForm(context, antiforgery, new List<string>(), string.Empty));
                        app.MapPost("/register", (HttpContext context, IAntiforgery antiforgery) => RegisterPost(context, antiforgery, connectionString));
                        return app;
                    }

                    private static IResult RegisterForm(HttpContext context, IAntiforgery antiforgery, List<string> errors, string userName)
                    {
                        var body = new StringBuilder();
                        AppendErrors(body, errors);
                        body.Append("<form method=\"post\" action=\"/register\">");
                        body.Append(Html.AntiforgeryField(context, antiforgery));
                        body.Append(Html.Input("username", "User name", "text", userName, true));
                        body.Append(Html.Input("password", "Password", "password", string.Empty, true));
                        body.Append(Html.Input("confirm", "Confirm password", "password", string.Empty, true));
                        body.Append("<button type=\"submit\" class=\"btn btn-primary\">Register</button></form>");
                        body.Append(Html.Link("/login", "Back to log in", "btn btn-link"));
                        return Html.Page("Register", body.ToString());
                    }

                    private static async Task<IResult> RegisterPost(HttpContext context, IAntiforgery antiforgery, string connectionString)
                    {
                        if (!await Html.IsAntiforgeryValidAsync(context, antiforgery))
                        {
                            return Results.BadRequest();
                        }

                        var form = await context.Request.ReadFormAsync();
                        var userName = form["username"].ToString().Trim();
                        var password = form["password"].ToString();
                        var errors = new List<string>();
                        if (userName.Length == 0)
                        {
                            errors.Add("Enter a user name.");
                        }

                        ValidatePassword(password, form["confirm"].ToString(), errors);
                        if (errors.Count > 0)
                        {
                            return RegisterForm(context, antiforgery, errors, userName);
                        }

                        await using var connection = new SqliteConnection(connectionString);
                        await connection.OpenAsync();
                        if (await connection.ExecuteScalarAsync<long>(ExistsSql, new { username = userName }) > 0)
                        {
                            return RegisterForm(context, antiforgery, new List<string> { "That user name is already taken." }, userName);
                        }

                        try
                        {
                            await connection.ExecuteAsync(InsertSql, new { username = userName, hash = Passwords.Hash(password) });
                        }
                        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
                        {
                            // SQLITE_CONSTRAINT: the user name was taken between the check and the insert.
                            return RegisterForm(context, antiforgery, new List<string> { "That user name is already taken." }, userName);
                        }

                        return Results.Redirect("/login?registered=1");
                    }

                    """).Replace("@@EXISTS@@", Templates.Lit("SELECT COUNT(*) FROM " + table + " WHERE " + user + " = @username"))
                    .Replace("@@INSERT@@", Templates.Lit("INSERT INTO " + table + " (" + user + ", " + pass + ") VALUES (@username, @hash)"))
                    .Replace("\r\n", "\n"));
            }
        }

        if (security.AllowChangePassword)
        {
            sb.Append(("""
                private const string StoredHashSql = @@STORED@@;
                private const string UpdateSql = @@UPDATE@@;

                public static IEndpointRouteBuilder MapChangePasswordPages(this IEndpointRouteBuilder app, string connectionString)
                {
                    app.MapGet("/changepassword", (HttpContext context, IAntiforgery antiforgery) => ChangePasswordForm(context, antiforgery, new List<string>(), null));
                    app.MapPost("/changepassword", (HttpContext context, IAntiforgery antiforgery) => ChangePasswordPost(context, antiforgery, connectionString));
                    return app;
                }

                private static IResult ChangePasswordForm(HttpContext context, IAntiforgery antiforgery, List<string> errors, string? success)
                {
                    var body = new StringBuilder();
                    if (success is not null)
                    {
                        body.Append("<div class=\"alert alert-success\">").Append(Html.Encode(success)).Append("</div>");
                    }

                    AppendErrors(body, errors);
                    body.Append("<form method=\"post\" action=\"/changepassword\">");
                    body.Append(Html.AntiforgeryField(context, antiforgery));
                    body.Append(Html.Input("current", "Current password", "password", string.Empty, true));
                    body.Append(Html.Input("password", "New password", "password", string.Empty, true));
                    body.Append(Html.Input("confirm", "Confirm new password", "password", string.Empty, true));
                    body.Append("<button type=\"submit\" class=\"btn btn-primary\">Change password</button></form>");
                    return Html.Page("Change password", body.ToString());
                }

                private static async Task<IResult> ChangePasswordPost(HttpContext context, IAntiforgery antiforgery, string connectionString)
                {
                    if (!await Html.IsAntiforgeryValidAsync(context, antiforgery))
                    {
                        return Results.BadRequest();
                    }

                    var form = await context.Request.ReadFormAsync();
                    var current = form["current"].ToString();
                    var password = form["password"].ToString();
                    var errors = new List<string>();
                    if (current.Length == 0)
                    {
                        errors.Add("Enter your current password.");
                    }

                    ValidatePassword(password, form["confirm"].ToString(), errors);
                    if (errors.Count > 0)
                    {
                        return ChangePasswordForm(context, antiforgery, errors, null);
                    }

                    // The signed-in user name is the one the login stored in the claims.
                    var userName = context.User.Identity?.Name ?? string.Empty;
                    await using var connection = new SqliteConnection(connectionString);
                    await connection.OpenAsync();
                    var storedHash = await connection.ExecuteScalarAsync<string?>(StoredHashSql, new { username = userName });
                    if (!Passwords.Verify(storedHash ?? string.Empty, current))
                    {
                        return ChangePasswordForm(context, antiforgery, new List<string> { "The current password is not correct." }, null);
                    }

                    await connection.ExecuteAsync(UpdateSql, new { hash = Passwords.Hash(password), username = userName });

                    return ChangePasswordForm(context, antiforgery, new List<string>(), "Your password has been changed.");
                }

                """).Replace("@@STORED@@", Templates.Lit("SELECT " + pass + " FROM " + table + " WHERE " + user + " = @username"))
                .Replace("@@UPDATE@@", Templates.Lit("UPDATE " + table + " SET " + pass + " = @hash WHERE " + user + " = @username"))
                .Replace("\r\n", "\n"));
        }

        sb.Append(("""
            private static void AppendErrors(StringBuilder body, List<string> errors)
            {
                if (errors.Count == 0)
                {
                    return;
                }

                body.Append("<div class=\"alert alert-danger\"><ul class=\"mb-0\">");
                foreach (var error in errors)
                {
                    body.Append("<li>").Append(Html.Encode(error)).Append("</li>");
                }

                body.Append("</ul></div>");
            }

            private static void ValidatePassword(string password, string confirm, List<string> errors)
            {
                if (password.Length == 0)
                {
                    errors.Add("Enter a password.");
                }
                else if (password.Length < MinimumPasswordLength)
                {
                    errors.Add($"The password must be at least {MinimumPasswordLength} characters.");
                }

                if (password != confirm)
                {
                    errors.Add("The passwords do not match.");
                }
            }
            """).Replace("\r\n", "\n"));

        var members = Localize(sb.ToString(headerLength, sb.Length - headerLength)).TrimEnd('\n');
        sb.Length = headerLength;
        foreach (var line in members.Split('\n'))
        {
            sb.AppendLine(line.Length == 0 ? string.Empty : "    " + line);
        }

        sb.AppendLine("}");
        return sb.ToString();
    }
}
