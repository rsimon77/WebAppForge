using System.Collections;
using BindingFlags = System.Reflection.BindingFlags;
using System.Xml.Linq;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

public class CustomizationXmlTests
{
    private const string Multiline = "line one\n  <b class=\"x\">two & three</b>\nline <three>";

    [Fact]
    public void EveryCustomizationPropertyRoundTripsThroughTheProjectFile()
    {
        var project = FullyCustomizedProject();

        var loaded = RoundTrip(project);

        var original = project.Tables.Single();
        var read = loaded.Tables.Single();
        AssertDeepEqual(original.Fields[0].Ui, read.Fields[0].Ui, "field.Ui");
        AssertDeepEqual(original.Ui, read.Ui, "table.Ui");
        AssertDeepEqual(original.Designs, read.Designs, "table.Designs");
        AssertDeepEqual(project.Meta, loaded.Meta, "project.Meta");
    }

    [Fact]
    public void TheFullySetSamplesReallyDifferFromTheDefaultsInEveryProperty()
    {
        // Guards the round-trip test: a property added to the contract later but not set in the sample would otherwise go unnoticed.
        var project = FullyCustomizedProject();
        var table = project.Tables.Single();

        AssertAllDifferFromDefault(table.Fields[0].Ui, new FieldUi(), "FieldUi");
        AssertAllDifferFromDefault(table.Ui, new TableUi(), "TableUi");
        AssertAllDifferFromDefault(project.Meta, new ProjectMetadata(), "ProjectMetadata");
        foreach (var (name, design) in Pages(table.Designs))
        {
            AssertAllDifferFromDefault(design, new PageDesign(), "PageDesign " + name);
        }
    }

    [Fact]
    public void MultiLineTextSurvivesAsElementText()
    {
        var project = FullyCustomizedProject();

        var document = ProjectFile.Write(project);

        var list = document.Root!.Element("table")!.Element("designs")!.Elements("page").Single(p => (string?)p.Attribute("name") == "list");
        Assert.Equal(Multiline, list.Element("headerHtml")!.Value);
        Assert.Equal(Multiline + "F", list.Element("footerHtml")!.Value);
        Assert.Null(list.Attribute("headerHtml"));
        Assert.Equal(Multiline + "S", document.Root.Element("meta")!.Element("globalScript")!.Value);
    }

    [Fact]
    public void WrittenElementsUseTheDocumentedNames()
    {
        var document = ProjectFile.Write(FullyCustomizedProject());

        var table = document.Root!.Element("table")!;
        var ui = table.Element("field")!.Element("ui")!;
        Assert.Equal("Email", (string?)ui.Attribute("control"));
        Assert.Equal("true", (string?)ui.Attribute("readOnlyOnEdit"));
        Assert.Equal(2, ui.Elements("option").Count());
        Assert.Equal(2, ui.Elements("badgeRule").Count());
        Assert.Equal("Categories", (string?)ui.Element("lookup")!.Attribute("table"));
        Assert.Equal("false", (string?)ui.Element("lookup")!.Attribute("showDisplayInView"));
        Assert.Equal(2, table.Element("tableUi")!.Elements("rowStyle").Count());
        var pages = table.Element("designs")!.Elements("page").Select(p => (string?)p.Attribute("name")).ToList();
        Assert.Equal(new[] { "list", "view", "add", "copy", "edit", "delete", "search" }, pages.ToArray());
        var list = table.Element("designs")!.Elements("page").First();
        Assert.Equal(2, list.Elements("column").Count());
        Assert.Equal(new[] { "10", "50", "100" }, list.Elements("pageSizeOption").Select(e => (string?)e.Attribute("value")).ToArray());
        Assert.NotNull(document.Root.Element("meta"));
    }

    [Fact]
    public void DefaultsWriteNoCustomizationElements()
    {
        var project = new Project { Name = "shop", ProjectId = Guid.Parse("095F6728-DF53-4763-A372-D8F2EAC959F2") };
        var table = new TableInfo { Name = "t" };
        table.Fields.Add(new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true });
        project.Tables.Add(table);

        var document = ProjectFile.Write(project);

