// UI test site harness. Builds a fresh SQLite database for a named scenario, then generates the ASP.NET Core
// site for it with ProjectGenerator. Usage: harness <shop|secure|designed|relations|reports> <siteFolder> <passwordHashFile>
// The database is always recreated, so every run starts from the same seed data.
using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;
using WebAppForge.Core.Schema;
using Microsoft.Data.Sqlite;

if (args.Length != 3)
{
    Console.Error.WriteLine("usage: harness <shop|secure|designed|relations|reports> <siteFolder> <passwordHashFile>");
    return 2;
}

var scenario = args[0];
var siteFolder = Path.GetFullPath(args[1]);
var passwordHash = File.ReadAllText(args[2]).Trim();
if (passwordHash.Length == 0)
{
    Console.Error.WriteLine("password hash file is empty");
    return 2;
}

Directory.CreateDirectory(siteFolder);
var db = Path.Combine(siteFolder, "app.db");
if (File.Exists(db)) File.Delete(db);

Project project = scenario switch
{
    "shop" => BuildShop(db),
    "secure" => BuildSecure(db, passwordHash),
    "designed" => BuildDesigned(db),
    "relations" => BuildRelations(db),
    "reports" => BuildReports(db),
    _ => throw new ArgumentException($"unknown scenario '{scenario}'"),
};

var outDir = Path.Combine(siteFolder, "code");
if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
var files = new ProjectGenerator().Generate(project, outDir, siteFolder);
Console.WriteLine($"Generated {files.Count} files for scenario '{scenario}' into {outDir}");
Console.WriteLine($"Database: {db}");
return 0;

static Project BuildShop(string db)
{
    using (var c = Open(db))
    {
        Exec(c,
            "CREATE TABLE customers (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, email TEXT);" +
            "CREATE TABLE orders (id INTEGER PRIMARY KEY AUTOINCREMENT, customer_id INTEGER NOT NULL, total REAL NOT NULL, note TEXT);");

        // 25 customers so the list needs a second page (PageSize 20).
        using var tx = c.BeginTransaction();
        var insert = c.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = "INSERT INTO customers (name, email) VALUES (@name, @email)";
        var name = insert.Parameters.Add("@name", SqliteType.Text);
        var email = insert.Parameters.Add("@email", SqliteType.Text);
        var seed = new List<(string Name, string Email)>
        {
            ("Ann Adams", "ann@example.com"),
            ("Bob Brown", "bob@example.com"),
        };
        for (var i = 3; i <= 25; i++)
        {
            seed.Add(($"Customer {i:00}", $"customer{i:00}@example.com"));
        }

        foreach (var (n, e) in seed)
        {
            name.Value = n;
            email.Value = e;
            insert.ExecuteNonQuery();
        }

        tx.Commit();
        Exec(c,
            "INSERT INTO orders (customer_id, total, note) VALUES (1, 19.99, 'first order'), (1, 5.00, NULL), (2, 42.50, 'gift');");
    }

    var project = new Project { Name = "shop" };
    project.Database.Path = db;
    project.Tables.AddRange(new SqliteSchemaReader().ReadTables($"Data Source={db}"));
    return project;
}

