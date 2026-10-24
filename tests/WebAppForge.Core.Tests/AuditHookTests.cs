using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

/// <summary>What the generator writes for audited tables: hooks only where the trail is on.</summary>
public class AuditHookGenerationTests
{
    private static Dictionary<string, string> Generate(Action<Project>? configure = null)
    {
        var root = MdSupport.TempFolder();
        Directory.CreateDirectory(root);
        try
        {
            var project = MdSupport.SampleProject(Path.Combine(root, "md.db"));
            configure?.Invoke(project);
            return MdSupport.GenerateText(project, root);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }

    private static void Audit(Project project, params string[] tables)
    {
        project.Audit.Enabled = true;
        foreach (var name in tables)
        {
            project.Tables.Single(t => t.Name == name).Audit.Enabled = true;
        }
    }

    [Fact]
    public void WithoutAnAuditTrailNoFileMentionsIt()
    {
        var files = Generate();

        Assert.DoesNotContain("Web/Audit.cs", files.Keys);
        Assert.DoesNotContain(files, f => f.Value.Contains("Audit.", StringComparison.Ordinal) || f.Value.Contains("AuditKey", StringComparison.Ordinal) || f.Value.Contains("audit_trail", StringComparison.Ordinal));
    }

    [Fact]
    public void OnlyAuditedTablesGetHooks()
    {
        var files = Generate(p => Audit(p, "orders"));

        var pages = files["Pages/OrdersPages.cs"];
        Assert.Contains("\"insert\"", pages);
        Assert.Contains("\"update\"", pages);
        Assert.Contains("\"delete\"", pages);
        Assert.Contains("var auditBefore = item.AuditValues();", pages);
        var api = files["Endpoints/OrdersEndpoints.cs"];
        Assert.Contains("Audit.WriteAsync(connection, http", api);

        foreach (var other in new[] { "Customers", "Products", "OrderLines" })
        {
            Assert.DoesNotContain("Audit.", files[$"Pages/{other}Pages.cs"]);
            Assert.DoesNotContain("Audit.", files[$"Endpoints/{other}Endpoints.cs"]);
        }
    }

    [Fact]
    public void AnActionTheTableDoesNotAuditGetsNoHook()
    {
        var files = Generate(p =>
        {
            Audit(p, "orders");
            var audit = p.Tables.Single(t => t.Name == "orders").Audit;
            audit.Inserts = false;
            audit.Deletes = false;
        });

        var pages = files["Pages/OrdersPages.cs"];
        Assert.DoesNotContain("\"insert\"", pages);
        Assert.DoesNotContain("\"delete\"", pages);
        Assert.Contains("\"update\"", pages);
    }

    [Fact]
    public void CascadesOfAnAuditedDetailTableAreCapturedBeforeTheyRun()
    {
        var files = Generate(p => Audit(p, "order_lines", "line_notes", "orders"));

        var integrity = files["Web/ReferentialIntegrity.cs"];
        Assert.Contains("Audit.Capture[]? Captures = null", integrity);
        Assert.Contains("new Audit.Capture(\"order_lines\", \"delete\"", integrity);
        Assert.Contains("new Audit.Capture(\"line_notes\", \"delete\"", integrity);
        Assert.Contains("new Audit.Capture(\"orders\", \"update\"", integrity);
        Assert.Contains("Audit.WriteUpdatedAsync(connection, http, \"orders\"", integrity);
        // The master that cascades gets the context, so the entries name the user.
        Assert.Contains("ReferentialIntegrity.Plan_Orders, ", files["Pages/OrdersPages.cs"]);
        Assert.Contains("Update_Customers(connection, mdOld, mdNew, context)", files["Pages/CustomersPages.cs"]);
    }
}

/// <summary>Generates, builds and runs the sample site with an audit trail on four of its tables.</summary>
public sealed class AuditSite : IAsyncLifetime
{
    private Process? _site;

    public string Root { get; } = MdSupport.TempFolder();

