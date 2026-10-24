using WebAppForge.Core.Model;
using WebAppForge.Desktop.Tests.TestSupport;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Tests;

/// <summary>View model tests for the Relations tab: links, suggestions, overview, validation, form pages, preview and dirty tracking. In-memory projects only.</summary>
public class RelationsTests
{
    /// <summary>The Customers/Orders project of the customization tests with Customers selected as the master.</summary>
    private static (MainViewModel Main, FakeDialogService Dialogs) Setup()
    {
        var (main, dialogs) = CustomizationTests.Project();
        main.Tables.SelectedTable = main.Current.Tables[0];
        return (main, dialogs);
    }

    private static TableInfo Customers(MainViewModel main) => main.Current.Tables.Single(t => t.Name == "Customers");

    private static TableInfo Orders(MainViewModel main) => main.Current.Tables.Single(t => t.Name == "Orders");

    private static DetailLink OrdersLink(string master = "Id", string detail = "CustomerId")
    {
        var link = new DetailLink { DetailTable = "Orders" };
        link.Links.Add(new KeyLink { MasterField = master, DetailField = detail });
        return link;
    }

    private static IEnumerable<string> Messages(MainViewModel main) => RelationValidator.Validate(main.Current.Tables).Select(i => i.Message);

    private static async Task CleanAsync(MainViewModel main, FakeDialogService dialogs, TempFolder folder)
    {
        dialogs.NextSaveProject = folder.File("clean.xml");
        await main.SaveAsCommand.ExecuteAsync(null);
        Assert.False(main.IsDirty);
    }

    // ---- links write through ----

    [Fact]
    public void TheTabIsAppendedAndTheBreadcrumbShowsMasterAndDetail()
    {
        var (main, _) = Setup();
        Assert.Equal(["Relations", "Reports"], MainViewModel.TabNames.TakeLast(2));
        main.ActiveTabIndex = MainViewModel.TabNames.ToList().IndexOf("Relations");
        Assert.EndsWith("> Relations > Customers", main.Breadcrumb);

        main.Relations.AddLinkCommand.Execute(null);
        Assert.EndsWith("> Relations > Customers > Orders", main.Breadcrumb);
    }

    [Fact]
    public void AddingALinkGuessesTheKeyPairAndWritesToTheMaster()
    {
        var (main, _) = Setup();
        var relations = main.Relations;
        Assert.False(relations.HasLink);

        relations.AddLinkCommand.Execute(null);

        var link = Assert.Single(Customers(main).Details);
        Assert.Equal("Orders", link.DetailTable);
        var pair = Assert.Single(link.Links);
        Assert.Equal(("Id", "CustomerId"), (pair.MasterField, pair.DetailField));
        Assert.True(relations.HasLink);
        Assert.Equal("Orders (Id = CustomerId)", relations.SelectedLink!.Text);
        Assert.Equal(["Id", "CustomerId"], relations.KeyPairs.Select(k => k.MasterField).Concat(relations.KeyPairs.Select(k => k.DetailField)).ToArray());
        Assert.Equal("Integer", relations.KeyPairs[0].MasterType);
        Assert.Equal(string.Empty, relations.KeyPairs[0].TypeNote);
    }

    [Fact]
    public void EveryLinkPropertyWritesThroughToTheModel()
    {
        var (main, _) = Setup();
        Orders(main).Fields[2].IsRequired = false;
        var relations = main.Relations;
        relations.AddLinkCommand.Execute(null);
        var link = Customers(main).Details.Single();

        relations.Caption = "Their orders";
        relations.ShowOnView = false;
        relations.ShowOnEdit = false;
        relations.AfterAddGoToEdit = true;
        relations.PreviewChoice = relations.PreviewChoices.Single(c => c.Value == DetailPreview.Offcanvas);
        relations.OnDeleteOption = relations.DeleteRuleOptions.Single(o => o.Rule == DeleteRule.Cascade);
        relations.UpdateChoice = relations.UpdateChoices.Single(c => c.Value == UpdateRule.Cascade);
        relations.PageSizeText = "25";
        relations.AllowAdd = false;
        relations.AllowEdit = false;
        relations.AllowDelete = false;

        Assert.Equal("Their orders", link.Caption);
        Assert.False(link.ShowOnView);
        Assert.False(link.ShowOnEdit);
        Assert.True(link.ShowOnAdd);
        Assert.Equal(DetailPreview.Offcanvas, link.Preview);
        Assert.Equal(DeleteRule.Cascade, link.OnDelete);
        Assert.Equal(UpdateRule.Cascade, link.OnUpdate);
        Assert.Equal(25, link.PageSize);
        Assert.False(link.AllowAdd);
        Assert.False(link.AllowEdit);
        Assert.False(link.AllowDelete);
        Assert.Equal("Delete the detail records together with the master record, in one transaction.", relations.OnDeleteExplanation);
        Assert.True(relations.HasUpdateNote);
    }

    [Fact]
    public void EachDeleteRuleHasAOneLineExplanation()
    {
        var (main, _) = Setup();
        var relations = main.Relations;
        relations.AddLinkCommand.Execute(null);
        foreach (var rule in Enum.GetValues<DeleteRule>())
        {
            relations.OnDeleteOption = relations.DeleteRuleOptions.Single(o => o.Rule == rule);
            Assert.False(string.IsNullOrWhiteSpace(relations.OnDeleteExplanation));
            Assert.DoesNotContain('\n', relations.OnDeleteExplanation);
        }

        Assert.Equal(4, relations.DeleteRuleOptions.Select(o => o.Explanation).Distinct().Count());
    }

    [Fact]
    public void PageSizeAcceptsOneToTwoHundredAndKeepsTheLastValidValue()
    {
        var (main, _) = Setup();
        var relations = main.Relations;
        relations.AddLinkCommand.Execute(null);
        var link = Customers(main).Details.Single();

        relations.PageSizeText = "200";
        Assert.Equal(200, link.PageSize);
        foreach (var bad in new[] { "0", "201", "abc", string.Empty })
        {
            relations.PageSizeText = bad;
            Assert.Equal(200, link.PageSize);
            Assert.True(relations.HasMessage);
            Assert.Equal(bad, relations.PageSizeText);
        }

        relations.PageSizeText = "1";
        Assert.Equal(1, link.PageSize);
        Assert.False(relations.HasMessage);
    }

