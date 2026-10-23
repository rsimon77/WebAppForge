using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

public class MasterDetailBuildTests
{
    /// <summary>A sample project with customized fields and every master/detail feature on several tables.</summary>
    private static Project Full(string sample)
    {
        var project = CustomizationSamples.Create(sample);
        CustomizationSamples.Customize(project);
        TableInfo T(string name) => project.Tables.Single(t => t.Name == name);

        var products = MdSupport.Link("products", ("id", "category_id"));
        products.Preview = DetailPreview.Modal;
        products.PageSize = 5;
        T("categories").Details.Add(products);

        var items = T("order_items");
        items.FormPages.Add(new FormPageDef { Name = "Main", Caption = "Main" });
        items.FormPages.Add(new FormPageDef { Name = "Lines", Caption = "Lines", Icon = "list" });
        items.Designs.Edit.MultiPage = MultiPageMode.Tabs;
        items.Designs.View.MultiPage = MultiPageMode.Accordion;
        items.Designs.Add.Container = PageContainer.Modal;
        items.Designs.Edit.SuccessMessage = "Saved";
        var lines = MdSupport.Link("order_lines", ("id", "order_id"));
        lines.OnDelete = DeleteRule.Cascade;
        lines.Preview = DetailPreview.Offcanvas;
        lines.ShowOnAdd = true;
        lines.FormPage = "Lines";
        items.Details.Add(lines);

        var codes = T("codes");
        codes.Designs.List.ListLayout = ListLayout.Cards;
        var byCode = MdSupport.Link("order_items", ("code", "sku"));
        byCode.Preview = DetailPreview.ExpandableRow;
        codes.Details.Add(byCode);
        var byLabel = MdSupport.Link("order_items", ("label", "sku"));
        byLabel.OnDelete = DeleteRule.None;
        byLabel.OnUpdate = UpdateRule.Cascade;
        codes.Details.Add(byLabel);

        if (sample != "plain")
        {
            var notes = T("notes");
            notes.Fields.Add(new FieldInfo { Name = "item_id", Type = FieldType.Integer });
            var toNotes = MdSupport.Link("notes", ("id", "item_id"));
            toNotes.OnDelete = DeleteRule.SetNull;
            toNotes.Preview = DetailPreview.ExpandableRow;
            items.Details.Add(toNotes);
        }

        return project;
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("secure")]
    [InlineData("localized")]
    public async Task AFullSiteWithEveryMasterDetailFeatureBuildsWithoutWarnings(string sample)
    {
        var root = MdSupport.TempFolder();
        try
        {
            Directory.CreateDirectory(root);
            var files = MdSupport.GenerateText(Full(sample), root);
            Assert.Contains("Web/MasterDetailKit.cs", files.Keys);
            Assert.Contains("Web/ReferentialIntegrity.cs", files.Keys);
            Assert.Contains("Web/PageKit.cs", files.Keys);

            var build = await MdSupport.RunDotnetAsync(Path.Combine(root, "site"), "build --nologo -v q -nodeReuse:false -warnaserror");
            Assert.True(build.ExitCode == 0, build.Output);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }

    [Fact]
    public void TheCardsLayoutOpensAnExpandableRowInAnOffcanvas()
    {
        var root = MdSupport.TempFolder();
        try
        {
            Directory.CreateDirectory(root);
            var files = MdSupport.GenerateText(Full("plain"), root);
            var codes = files["Pages/CodesPages.cs"];
            Assert.Contains("PageKit.OpenLink(Route + \"/details/0/\"", codes);
            Assert.DoesNotContain("ExpandButton", codes);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }

    [Fact]
    public void LocalizedSitesTranslateTheCaptionsOfTheGridsAndKeepTheFixedMessagesInEnglish()
    {
        var root = MdSupport.TempFolder();
        try
        {
            Directory.CreateDirectory(root);
            var files = MdSupport.GenerateText(Full("localized"), root);
            var items = files["Pages/OrderItemsPages.cs"];
            Assert.Contains("Strings.T(\"table.order_lines\"", items);
            Assert.Contains("Strings.T(\"ui.add\"", items);
            Assert.Contains("Strings.T(\"table.order_lines\"", files["Web/ReferentialIntegrity.cs"]);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }
}