        Assert.Empty(document.Descendants("ui"));
        Assert.Empty(document.Descendants("tableUi"));
        Assert.Empty(document.Descendants("designs"));
        Assert.Empty(document.Descendants("meta"));
    }

    [Fact]
    public void OnlyNonDefaultDesignsAndAttributesAreWritten()
    {
        var project = new Project { Name = "shop", ProjectId = Guid.NewGuid() };
        var table = new TableInfo { Name = "t" };
        var field = new FieldInfo { Name = "id", Type = FieldType.Integer };
        field.Ui.HelpText = "help";
        table.Fields.Add(field);
        table.Designs.Edit.SubmitText = "Go";
        project.Tables.Add(table);

        var document = ProjectFile.Write(project);

        var ui = document.Descendants("ui").Single();
        Assert.Equal(new[] { "helpText" }, ui.Attributes().Select(a => a.Name.LocalName).ToArray());
        var page = document.Descendants("page").Single();
        Assert.Equal("edit", (string?)page.Attribute("name"));
        Assert.Equal(new[] { "name", "submitText" }, page.Attributes().Select(a => a.Name.LocalName).ToArray());
    }

    [Fact]
    public void MissingElementsReadAsDefaults()
    {
        var document = XDocument.Parse("""
            <project name="shop" id="095F6728-DF53-4763-A372-D8F2EAC959F2">
              <database provider="sqlite" path="shop.db" />
              <table name="items" caption="" generate="true" filter="" list="true" view="true" add="true" copy="true" edit="true" delete="true" search="true" pageSize="20">
                <field name="id" caption="" type="Integer" pk="true" autoinc="true" required="true" inList="true" inView="true" inAdd="true" inEdit="true" searchable="true" />
                <field name="name" caption="" type="Text" pk="false" autoinc="false" required="false" inList="true" inView="true" inAdd="true" inEdit="true" searchable="true">
                  <ui helpText="Only this" />
                </field>
              </table>
            </project>
            """);

        var project = ProjectFile.Read(document);

        var table = Assert.Single(project.Tables);
        Assert.True(table.Fields[0].Ui.IsDefault());
        Assert.Equal("Only this", table.Fields[1].Ui.HelpText);
        Assert.Equal(EditControl.Auto, table.Fields[1].Ui.Control);
        Assert.Equal(3, table.Fields[1].Ui.Rows);
        Assert.True(table.Fields[1].Ui.Sortable);
        Assert.Empty(table.Fields[1].Ui.Options);
        Assert.Null(table.Fields[1].Ui.Lookup);
        Assert.Equal(string.Empty, table.Ui.MenuGroup);
        Assert.True(table.Ui.ShowInMenu);
        Assert.Empty(table.Ui.RowStyles);
        Assert.Equal(PageContainer.Page, table.Designs.List.Container);
        Assert.True(table.Designs.List.Striped);
        Assert.True(table.Designs.Add.ShowCancel);
        Assert.Equal("1.0.0", project.Meta.Version);
        Assert.Equal(5000, project.Meta.HttpPort);
        Assert.True(project.Meta.EnableResponseCompression);
        Assert.Equal("auto", project.Meta.ColorMode);
        Assert.Equal("light", project.Meta.NavbarStyle);
    }

    [Fact]
    public void ProjectFileFromBeforeCustomizationStillLoads()
    {
        var document = XDocument.Parse("""
            <project name="shop" id="095F6728-DF53-4763-A372-D8F2EAC959F2" outputFolder="out">
              <database provider="sqlite" path="shop.db" />
              <aspnet title="Shop" theme="bootstrap" homePage="true" />
              <security enabled="true" userTable="users" userNameField="username" passwordField="password_hash" />
              <table name="users" caption="Users" generate="true" filter="" list="true" view="true" add="true" copy="true" edit="true" delete="true" search="true" pageSize="10">
                <sort field="username" descending="false" />
                <field name="id" caption="" type="Integer" pk="true" autoinc="true" required="true" inList="true" inView="true" inAdd="true" inEdit="true" searchable="true" />
                <field name="username" caption="User" type="Text" pk="false" autoinc="false" required="true" inList="true" inView="true" inAdd="true" inEdit="true" searchable="true" />
              </table>
              <build buildAfterGenerate="false" configuration="Debug" />
            </project>
            """);

        var project = ProjectFile.Read(document);

        Assert.Equal("shop", project.Name);
        Assert.Equal("out", project.OutputFolder);
        Assert.True(project.Security.Enabled);
        var table = Assert.Single(project.Tables);
        Assert.Equal(10, table.Pages.PageSize);
        Assert.Equal(2, table.Fields.Count);
        Assert.All(table.Fields, f => Assert.True(f.Ui.IsDefault()));
        Assert.True(table.Ui.IsDefault());
        Assert.True(table.Designs.IsDefault());
        Assert.True(project.Meta.IsDefault());
    }

    [Fact]
    public void UnknownEnumValuesAndBadNumbersFallBackToDefaults()
    {
        var document = XDocument.Parse("""
            <project name="shop" id="095F6728-DF53-4763-A372-D8F2EAC959F2">
              <meta httpPort="abc" colorMode="dark" />
              <table name="t" caption="" generate="true" filter="" list="true" view="true" add="true" copy="true" edit="true" delete="true" search="true" pageSize="20">
                <field name="a" caption="" type="Text" pk="false" autoinc="false" required="false" inList="true" inView="true" inAdd="true" inEdit="true" searchable="true">
                  <ui control="Teleporter" format="99" rows="many" />
                </field>
                <designs><page name="nonsense" title="x" /><page name="list" container="Hologram" /></designs>
              </table>
            </project>
            """);

        var project = ProjectFile.Read(document);

        var field = project.Tables.Single().Fields.Single();
        Assert.Equal(EditControl.Auto, field.Ui.Control);
        Assert.Equal(ViewFormat.Auto, field.Ui.Format);
        Assert.Equal(3, field.Ui.Rows);
        Assert.Equal(PageContainer.Page, project.Tables.Single().Designs.List.Container);
        Assert.Equal(5000, project.Meta.HttpPort);
        Assert.Equal("dark", project.Meta.ColorMode);
    }

    [Fact]
    public void SavedFileOnDiskLoadsBack()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");
        try
        {
            var project = FullyCustomizedProject();
            ProjectFile.Save(project, path);

            var loaded = ProjectFile.Load(path);

            AssertDeepEqual(project.Tables.Single().Designs, loaded.Tables.Single().Designs, "designs");
            Assert.Equal(Multiline + "S", loaded.Meta.GlobalScript);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---- fixtures -------------------------------------------------------------------------------

    internal static Project FullyCustomizedProject()
    {
        var project = new Project { Name = "shop", ProjectId = Guid.Parse("095F6728-DF53-4763-A372-D8F2EAC959F2") };
        var table = new TableInfo { Name = "products", Caption = "Products" };
        var field = new FieldInfo { Name = "email", Type = FieldType.Text };
        var ui = field.Ui;
        ui.Control = EditControl.Email;
        ui.HelpText = "Help <me> & \"you\"";
        ui.Placeholder = "name@example.com";
        ui.Tooltip = "Tip";
        ui.DefaultValue = "a@b.c";
        ui.ReadOnlyOnEdit = true;
        ui.AutoFocus = true;
        ui.Rows = 7;
        ui.Options.Add(new FieldOption { Value = "1", Text = "One" });
        ui.Options.Add(new FieldOption { Value = "2", Text = "Two & more" });
        ui.Lookup = new FieldLookup { Table = "Categories", KeyField = "Id", DisplayField = "Name", Filter = "Id > 0", OrderField = "Name", ShowDisplayInView = false };
        ui.MaxLength = 50;
        ui.MinLength = 3;
        ui.Min = "1";
        ui.Max = "99";
        ui.Step = "0.5";
        ui.Pattern = "[a-z]+@[a-z]+";
        ui.PatternMessage = "Bad format";
        ui.RequiredMessage = "Needed";
        ui.Format = ViewFormat.Badge;
        ui.FormatString = "N2";
        ui.TrueText = "Yes!";
        ui.FalseText = "No!";
        ui.BadgeRules.Add(new ValueRule { Operator = ConditionOperator.GreaterThan, Value = "5", Style = "danger", Text = "High" });
        ui.BadgeRules.Add(new ValueRule { Operator = ConditionOperator.IsEmpty, Value = "", Style = "light", Text = "" });
        ui.Width = "120px";
        ui.Align = TextAlign.Right;
        ui.CssClass = "fw-bold";
        ui.Style = "color:red";
        ui.Sortable = false;
        ui.Group = "Contact";
        ui.FormPage = "Contact page";
        table.Fields.Add(field);

        var tableUi = table.Ui;
        tableUi.Description = "All products";
        tableUi.Icon = "box";
        tableUi.MenuGroup = "Sales";
        tableUi.ShowInMenu = false;
        tableUi.MenuOrder = 4;
        tableUi.CssClass = "my-table";
        tableUi.RowStyles.Add(new RowStyleRule { Field = "email", Operator = ConditionOperator.Contains, Value = "x", Style = "danger" });
        tableUi.RowStyles.Add(new RowStyleRule { Field = "email", Operator = ConditionOperator.NotEquals, Value = "y", Style = "info" });

        foreach (var (name, design) in Pages(table.Designs))
        {
            FillPage(design, name);
        }

        var meta = project.Meta;
        meta.Description = "A shop";
        meta.Author = "Ann";
        meta.Company = "Acme";
        meta.Copyright = "(c) Acme";
        meta.Version = "2.3.4";
        meta.RootNamespace = "Acme.Shop";
        meta.AssemblyName = "AcmeShop";
        meta.Environment = "Production";
        meta.HttpPort = 8080;
        meta.HttpsPort = 8443;
        meta.UseHttps = true;
        meta.EnableHsts = true;
        meta.EnableResponseCompression = false;
        meta.EnableHealthEndpoint = true;
        meta.LogLevel = "Warning";
        meta.FaviconPath = "assets/favicon.ico";
        meta.LogoPath = "assets/logo.png";
        meta.FooterText = "Footer & <text>";
        meta.GlobalCss = "body { color: red; }\n";
        meta.GlobalScript = Multiline + "S";
        meta.ColorMode = "dark";
        meta.NavbarStyle = "primary";

        project.Tables.Add(table);
        return project;
    }

    private static void FillPage(PageDesign design, string name)
    {
        design.Title = name + " title";
        design.Subtitle = name + " sub";
        design.HeaderHtml = Multiline;
        design.FooterHtml = Multiline + "F";
        design.CssClass = "page-" + name;
        design.CustomCss = ".a { color: red }\n.b { color: blue }";
        design.CustomScript = "console.log('<" + name + ">');\nvar x = 1 & 2;";
        design.Container = PageContainer.Modal;
        design.FormLayout = FormLayout.TwoColumns;
        design.SubmitText = "Go";
        design.CancelText = "Back";
        design.ShowCancel = false;
        design.SubmitCssClass = "btn-success";
        design.Buttons = ButtonPlacement.Both;
        design.MultiPage = MultiPageMode.Wizard;
        design.SuccessMessage = "Saved!";
        design.ConfirmBeforeSave = true;
        design.ListLayout = ListLayout.Cards;
        design.Columns.Add(new ListColumn { Field = "email", Width = "10%", Visible = false });
        design.Columns.Add(new ListColumn { Field = "name", Width = "", Visible = true });
        design.ShowRowNumbers = true;
        design.ShowPager = false;
        design.PageSizeOptions.AddRange([10, 50, 100]);
        design.StickyHeader = true;
        design.Striped = false;
        design.Hover = false;
        design.Bordered = true;
        design.ShowActionsFirst = true;
        design.FilterBuilder = true;
        design.QuickFilter = true;
        design.ResetButton = true;
        design.AddButtonText = "New one";
        design.EmptyText = "Nothing here";
    }

    private static IEnumerable<(string Name, PageDesign Design)> Pages(PageDesigns designs) =>
    [
        ("list", designs.List), ("view", designs.View), ("add", designs.Add), ("copy", designs.Copy),
        ("edit", designs.Edit), ("delete", designs.Delete), ("search", designs.Search),
    ];

    private static Project RoundTrip(Project project) => ProjectFile.Read(ProjectFile.Write(project));

    private static void AssertAllDifferFromDefault(object value, object defaults, string path)
    {
        foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var actual = property.GetValue(value);
            var fallback = property.GetValue(defaults);
            if (actual is IList list)
            {
                Assert.True(list.Count > 0, $"{path}.{property.Name} is empty in the sample");
            }
            else
            {
                Assert.False(Equals(actual, fallback), $"{path}.{property.Name} equals its default in the sample");
            }
        }
    }

    private static void AssertDeepEqual(object? expected, object? actual, string path)
    {
        if (expected is null || actual is null)
        {
            Assert.True(expected is null && actual is null, $"{path}: one side is null");
            return;
        }

        var type = expected.GetType();
        if (type.IsPrimitive || type.IsEnum || expected is string || expected is decimal)
        {
            Assert.True(Equals(expected, actual), $"{path}: expected '{expected}' but was '{actual}'");
            return;
        }

        if (expected is IEnumerable sequence)
        {
            var left = sequence.Cast<object?>().ToList();
            var right = ((IEnumerable)actual).Cast<object?>().ToList();
            Assert.True(left.Count == right.Count, $"{path}: expected {left.Count} items but found {right.Count}");
            for (var i = 0; i < left.Count; i++)
            {
                AssertDeepEqual(left[i], right[i], $"{path}[{i}]");
            }

            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            AssertDeepEqual(property.GetValue(expected), property.GetValue(actual), $"{path}.{property.Name}");
        }
    }
}
