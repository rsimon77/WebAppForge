using System.Net;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

public class MasterDetailTextTests
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

    private static Project Sample()
    {
        var root = MdSupport.TempFolder();
        Directory.CreateDirectory(root);
        try
        {
            return MdSupport.SampleProject(Path.Combine(root, "md.db"));
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }

    [Fact]
    public void ProjectsWithoutDetailsGetNoMasterDetailCode()
    {
        var project = CustomizationSamples.Create("plain");
        var files = Generate(project);

        Assert.DoesNotContain("Web/MasterDetailKit.cs", files.Keys);
        Assert.DoesNotContain("Web/ReferentialIntegrity.cs", files.Keys);
        foreach (var (path, text) in files.Where(f => f.Key.StartsWith("Pages/") || f.Key.StartsWith("Endpoints/")))
        {
            Assert.DoesNotContain("DetailGrid", text);
            Assert.DoesNotContain("MasterDetailKit", text);
            Assert.DoesNotContain("ReferentialIntegrity", text);
            Assert.DoesNotContain("presets", text);
        }
    }

    [Fact]
    public void TheGridFiltersByTheLinkWithParametersAndPagesWithItsOwnParameter()
    {
        var files = Generate(Sample());
        var customers = files["Pages/CustomersPages.cs"];

        Assert.Contains("const string where = \" WHERE \\\"customer_id\\\" = @m0\";", customers);
        Assert.Contains("request.Query[\"dp0\"]", customers);
        Assert.Contains("request.Query[\"dp1\"]", customers);
        Assert.Contains("private const int MdPageSize0 = 3;", customers);
        Assert.Contains("Html.Encode(\"Orders of <customer>\")", customers);
        Assert.Contains("MasterDetailKit.Pager(request, \"dp0\"", customers);
        Assert.DoesNotContain("customer_id\\\" = '", customers);
    }

    [Fact]
    public void ACompositeLinkComparesEveryPair()
    {
        var files = Generate(Sample());
        var lines = files["Pages/OrderLinesPages.cs"];

        Assert.Contains("const string where = \" WHERE \\\"order_id\\\" = @m0 AND \\\"line_no\\\" = @m1\";", lines);
        Assert.Contains("args.Add(\"@m1\"", lines);
        Assert.Contains("t1.\\\"order_id\\\" = \\\"line_notes\\\".\\\"order_id\\\" AND t1.\\\"line_no\\\" = \\\"line_notes\\\".\\\"line_no\\\"", files["Web/ReferentialIntegrity.cs"]);
        Assert.Contains("/api/order-lines", files["Endpoints/OrderLinesEndpoints.cs"]);
        Assert.Contains("group.MapGet(\"/{k0}/{k1}/line-notes\"", files["Endpoints/OrderLinesEndpoints.cs"]);
    }

    [Fact]
    public void RowActionsFollowTheAllowFlagsAndThePagesOfTheDetailTable()
    {
        var project = Sample();
        var link = project.Tables.Single(t => t.Name == "customers").Details[0];
        link.AllowAdd = false;
        link.AllowDelete = false;
        project.Tables.Single(t => t.Name == "orders").Pages.Edit = false;
        var customers = Generate(project)["Pages/CustomersPages.cs"];
        var grid = customers[customers.IndexOf("DetailGrid0(", StringComparison.Ordinal)..customers.IndexOf("DetailGrid1(", StringComparison.Ordinal)];

        Assert.DoesNotContain("/orders/add", grid);
        Assert.DoesNotContain("/orders/delete/", grid);
        Assert.DoesNotContain("/orders/edit/", grid);
        Assert.Contains("/orders/view/", grid);
    }

    [Fact]
    public void ThePresetsAreVerifiedAgainstTheMasterAndForcedOnPost()
    {
        var orders = Generate(Sample())["Pages/OrdersPages.cs"];

        Assert.Contains("MasterDetailKit.PresetState(query, new[] { \"customer_id\" }", orders);
        Assert.Contains("SELECT COUNT(*) FROM \\\"customers\\\" WHERE \\\"id\\\" = @p0", orders);
        Assert.Contains("form = MasterDetailKit.WithValues(form, presets);", orders);
        Assert.Contains("ApplyPresets(item, presets);", orders);
        Assert.Contains("MasterDetailKit.Fixed(presets, \"customer_id\")", orders);
        Assert.Contains("Results.Redirect(MasterDetailKit.Back(context.Request, Route))", orders);
    }

    [Fact]
    public void TheReturnUrlHelperAcceptsOnlyLocalPathsLikeTheLoginPage()
    {
        var secure = CustomizationSamples.Create("secure");
        var kit = Generate(Sample())["Web/MasterDetailKit.cs"];
        var login = Generate(secure)["Pages/AuthPages.cs"];

        foreach (var rule in new[]
        {
            "url.StartsWith(\"/\", StringComparison.Ordinal)",
            "!url.StartsWith(\"//\", StringComparison.Ordinal)",
            "!url.StartsWith(\"/\\\\\", StringComparison.Ordinal)",
            "url.IndexOf(':') < 0",
            "url.IndexOf('\\\\') < 0",
            "!url.Any(char.IsControl)",
        })
        {
            Assert.Contains(rule, kit);
            Assert.Contains(rule, login);
        }
    }

    [Fact]
    public void TheDetailEndpointExistsOnlyForTablesWithAPreview()
    {
        var files = Generate(Sample());

        Assert.Contains("app.MapGet(Route + \"/details/0/{id}\"", files["Pages/CustomersPages.cs"]);
        Assert.Contains("app.MapGet(Route + \"/details/0/{id}\"", files["Pages/CategoriesPages.cs"]);
        Assert.DoesNotContain("app.MapGet(Route + \"/details/", files["Pages/EmployeesPages.cs"]);
        Assert.Contains("data-md-expand", files["Web/MasterDetailKit.cs"]);
        Assert.Contains("PageKit.OpenLink(Route + \"/details/0/\"", files["Pages/OrdersPages.cs"]);
        Assert.Contains("\"offcanvas\"", files["Pages/OrdersPages.cs"]);
        Assert.Contains("\"modal\"", files["Pages/CategoriesPages.cs"]);
        Assert.Contains("Web/PageKit.cs", files.Keys);
    }

    [Fact]
    public void ShowOnAddRedirectsToTheEditPageAfterReadingTheNewKey()
    {
        var customers = Generate(Sample())["Pages/CustomersPages.cs"];

        Assert.Contains("SELECT last_insert_rowid()", customers);
        Assert.Contains("return Results.Redirect(editUrl);", customers);
    }

    [Fact]
    public void TheGridAppliesTheOwnershipFilterAndTheLevelGuardsOfTheDetailTable()
    {
        var project = CustomizationSamples.Create("secure");
        var items = project.Tables.Single(t => t.Name == "order_items");
        var notes = project.Tables.Single(t => t.Name == "notes");
        notes.Fields.Add(new FieldInfo { Name = "item_id", Type = FieldType.Integer });
        items.Details.Add(MdSupport.Link("notes", ("id", "item_id")));
        items.Details[0].OnDelete = DeleteRule.Cascade;
        project.Tables.Single(t => t.Name == "users").Details.Clear();
        var files = Generate(project);
        var page = files["Pages/OrderItemsPages.cs"];

        Assert.Contains("Permissions.Allowed(context.User, 1)", page);
        Assert.Contains("var mayChange = Permissions.Allowed(context.User, 2);", page);
        Assert.Contains("\\\"owner\\\" = @owner", page);
        Assert.Contains("using SecureShop.Security;", page);
        Assert.Contains("ReadPresets", files["Pages/NotesPages.cs"]);
        Assert.Contains("Cascades intentionally bypass the ownership filter", files["Web/ReferentialIntegrity.cs"]);
        var api = files["Endpoints/OrderItemsEndpoints.cs"];
        Assert.Contains("Permissions.Allowed(user, 1)", api);
        Assert.Contains("\\\"owner\\\" = @downer", api);
    }

    [Fact]
    public void DeletesAndCascadeUpdatesRunInOneTransactionWithRollbackOnError()
    {
        var files = Generate(Sample());
        var integrity = files["Web/ReferentialIntegrity.cs"];

        Assert.Contains("\"BEGIN IMMEDIATE\"", integrity);
        Assert.Contains("\"ROLLBACK\"", integrity);
        Assert.Contains("\"COMMIT\"", integrity);
        var begin = integrity.IndexOf("public static async Task<Outcome> DeleteAsync", StringComparison.Ordinal);
        var delete = integrity[begin..];
        Assert.Contains("catch", delete);
        Assert.Contains("await RollbackAsync(connection);", delete);
        Assert.Contains("DELETE FROM \\\"order_lines\\\" WHERE EXISTS", integrity);
        Assert.Contains("UPDATE \\\"orders\\\" SET \\\"employee_id\\\" = NULL WHERE EXISTS", integrity);
        Assert.Contains("UPDATE \\\"orders\\\" SET \\\"customer_name\\\" = @n0 WHERE \\\"customer_name\\\" = @o0", integrity);

        var customers = files["Pages/CustomersPages.cs"];
        Assert.Contains("await ReferentialIntegrity.BeginAsync(connection);", customers);
        Assert.Contains("ReferentialIntegrity.Update_Customers(connection, mdOld, mdNew)", customers);
        Assert.Contains("await ReferentialIntegrity.RollbackAsync(connection);", customers);
        Assert.Contains("ReferentialIntegrity.Blocked => Results.Json(new { error = outcome.Message }, statusCode: StatusCodes.Status409Conflict)", files["Endpoints/CategoriesEndpoints.cs"]);
    }

    [Fact]
    public void AFormPageHostsAGridAsATabPillAccordionItemOrStep()
    {
        foreach (var mode in new[] { MultiPageMode.Tabs, MultiPageMode.Pills, MultiPageMode.Accordion, MultiPageMode.Wizard })
        {
            var project = Sample();
            var customers = project.Tables.Single(t => t.Name == "customers");
            customers.FormPages.Add(new FormPageDef { Name = "Main", Caption = "Main" });
            customers.FormPages.Add(new FormPageDef { Name = "Orders", Caption = "Orders" });
            customers.Designs.Edit.MultiPage = mode;
            customers.Designs.View.MultiPage = mode;
            customers.Details[0].FormPage = "Orders";
            var text = Generate(project)["Pages/CustomersPages.cs"];

            Assert.Contains("(grids?[0] ?? string.Empty)", text);
            Assert.Contains("pages.Add(new FormPages.Page(1, \"Orders\"", text);
            Assert.Contains("string.Empty + md0", text);
            Assert.Contains("if (mp == \"None\") body.Append(grids?[0] ?? string.Empty);", text);
        }
    }
}

