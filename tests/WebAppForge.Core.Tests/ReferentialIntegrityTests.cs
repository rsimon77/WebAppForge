using System.Net;
using System.Text;

namespace WebAppForge.Core.Tests;

[Collection("mdsite")]
public class ReferentialIntegrityTests(MdSite site)
{
    private HttpClient Client => site.Client;

    [Fact]
    public async Task RestrictShowsTheMessageAndDisablesTheDeleteButton()
    {
        var page = await Client.GetStringAsync("/categories/delete/1");

        Assert.Contains("Cannot delete: 3 related products record(s) exist.", page);
        Assert.Contains("<button type=\"submit\" disabled class=\"btn btn-danger\"", page);
        var free = await Client.GetStringAsync("/categories/delete/2");
        Assert.DoesNotContain("Cannot delete", free);
        Assert.DoesNotContain("disabled", free);
    }

    [Fact]
    public async Task RestrictRefusesThePostAndTheApiWithConflict()
    {
        var post = await site.PostAsync("/categories/delete/1", "/categories/delete/1");
        var text = await post.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Conflict, post.StatusCode);
        Assert.Contains("Cannot delete: 3 related products record(s) exist.", text);
        Assert.Equal(1, await site.Count("SELECT COUNT(*) FROM categories WHERE id = 1"));