    [Fact]
    public void SetNullIsDisabledAndRefusedWhileALinkFieldIsRequired()
    {
        var (main, _) = Setup();
        var relations = main.Relations;
        relations.AddLinkCommand.Execute(null);
        var setNull = relations.DeleteRuleOptions.Single(o => o.Rule == DeleteRule.SetNull);

        Assert.True(setNull.IsEnabled);
        Assert.Equal(string.Empty, relations.SetNullNote);

        Orders(main).Fields.Single(f => f.Name == "CustomerId").IsRequired = true;
        relations.AddKeyPairCommand.Execute(null);
        relations.RemoveKeyPairCommand.Execute(null);
        Assert.False(setNull.IsEnabled);
        Assert.False(relations.CanSetNull);
        Assert.Contains("'CustomerId' of 'Orders' is required", relations.SetNullNote);

        relations.OnDelete = DeleteRule.SetNull;
        Assert.Equal(DeleteRule.Restrict, Customers(main).Details.Single().OnDelete);
        Assert.True(relations.HasMessage);
        Assert.Contains("last rule was kept", relations.Message);

        Orders(main).Fields.Single(f => f.Name == "CustomerId").IsRequired = false;
        relations.AddKeyPairCommand.Execute(null);
        Assert.True(setNull.IsEnabled);
        relations.OnDelete = DeleteRule.SetNull;
        Assert.Equal(DeleteRule.SetNull, Customers(main).Details.Single().OnDelete);
    }

    [Fact]
    public void ChangingTheDetailTableStartsTheKeyPairsAndColumnsOver()
    {
        var (main, _) = Setup();
        var relations = main.Relations;
        relations.AddLinkCommand.Execute(null);
        relations.AddColumnCommand.Execute(null);
        Assert.Single(Customers(main).Details.Single().Columns);

        relations.DetailTable = "Customers";

        var link = Customers(main).Details.Single();
        Assert.Equal("Customers", link.DetailTable);
        Assert.Empty(link.Columns);
        Assert.Empty(relations.Columns);
        Assert.Equal(relations.DetailFieldNames, Customers(main).Fields.Select(f => f.Name));
        // A self link never pairs the key with itself.
        Assert.DoesNotContain(link.Links, l => l.MasterField == l.DetailField);
        Assert.Contains("Customers", relations.DetailTableNames);
    }

    [Fact]
    public void KeyPairsAddRemoveAndShowTypesAndWarnings()
    {
        var (main, _) = Setup();
        var relations = main.Relations;
        relations.AddLinkCommand.Execute(null);
        var link = Customers(main).Details.Single();

        relations.KeyPairs[0].MasterField = "Name";
        Assert.Equal("Text", relations.KeyPairs[0].MasterType);
        Assert.Equal("Integer", relations.KeyPairs[0].DetailType);
        Assert.Equal("Types differ", relations.KeyPairs[0].TypeNote);
        Assert.Contains(relations.LinkIssues, i => i.Contains("is Text but detail field 'Orders.CustomerId' is Integer"));

        relations.AddKeyPairCommand.Execute(null);
        Assert.Equal(2, link.Links.Count);
        relations.SelectedKeyPair = relations.KeyPairs[1];
        relations.RemoveKeyPairCommand.Execute(null);
        relations.SelectedKeyPair = relations.KeyPairs[0];
        relations.RemoveKeyPairCommand.Execute(null);
        Assert.Empty(link.Links);
        Assert.Contains(relations.LinkIssues, i => i.Contains("add at least one key link"));

        // A combo box reports null while its list is rebuilt: that is not a choice.
        relations.AddKeyPairCommand.Execute(null);
        var before = link.Links[0].MasterField;
        relations.KeyPairs[0].MasterField = null!;
        Assert.Equal(before, link.Links[0].MasterField);
    }

    [Fact]
    public void LinksMoveAndRemoveInTheModelOrder()
    {
        var (main, _) = Setup();
        var relations = main.Relations;
        relations.AddLinkCommand.Execute(null);
        relations.DetailTable = "Orders";
        relations.Caption = "first";
        relations.AddLinkCommand.Execute(null);
        relations.Caption = "second";
        var details = Customers(main).Details;

        relations.MoveLinkUpCommand.Execute(null);
        Assert.Equal(["second", "first"], details.Select(d => d.Caption).ToArray());
        Assert.Equal(["Orders (Id = CustomerId)", "Orders (Id = CustomerId)"], relations.Links.Select(l => l.Text).ToArray());
        Assert.Same(details[0], relations.Links[0].Link);

        relations.MoveLinkUpCommand.Execute(null);
        Assert.Equal("second", details[0].Caption);
        relations.MoveLinkDownCommand.Execute(null);
        Assert.Equal(["first", "second"], details.Select(d => d.Caption).ToArray());

        relations.RemoveLinkCommand.Execute(null);
        Assert.Equal(["first"], details.Select(d => d.Caption).ToArray());
        Assert.Same(details[0], relations.SelectedLink!.Link);
        relations.RemoveLinkCommand.Execute(null);
        Assert.Empty(details);
        Assert.False(relations.HasLink);
    }

    [Fact]
    public void DetailGridColumnsAddRemoveAndMove()
    {
        var (main, _) = Setup();
        var relations = main.Relations;
        relations.AddLinkCommand.Execute(null);
        var link = Customers(main).Details.Single();

        relations.AddColumnCommand.Execute(null);
        relations.AddColumnCommand.Execute(null);
        relations.AddColumnCommand.Execute(null);
        Assert.Equal(["Id", "CustomerId", "Total"], link.Columns.ToArray());

        relations.SelectedColumn = relations.Columns[2];
        relations.MoveColumnUpCommand.Execute(null);
        Assert.Equal(["Id", "Total", "CustomerId"], link.Columns.ToArray());
        relations.MoveColumnDownCommand.Execute(null);
        Assert.Equal(["Id", "CustomerId", "Total"], link.Columns.ToArray());

        relations.Columns[0].Field = "Status";
        Assert.Equal(["Status", "CustomerId", "Total"], link.Columns.ToArray());

        relations.SelectedColumn = relations.Columns[1];
        relations.RemoveColumnCommand.Execute(null);
        Assert.Equal(["Status", "Total"], link.Columns.ToArray());

        relations.AddColumnCommand.Execute(null);
        relations.AddColumnCommand.Execute(null);
        relations.AddColumnCommand.Execute(null);
        Assert.Equal(4, link.Columns.Count);
        Assert.Contains("Every field is already a column", relations.Message);
    }

    [Fact]
    public void TheGridFormPageIsAPickerOfTheMastersPagesPlusBelowTheForm()
    {
        var (main, _) = Setup();
        Customers(main).FormPages.Add(new FormPageDef { Name = "Orders tab" });
        var relations = main.Relations;
        relations.AddLinkCommand.Execute(null);

        Assert.Equal([RelationsViewModel.BelowFormLabel, "Orders tab"], relations.FormPageChoices.ToArray());
        Assert.Equal(RelationsViewModel.BelowFormLabel, relations.FormPageChoice);

        relations.FormPageChoice = "Orders tab";
        Assert.Equal("Orders tab", Customers(main).Details.Single().FormPage);
        relations.FormPageChoice = RelationsViewModel.BelowFormLabel;
        Assert.Equal(string.Empty, Customers(main).Details.Single().FormPage);
    }

