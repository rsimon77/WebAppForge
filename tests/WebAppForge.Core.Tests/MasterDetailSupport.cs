using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;
using WebAppForge.Core.Schema;
using Microsoft.Data.Sqlite;

namespace WebAppForge.Core.Tests;

/// <summary>Sample projects, generation and running of generated sites for the master/detail tests.</summary>
internal static class MdSupport
{
    public const string Schema = """
        CREATE TABLE customers (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, region TEXT);
        CREATE TABLE employees (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, boss_id INTEGER);
        CREATE TABLE orders (id INTEGER PRIMARY KEY AUTOINCREMENT, customer_id INTEGER NOT NULL, employee_id INTEGER, customer_name TEXT, note TEXT);
        CREATE TABLE order_lines (order_id INTEGER NOT NULL, line_no INTEGER NOT NULL, product TEXT, qty INTEGER, PRIMARY KEY (order_id, line_no));
        CREATE TABLE line_notes (id INTEGER PRIMARY KEY AUTOINCREMENT, order_id INTEGER NOT NULL, line_no INTEGER NOT NULL, text TEXT);
        CREATE TABLE categories (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL);
        CREATE TABLE products (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, category_id INTEGER NOT NULL);
        CREATE TABLE audit (id INTEGER PRIMARY KEY AUTOINCREMENT, order_id INTEGER REFERENCES orders(id));
        """;

    public const string Seed = """
        INSERT INTO customers (id, name, region) VALUES (1, 'Ann', 'N'), (2, 'Bob', 'S'), (3, 'Cy', 'S');
        INSERT INTO employees (id, name, boss_id) VALUES (1, 'Boss', NULL), (2, 'Sub', 1), (3, 'Sub2', 1);
        INSERT INTO orders (id, customer_id, employee_id, customer_name, note) VALUES
            (1, 1, NULL, 'Ann', 'o1'), (2, 1, NULL, 'Ann', 'o2'), (3, 1, 1, 'Ann', 'o3'), (4, 1, NULL, 'Ann', 'o4'), (5, 1, NULL, 'Ann', 'o5'),
            (6, 2, NULL, 'Bob', 'o6');
        INSERT INTO order_lines (order_id, line_no, product, qty) VALUES (1, 1, 'x', 2), (1, 2, 'y', 3), (2, 1, 'z', 1);
        INSERT INTO line_notes (id, order_id, line_no, text) VALUES (1, 1, 1, 'n1'), (2, 1, 1, 'n2'), (3, 1, 2, 'n3');
        INSERT INTO categories (id, name) VALUES (1, 'Tools'), (2, 'Empty');
        INSERT INTO products (id, name, category_id) VALUES (1, 'Hammer', 1), (2, 'Saw', 1), (3, 'Drill', 1);
        """;

    public static string TempFolder() => Path.Combine(Path.GetTempPath(), $"md-{Guid.NewGuid():N}");

    public static void Exec(string dbPath, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public static long Scalar(string dbPath, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public static DetailLink Link(string detail, params (string Master, string Detail)[] pairs)
    {
        var link = new DetailLink { DetailTable = detail };
        foreach (var (m, d) in pairs)
        {
            link.Links.Add(new KeyLink { MasterField = m, DetailField = d });
        }

        return link;
    }

    /// <summary>Creates the sample database and the project that reads it, with every master/detail feature set.</summary>
    public static Project SampleProject(string dbPath)
    {
        Exec(dbPath, Schema);
        SqliteConnection.ClearAllPools();
        var project = new Project { Name = "MdShop", Database = new DatabaseSettings { Provider = "sqlite", Path = dbPath } };
        var tables = new SqliteSchemaReader().ReadTables($"Data Source={dbPath}");
        project.Tables.AddRange(tables.Where(t => t.Name != "audit"));
        TableInfo T(string name) => project.Tables.Single(t => t.Name == name);

        var toOrders = Link("orders", ("id", "customer_id"));
        toOrders.Preview = DetailPreview.ExpandableRow;
        toOrders.ShowOnAdd = true;
        toOrders.PageSize = 3;
        toOrders.Caption = "Orders of <customer>";
        T("customers").Details.Add(toOrders);
        var byName = Link("orders", ("name", "customer_name"));
        byName.OnDelete = DeleteRule.None;
        byName.OnUpdate = UpdateRule.Cascade;
        byName.ShowOnView = false;
        byName.ShowOnEdit = false;
        T("customers").Details.Add(byName);

        var employeeOrders = Link("orders", ("id", "employee_id"));
        employeeOrders.OnDelete = DeleteRule.SetNull;
        T("employees").Details.Add(employeeOrders);
        var subordinates = Link("employees", ("id", "boss_id"));
        subordinates.OnDelete = DeleteRule.SetNull;
        T("employees").Details.Add(subordinates);

        var lines = Link("order_lines", ("id", "order_id"));
        lines.OnDelete = DeleteRule.Cascade;
        lines.Preview = DetailPreview.Offcanvas;
        T("orders").Details.Add(lines);
        var notes = Link("line_notes", ("order_id", "order_id"), ("line_no", "line_no"));
        notes.OnDelete = DeleteRule.Cascade;
        notes.Columns.Add("text");
        T("order_lines").Details.Add(notes);

        var products = Link("products", ("id", "category_id"));
        products.Preview = DetailPreview.Modal;
        T("categories").Details.Add(products);
        return project;
    }

    public static Dictionary<string, string> GenerateText(Project project, string root)
    {
        var output = Path.Combine(root, "site");
        var files = new ProjectGenerator().Generate(project, output, root);
        return files.ToDictionary(f => f, f => File.ReadAllText(Path.Combine(output, f)));
    }

    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static async Task<(int ExitCode, string Output)> RunDotnetAsync(string folder, string arguments)
    {
        var info = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await output + await error);
    }

    public static void TryDelete(string path)
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Generates, builds and runs the sample site once; the tests of the collection share it.</summary>
public sealed class MdSite : IAsyncLifetime
{
    private Process? _site;

    public string Root { get; } = MdSupport.TempFolder();

    public string DbPath => Path.Combine(Root, "md.db");

    public HttpClient Client { get; private set; } = null!;

    public Dictionary<string, string> Files { get; private set; } = [];

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Root);
        var project = MdSupport.SampleProject(DbPath);
        Files = MdSupport.GenerateText(project, Root);
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

    public async Task<string> TokenAsync(string url)
    {
        var page = await Client.GetStringAsync(url);
        var match = Regex.Match(page, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"");
        Assert.True(match.Success, "no anti-forgery token on " + url);
        return match.Groups[1].Value;
    }

    /// <summary>Posts a form to <paramref name="url"/> with a token fetched from <paramref name="tokenPage"/>.</summary>
    public async Task<HttpResponseMessage> PostAsync(string tokenPage, string url, Dictionary<string, string>? fields = null)
    {
        var token = await TokenAsync(tokenPage);
        var data = new Dictionary<string, string>(fields ?? []) { ["__RequestVerificationToken"] = token };
        return await Client.PostAsync(url, new FormUrlEncodedContent(data));
    }

    public async Task<long> Count(string sql) => await Task.Run(() => MdSupport.Scalar(DbPath, sql));

    public void Run(string sql) => MdSupport.Exec(DbPath, sql);
}

[CollectionDefinition("mdsite")]
public sealed class MdSiteCollection : ICollectionFixture<MdSite>
{
}