        var api = await Client.DeleteAsync("/api/categories/1");
        Assert.Equal(HttpStatusCode.Conflict, api.StatusCode);
        Assert.Equal("application/json; charset=utf-8", api.Content.Headers.ContentType!.ToString());
        Assert.Contains("\"error\":\"Cannot delete: 3 related products record(s) exist.\"", await api.Content.ReadAsStringAsync());
        Assert.Equal(3, await site.Count("SELECT COUNT(*) FROM products WHERE category_id = 1"));
    }

    [Fact]
    public async Task ARestrictedMasterWithoutDetailsIsDeleted()
    {
        site.Run("INSERT INTO categories (id, name) VALUES (50, 'Gone')");
        var post = await site.PostAsync("/categories/delete/50", "/categories/delete/50");
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Equal(0, await site.Count("SELECT COUNT(*) FROM categories WHERE id = 50"));
        Assert.Equal(HttpStatusCode.NotFound, (await site.PostAsync("/categories/delete/2", "/categories/delete/50")).StatusCode);
    }

    [Fact]
    public async Task TheConfirmationListsWhatACascadeWillAlsoDelete()
    {
        site.Run("INSERT INTO orders (id, customer_id, note) VALUES (60, 2, 'c'); INSERT INTO order_lines VALUES (60, 1, 'a', 1), (60, 2, 'b', 1); INSERT INTO line_notes (order_id, line_no, text) VALUES (60, 1, 'x'), (60, 1, 'y'), (60, 2, 'z')");
        var page = await Client.GetStringAsync("/orders/delete/60");

        Assert.Contains("This will also delete 2 record(s) in order_lines.", page);
        Assert.Contains("This will also delete 3 record(s) in line_notes.", page);
        Assert.DoesNotContain("disabled", page);
    }

    [Fact]
    public async Task CascadeDeletesTheDetailsAndTheirNestedDetailsInOneTransaction()
    {
        site.Run("INSERT INTO orders (id, customer_id, note) VALUES (61, 2, 'c'), (62, 2, 'keep'); INSERT INTO order_lines VALUES (61, 1, 'a', 1), (61, 2, 'b', 1), (62, 1, 'k', 1); INSERT INTO line_notes (order_id, line_no, text) VALUES (61, 1, 'x'), (61, 2, 'z'), (62, 1, 'keep')");
        var post = await site.PostAsync("/orders/delete/61", "/orders/delete/61");

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Equal(0, await site.Count("SELECT COUNT(*) FROM orders WHERE id = 61"));
        Assert.Equal(0, await site.Count("SELECT COUNT(*) FROM order_lines WHERE order_id = 61"));
        Assert.Equal(0, await site.Count("SELECT COUNT(*) FROM line_notes WHERE order_id = 61"));
        Assert.Equal(1, await site.Count("SELECT COUNT(*) FROM orders WHERE id = 62"));
        Assert.Equal(1, await site.Count("SELECT COUNT(*) FROM order_lines WHERE order_id = 62"));
        Assert.Equal(1, await site.Count("SELECT COUNT(*) FROM line_notes WHERE order_id = 62"));
    }

    [Fact]
    public async Task TheApiCascadesToo()
    {
        site.Run("INSERT INTO orders (id, customer_id, note) VALUES (63, 2, 'c'); INSERT INTO order_lines VALUES (63, 1, 'a', 1); INSERT INTO line_notes (order_id, line_no, text) VALUES (63, 1, 'x')");
        var response = await Client.DeleteAsync("/api/orders/63");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await site.Count("SELECT COUNT(*) FROM order_lines WHERE order_id = 63"));
        Assert.Equal(0, await site.Count("SELECT COUNT(*) FROM line_notes WHERE order_id = 63"));
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync("/api/orders/63")).StatusCode);
    }

    [Fact]
    public async Task SetNullClearsTheLinkColumnsOfTheDetailRows()
    {
        site.Run("INSERT INTO employees (id, name, boss_id) VALUES (70, 'Chief', NULL), (71, 'Report', 70); INSERT INTO orders (id, customer_id, employee_id, note) VALUES (70, 2, 70, 'e1'), (71, 2, 70, 'e2'), (72, 2, 1, 'other')");
        var post = await site.PostAsync("/employees/delete/70", "/employees/delete/70");

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Equal(0, await site.Count("SELECT COUNT(*) FROM employees WHERE id = 70"));
        Assert.Equal(2, await site.Count("SELECT COUNT(*) FROM orders WHERE id IN (70, 71) AND employee_id IS NULL"));
        Assert.Equal(1, await site.Count("SELECT COUNT(*) FROM employees WHERE id = 71 AND boss_id IS NULL"));
        Assert.Equal(1, await site.Count("SELECT COUNT(*) FROM orders WHERE id = 72 AND employee_id = 1"));
    }

    [Fact]
    public async Task AFailureAfterTheDetailsAreDeletedRollsEverythingBack()
    {
        // The audit table has a foreign key to orders but no link, so the delete of the order fails after its details are gone.
        site.Run("INSERT INTO orders (id, customer_id, note) VALUES (80, 2, 'r'); INSERT INTO order_lines VALUES (80, 1, 'a', 1); INSERT INTO line_notes (order_id, line_no, text) VALUES (80, 1, 'x'); INSERT INTO audit (order_id) VALUES (80)");
        var response = await site.PostAsync("/orders/delete/80", "/orders/delete/80");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, await site.Count("SELECT COUNT(*) FROM orders WHERE id = 80"));
        Assert.Equal(1, await site.Count("SELECT COUNT(*) FROM order_lines WHERE order_id = 80"));
        Assert.Equal(1, await site.Count("SELECT COUNT(*) FROM line_notes WHERE order_id = 80"));

        // The transaction was closed: the next change works.
        site.Run("DELETE FROM audit WHERE order_id = 80");
        var again = await site.PostAsync("/orders/delete/80", "/orders/delete/80");
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        Assert.Equal(0, await site.Count("SELECT COUNT(*) FROM order_lines WHERE order_id = 80"));
    }

    [Fact]
    public async Task APutThatChangesALinkedValueCascadesInTheApiToo()
    {
        site.Run("INSERT INTO customers (id, name) VALUES (90, 'Fay'); INSERT INTO orders (id, customer_id, customer_name, note) VALUES (901, 90, 'Fay', 'p')");
        var body = new StringContent("{\"id\":90,\"name\":\"Faye\",\"region\":null}", Encoding.UTF8, "application/json");
        var response = await Client.PutAsync("/api/customers/90", body);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, await site.Count("SELECT COUNT(*) FROM orders WHERE id = 901 AND customer_name = 'Faye'"));
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PutAsync("/api/customers/9999", new StringContent("{\"id\":9999,\"name\":\"x\"}", Encoding.UTF8, "application/json"))).StatusCode);
    }
}