    // ---- suggestions ----

    /// <summary>OrderItems.OrderId -> Orders.Id (cascade) and Orders.CustomerId -> Customers.Id (set null), plus an unknown table.</summary>
    private static void AddForeignKeys(MainViewModel main)
    {
        var orders = Orders(main);
        var key = new ForeignKeyInfo { ReferencedTable = "customers", OnDelete = "SET NULL", OnUpdate = "CASCADE" };
        key.Columns.Add("CustomerId");
        key.ReferencedColumns.Add("Id");
        orders.ForeignKeys.Add(key);

        var items = new TableInfo { Name = "OrderItems" };
        items.Fields.Add(new FieldInfo { Name = "Id", Type = FieldType.Integer, IsPrimaryKey = true });
        items.Fields.Add(new FieldInfo { Name = "OrderId", Type = FieldType.Integer });
        var cascade = new ForeignKeyInfo { ReferencedTable = "Orders", OnDelete = "CASCADE" };
        cascade.Columns.Add("OrderId");
        items.ForeignKeys.Add(cascade);
        var missing = new ForeignKeyInfo { ReferencedTable = "Nowhere" };
        missing.Columns.Add("OrderId");
        missing.ReferencedColumns.Add("Id");
        items.ForeignKeys.Add(missing);
        main.Current.Tables.Add(items);
        main.Tables.Reload();
        main.Tables.SelectedTable = Customers(main);
    }

    [Fact]
    public void SuggestionsComeFromForeignKeysWithSensibleDefaults()
    {
        var (main, _) = Setup();
        AddForeignKeys(main);

        var suggestions = RelationSuggester.Suggest(main.Current.Tables);

        Assert.Equal(2, suggestions.Count);
        var customers = suggestions.Single(s => s.Master.Name == "Customers");
        Assert.Equal("Orders", customers.DetailTable);
        Assert.Equal([("Id", "CustomerId")], customers.Pairs.ToArray());
        Assert.Equal(UpdateRule.Cascade, customers.OnUpdate);
        // The key says SET NULL, but CustomerId is not required, so Set null stands.
        Assert.Equal(DeleteRule.SetNull, customers.OnDelete);
        Assert.False(customers.Exists);

        var orders = suggestions.Single(s => s.Master.Name == "Orders");
        Assert.Equal(DeleteRule.Cascade, orders.OnDelete);
        // No referenced columns means the primary key of the referenced table.
        Assert.Equal([("Id", "OrderId")], orders.Pairs.ToArray());
        Assert.Equal(DeleteRule.Restrict, RelationSuggester.DeleteRuleFrom("NO ACTION"));
        Assert.Equal(DeleteRule.Restrict, RelationSuggester.DeleteRuleFrom(string.Empty));
        Assert.Equal(DeleteRule.Restrict, RelationSuggester.DeleteRuleFrom("SET DEFAULT"));
        Assert.Equal(DeleteRule.SetNull, RelationSuggester.DeleteRuleFrom("set  null"));
    }

    [Fact]
    public void ASetNullKeyOnARequiredColumnFallsBackToRestrict()
    {
        var (main, _) = Setup();
        AddForeignKeys(main);
        Orders(main).Fields.Single(f => f.Name == "CustomerId").IsRequired = true;

        var suggestion = RelationSuggester.Suggest(main.Current.Tables).Single(s => s.Master.Name == "Customers");

        Assert.Equal(DeleteRule.Restrict, suggestion.OnDelete);
    }

    [Fact]
    public async Task WithoutForeignKeysTheButtonSaysSoAndShowsNoDialog()
    {
        var (main, dialogs) = Setup();

        await main.Relations.SuggestFromForeignKeysCommand.ExecuteAsync(null);

        Assert.Contains("No foreign keys were found", main.Relations.SuggestMessage);
        Assert.Equal(main.Relations.SuggestMessage, main.Status);
        Assert.Empty(dialogs.LastChoicesOffered);
        Assert.Empty(Customers(main).Details);
    }

    [Fact]
    public async Task TheDialogDisablesExistingLinksAndAddsTheChosenOnes()
    {
        var (main, dialogs) = Setup();
        AddForeignKeys(main);
        Customers(main).Details.Add(OrdersLink());
        main.Relations.Reload();

        await main.Relations.SuggestFromForeignKeysCommand.ExecuteAsync(null);
        Assert.Equal(2, dialogs.LastChoicesOffered.Count);
        var existing = Assert.Single(dialogs.LastChoicesDisabled);
        Assert.StartsWith("Customers -> Orders", existing);

        // Ticking the disabled one is ignored; the other one is added to Orders.
        dialogs.NextChoices = [.. dialogs.LastChoicesOffered];
        await main.Relations.SuggestFromForeignKeysCommand.ExecuteAsync(null);

        Assert.Single(Customers(main).Details);
        var added = Assert.Single(Orders(main).Details, d => d.DetailTable == "OrderItems");
        Assert.Equal(DeleteRule.Cascade, added.OnDelete);
        Assert.Equal(("Id", "OrderId"), (added.Links[0].MasterField, added.Links[0].DetailField));
        Assert.Equal(10, added.PageSize);
        Assert.Contains("Added 1 master/detail link", main.Status);

        // Now every foreign key has a link.
        dialogs.NextChoices = null;
        await main.Relations.SuggestFromForeignKeysCommand.ExecuteAsync(null);
        Assert.Contains("Every foreign key already has", main.Relations.SuggestMessage);
    }

    [Fact]
    public async Task CancellingTheDialogAddsNothingAndSelectsTheAddedLink()
    {
        var (main, dialogs) = Setup();
        AddForeignKeys(main);

        dialogs.NextChoices = null;
        await main.Relations.SuggestFromForeignKeysCommand.ExecuteAsync(null);
        Assert.Empty(Customers(main).Details);
        Assert.Empty(Orders(main).Details);

        dialogs.NextChoices = [dialogs.LastChoicesOffered.Single(c => c.StartsWith("Customers -> Orders", StringComparison.Ordinal))];
        await main.Relations.SuggestFromForeignKeysCommand.ExecuteAsync(null);

        var link = Assert.Single(Customers(main).Details);
        Assert.Equal(DeleteRule.SetNull, link.OnDelete);
        Assert.Same(link, main.Relations.SelectedLink!.Link);
    }

    // ---- overview ----

    [Fact]
    public void TheOverviewListsEveryLinkAndOpeningOneSelectsIt()
    {
        var (main, _) = Setup();
        Customers(main).Details.Add(OrdersLink());
        var second = new DetailLink { DetailTable = "Customers", OnDelete = DeleteRule.Cascade };
        second.Links.Add(new KeyLink { MasterField = "Id", DetailField = "Age" });
        Orders(main).Details.Add(second);
        main.Relations.Reload();

        var overview = main.Relations.Overview;
        Assert.Equal(2, overview.Count);
        Assert.Equal("Customers -> Orders   [Id = CustomerId]   delete: Restrict, update: None", overview[0].Text);
        Assert.Equal("Orders -> Customers   [Id = Age]   delete: Cascade, update: None", overview[1].Text);

        main.Relations.OpenOverviewItemCommand.Execute(overview[1]);

        Assert.Same(Orders(main), main.Tables.SelectedTable);
        Assert.Same(second, main.Relations.SelectedLink!.Link);
        Assert.Equal("Customers", main.Relations.DetailTable);
    }