static Project BuildSecure(string db, string passwordHash)
{
    using (var c = Open(db))
    {
        Exec(c,
            "CREATE TABLE users (username TEXT PRIMARY KEY, password TEXT NOT NULL, level INTEGER NOT NULL DEFAULT 0);" +
            "CREATE TABLE notes (id INTEGER PRIMARY KEY AUTOINCREMENT, owner TEXT NOT NULL, title TEXT NOT NULL, body TEXT);" +
            "CREATE TABLE reports (id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT NOT NULL);");

        // Both users share the password secret-pass-123. alice is level 1, bob is level 0.
        using (var insert = c.CreateCommand())
        {
            insert.CommandText = "INSERT INTO users (username, password, level) VALUES (@u, @p, @l)";
            insert.Parameters.AddWithValue("@p", passwordHash);
            var u = insert.Parameters.Add("@u", SqliteType.Text);
            var l = insert.Parameters.Add("@l", SqliteType.Integer);
            u.Value = "alice"; l.Value = 1; insert.ExecuteNonQuery();
            u.Value = "bob"; l.Value = 0; insert.ExecuteNonQuery();
        }

        Exec(c,
            "INSERT INTO notes (owner, title, body) VALUES ('alice', 'Alice secret', 'alice body'), ('bob', 'Bob secret', 'bob body');" +
            "INSERT INTO reports (title) VALUES ('Quarterly report');");
    }

    var project = new Project { Name = "secure" };
    project.Database.Path = db;
    project.Tables.AddRange(new SqliteSchemaReader().ReadTables($"Data Source={db}"));
    project.Security = new SecuritySettings
    {
        Enabled = true,
        UserTable = "users",
        UserNameField = "username",
        PasswordField = "password",
        UserLevelField = "level",
        TrackFailedAttempts = true,
        MaxFailedAttempts = 5,
        FailedAttemptsWindowMinutes = 10,
        AllowRegistration = true,
        AllowChangePassword = true,
    };

    var notes = project.Tables.Single(t => t.Name == "notes");
    notes.OwnerField = "owner";

    var reports = project.Tables.Single(t => t.Name == "reports");
    reports.EditMinLevel = 2;

    return project;
}

static Project BuildDesigned(string db)
{
    using (var c = Open(db))
    {
        Exec(c,
            "CREATE TABLE categories (id INTEGER PRIMARY KEY, name TEXT NOT NULL);" +
            "CREATE TABLE products (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, price REAL, category_id INTEGER, discontinued BOOLEAN, notes TEXT);" +
            "INSERT INTO categories (id, name) VALUES (1, 'Tools'), (2, 'Toys'), (3, 'Books');");

        // 30 products: Product 01..30, price 10 + i, categories rotate 1,2,3, every 7th is discontinued.
        using var tx = c.BeginTransaction();
        var insert = c.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = "INSERT INTO products (name, price, category_id, discontinued, notes) VALUES (@n, @p, @c, @d, @t)";
        var n = insert.Parameters.Add("@n", SqliteType.Text);
        var p = insert.Parameters.Add("@p", SqliteType.Real);
        var cat = insert.Parameters.Add("@c", SqliteType.Integer);
        var d = insert.Parameters.Add("@d", SqliteType.Integer);
        var t = insert.Parameters.Add("@t", SqliteType.Text);
        for (var i = 1; i <= 30; i++)
        {
            n.Value = $"Product {i:00}";
            p.Value = 10.0 + i;
            cat.Value = ((i - 1) % 3) + 1;
            d.Value = i % 7 == 0 ? 1 : 0;
            t.Value = $"note {i}";
            insert.ExecuteNonQuery();
        }

        tx.Commit();
    }

    var project = new Project { Name = "designed" };
    project.Database.Path = db;
    project.Tables.AddRange(new SqliteSchemaReader().ReadTables($"Data Source={db}"));

    var products = project.Tables.Single(t => t.Name == "products");
    var list = products.Designs.List;
    list.ListLayout = ListLayout.Cards;
    foreach (var col in new[] { "name", "price", "category_id" })
    {
        list.Columns.Add(new ListColumn { Field = col });
    }

    list.PageSizeOptions.AddRange([10, 20, 50]);
    list.Title = "Product catalog";
    list.Subtitle = "All products";
    list.FooterHtml = "<p id=\"design-footer\">Footer text</p>";

    foreach (var form in new[] { products.Designs.Add, products.Designs.Edit })
    {
        form.Container = PageContainer.Modal;
        form.FormLayout = FormLayout.TwoColumns;
        form.SubmitText = "Save product";
        form.SuccessMessage = "Product saved.";
        form.ConfirmBeforeSave = false;
    }

    products.Designs.Delete.Container = PageContainer.Offcanvas;
    products.Designs.View.Container = PageContainer.Modal;

    var categoryId = products.Fields.Single(f => f.Name == "category_id");
    categoryId.Ui.Lookup = new FieldLookup { Table = "categories", KeyField = "id", DisplayField = "name", ShowDisplayInView = true };

    var price = products.Fields.Single(f => f.Name == "price");
    price.Ui.Format = ViewFormat.Currency;

    var discontinued = products.Fields.Single(f => f.Name == "discontinued");
    discontinued.Ui.Format = ViewFormat.Badge;
    discontinued.Ui.BadgeRules.Add(new ValueRule { Operator = ConditionOperator.Equals, Value = "1", Style = "danger", Text = "Discontinued" });

    products.Ui.RowStyles.Add(new RowStyleRule { Field = "discontinued", Operator = ConditionOperator.Equals, Value = "1", Style = "danger" });

    var name = products.Fields.Single(f => f.Name == "name");
    name.Ui.Pattern = "^[A-Za-z0-9 ]+$";
    name.Ui.PatternMessage = "Name has invalid characters.";

    var categories = project.Tables.Single(t => t.Name == "categories");
    categories.Designs.List.ListLayout = ListLayout.Compact;
    categories.Designs.List.StickyHeader = true;
    categories.Designs.List.ShowRowNumbers = true;

    var loc = project.Localization;
    loc.Enabled = true;
    loc.DefaultLanguage = "en";
    loc.Languages.AddRange(["en", "hu"]);
    void Hu(string key, string text) => loc.Entries.Add(new TranslationEntry
    {
        Key = key, Language = "hu", Text = text, Source = TranslationSource.Manual, Reviewed = true,
    });

    Hu("table.products", "Termékek");
    Hu("field.products.id", "Azonosító");
    Hu("field.products.name", "Név");
    Hu("field.products.price", "Ár");
    Hu("field.products.category_id", "Kategória");
    Hu("field.products.discontinued", "Megszűnt");
    Hu("field.products.notes", "Megjegyzés");
    Hu("ui.add", "Hozzáadás");
    Hu("ui.edit", "Szerkesztés");
    Hu("ui.delete", "Törlés");
    Hu("ui.search", "Keresés");
    Hu("ui.save", "Mentés");
    Hu("ui.cancel", "Mégse");

    return project;
}