[Collection("mdsite")]
public class MasterDetailSiteTests(MdSite site)
{
    private HttpClient Client => site.Client;

    [Fact]
    public async Task TheViewPageOfACategoryShowsItsProductsWithAnAddLinkThatCarriesThePreset()
    {
        var page = await Client.GetStringAsync("/categories/view/1");

        Assert.Contains("data-md-grid=\"0\"", page);
        Assert.Contains("<td>Hammer</td>", page);
        Assert.Contains("<td>Drill</td>", page);
        Assert.Contains("href=\"/products/add?m_category_id=1&amp;returnUrl=%2Fcategories%2Fview%2F1\"", page);
        Assert.Contains("href=\"/products/edit/1?returnUrl=%2Fcategories%2Fview%2F1\"", page);
        Assert.Contains("href=\"/products/view/1\"", page);
        Assert.DoesNotContain("<td>Hammer</td>", await Client.GetStringAsync("/categories/view/2"));
    }

    [Fact]
    public async Task TheGridPagesWithItsOwnParameterAndKeepsOtherParameters()
    {
        var first = await Client.GetStringAsync("/customers/view/1");
        Assert.Contains("<td>o1</td>", first);
        Assert.Contains("<td>o3</td>", first);
        Assert.DoesNotContain("<td>o4</td>", first);
        Assert.Contains("?dp0=2", first);

        var second = await Client.GetStringAsync("/customers/view/1?dp0=2&x=1");
        Assert.Contains("<td>o4</td>", second);
        Assert.Contains("<td>o5</td>", second);
        Assert.DoesNotContain("<td>o1</td>", second);
        Assert.Contains("x=1&amp;dp0=1", second);
        Assert.DoesNotContain("<td>o1</td>", await Client.GetStringAsync("/customers/view/1?dp0=99"));
        Assert.DoesNotContain("<td>o6</td>", first);
    }

