using System.Xml.Linq;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

public class MasterDetailXmlTests
{
    [Fact]
    public void EveryMasterDetailValueRoundTripsThroughTheProjectFile()
    {
        var project = Sample();

        var loaded = ProjectFile.Read(ProjectFile.Write(project));

        var original = project.Tables[0];
        var read = loaded.Tables[0];
        var detail = Assert.Single(read.Details);
        var expected = original.Details[0];
        Assert.Equal("order_lines", detail.DetailTable);
        Assert.Equal("Lines \"of\" <the> order", detail.Caption);
        Assert.False(detail.ShowOnView);
        Assert.False(detail.ShowOnEdit);
        Assert.True(detail.ShowOnAdd);
        Assert.Equal(DetailPreview.Offcanvas, detail.Preview);
        Assert.Equal(DeleteRule.SetNull, detail.OnDelete);
        Assert.Equal(UpdateRule.Cascade, detail.OnUpdate);
        Assert.Equal(25, detail.PageSize);
        Assert.False(detail.AllowAdd);
        Assert.False(detail.AllowEdit);
        Assert.False(detail.AllowDelete);
        Assert.True(detail.FilterAndSort);
        Assert.Equal("Items", detail.FormPage);
        Assert.Equal(["order_id", "region"], detail.Links.Select(l => l.DetailField));
        Assert.Equal(["id", "region_code"], detail.Links.Select(l => l.MasterField));
        Assert.Equal(["qty", "price"], detail.Columns);

        // Every property of the contract differs from its default in the sample (a new property must be added to the sample).
        var defaults = new DetailLink();
        foreach (var property in typeof(DetailLink).GetProperties())
        {
            var value = property.GetValue(expected);
            if (value is System.Collections.ICollection list)
            {
                Assert.True(list.Count > 0, property.Name);
            }
            else
            {
                Assert.False(Equals(value, property.GetValue(defaults)), property.Name);
            }
        }

        var fk = Assert.Single(read.ForeignKeys);
        Assert.Equal("customers", fk.ReferencedTable);
        Assert.Equal(["customer_id", "region"], fk.Columns);
        Assert.Equal(["id", "region_code"], fk.ReferencedColumns);
        Assert.Equal("CASCADE", fk.OnDelete);
        Assert.Equal("SET NULL", fk.OnUpdate);

        Assert.Equal(["Personal", "Work"], read.FormPages.Select(p => p.Name));
        Assert.Equal("Personal data <1>", read.FormPages[0].Caption);
        Assert.Equal("person", read.FormPages[0].Icon);
        Assert.Equal("Who \"they\" are", read.FormPages[0].Description);
        Assert.Equal("", read.FormPages[1].Icon);

        Assert.Equal("Work", read.Fields[1].Ui.FormPage);
        Assert.Equal(MultiPageMode.Accordion, read.Designs.View.MultiPage);
        Assert.Equal(MultiPageMode.Tabs, read.Designs.Add.MultiPage);
        Assert.Equal(MultiPageMode.Pills, read.Designs.Copy.MultiPage);
        Assert.Equal(MultiPageMode.Wizard, read.Designs.Edit.MultiPage);
    }

    [Fact]
    public void WrittenElementsUseTheDocumentedNames()
    {
        var table = ProjectFile.Write(Sample()).Root!.Element("table")!;

        var detail = table.Element("details")!.Element("detail")!;
        Assert.Equal(
            ["table", "caption", "showOnView", "showOnEdit", "showOnAdd", "preview", "onDelete", "onUpdate", "pageSize", "allowAdd", "allowEdit", "allowDelete", "filterAndSort", "formPage"],
            detail.Attributes().Select(a => a.Name.LocalName));
        Assert.Equal(2, detail.Elements("link").Count());
        Assert.Equal("id", (string?)detail.Elements("link").First().Attribute("master"));
        Assert.Equal("order_id", (string?)detail.Elements("link").First().Attribute("detail"));
        Assert.Equal(["qty", "price"], detail.Elements("column").Select(c => (string?)c.Attribute("field")));

        var fk = table.Element("foreignKeys")!.Element("fk")!;
        Assert.Equal(["onDelete", "onUpdate", "referencedTable"], fk.Attributes().Select(a => a.Name.LocalName));
        Assert.Equal("customer_id", (string?)fk.Elements("column").First().Attribute("from"));
        Assert.Equal("id", (string?)fk.Elements("column").First().Attribute("to"));

        var page = table.Element("formPages")!.Elements("page").First();
        Assert.Equal(["name", "caption", "icon", "description"], page.Attributes().Select(a => a.Name.LocalName));

        Assert.Equal("Work", (string?)table.Elements("field").ElementAt(1).Element("ui")!.Attribute("formPage"));
        var designs = table.Element("designs")!.Elements("page").ToDictionary(p => (string)p.Attribute("name")!);
        Assert.Equal("Tabs", (string?)designs["add"].Attribute("multiPage"));
        Assert.Equal("Wizard", (string?)designs["edit"].Attribute("multiPage"));
    }

    [Fact]
    public void DefaultsAreLeftOut()
    {
        var project = new Project { Name = "shop", ProjectId = Guid.NewGuid() };
        var table = new TableInfo { Name = "orders" };
        table.Fields.Add(new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true });
        var detail = new DetailLink { DetailTable = "lines" };
        detail.Links.Add(new KeyLink { MasterField = "id", DetailField = "order_id" });
        table.Details.Add(detail);
        table.FormPages.Add(new FormPageDef { Name = "Main" });
        project.Tables.Add(table);

        var xml = ProjectFile.Write(project).Root!.Element("table")!;