static Project BuildRelations(string db)
{
    using (var c = Open(db))
    {
        Exec(c,
            "PRAGMA foreign_keys = ON;" +
            "CREATE TABLE customers (id TEXT PRIMARY KEY, name TEXT NOT NULL, city TEXT, phone TEXT, notes TEXT);" +
            "CREATE TABLE orders (id INTEGER PRIMARY KEY AUTOINCREMENT, customer_id TEXT NOT NULL REFERENCES customers(id), order_date TEXT, status TEXT);" +
            "CREATE TABLE order_lines (order_id INTEGER NOT NULL, line_no INTEGER NOT NULL, product TEXT NOT NULL, qty INTEGER NOT NULL, price REAL, PRIMARY KEY(order_id, line_no));" +
            "CREATE TABLE categories (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL);" +
            "CREATE TABLE products (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, price REAL, category_id INTEGER REFERENCES categories(id), supplier TEXT, description TEXT);");

        // 5 customers C1..C5. C5 has no orders. Orders 1..30 are spread over C1..C4 (order n belongs to C((n-1)%4+1)),
        // so C1 has 8 orders (2 pages of 5), C2 and C3 have 8 and 7, C4 has 7.
        for (var i = 1; i <= 5; i++)
        {
            Exec(c, $"INSERT INTO customers (id, name, city, phone, notes) VALUES ('C{i}', 'Customer {i}', 'City {i}', '555-000{i}', 'Notes {i}');");
        }

        using var tx = c.BeginTransaction();
        for (var n = 1; n <= 30; n++)
        {
            ExecTx(c, tx, $"INSERT INTO orders (customer_id, order_date, status) VALUES ('C{((n - 1) % 4) + 1}', '2024-01-{n:00}', '{(n % 2 == 0 ? "shipped" : "open")}');");
        }

        for (var n = 1; n <= 10; n++)
        {
            for (var l = 1; l <= 3; l++)
            {
                ExecTx(c, tx, $"INSERT INTO order_lines (order_id, line_no, product, qty, price) VALUES ({n}, {l}, 'Item {n}-{l}', {l}, {(l * 1.5).ToString(System.Globalization.CultureInfo.InvariantCulture)});");
            }
        }

        ExecTx(c, tx, "INSERT INTO categories (name) VALUES ('Tools'), ('Toys'), ('Books');");
        for (var n = 1; n <= 12; n++)
        {
            ExecTx(c, tx, $"INSERT INTO products (name, price, category_id, supplier, description) VALUES ('Product {n:00}', {10 + n}, {((n - 1) % 3) + 1}, 'Supplier {n}', 'Description {n}');");
        }

        tx.Commit();
    }

    var project = new Project { Name = "relations" };
    project.Database.Path = db;
    project.Tables.AddRange(new SqliteSchemaReader().ReadTables($"Data Source={db}"));

    var customers = project.Tables.Single(t => t.Name == "customers");
    var orders = project.Tables.Single(t => t.Name == "orders");
    var lines = project.Tables.Single(t => t.Name == "order_lines");
    var categories = project.Tables.Single(t => t.Name == "categories");

    var toOrders = new DetailLink
    {
        DetailTable = "orders", OnDelete = DeleteRule.Restrict, Preview = DetailPreview.ExpandableRow, PageSize = 5,
    };
    toOrders.Links.Add(new KeyLink { MasterField = "id", DetailField = "customer_id" });
    toOrders.Columns.AddRange(["id", "order_date", "status"]);
    customers.Details.Add(toOrders);

    var toLines = new DetailLink
    {
        DetailTable = "order_lines", OnDelete = DeleteRule.Cascade, Preview = DetailPreview.Offcanvas, ShowOnAdd = true, FormPage = "Lines",
    };
    toLines.Links.Add(new KeyLink { MasterField = "id", DetailField = "order_id" });
    orders.Details.Add(toLines);

    var toProducts = new DetailLink { DetailTable = "products", OnDelete = DeleteRule.SetNull, Preview = DetailPreview.Modal };
    toProducts.Links.Add(new KeyLink { MasterField = "id", DetailField = "category_id" });
    categories.Details.Add(toProducts);

    customers.FormPages.Add(new FormPageDef { Name = "Main" });
    customers.FormPages.Add(new FormPageDef { Name = "Contact" });
    customers.FormPages.Add(new FormPageDef { Name = "Notes" });
    void Page(TableInfo t, string field, string page) => t.Fields.Single(f => f.Name == field).Ui.FormPage = page;
    Page(customers, "name", "Main");
    Page(customers, "city", "Main");
    Page(customers, "phone", "Contact");
    Page(customers, "notes", "Notes");
    customers.Designs.Add.MultiPage = MultiPageMode.Tabs;
    customers.Designs.Edit.MultiPage = MultiPageMode.Wizard;
    customers.Designs.View.MultiPage = MultiPageMode.Accordion;

    orders.FormPages.Add(new FormPageDef { Name = "Header" });
    orders.FormPages.Add(new FormPageDef { Name = "Lines" });
    Page(orders, "customer_id", "Header");
    Page(orders, "order_date", "Header");
    orders.Designs.Add.MultiPage = MultiPageMode.Pills;
    orders.Designs.Edit.MultiPage = MultiPageMode.Tabs;

    return project;
}