    // ---- validation: every message ----

    [Fact]
    public void ALinkWithoutAnyProblemHasNoFindings()
    {
        var (main, _) = Setup();
        Customers(main).Details.Add(OrdersLink());

        Assert.Empty(RelationValidator.Validate(main.Current.Tables));
        Assert.Null(RelationValidator.FirstError(main.Current.Tables));
    }

    [Fact]
    public void DetailTableMessages()
    {
        var (main, _) = Setup();
        var customers = Customers(main);
        customers.Details.Add(new DetailLink());
        var gone = OrdersLink();
        gone.DetailTable = "Ghosts";
        customers.Details.Add(gone);
        Orders(main).Generate = false;
        customers.Details.Add(OrdersLink());

        var messages = Messages(main).ToList();

        Assert.Contains("Master/detail Customers -> (no detail table): choose the detail table.", messages);
        Assert.Contains("Master/detail Customers -> (no detail table): add at least one key link (master field = detail field).", messages);
        Assert.Contains("Master/detail Customers -> Ghosts: the detail table 'Ghosts' does not exist.", messages);
        Assert.Contains("Master/detail Customers -> Orders: the detail table 'Orders' is not generated (Generate is off).", messages);
    }

    [Fact]
    public void KeyPairMessages()
    {
        var (main, _) = Setup();
        var customers = Customers(main);
        var link = new DetailLink { DetailTable = "Orders" };
        link.Links.Add(new KeyLink { MasterField = string.Empty, DetailField = "CustomerId" });
        link.Links.Add(new KeyLink { MasterField = "Id", DetailField = string.Empty });
        link.Links.Add(new KeyLink { MasterField = "Nope", DetailField = "CustomerId" });
        link.Links.Add(new KeyLink { MasterField = "Id", DetailField = "Missing" });
        link.Links.Add(new KeyLink { MasterField = "Age", DetailField = "Total" });
        link.Links.Add(new KeyLink { MasterField = "age", DetailField = "TOTAL" });
        customers.Details.Add(link);

        var messages = Messages(main).ToList();

        Assert.Contains("Master/detail Customers -> Orders: key link 1 has no master field.", messages);
        Assert.Contains("Master/detail Customers -> Orders: key link 2 has no detail field.", messages);
        Assert.Contains("Master/detail Customers -> Orders: the master field 'Nope' does not exist in table 'Customers'.", messages);
        Assert.Contains("Master/detail Customers -> Orders: the detail field 'Missing' does not exist in table 'Orders'.", messages);
        Assert.Contains("Master/detail Customers -> Orders: the key link 'age = TOTAL' is listed twice.", messages);
        Assert.Contains("Master/detail Customers -> Orders: master field 'Customers.Age' is Integer but detail field 'Orders.Total' is Decimal; the values may never match.", messages);
    }

    [Fact]
    public void DuplicateLinkMessageIgnoresPairOrderAndCase()
    {
        var (main, _) = Setup();
        var a = OrdersLink();
        a.Links.Add(new KeyLink { MasterField = "Age", DetailField = "Id" });
        var b = new DetailLink { DetailTable = "orders" };
        b.Links.Add(new KeyLink { MasterField = "AGE", DetailField = "id" });
        b.Links.Add(new KeyLink { MasterField = "id", DetailField = "customerid" });
        Customers(main).Details.Add(a);
        Customers(main).Details.Add(b);

        var issue = RelationValidator.Validate(main.Current.Tables).Single(i => i.Message.Contains("duplicate of another link", StringComparison.Ordinal));

        Assert.True(issue.IsError);
        Assert.Same(b, issue.Link);
        Assert.Equal("Master/detail Customers -> orders: duplicate of another link of 'Customers' to 'orders' with the same key fields (AGE = id, id = customerid).", issue.Message);
    }

    [Fact]
    public void GridPageSizeFormPageAndColumnMessages()
    {
        var (main, _) = Setup();
        var link = OrdersLink();
        link.PageSize = 201;
        link.FormPage = "Gone";
        link.Columns.Add("Total");
        link.Columns.Add("Bogus");
        Customers(main).Details.Add(link);

        var messages = Messages(main).ToList();

        Assert.Contains("Master/detail Customers -> Orders: the page size 201 must be from 1 to 200.", messages);
        Assert.Contains("Master/detail Customers -> Orders: the grid is placed on form page 'Gone', which 'Customers' does not have.", messages);
        Assert.Contains("Master/detail Customers -> Orders: the grid column 'Bogus' does not exist in table 'Orders'.", messages);
        Assert.DoesNotContain(messages, m => m.Contains("'Total'", StringComparison.Ordinal));

        link.PageSize = 0;
        Assert.Contains("Master/detail Customers -> Orders: the page size 0 must be from 1 to 200.", Messages(main));
    }

    [Fact]
    public void SetNullAndUpdateAndPartialKeyMessages()
    {
        var (main, _) = Setup();
        Orders(main).Fields.Single(f => f.Name == "CustomerId").IsRequired = true;
        var link = OrdersLink();
        link.OnDelete = DeleteRule.SetNull;
        link.OnUpdate = UpdateRule.Cascade;
        Customers(main).Details.Add(link);

        var messages = Messages(main).ToList();

        Assert.Contains("Master/detail Customers -> Orders: on delete 'Set null' needs nullable link fields, but 'CustomerId' of 'Orders' is required.", messages);
        Assert.Contains("Master/detail Customers -> Orders: 'Cascade' on update only matters for editable non-key link fields, and every master field here is a key field.", messages);
        Assert.True(RelationValidator.Validate(main.Current.Tables).Single(i => i.Message.Contains("on update only", StringComparison.Ordinal)).Severity == RelationSeverity.Warning);

        var composite = new TableInfo { Name = "Lines" };
        composite.Fields.Add(new FieldInfo { Name = "A", Type = FieldType.Integer, IsPrimaryKey = true, KeyPosition = 1 });
        composite.Fields.Add(new FieldInfo { Name = "B", Type = FieldType.Integer, IsPrimaryKey = true, KeyPosition = 2 });
        var partial = new DetailLink { DetailTable = "Orders" };
        partial.Links.Add(new KeyLink { MasterField = "A", DetailField = "CustomerId" });
        composite.Details.Add(partial);
        main.Current.Tables.Add(composite);

        Assert.Contains("Master/detail Lines -> Orders: links 1 of the 2 primary key columns of 'Lines'; one detail row can match several master rows.", Messages(main));
    }