    public string DbPath => Path.Combine(Root, "md.db");

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Root);
        var project = MdSupport.SampleProject(DbPath);
        // A project table named "audit" has a model class named like the audit runtime; the site must still build.
        project.Tables.Add(new Schema.SqliteSchemaReader().ReadTables($"Data Source={DbPath}").Single(t => t.Name == "audit"));
        project.Audit.Enabled = true;
        foreach (var name in new[] { "customers", "orders", "order_lines", "line_notes" })
        {
            project.Tables.Single(t => t.Name == name).Audit.Enabled = true;
        }

        MdSupport.GenerateText(project, Root);
        MdSupport.Exec(DbPath, MdSupport.Seed);
        var siteFolder = Path.Combine(Root, "site");
        var build = await MdSupport.RunDotnetAsync(siteFolder, "build --nologo -v q -nodeReuse:false -warnaserror");
        Assert.True(build.ExitCode == 0, build.Output);

        var port = MdSupport.FreePort();
        _site = Process.Start(new ProcessStartInfo("dotnet", "run --no-build")
        {
            WorkingDirectory = siteFolder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Environment = { ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}" },
        })!;
        _ = _site.StandardOutput.ReadToEndAsync();
        _ = _site.StandardError.ReadToEndAsync();
        Client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        };
        for (var attempt = 0; attempt < 80; attempt++)
        {
            try
            {
                await Client.GetStringAsync("/customers");
                return;
            }
            catch (HttpRequestException)
            {
                await Task.Delay(500);
            }
        }

        throw new TimeoutException("The generated site did not start.");
    }

    public Task DisposeAsync()
    {
        Client?.Dispose();
        if (_site is { HasExited: false })
        {
            _site.Kill(entireProcessTree: true);
            _site.WaitForExit(10000);
        }

        MdSupport.TryDelete(Root);
        return Task.CompletedTask;
    }

    public async Task<HttpResponseMessage> PostAsync(string tokenPage, string url, Dictionary<string, string>? fields = null)
    {
        var page = await Client.GetStringAsync(tokenPage);
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
        var data = new Dictionary<string, string>(fields ?? []) { ["__RequestVerificationToken"] = token };
        return await Client.PostAsync(url, new FormUrlEncodedContent(data));
    }

    public void Run(string sql) => MdSupport.Exec(DbPath, sql);

    /// <summary>The entries of the trail as "action key" lines, for one table.</summary>
    public List<string> Entries(string table)
    {
        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT action || ' ' || COALESCE(record_key, '') || ' ' || COALESCE(changes, '') FROM audit_trail WHERE table_name = $t ORDER BY id";
        command.Parameters.AddWithValue("$t", table);
        var result = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }
}

[CollectionDefinition("auditsite")]
public sealed class AuditSiteCollection : ICollectionFixture<AuditSite>
{
}

[Collection("auditsite")]
public class AuditHookSiteTests(AuditSite site)
{
    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [Fact]
    public async Task PagesWriteAnEntryForAddEditAndDelete()
    {
        var add = await site.PostAsync("/customers/add", "/customers/add", new() { ["name"] = "Pia", ["region"] = "N" });
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        var id = Regex.Match(add.Headers.Location!.ToString(), @"/customers/edit/(\d+)").Groups[1].Value;
        Assert.Contains(site.Entries("customers"), e => e.StartsWith($"insert {id} ") && e.Contains("\"name\":{\"new\":\"Pia\"}"));

        await site.PostAsync($"/customers/edit/{id}", $"/customers/edit/{id}", new() { ["name"] = "Pia", ["region"] = "S" });
        Assert.Contains(site.Entries("customers"), e => e.StartsWith($"update {id} ") && e.Contains("\"region\":{\"old\":\"N\",\"new\":\"S\"}") && !e.Contains("\"name\""));

        // Saving without a change writes nothing.
        var before = site.Entries("customers").Count;
        await site.PostAsync($"/customers/edit/{id}", $"/customers/edit/{id}", new() { ["name"] = "Pia", ["region"] = "S" });
        Assert.Equal(before, site.Entries("customers").Count);

        await site.PostAsync($"/customers/delete/{id}", $"/customers/delete/{id}");
        Assert.Contains(site.Entries("customers"), e => e.StartsWith($"delete {id} ") && e.Contains("\"region\":{\"old\":\"S\"}"));
    }