        var written = xml.Element("details")!.Element("detail")!;
        Assert.Equal(["table"], written.Attributes().Select(a => a.Name.LocalName));
        Assert.Null(written.Element("column"));
        Assert.Equal(["name"], xml.Element("formPages")!.Element("page")!.Attributes().Select(a => a.Name.LocalName));
        Assert.Null(xml.Element("foreignKeys"));
        Assert.Null(xml.Element("field")!.Element("ui"));
        Assert.Null(xml.Element("designs"));

        var read = ProjectFile.Read(ProjectFile.Write(project)).Tables[0];
        var back = Assert.Single(read.Details);
        Assert.True(back.ShowOnView);
        Assert.True(back.ShowOnEdit);
        Assert.False(back.ShowOnAdd);
        Assert.Equal(DeleteRule.Restrict, back.OnDelete);
        Assert.Equal(10, back.PageSize);
        Assert.Equal("Main", read.FormPages.Single().Name);
    }

    [Fact]
    public void AProjectWithoutTheNewElementsWritesNoneAndLoadsEmpty()
    {
        var project = CustomizationSamples.Create("plain");

        var document = ProjectFile.Write(project);

        Assert.Empty(document.Descendants("details"));
        Assert.Empty(document.Descendants("foreignKeys"));
        Assert.Empty(document.Descendants("formPages"));
        Assert.DoesNotContain("formPage", document.ToString());
        Assert.DoesNotContain("multiPage", document.ToString());
        foreach (var table in ProjectFile.Read(document).Tables)
        {
            Assert.Empty(table.Details);
            Assert.Empty(table.ForeignKeys);
            Assert.Empty(table.FormPages);
        }
    }

    [Fact]
    public void AnOldProjectFileLoads()
    {
        const string xml = """
            <project name="shop" id="095F6728-DF53-4763-A372-D8F2EAC959F2" outputFolder="out">
              <database provider="sqlite" path="shop.db" />
              <table name="orders" caption="Orders" list="true">
                <field name="id" caption="Id" type="Integer" pk="true" autoinc="true" required="true" />
                <field name="note" caption="Note" type="Text"><ui helpText="Hello" /></field>
                <designs><page name="add" title="New"/></designs>
              </table>
            </project>
            """;

        var table = ProjectFile.Read(XDocument.Parse(xml)).Tables.Single();

        Assert.Empty(table.Details);
        Assert.Empty(table.ForeignKeys);
        Assert.Empty(table.FormPages);
        Assert.Equal("", table.Fields[1].Ui.FormPage);
        Assert.Equal(MultiPageMode.None, table.Designs.Add.MultiPage);
        Assert.Equal("New", table.Designs.Add.Title);
    }

    [Fact]
    public void UnknownEnumValuesFallBackToTheDefault()
    {
        const string xml = """
            <project name="shop" id="095F6728-DF53-4763-A372-D8F2EAC959F2">
              <table name="orders">
                <field name="id" type="Integer" />
                <details><detail table="lines" onDelete="Explode" preview="Sideways" pageSize="x" /></details>
                <designs><page name="edit" multiPage="Carousel" /></designs>
              </table>
            </project>
            """;

        var table = ProjectFile.Read(XDocument.Parse(xml)).Tables.Single();

        Assert.Equal(DeleteRule.Restrict, table.Details[0].OnDelete);
        Assert.Equal(DetailPreview.None, table.Details[0].Preview);
        Assert.Equal(10, table.Details[0].PageSize);
        Assert.Equal(MultiPageMode.None, table.Designs.Edit.MultiPage);
    }

    private static Project Sample()
    {
        var project = new Project { Name = "shop", ProjectId = Guid.Parse("095F6728-DF53-4763-A372-D8F2EAC959F2") };
        var table = new TableInfo { Name = "orders" };
        table.Fields.Add(new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true });
        var work = new FieldInfo { Name = "note", Type = FieldType.Text };
        work.Ui.FormPage = "Work";
        table.Fields.Add(work);

        var detail = new DetailLink
        {
            DetailTable = "order_lines",
            Caption = "Lines \"of\" <the> order",
            ShowOnView = false,
            ShowOnEdit = false,
            ShowOnAdd = true,
            Preview = DetailPreview.Offcanvas,
            OnDelete = DeleteRule.SetNull,
            OnUpdate = UpdateRule.Cascade,
            PageSize = 25,
            AllowAdd = false,
            AllowEdit = false,
            AllowDelete = false,
            FilterAndSort = true,
            FormPage = "Items",
        };
        detail.Links.Add(new KeyLink { MasterField = "id", DetailField = "order_id" });
        detail.Links.Add(new KeyLink { MasterField = "region_code", DetailField = "region" });
        detail.Columns.AddRange(["qty", "price"]);
        table.Details.Add(detail);

        var fk = new ForeignKeyInfo { ReferencedTable = "customers", OnDelete = "CASCADE", OnUpdate = "SET NULL" };
        fk.Columns.AddRange(["customer_id", "region"]);
        fk.ReferencedColumns.AddRange(["id", "region_code"]);
        table.ForeignKeys.Add(fk);

        table.FormPages.Add(new FormPageDef { Name = "Personal", Caption = "Personal data <1>", Icon = "person", Description = "Who \"they\" are" });
        table.FormPages.Add(new FormPageDef { Name = "Work" });

        table.Designs.View.MultiPage = MultiPageMode.Accordion;
        table.Designs.Add.MultiPage = MultiPageMode.Tabs;
        table.Designs.Copy.MultiPage = MultiPageMode.Pills;
        table.Designs.Edit.MultiPage = MultiPageMode.Wizard;
        project.Tables.Add(table);
        return project;
    }
}
