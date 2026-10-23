using System.Text.RegularExpressions;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

/// <summary>Regression tests for three generator bugs found by the browser suite (tests\ui): wizard Save, decimal step, return URL of a paged master page.</summary>
public class BrowserBugFixTests
{
    private static Dictionary<string, string> Generate(Project project)
    {
        var root = MdSupport.TempFolder();
        try
        {
            Directory.CreateDirectory(root);
            return MdSupport.GenerateText(project, root);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }

    private static Project Wizard()
    {
        var project = CustomizationSamples.Create("plain");
        var items = project.Tables.Single(t => t.Name == "order_items");
        items.FormPages.Add(new FormPageDef { Name = "Main", Caption = "Main" });
        items.FormPages.Add(new FormPageDef { Name = "More", Caption = "More" });
        items.Designs.Edit.MultiPage = MultiPageMode.Wizard;
        return project;
    }

    private static string AllText(Dictionary<string, string> files) => string.Join("\n", files.Values);

    // ---- 1: wizard Save --------------------------------------------------------------------------

    [Fact]
    public void TheWizardScriptCollectsTheSubmitButtonsWhenAStepIsShownNotWhenTheScriptRuns()
    {
        var script = Generate(Wizard())["Web/FormPages.cs"];

        Assert.DoesNotContain("var submits=", script);
        Assert.Contains("function submits(){", script);
        Assert.Contains("submits().forEach(function(b){b.classList.toggle('d-none',i!==n-1);});", script);
    }

    [Fact]
    public void TheDocumentIsInitializedAfterItIsParsedAndInsertedFragmentsStillThroughPkPages()
    {
        var script = Generate(Wizard())["Web/FormPages.cs"];

        Assert.Contains("document.readyState==='loading'", script);
        Assert.Contains("document.addEventListener('DOMContentLoaded',start)", script);
        Assert.Contains("window.pkPages=function(root){if(root===document&&document.readyState==='loading'){return;}init(root);};", script);
        // Fragments inserted later (modal, offcanvas) are still initialized by the observer.
        Assert.Contains("new MutationObserver(", script);
        Assert.Contains("init(x)", script);
    }

    [Fact]
    public void WithoutJavaScriptNothingHidesTheSubmitButtonsOrTheSteps()
    {
        var script = Generate(Wizard())["Web/FormPages.cs"];

        // Only the script toggles d-none on the submit controls; the generated markup never carries it.
        Assert.DoesNotMatch(new Regex("<button[^>]*type=\\\\?\"submit\\\\?\"[^>]*d-none"), script);
        Assert.Contains("pk-js-only", script);
        // The script marks the container "pk-js" itself; steps are only hidden by CSS that depends on that class.
        Assert.Contains("el.classList.add('pk-js')", script);
    }

    // ---- 2: decimal step -------------------------------------------------------------------------

    [Fact]
    public void ADecimalInputGetsStepAnyAndAnIntegerInputDoesNot()
    {
        var pages = Generate(CustomizationSamples.Create("plain"))["Pages/OrderItemsPages.cs"];

        var price = pages.Split('\n').Single(l => l.Contains("Html.Input(\"price\"", StringComparison.Ordinal));
        var qty = pages.Split('\n').Single(l => l.Contains("Html.Input(\"qty\"", StringComparison.Ordinal));
        Assert.Contains("step=\\\"any\\\"", price);
        Assert.DoesNotContain("step", qty);
    }

    [Fact]
    public void TheGeneratedReplaceInsertsTheStepRightAfterTheType()
    {
        // The same replacement the generated code performs, on the markup shape of Html.Input.
        var html = "<input class=\"form-control\" id=\"price\" name=\"price\" type=\"number\" value=\"9.5\">";

        var result = html.Replace(" type=\"number\"", " type=\"number\" step=\"any\"", StringComparison.Ordinal);

        Assert.Equal("<input class=\"form-control\" id=\"price\" name=\"price\" type=\"number\" step=\"any\" value=\"9.5\">", result);
    }

    private static Project NumberProject(string? step, FieldType type)
    {
        var project = new Project { Name = "StepShop" };
        var table = new TableInfo { Name = "items" };
        table.Fields.Add(new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true, IsAutoIncrement = true, IsRequired = true });
        var amount = new FieldInfo { Name = "amount", Type = type };
        amount.Ui.Min = "0";
        if (step is not null)
        {
            amount.Ui.Step = step;
        }

        table.Fields.Add(amount);
        project.Tables.Add(table);
        return project;
    }

    [Fact]
    public void ACustomizedDecimalFieldWithoutStepGetsAny()
    {
        var text = AllText(Generate(NumberProject(null, FieldType.Decimal)));

        Assert.Contains("Step = \"any\"", text);
    }

    [Fact]
    public void AnExplicitStepWinsOverAny()
    {
        var text = AllText(Generate(NumberProject("0.05", FieldType.Decimal)));

        Assert.Contains("Step = \"0.05\"", text);
        Assert.DoesNotContain("Step = \"any\"", text);
    }