    [Fact]
    public void ASelfLinkOnTheSameFieldIsAWarning()
    {
        var (main, _) = Setup();
        var link = new DetailLink { DetailTable = "Customers" };
        link.Links.Add(new KeyLink { MasterField = "Age", DetailField = "Age" });
        Customers(main).Details.Add(link);

        var issues = RelationValidator.Validate(main.Current.Tables);

        var issue = Assert.Single(issues);
        Assert.Equal(RelationSeverity.Warning, issue.Severity);
        Assert.Equal("Master/detail Customers -> Customers: the key link 'Age = Age' relates every row to itself.", issue.Message);
        Assert.Equal("Warning: " + issue.Message, issue.Text);
    }

    [Fact]
    public void CascadeDeleteCyclesAreErrors()
    {
        var (main, _) = Setup();
        var down = OrdersLink();
        down.OnDelete = DeleteRule.Cascade;
        Customers(main).Details.Add(down);
        Assert.DoesNotContain(Messages(main), m => m.StartsWith("Cascade delete cycle", StringComparison.Ordinal));

        var up = new DetailLink { DetailTable = "Customers", OnDelete = DeleteRule.Cascade };
        up.Links.Add(new KeyLink { MasterField = "Id", DetailField = "Age" });
        Orders(main).Details.Add(up);

        var cycle = Assert.Single(Messages(main), m => m.StartsWith("Cascade delete cycle", StringComparison.Ordinal));
        Assert.Equal("Cascade delete cycle: Customers -> Orders -> Customers. Deleting a record would loop; use Restrict or Set null on one of these links.", cycle);

        var self = new DetailLink { DetailTable = "Customers", OnDelete = DeleteRule.Cascade };
        self.Links.Add(new KeyLink { MasterField = "Id", DetailField = "Age" });
        up.OnDelete = DeleteRule.Restrict;
        Customers(main).Details.Add(self);
        Assert.Contains("Cascade delete cycle: 'Customers' would delete its own records through a link Customers -> Customers. Use Restrict or Set null for a link to the same table.", Messages(main));
    }

    [Fact]
    public void FormPageMessages()
    {
        var (main, _) = Setup();
        var customers = Customers(main);
        customers.FormPages.Add(new FormPageDef { Name = "One" });
        customers.FormPages.Add(new FormPageDef { Name = "one" });
        customers.FormPages.Add(new FormPageDef { Name = " " });
        customers.Fields[1].Ui.FormPage = "Lost";
        customers.Designs.Edit.MultiPage = MultiPageMode.Tabs;
        Orders(main).Designs.View.MultiPage = MultiPageMode.Wizard;

        var issues = RelationValidator.Validate(main.Current.Tables);
        var messages = issues.Select(i => i.Message).ToList();

        Assert.Contains("Table 'Customers': the form page 'one' exists twice.", messages);
        Assert.Contains("Table 'Customers': a form page has no name.", messages);
        Assert.Contains("Table 'Customers': field 'Name' is on form page 'Lost', which does not exist.", messages);
        Assert.Contains("Table 'Orders': the View page uses Wizard form pages but the table has no form pages.", messages);
        Assert.Equal(RelationSeverity.Error, issues.Single(i => i.Message.Contains("exists twice", StringComparison.Ordinal)).Severity);
        Assert.Equal(RelationSeverity.Warning, issues.Single(i => i.Message.Contains("has no form pages", StringComparison.Ordinal)).Severity);
        Assert.Equal(RelationSeverity.Warning, issues.Single(i => i.Message.Contains("'Lost'", StringComparison.Ordinal)).Severity);
    }

    [Fact]
    public void LinksOfTablesThatAreNotGeneratedAreIgnored()
    {
        var (main, _) = Setup();
        var link = new DetailLink { DetailTable = "Ghosts" };
        Customers(main).Details.Add(link);
        Customers(main).Generate = false;

        Assert.Empty(RelationValidator.Validate(main.Current.Tables));
        main.Relations.Reload();
        Assert.True(main.Relations.HasMasterNote);
    }

    [Fact]
    public void ValidationErrorsDisableGenerateAndWarningsDoNot()
    {
        var (main, _) = Setup();
        Assert.True(main.Security.CanGenerate);

        main.Relations.AddLinkCommand.Execute(null);
        Assert.True(main.Security.CanGenerate);
        Assert.Equal("No problems found.", main.Relations.IssueSummary);

        // A warning (types differ) leaves Generate on.
        main.Relations.KeyPairs[0].MasterField = "Name";
        Assert.True(main.Security.CanGenerate);
        Assert.Equal("0 error(s) (they disable Generate), 1 warning(s).", main.Relations.IssueSummary);

        // An error (no key links) turns it off and the same text appears on the Generate tab.
        main.Relations.SelectedKeyPair = main.Relations.KeyPairs[0];
        main.Relations.RemoveKeyPairCommand.Execute(null);
        Assert.False(main.Security.CanGenerate);
        Assert.Equal("Master/detail Customers -> Orders: add at least one key link (master field = detail field).", main.Security.ValidationMessage);
        Assert.Single(main.Relations.Issues);
        Assert.True(main.Relations.Issues[0].IsError);

        main.Relations.RemoveLinkCommand.Execute(null);
        Assert.True(main.Security.CanGenerate);
        Assert.Empty(main.Relations.Issues);
    }

    [Fact]
    public void LoginProblemsAreReportedBeforeRelationProblems()
    {
        var (main, _) = Setup();
        Customers(main).Details.Add(new DetailLink());
        main.Security.Enabled = true;

        Assert.Contains("Choose the user table", main.Security.ValidationMessage);
        main.Security.Enabled = false;
        Assert.Contains("choose the detail table", main.Security.ValidationMessage);
    }

    // ---- form pages ----