static Project BuildReports(string db)
{
    using (var c = Open(db))
    {
        Exec(c,
            "CREATE TABLE regions (code TEXT PRIMARY KEY, title TEXT NOT NULL);" +
            "CREATE TABLE sales (id INTEGER PRIMARY KEY AUTOINCREMENT, region TEXT NOT NULL, day TEXT NOT NULL, amount REAL NOT NULL, paid INTEGER NOT NULL, note TEXT NOT NULL, stamp INTEGER NOT NULL);" +
            "INSERT INTO regions (code, title) VALUES ('N', 'North'), ('S', 'South'), ('W', 'West');");

        // Sale n (1..24): region N, S, W in turn; month (n-1)/4+1 of 2024 on day 3, 10, 17 or 24; amount n * 10.5; paid when n is even.
        // stamp is the same day as Unix seconds (noon UTC). reports.spec.mjs computes its expectations from this rule.
        using var tx = c.BeginTransaction();
        for (var n = 1; n <= 24; n++)
        {
            var region = "NSW"[(n - 1) % 3];
            var day = new DateTime(2024, (n - 1) / 4 + 1, (n - 1) % 4 * 7 + 3, 12, 0, 0, DateTimeKind.Utc);
            var amount = (n * 10.5).ToString(System.Globalization.CultureInfo.InvariantCulture);
            ExecTx(c, tx, $"INSERT INTO sales (region, day, amount, paid, note, stamp) VALUES ('{region}', '{day:yyyy-MM-dd}', {amount}, {(n % 2 == 0 ? 1 : 0)}, 'Sale {n:00}', {new DateTimeOffset(day).ToUnixTimeSeconds()});");
        }

        tx.Commit();
    }

    var project = new Project { Name = "reports" };
    project.Database.Path = db;
    project.Tables.AddRange(new SqliteSchemaReader().ReadTables($"Data Source={db}"));
    var sales = project.Tables.Single(t => t.Name == "sales");
    sales.Fields.Single(f => f.Name == "region").Ui.Lookup = new FieldLookup { Table = "regions", KeyField = "code", DisplayField = "title" };
    sales.Fields.Single(f => f.Name == "paid").Type = FieldType.Boolean;
    sales.Fields.Single(f => f.Name == "day").Type = FieldType.Date;
    sales.Fields.Single(f => f.Name == "stamp").DateStorage = DateStorage.UnixSeconds;
    // The sales list has every grid tool; grid.spec.mjs drives them.
    sales.Designs.List.FilterBuilder = true;
    sales.Designs.List.QuickFilter = true;
    sales.Designs.List.ResetButton = true;
    sales.Pages.PageSize = 50;

    var summary = new ReportInfo { Name = "sales", Caption = "Sales by region", SourceTable = "sales", DateFilterField = "day", MenuGroup = "Reports" };
    summary.Groups.Add(new ReportGroup { Field = "region" });
    summary.Groups.Add(new ReportGroup { Field = "day", Interval = DateInterval.Month });
    summary.Aggregates.Add(new ReportAggregate { Function = AggregateFunction.Count });
    summary.Aggregates.Add(new ReportAggregate { Function = AggregateFunction.Sum, Field = "amount", Caption = "Amount" });
    summary.FilterFields.AddRange(["region", "paid"]);
    summary.DetailColumns.AddRange(["note", "day", "amount"]);
    project.Reports.Add(summary);

    project.Reports.Add(new ReportInfo
    {
        Name = "monthly", Caption = "Amount per month", Kind = ReportKind.Chart, SourceTable = "sales", RowField = "stamp",
        RowInterval = DateInterval.Month, ValueField = "amount", Function = AggregateFunction.Sum, MenuGroup = "Reports",
    });
    project.Reports.Add(new ReportInfo
    {
        Name = "grid", Caption = "Sales by region and payment", Kind = ReportKind.Crosstab, SourceTable = "sales", RowField = "region",
        ColumnField = "paid", MenuGroup = "Reports",
    });
    var calendar = new ReportInfo { Name = "days", Caption = "Sales calendar", Kind = ReportKind.Calendar, SourceTable = "sales", DateField = "day", TitleField = "note", MenuGroup = "Reports" };
    calendar.FilterFields.Add("region");
    project.Reports.Add(calendar);
    var board = new ReportInfo { Name = "board", Caption = "Overview", Kind = ReportKind.Dashboard, MenuGroup = "Reports" };
    board.Items.Add(new DashboardItem { Report = "monthly", Width = 8 });
    board.Items.Add(new DashboardItem { Report = "grid", Width = 4 });
    project.Reports.Add(board);
    return project;
}

static void ExecTx(SqliteConnection c, SqliteTransaction tx, string sql)
{
    using var cmd = c.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = sql;
    cmd.ExecuteNonQuery();
}

static SqliteConnection Open(string db)
{
    var c = new SqliteConnection($"Data Source={db}");
    c.Open();
    return c;
}

static void Exec(SqliteConnection c, string sql)
{
    using var cmd = c.CreateCommand();
    cmd.CommandText = sql;
    cmd.ExecuteNonQuery();
}