    [Fact]
    public async Task TheEditPageShowsTheGridsUnderTheForm()
    {
        var page = await Client.GetStringAsync("/orders/edit/1");
        Assert.Contains("data-md-grid=\"0\"", page);
        Assert.Contains("<td>x</td>", page);
        Assert.Contains("href=\"/order-lines/view/1/1\"", page);
        Assert.Contains("/order-lines/add?m_order_id=1&amp;returnUrl=%2Forders%2Fedit%2F1", page);
        Assert.True(page.IndexOf("</form>", StringComparison.Ordinal) < page.IndexOf("data-md-grid", StringComparison.Ordinal));

        var composite = await Client.GetStringAsync("/order-lines/edit/1/1");
        Assert.Contains("data-md-grid=\"0\"", composite);
        Assert.Contains("<td>n1</td>", composite);
        Assert.Contains("<td>n2</td>", composite);
        Assert.DoesNotContain("<td>n3</td>", composite);
        Assert.Contains("m_order_id=1&amp;m_line_no=1", composite);
    }

    [Fact]
    public async Task ThePresetFieldIsReadOnlyAndTheServerForcesTheVerifiedValue()
    {
        var form = await Client.GetStringAsync("/orders/add?m_customer_id=2&returnUrl=%2Fcustomers%2Fview%2F2");
        Assert.Contains("<input type=\"hidden\" name=\"customer_id\" value=\"2\">", form);
        Assert.Contains("readonly", form);
        Assert.DoesNotContain("id=\"customer_id\"", form);
        Assert.Contains("action=\"/orders/add?m_customer_id=2&amp;returnUrl=%2Fcustomers%2Fview%2F2\"", form);

        var response = await site.PostAsync("/orders/add", "/orders/add?m_customer_id=2&returnUrl=%2Fcustomers%2Fview%2F2",
            new() { ["customer_id"] = "3", ["note"] = "forced-note" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/customers/view/2", response.Headers.Location!.ToString());
        Assert.Equal(1, await site.Count("SELECT COUNT(*) FROM orders WHERE note = 'forced-note' AND customer_id = 2"));
        Assert.Equal(0, await site.Count("SELECT COUNT(*) FROM orders WHERE note = 'forced-note' AND customer_id = 3"));
    }

    [Theory]
    [InlineData("m_customer_id=999")]
    [InlineData("m_customer_id=")]
    [InlineData("m_customer_id=abc")]
    [InlineData("m_customer_id=1%27%20OR%201%3D1")]
    public async Task APresetForAMasterThatDoesNotExistIsRejectedOnGetAndPost(string query)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/orders/add?" + query)).StatusCode);
        var response = await site.PostAsync("/orders/add", "/orders/add?" + query, new() { ["customer_id"] = "1", ["note"] = "tampered" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await site.Count("SELECT COUNT(*) FROM orders WHERE note = 'tampered'"));
    }

    [Fact]
    public async Task AHalfSuppliedCompositePresetIsRejected()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/line-notes/add?m_order_id=1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/line-notes/add?m_order_id=1&m_line_no=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/line-notes/add?m_order_id=1&m_line_no=77")).StatusCode);
    }

    [Theory]
    [InlineData("%2Fcustomers%2Fview%2F1", "/customers/view/1")]
    [InlineData("%2F%2Fevil.example", "/orders")]
    [InlineData("https%3A%2F%2Fevil.example", "/orders")]
    [InlineData("%2F%5Cevil.example", "/orders")]
    [InlineData("%2Fa%5Cb", "/orders")]
    [InlineData("%2Fa%0Ab", "/orders")]
    [InlineData("", "/orders")]
    public async Task OnlyLocalReturnUrlsAreFollowed(string returnUrl, string expected)
    {
        var response = await site.PostAsync("/orders/add?m_customer_id=1", "/orders/add?m_customer_id=1&returnUrl=" + returnUrl, new() { ["note"] = "back" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task EditAndDeleteReturnToTheMasterPage()
    {
        site.Run("INSERT INTO orders (id, customer_id, note) VALUES (200, 2, 'ret')");
        var edit = await site.PostAsync("/orders/edit/200", "/orders/edit/200?returnUrl=%2Fcustomers%2Fview%2F2", new() { ["customer_id"] = "2", ["note"] = "ret2" });
        Assert.Equal("/customers/view/2", edit.Headers.Location!.ToString());
        Assert.Equal(1, await site.Count("SELECT COUNT(*) FROM orders WHERE id = 200 AND note = 'ret2'"));
        var form = await Client.GetStringAsync("/orders/edit/200?returnUrl=%2Fcustomers%2Fview%2F2");
        Assert.Contains("href=\"/customers/view/2\"", form);

        var confirm = await Client.GetStringAsync("/orders/delete/200?returnUrl=%2Fcustomers%2Fview%2F2");
        Assert.Contains("action=\"/orders/delete/200?returnUrl=%2Fcustomers%2Fview%2F2\"", confirm);
        var delete = await site.PostAsync("/orders/delete/200?returnUrl=%2Fcustomers%2Fview%2F2", "/orders/delete/200?returnUrl=%2Fcustomers%2Fview%2F2");
        Assert.Equal("/customers/view/2", delete.Headers.Location!.ToString());
        Assert.Equal(0, await site.Count("SELECT COUNT(*) FROM orders WHERE id = 200"));
    }

    [Fact]
    public async Task CopyTreatsThePresetAsFixedToo()
    {
        var form = await Client.GetStringAsync("/orders/copy/1?m_customer_id=2");
        Assert.Contains("<input type=\"hidden\" name=\"customer_id\" value=\"2\">", form);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/orders/copy/1?m_customer_id=999")).StatusCode);
    }

    [Fact]
    public async Task ANewMasterRecordOpensItsEditPage()
    {
        var response = await site.PostAsync("/customers/add", "/customers/add", new() { ["name"] = "Dee", ["region"] = "W" });
        var id = await site.Count("SELECT id FROM customers WHERE name = 'Dee'");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/customers/edit/" + id, response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task ChangingALinkedValueUpdatesTheDetailRowsInTheSameTransaction()
    {
        site.Run("INSERT INTO customers (id, name) VALUES (30, 'Eve'); INSERT INTO orders (id, customer_id, customer_name, note) VALUES (301, 30, 'Eve', 'cu1'), (302, 30, 'Eve', 'cu2'), (303, 1, 'Ann', 'cu3')");
        var response = await site.PostAsync("/customers/edit/30", "/customers/edit/30", new() { ["name"] = "Eva", ["region"] = "E" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(2, await site.Count("SELECT COUNT(*) FROM orders WHERE customer_name = 'Eva' AND customer_id = 30"));
        Assert.Equal(0, await site.Count("SELECT COUNT(*) FROM orders WHERE customer_name = 'Eve'"));
        Assert.Equal(1, await site.Count("SELECT COUNT(*) FROM orders WHERE id = 303 AND customer_name = 'Ann'"));
    }

    [Fact]
    public async Task TheListsOfferTheConfiguredPreviews()
    {
        var customers = await Client.GetStringAsync("/customers");
        Assert.Contains("data-md-expand=\"/customers/details/0/1\"", customers);
        Assert.Contains("window.mdExpand", customers);
        Assert.Contains("Orders of &lt;customer&gt;", customers);
        Assert.Contains("href=\"/orders/details/0/1\"", await Client.GetStringAsync("/orders"));
        Assert.Contains("data-open=\"offcanvas\"", await Client.GetStringAsync("/orders"));
        Assert.Contains("id=\"pk-offcanvas\"", await Client.GetStringAsync("/orders"));
        var categories = await Client.GetStringAsync("/categories");
        Assert.Contains("data-open=\"modal\"", categories);
        Assert.Contains("id=\"pk-modal\"", categories);
    }

    [Fact]
    public async Task TheDetailsEndpointReturnsOnlyTheGridFragment()
    {
        var response = await Client.GetAsync("/customers/details/0/1?partial=1");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("<div data-partial=\"1\">", text);
        Assert.DoesNotContain("<html", text);
        Assert.Contains("<td>o1</td>", text);
        Assert.Contains("href=\"/customers/details/0/1?dp0=2\" data-md-page", text);
        Assert.Contains("returnUrl=%2Fcustomers", text);
        var page2 = await Client.GetStringAsync("/customers/details/0/1?dp0=2");
        Assert.Contains("<td>o5</td>", page2);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/customers/details/0/999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/customers/details/1/1")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Client.PostAsync("/customers/details/0/1", new StringContent(string.Empty))).StatusCode);
    }

    [Fact]
    public async Task TheApiListsTheDetailRowsOfAMasterRow()
    {
        var orders = await Client.GetStringAsync("/api/customers/1/orders");
        Assert.Equal(await site.Count("SELECT COUNT(*) FROM orders WHERE customer_id = 1"), System.Text.Json.JsonDocument.Parse(orders).RootElement.GetArrayLength());
        Assert.Contains("\"customerId\":1", orders);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/customers/999/orders")).StatusCode);
        var notes = await Client.GetStringAsync("/api/order-lines/1/1/line-notes");
        Assert.Equal(2, System.Text.Json.JsonDocument.Parse(notes).RootElement.GetArrayLength());
        Assert.Equal(0, System.Text.Json.JsonDocument.Parse(await Client.GetStringAsync("/api/categories/2/products")).RootElement.GetArrayLength());
    }
}