    [Fact]
    public void FormPagesAddRemoveAndReorderWriteToTheTable()
    {
        var (main, _) = Setup();
        var pages = main.PageDesigner.FormPages;
        var customers = Customers(main);

        pages.AddPageCommand.Execute(null);
        pages.AddPageCommand.Execute(null);
        pages.AddPageCommand.Execute(null);
        Assert.Equal(["Page 1", "Page 2", "Page 3"], customers.FormPages.Select(p => p.Name).ToArray());
        Assert.Equal(["(first page)", "Page 1", "Page 2", "Page 3"], pages.PageChoices.ToArray());

        pages.SelectedPage = pages.Pages[2];
        pages.MovePageUpCommand.Execute(null);
        Assert.Equal(["Page 1", "Page 3", "Page 2"], customers.FormPages.Select(p => p.Name).ToArray());
        pages.MovePageDownCommand.Execute(null);
        pages.MovePageDownCommand.Execute(null);
        Assert.Equal(["Page 1", "Page 2", "Page 3"], customers.FormPages.Select(p => p.Name).ToArray());
        Assert.Equal(customers.FormPages, pages.Pages.Select(p => p.Model));

        pages.Pages[0].Caption = "Contact";
        pages.Pages[0].Icon = "bi-person";
        pages.Pages[0].Description = "Who they are";
        Assert.Equal(("Contact", "person", "Who they are"), (customers.FormPages[0].Caption, customers.FormPages[0].Icon, customers.FormPages[0].Description));

        pages.SelectedPage = pages.Pages[1];
        pages.RemovePageCommand.Execute(null);
        Assert.Equal(["Page 1", "Page 3"], customers.FormPages.Select(p => p.Name).ToArray());

        // A new page does not reuse a name that exists.
        pages.AddPageCommand.Execute(null);
        Assert.Equal(["Page 1", "Page 3", "Page 4"], customers.FormPages.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void RenamingAPageRenamesTheFieldAssignmentsAndTheGridsThatSitOnIt()
    {
        var (main, _) = Setup();
        var pages = main.PageDesigner.FormPages;
        var customers = Customers(main);
        pages.AddPageCommand.Execute(null);
        pages.AddPageCommand.Execute(null);
        customers.Fields[1].Ui.FormPage = "Page 1";
        customers.Fields[2].Ui.FormPage = "page 1";
        customers.Fields[3].Ui.FormPage = "Page 2";
        var link = OrdersLink();
        link.FormPage = "Page 1";
        customers.Details.Add(link);

        pages.Pages[0].Name = "Contact";

        Assert.Equal("Contact", customers.FormPages[0].Name);
        Assert.Equal("Contact", customers.Fields[1].Ui.FormPage);
        Assert.Equal("Contact", customers.Fields[2].Ui.FormPage);
        Assert.Equal("Page 2", customers.Fields[3].Ui.FormPage);
        Assert.Equal("Contact", link.FormPage);
        Assert.Equal("Contact", pages.FieldRows[1].PageChoice);
        Assert.Equal(["(first page)", "Contact", "Page 2"], pages.PageChoices.ToArray());
        Assert.Empty(pages.Warnings);
        Assert.False(pages.HasMessage);

        // A duplicate (ignoring case), an empty name and a long name are refused; the old name stays.
        foreach (var bad in new[] { "page 2", "   ", new string('x', 51) })
        {
            pages.Pages[0].Name = bad;
            Assert.Equal("Contact", customers.FormPages[0].Name);
            Assert.True(pages.HasMessage);
        }

        pages.Pages[0].Name = "contact";
        Assert.Equal("contact", customers.FormPages[0].Name);
        Assert.Equal("contact", customers.Fields[1].Ui.FormPage);
        Assert.False(pages.HasMessage);
    }

    [Fact]
    public void RemovingAPageSendsItsFieldsAndGridsBack()
    {
        var (main, _) = Setup();
        var pages = main.PageDesigner.FormPages;
        var customers = Customers(main);
        pages.AddPageCommand.Execute(null);
        pages.AddPageCommand.Execute(null);
        customers.Fields[1].Ui.FormPage = "Page 2";
        var link = OrdersLink();
        link.FormPage = "Page 2";
        customers.Details.Add(link);

        pages.SelectedPage = pages.Pages[1];
        pages.RemovePageCommand.Execute(null);

        Assert.Equal(string.Empty, customers.Fields[1].Ui.FormPage);
        Assert.Equal(string.Empty, link.FormPage);
        Assert.Empty(pages.Warnings);
    }

    [Fact]
    public void PageModesWriteToTheFourPageTypesAndWarnWithoutPages()
    {
        var (main, _) = Setup();
        var pages = main.PageDesigner.FormPages;
        var customers = Customers(main);

        pages.ViewMode = MultiPageMode.Tabs;
        pages.AddMode = MultiPageMode.Pills;
        pages.CopyMode = MultiPageMode.Accordion;
        pages.EditMode = MultiPageMode.Wizard;

        Assert.Equal(MultiPageMode.Tabs, customers.Designs.View.MultiPage);
        Assert.Equal(MultiPageMode.Pills, customers.Designs.Add.MultiPage);
        Assert.Equal(MultiPageMode.Accordion, customers.Designs.Copy.MultiPage);
        Assert.Equal(MultiPageMode.Wizard, customers.Designs.Edit.MultiPage);
        Assert.Equal(
            [
                "Warning: Table 'Customers': the View page uses Tabs form pages but the table has no form pages.",
                "Warning: Table 'Customers': the Add page uses Pills form pages but the table has no form pages.",
                "Warning: Table 'Customers': the Copy page uses Accordion form pages but the table has no form pages.",
                "Warning: Table 'Customers': the Edit page uses Wizard form pages but the table has no form pages.",
            ],
            pages.Warnings.ToArray());

        pages.AddPageCommand.Execute(null);
        Assert.Empty(pages.Warnings);
        Assert.False(pages.HasWarnings);
        Assert.Equal(5, FormPagesViewModel.Modes.Count);

        pages.ViewMode = MultiPageMode.None;
        Assert.Equal(MultiPageMode.None, customers.Designs.View.MultiPage);
    }

    [Fact]
    public void AFieldOnAMissingPageShowsAWarning()
    {
        var (main, _) = Setup();
        var pages = main.PageDesigner.FormPages;
        Customers(main).Fields[2].Ui.FormPage = "Ghost";
        pages.Reload();

        Assert.Equal(["Warning: Table 'Customers': field 'Email' is on form page 'Ghost', which does not exist."], pages.Warnings.ToArray());
        Assert.Equal("Ghost", pages.FieldRows[2].PageChoice);

        pages.AddPageCommand.Execute(null);
        pages.Pages[0].Name = "Ghost";
        Assert.Empty(pages.Warnings);
    }

    [Fact]
    public void TheAssignmentGridWritesTheFieldsPageAndBulkActionsMoveTickedFields()
    {
        var (main, _) = Setup();
        var pages = main.PageDesigner.FormPages;
        var customers = Customers(main);
        pages.AddPageCommand.Execute(null);
        pages.AddPageCommand.Execute(null);
        pages.AddPageCommand.Execute(null);
        Assert.Equal(customers.Fields.Select(f => f.Name), pages.FieldRows.Select(r => r.Name));

        pages.FieldRows[1].PageChoice = "Page 2";
        Assert.Equal("Page 2", customers.Fields[1].Ui.FormPage);
        pages.FieldRows[1].PageChoice = null!;
        Assert.Equal("Page 2", customers.Fields[1].Ui.FormPage);
        pages.FieldRows[1].PageChoice = FormPagesViewModel.FirstPageLabel;
        Assert.Equal(string.Empty, customers.Fields[1].Ui.FormPage);

        // Assign: nothing ticked is a message.
        pages.AssignSelectedCommand.Execute(null);
        Assert.Equal("Tick the fields to assign first.", pages.Message);

        pages.FieldRows[1].IsSelected = true;
        pages.FieldRows[2].IsSelected = true;
        pages.BulkTarget = "Page 3";
        pages.AssignSelectedCommand.Execute(null);
        Assert.Equal(["Page 3", "Page 3"], new[] { customers.Fields[1].Ui.FormPage, customers.Fields[2].Ui.FormPage });
        Assert.Equal(string.Empty, customers.Fields[0].Ui.FormPage);

        // Move up: previous page; down: next page; the ends stay.
        pages.MoveSelectedUpCommand.Execute(null);
        Assert.Equal(["Page 2", "Page 2"], new[] { customers.Fields[1].Ui.FormPage, customers.Fields[2].Ui.FormPage });
        pages.MoveSelectedUpCommand.Execute(null);
        pages.MoveSelectedUpCommand.Execute(null);
        Assert.Equal("Page 1", customers.Fields[1].Ui.FormPage);
        pages.MoveSelectedDownCommand.Execute(null);
        pages.MoveSelectedDownCommand.Execute(null);
        pages.MoveSelectedDownCommand.Execute(null);
        Assert.Equal("Page 3", customers.Fields[1].Ui.FormPage);

        // An unticked field is untouched; an unassigned ticked field moves from the first page.
        pages.FieldRows[1].IsSelected = false;
        pages.FieldRows[2].IsSelected = false;
        pages.FieldRows[3].IsSelected = true;
        pages.MoveSelectedDownCommand.Execute(null);
        Assert.Equal("Page 2", customers.Fields[3].Ui.FormPage);
        Assert.Equal("Page 3", customers.Fields[2].Ui.FormPage);

        pages.ClearFieldSelectionCommand.Execute(null);
        Assert.DoesNotContain(pages.FieldRows, r => r.IsSelected);
        pages.SelectAllFieldsCommand.Execute(null);
        Assert.All(pages.FieldRows, r => Assert.True(r.IsSelected));

        // Without pages there is nowhere to move to.
        main.Tables.SelectedTable = Orders(main);
        pages.SelectAllFieldsCommand.Execute(null);
        pages.MoveSelectedDownCommand.Execute(null);
        Assert.Equal("Add a form page first.", pages.Message);
    }

    // ---- preview model ----

    private static (MainViewModel Main, TableInfo Customers) PagedCustomers()
    {
        var (main, _) = Setup();
        var customers = Customers(main);
        customers.FormPages.Add(new FormPageDef { Name = "Contact", Caption = "Contact data", Icon = "person", Description = "How to reach them" });
        customers.FormPages.Add(new FormPageDef { Name = "Billing" });
        customers.Fields.Single(f => f.Name == "Name").Ui.FormPage = "Contact";
        customers.Fields.Single(f => f.Name == "Age").Ui.FormPage = "Billing";
        customers.Fields.Single(f => f.Name == "Active").Ui.FormPage = "Gone";
        return (main, customers);
    }

    [Theory]
    [InlineData(MultiPageMode.Tabs)]
    [InlineData(MultiPageMode.Pills)]
    [InlineData(MultiPageMode.Accordion)]
    [InlineData(MultiPageMode.Wizard)]
    public void ThePreviewSplitsTheFieldsIntoThePages(MultiPageMode mode)
    {
        var (main, customers) = PagedCustomers();
        customers.Designs.Edit.MultiPage = mode;

        var schematic = SchematicBuilder.Build(customers, PageKind.Edit, main.Current.Tables);

        var multi = Assert.IsType<MultiPageSchematic>(schematic.MultiPage);
        Assert.Equal(mode, multi.Mode);
        Assert.Equal(["Contact", "Billing"], multi.Pages.Select(p => p.Name).ToArray());
        Assert.Equal("Contact data", multi.Pages[0].Caption);
        Assert.Equal("Billing", multi.Pages[1].Caption);
        Assert.Equal("person", multi.Pages[0].Icon);
        Assert.Equal("How to reach them", multi.Pages[0].Description);
        // Id, Email, Notes and Active (a page that does not exist) go on the first page.
        Assert.Equal(["Id", "Customer name", "Email", "Active", "Notes"], multi.Pages[0].Fields.Select(f => f.Caption).ToArray());
        Assert.Equal(["Age"], multi.Pages[1].Fields.Select(f => f.Caption).ToArray());
    }

    [Fact]
    public void ThePreviewIsASinglePageWhenTheModeIsNoneOrThereAreNoPages()
    {
        var (main, customers) = PagedCustomers();
        Assert.Null(SchematicBuilder.Build(customers, PageKind.Edit, main.Current.Tables).MultiPage);

        customers.Designs.Edit.MultiPage = MultiPageMode.Tabs;
        customers.FormPages.Clear();
        Assert.Null(SchematicBuilder.Build(customers, PageKind.Edit, main.Current.Tables).MultiPage);

        // Only the four form page types split; the list and delete pages never do.
        customers.FormPages.Add(new FormPageDef { Name = "A" });
        customers.Designs.List.MultiPage = MultiPageMode.Tabs;
        customers.Designs.Delete.MultiPage = MultiPageMode.Tabs;
        Assert.Null(SchematicBuilder.Build(customers, PageKind.List, main.Current.Tables).MultiPage);
        Assert.Null(SchematicBuilder.Build(customers, PageKind.Delete, main.Current.Tables).MultiPage);
        Assert.NotNull(SchematicBuilder.Build(customers, PageKind.Edit, main.Current.Tables).MultiPage);
    }

    [Fact]
    public void DetailGridsSitOnTheirFormPageOrBelowTheForm()
    {
        var (main, customers) = PagedCustomers();
        customers.Designs.Edit.MultiPage = MultiPageMode.Tabs;
        var onPage = OrdersLink();
        onPage.FormPage = "Billing";
        onPage.Caption = "Their orders";
        onPage.Columns.Add("Total");
        var below = OrdersLink();
        below.ShowOnView = false;
        var hidden = OrdersLink();
        hidden.ShowOnEdit = false;
        hidden.FormPage = "Contact";
        customers.Details.AddRange([onPage, below, hidden]);

        var edit = SchematicBuilder.Build(customers, PageKind.Edit, main.Current.Tables);

        var grid = Assert.Single(edit.MultiPage!.Pages[1].Grids);
        Assert.Equal("Their orders", grid.Caption);
        Assert.Equal(["Total", "Actions"], grid.Headers.ToArray());
        Assert.True(grid.CanAdd);
        Assert.Empty(edit.MultiPage.Pages[0].Grids);
        var underForm = Assert.Single(edit.DetailGrids);
        Assert.Equal("Orders", underForm.Caption);
        Assert.Equal(["Id", "CustomerId", "Total", "Status", "Actions"], underForm.Headers.ToArray());

        // The view page shows the links marked "show on view": the grid on the Billing page and the hidden one on Contact.
        customers.Designs.View.MultiPage = MultiPageMode.Tabs;
        var view = SchematicBuilder.Build(customers, PageKind.View, main.Current.Tables);
        Assert.Single(view.MultiPage!.Pages[1].Grids);
        Assert.Single(view.MultiPage.Pages[0].Grids);
        Assert.Empty(view.DetailGrids);
        Assert.False(view.MultiPage.Pages[1].Grids[0].CanAdd);
        Assert.DoesNotContain("Actions", view.MultiPage.Pages[1].Grids[0].Headers);

        // Add and Copy have no grids. Without the project's tables the grid still draws.
        Assert.Empty(SchematicBuilder.Build(customers, PageKind.Add, main.Current.Tables).DetailGrids);
        var bare = SchematicBuilder.Build(customers, PageKind.Edit);
        Assert.Contains("(columns)", bare.DetailGrids[0].Headers);
    }

    [Fact]
    public void AGridOnAMissingPageFallsBackToBelowTheFormAndAddNotesTheEditRedirect()
    {
        var (main, customers) = PagedCustomers();
        customers.Designs.Edit.MultiPage = MultiPageMode.Pills;
        var lost = OrdersLink();
        lost.FormPage = "Nowhere";
        lost.ShowOnAdd = true;
        customers.Details.Add(lost);

        Assert.Single(SchematicBuilder.Build(customers, PageKind.Edit, main.Current.Tables).DetailGrids);
        Assert.Equal(
            "After saving, the edit page opens so detail rows can be added.",
            SchematicBuilder.Build(customers, PageKind.Add, main.Current.Tables).Note);
        Assert.Equal(string.Empty, SchematicBuilder.Build(customers, PageKind.Edit, main.Current.Tables).Note);
    }

    [Fact]
    public void ThePageDesignerSchematicFollowsTheFormPagesEditor()
    {
        var (main, _) = Setup();
        var designer = main.PageDesigner;
        designer.SelectedPage = PageKind.Edit;
        Assert.Null(designer.Schematic!.MultiPage);

        designer.FormPages.AddPageCommand.Execute(null);
        designer.FormPages.EditMode = MultiPageMode.Wizard;

        Assert.Equal(MultiPageMode.Wizard, designer.Schematic!.MultiPage!.Mode);
        var changed = new List<string?>();
        designer.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        designer.FormPages.Pages[0].Caption = "Step one";
        Assert.Contains(nameof(PageDesignerViewModel.Schematic), changed);
        Assert.Equal("Step one", designer.Schematic!.MultiPage!.Pages[0].Caption);
    }

    // ---- dirty tracking ----

    [Fact]
    public void TheFingerprintCoversLinksFormPagesAndFieldPages()
    {
        var (main, _) = Setup();
        var baseline = CustomizationFingerprint.Compute(main.Current);
        var customers = Customers(main);

        customers.Details.Add(OrdersLink());
        var withLink = CustomizationFingerprint.Compute(main.Current);
        Assert.NotEqual(baseline, withLink);

        customers.Details[0].OnDelete = DeleteRule.Cascade;
        var cascade = CustomizationFingerprint.Compute(main.Current);
        Assert.NotEqual(withLink, cascade);

        customers.Details[0].Links[0].DetailField = "Status";
        var pair = CustomizationFingerprint.Compute(main.Current);
        Assert.NotEqual(cascade, pair);

        customers.Details[0].Columns.Add("Total");
        var columns = CustomizationFingerprint.Compute(main.Current);
        Assert.NotEqual(pair, columns);

        customers.FormPages.Add(new FormPageDef { Name = "One" });
        var page = CustomizationFingerprint.Compute(main.Current);
        Assert.NotEqual(columns, page);

        customers.FormPages[0].Caption = "Caption";
        var caption = CustomizationFingerprint.Compute(main.Current);
        Assert.NotEqual(page, caption);

        customers.Fields[1].Ui.FormPage = "One";
        var assigned = CustomizationFingerprint.Compute(main.Current);
        Assert.NotEqual(caption, assigned);

        customers.Designs.Edit.MultiPage = MultiPageMode.Tabs;
        Assert.NotEqual(assigned, CustomizationFingerprint.Compute(main.Current));

        // Foreign keys are database metadata, not edits.
        var before = CustomizationFingerprint.Compute(main.Current);
        Orders(main).ForeignKeys.Add(new ForeignKeyInfo { ReferencedTable = "Customers" });
        Assert.Equal(before, CustomizationFingerprint.Compute(main.Current));
    }

    [Fact]
    public async Task EditingRelationsAndFormPagesMarksTheProjectDirty()
    {
        var (main, dialogs) = Setup();
        using var folder = new TempFolder();
        await CleanAsync(main, dialogs, folder);
        var relations = main.Relations;
        var pages = main.PageDesigner.FormPages;

        relations.AddLinkCommand.Execute(null);
        Assert.True(main.IsDirty);
        await CleanAsync(main, dialogs, folder);

        relations.Caption = "Orders of the customer";
        Assert.True(main.IsDirty);
        await CleanAsync(main, dialogs, folder);

        relations.OnDelete = DeleteRule.Cascade;
        Assert.True(main.IsDirty);
        await CleanAsync(main, dialogs, folder);

        relations.AddColumnCommand.Execute(null);
        Assert.True(main.IsDirty);
        await CleanAsync(main, dialogs, folder);

        relations.KeyPairs[0].DetailField = "Status";
        Assert.True(main.IsDirty);
        await CleanAsync(main, dialogs, folder);

        pages.AddPageCommand.Execute(null);
        Assert.True(main.IsDirty);
        await CleanAsync(main, dialogs, folder);

        pages.Pages[0].Name = "Contact";
        Assert.True(main.IsDirty);
        await CleanAsync(main, dialogs, folder);

        pages.FieldRows[1].PageChoice = "Contact";
        Assert.True(main.IsDirty);
        await CleanAsync(main, dialogs, folder);

        pages.AddMode = MultiPageMode.Accordion;
        Assert.True(main.IsDirty);
        await CleanAsync(main, dialogs, folder);

        relations.RemoveLinkCommand.Execute(null);
        Assert.True(main.IsDirty);
    }

    [Fact]
    public async Task OpeningAnotherProjectResetsTheRelationsTab()
    {
        var (main, dialogs) = Setup();
        main.Relations.AddLinkCommand.Execute(null);
        dialogs.NextUnsavedChoice = Desktop.Services.UnsavedChangesChoice.Discard;

        await main.NewProjectCommand.ExecuteAsync(null);

        Assert.Empty(main.Relations.Links);
        Assert.Empty(main.Relations.Overview);
        Assert.Empty(main.Relations.Issues);
        Assert.False(main.Relations.HasLink);
        Assert.True(main.Security.CanGenerate);
    }
}