    [Fact]
    public async Task TheApiWritesAnEntryForPostPutAndDelete()
    {
        var post = await site.Client.PostAsync("/api/customers", Json("{\"name\":\"Api\",\"region\":\"E\"}"));
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        var id = Regex.Match(post.Headers.Location!.ToString(), @"/api/customers/(\d+)").Groups[1].Value;
        Assert.Contains(site.Entries("customers"), e => e.StartsWith($"insert {id} "));

        Assert.Equal(HttpStatusCode.NoContent, (await site.Client.PutAsync($"/api/customers/{id}", Json($"{{\"id\":{id},\"name\":\"Api\",\"region\":\"W\"}}"))).StatusCode);
        Assert.Contains(site.Entries("customers"), e => e.StartsWith($"update {id} ") && e.Contains("\"region\":{\"old\":\"E\",\"new\":\"W\"}"));

        Assert.Equal(HttpStatusCode.NoContent, (await site.Client.DeleteAsync($"/api/customers/{id}")).StatusCode);
        Assert.Contains(site.Entries("customers"), e => e.StartsWith($"delete {id} "));
        Assert.Equal(HttpStatusCode.NotFound, (await site.Client.DeleteAsync($"/api/customers/{id}")).StatusCode);
    }

    [Fact]
    public async Task ACascadeDeleteRecordsTheRowsItRemoved()
    {
        site.Run("INSERT INTO orders (id, customer_id, note) VALUES (700, 2, 'c'); INSERT INTO order_lines VALUES (700, 1, 'a', 1), (700, 2, 'b', 1); INSERT INTO line_notes (id, order_id, line_no, text) VALUES (700, 700, 1, 'x')");

        var post = await site.PostAsync("/orders/delete/700", "/orders/delete/700");

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Contains(site.Entries("orders"), e => e.StartsWith("delete 700 ") && e.Contains("\"note\":{\"old\":\"c\"}"));
        var lines = site.Entries("order_lines");
        Assert.Contains(lines, e => e.StartsWith("delete 700/1 ") && e.Contains("\"product\":{\"old\":\"a\"}"));
        Assert.Contains(lines, e => e.StartsWith("delete 700/2 "));
        Assert.Contains(site.Entries("line_notes"), e => e.StartsWith("delete 700 ") && e.Contains("\"text\":{\"old\":\"x\"}"));
    }

    [Fact]
    public async Task ASetNullAndACascadingUpdateRecordTheChangedDetailRows()
    {
        site.Run("INSERT INTO employees (id, name) VALUES (710, 'Temp'); INSERT INTO orders (id, customer_id, employee_id, note) VALUES (710, 2, 710, 'e')");
        await site.PostAsync("/employees/delete/710", "/employees/delete/710");
        Assert.Contains(site.Entries("orders"), e => e.StartsWith("update 710 ") && e.Contains("\"employee_id\":{\"old\":710,\"new\":null}"));

        site.Run("INSERT INTO customers (id, name) VALUES (720, 'Old'); INSERT INTO orders (id, customer_id, customer_name, note) VALUES (720, 3, 'Old', 'r')");
        await site.PostAsync("/customers/edit/720", "/customers/edit/720", new() { ["name"] = "New" });
        Assert.Contains(site.Entries("orders"), e => e.StartsWith("update 720 ") && e.Contains("\"customer_name\":{\"old\":\"Old\",\"new\":\"New\"}"));
    }

    [Fact]
    public async Task ARefusedDeleteWritesNothing()
    {
        site.Run("INSERT INTO categories (id, name) VALUES (730, 'Busy'); INSERT INTO products (id, name, category_id) VALUES (730, 'P', 730)");
        var before = site.Entries("products").Count;

        var post = await site.PostAsync("/categories/delete/730", "/categories/delete/730");

        Assert.Equal(HttpStatusCode.Conflict, post.StatusCode);
        Assert.Equal(before, site.Entries("products").Count);
        Assert.Empty(site.Entries("categories"));
    }
}