    [Fact]
    public void ACustomizedIntegerFieldHasNoStepAny()
    {
        var text = AllText(Generate(NumberProject(null, FieldType.Integer)));

        Assert.DoesNotContain("Step = \"any\"", text);
        Assert.DoesNotContain("step=\\\"any\\\"", text);
    }

    // ---- 3: return URL helper --------------------------------------------------------------------

    [Fact]
    public async Task HereBuildsTheReturnUrlOnceEscapedWithAndWithoutAPathBase()
    {
        var root = MdSupport.TempFolder();
        try
        {
            Directory.CreateDirectory(root);
            var files = MdSupport.GenerateText(MdSupport.SampleProject(Path.Combine(root, "md.db")), root);
            var probe = Path.Combine(root, "probe");
            Directory.CreateDirectory(probe);
            File.WriteAllText(Path.Combine(probe, "probe.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>"
                + "<ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(probe, "Html.cs"), files["Web/Html.cs"]);
            File.WriteAllText(Path.Combine(probe, "MasterDetailKit.cs"), files["Web/MasterDetailKit.cs"]);
            File.WriteAllText(Path.Combine(probe, "Program.cs"), """
                using Microsoft.AspNetCore.Http;
                using MdShop.Web;

                var cases = new (string Base, string Path, string Query)[]
                {
                    ("", "/customers/view/C4", "?dp0=2"),
                    ("/app", "/customers/view/C4", "?dp0=2"),
                    ("", "/customers/view/C4", ""),
                    ("/app", "/customers/view/C4", ""),
                    ("", "/customers/view/a b", "?q=a%26b&x="),
                    ("", "//evil.example", ""),
                };
                foreach (var (b, p, q) in cases)
                {
                    var context = new DefaultHttpContext();
                    context.Request.PathBase = new PathString(b);
                    context.Request.Path = new PathString(p);
                    context.Request.QueryString = new QueryString(q);
                    var here = MasterDetailKit.Here(context.Request);
                    var encoded = Uri.EscapeDataString(here);
                    var next = new DefaultHttpContext();
                    next.Request.QueryString = new QueryString("?returnUrl=" + encoded);
                    Console.WriteLine("CASE|" + here + "|" + encoded + "|" + MasterDetailKit.Back(next.Request, "/fallback"));
                }
                """);

            var run = await MdSupport.RunDotnetAsync(probe, "run -nodeReuse:false --nologo");
            Assert.True(run.ExitCode == 0, run.Output);
            var lines = run.Output.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("CASE|", StringComparison.Ordinal)).Select(l => l.Split('|')).ToList();
            Assert.Equal(6, lines.Count);

            // Path with a query: the question mark and the equals sign are escaped once, in the returnUrl value only.
            Assert.Equal(["CASE", "/customers/view/C4?dp0=2", "%2Fcustomers%2Fview%2FC4%3Fdp0%3D2", "/customers/view/C4?dp0=2"], lines[0]);
            // PathBase is kept.
            Assert.Equal(["CASE", "/app/customers/view/C4?dp0=2", "%2Fapp%2Fcustomers%2Fview%2FC4%3Fdp0%3D2", "/app/customers/view/C4?dp0=2"], lines[1]);
            Assert.Equal(["CASE", "/customers/view/C4", "%2Fcustomers%2Fview%2FC4", "/customers/view/C4"], lines[2]);
            Assert.Equal(["CASE", "/app/customers/view/C4", "%2Fapp%2Fcustomers%2Fview%2FC4", "/app/customers/view/C4"], lines[3]);
            // A path with special characters is escaped as a path (once), query values stay escaped.
            Assert.Equal(["CASE", "/customers/view/a%20b?q=a%26b&x=", "%2Fcustomers%2Fview%2Fa%2520b%3Fq%3Da%2526b%26x%3D", "/customers/view/a%20b?q=a%26b&x="], lines[4]);
            // The local-path rule still applies.
            Assert.Equal("/", lines[5][1]);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }

    [Fact]
    public void TheGeneratedHereNoLongerAddsTheQueryToAPathString()
    {
        var kit = Generate(MdSupport.SampleProject(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db")))["Web/MasterDetailKit.cs"];

        Assert.DoesNotContain("var url = request.PathBase + request.Path;", kit);
        Assert.Contains("request.PathBase.ToUriComponent() + request.Path.ToUriComponent()", kit);
    }
}

[Collection("mdsite")]
public class BrowserBugFixSiteTests(MdSite site)
{
    [Fact]
    public async Task LinksOnAPagedMasterPageCarryTheReturnUrlEscapedOnce()
    {
        var page = await site.Client.GetStringAsync("/customers/view/1?dp0=2");

        Assert.Contains("returnUrl=%2Fcustomers%2Fview%2F1%3Fdp0%3D2", page);
        Assert.DoesNotContain("%253F", page);
    }
}
