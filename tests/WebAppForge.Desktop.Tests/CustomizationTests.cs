using WebAppForge.Core.Model;
using WebAppForge.Desktop.Tests.TestSupport;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Tests;

/// <summary>View model tests for the field designer, the page designer and the Project tab. In-memory projects only.</summary>
public class CustomizationTests
{
    /// <summary>Customers (Id key, Name, Email, Age, Active, Notes) and Orders (Id, CustomerId, Total, Status).</summary>
    internal static (MainViewModel Main, FakeDialogService Dialogs) Project()
    {
        var dialogs = new FakeDialogService();
        var main = new MainViewModel(dialogs);

        var customers = new TableInfo { Name = "Customers", Caption = "Customers" };
        customers.Fields.Add(new FieldInfo { Name = "Id", Type = FieldType.Integer, IsPrimaryKey = true, IsAutoIncrement = true, ShowInAdd = false });
        customers.Fields.Add(new FieldInfo { Name = "Name", Type = FieldType.Text, Caption = "Customer name" });
        customers.Fields.Add(new FieldInfo { Name = "Email", Type = FieldType.Text });
        customers.Fields.Add(new FieldInfo { Name = "Age", Type = FieldType.Integer });
        customers.Fields.Add(new FieldInfo { Name = "Active", Type = FieldType.Boolean });
        customers.Fields.Add(new FieldInfo { Name = "Notes", Type = FieldType.Text });
        main.Current.Tables.Add(customers);

        var orders = new TableInfo { Name = "Orders" };
        orders.Fields.Add(new FieldInfo { Name = "Id", Type = FieldType.Integer, IsPrimaryKey = true });
        orders.Fields.Add(new FieldInfo { Name = "CustomerId", Type = FieldType.Integer });
        orders.Fields.Add(new FieldInfo { Name = "Total", Type = FieldType.Decimal });
        orders.Fields.Add(new FieldInfo { Name = "Status", Type = FieldType.Text });
        main.Current.Tables.Add(orders);

        main.Tables.Reload();
        return (main, dialogs);
    }

    /// <summary>Saves the project so it counts as unchanged; later edits must make it dirty.</summary>
    private static async Task CleanAsync(MainViewModel main, FakeDialogService dialogs, TempFolder folder)
    {
        dialogs.NextSaveProject = folder.File("clean.xml");
        await main.SaveAsCommand.ExecuteAsync(null);
        Assert.False(main.IsDirty);
    }

    private static FieldInfo Select(MainViewModel main, string table, string field)
    {
        main.Tables.SelectedTable = main.Current.Tables.Single(t => t.Name == table);
        var selected = main.Tables.SelectedTable!.Fields.Single(f => f.Name == field);
        main.Tables.SelectedField = selected;
        return selected;
    }

    // ---- field designer ----

    [Fact]
    public void FieldPropertiesWriteThroughToTheModel()
    {
        var (main, _) = Project();
        var email = Select(main, "Customers", "Email");
        var designer = main.FieldDesigner;

        designer.Caption = "E-mail address";
        designer.Required = true;
        designer.ShowInList = false;
        designer.Searchable = false;
        designer.Sortable = false;
        designer.Control = EditControl.Email;
        designer.HelpText = "Work address";
        designer.Placeholder = "name@example.com";
        designer.Tooltip = "tip";
        designer.DefaultValue = "x@y.z";
        designer.ReadOnlyOnEdit = true;
        designer.AutoFocus = true;
        designer.Group = "Contact";
        designer.RowsText = "5";
        designer.Format = ViewFormat.EmailLink;
        designer.Width = "120px";
        designer.Align = TextAlign.Center;
        designer.CssClass = "text-muted";
        designer.InlineStyle = "color:red";

        Assert.Equal("E-mail address", email.Caption);
        Assert.True(email.IsRequired);
        Assert.False(email.ShowInList);
        Assert.False(email.Searchable);
        Assert.False(email.Ui.Sortable);
        Assert.Equal(EditControl.Email, email.Ui.Control);
        Assert.Equal("Work address", email.Ui.HelpText);
        Assert.Equal("name@example.com", email.Ui.Placeholder);
        Assert.Equal("tip", email.Ui.Tooltip);
        Assert.Equal("x@y.z", email.Ui.DefaultValue);
        Assert.True(email.Ui.ReadOnlyOnEdit);
        Assert.True(email.Ui.AutoFocus);
        Assert.Equal("Contact", email.Ui.Group);
        Assert.Equal(5, email.Ui.Rows);
        Assert.Equal(ViewFormat.EmailLink, email.Ui.Format);
        Assert.Equal("120px", email.Ui.Width);
        Assert.Equal(TextAlign.Center, email.Ui.Align);
        Assert.Equal("text-muted", email.Ui.CssClass);
        Assert.Equal("color:red", email.Ui.Style);
    }

    [Fact]
    public async Task EditingAnyFieldValueMarksTheProjectDirty()
    {
        using var folder = new TempFolder();
        var (main, dialogs) = Project();
        Select(main, "Customers", "Email");
        await CleanAsync(main, dialogs, folder);

        main.FieldDesigner.HelpText = "changed";
        Assert.True(main.IsDirty);

        main.FieldDesigner.HelpText = string.Empty;
        Assert.False(main.IsDirty);

        main.FieldDesigner.Caption = "Mail";
        Assert.True(main.IsDirty);
    }

    [Fact]
    public async Task ChangesAfterSavingMakeTheProjectDirtyAgain()
    {
        using var folder = new TempFolder();
        var main = await Harness.SavedProjectAsync(folder);
        Assert.False(main.IsDirty);

        main.ProjectSettings.Description = "A description";
        Assert.True(main.IsDirty);

        await main.SaveProjectCommand.ExecuteAsync(null);
        Assert.False(main.IsDirty);
    }

    [Fact]
    public void NumbersAreCheckedAndTheLastValidValueIsKept()
    {
        var (main, _) = Project();
        var name = Select(main, "Customers", "Name");
        var designer = main.FieldDesigner;

        designer.MaxLengthText = "50";
        Assert.Equal(50, name.Ui.MaxLength);
        Assert.False(designer.HasMessage);

        designer.MaxLengthText = "many";
        Assert.Equal(50, name.Ui.MaxLength);
        Assert.True(designer.HasMessage);
        Assert.Equal("many", designer.MaxLengthText);

        designer.MinLengthText = "60";
        Assert.Equal(0, name.Ui.MinLength);
        Assert.Contains("Minimum length", designer.Message);

        designer.MinLengthText = "5";
        Assert.Equal(5, name.Ui.MinLength);
        Assert.False(designer.HasMessage);

        designer.RowsText = "0";
        Assert.Equal(3, name.Ui.Rows);
        Assert.True(designer.HasMessage);
    }

    [Fact]
    public void MinMaxAndStepFollowTheFieldType()
    {
        var (main, _) = Project();
        var age = Select(main, "Customers", "Age");
        var designer = main.FieldDesigner;

        designer.MinText = "0";
        designer.MaxText = "120";
        designer.StepText = "1";
        Assert.Equal("0", age.Ui.Min);
        Assert.Equal("120", age.Ui.Max);
        Assert.Equal("1", age.Ui.Step);

        designer.MaxText = "old";
        Assert.Equal("120", age.Ui.Max);
        Assert.Contains("Maximum must be a whole number", designer.Message);

        designer.MaxText = "-5";
        Assert.Equal("120", age.Ui.Max);
        Assert.Contains("smaller than the minimum", designer.Message);

        designer.StepText = "0";
        Assert.Equal("1", age.Ui.Step);
        Assert.Contains("greater than 0", designer.Message);

        var name = Select(main, "Customers", "Name");
        designer.MinText = "anything";
        Assert.Equal("anything", name.Ui.Min);
    }

    [Fact]
    public void InvalidPatternIsRefusedAndTheLastValidPatternKept()
    {
        var (main, _) = Project();
        var name = Select(main, "Customers", "Name");
        var designer = main.FieldDesigner;

        designer.Pattern = "[A-Z]+";
        Assert.Equal("[A-Z]+", name.Ui.Pattern);

        designer.Pattern = "([A-Z";
        Assert.Equal("[A-Z]+", name.Ui.Pattern);
        Assert.True(designer.HasMessage);
        Assert.Equal(RegexTestOutcome.InvalidPattern, designer.PatternTest.Outcome);
    }

    [Fact]
    public void PatternTesterReportsMatchNoMatchInvalidAndTimeout()
    {
        Assert.Equal(RegexTestOutcome.NoPattern, RegexTester.Test("", "abc").Outcome);
        Assert.Equal(RegexTestOutcome.Match, RegexTester.Test("[a-z]{3}", "abc").Outcome);
        Assert.Equal(RegexTestOutcome.NoMatch, RegexTester.Test("[a-z]{3}", "abcd").Outcome);
        Assert.Equal(RegexTestOutcome.InvalidPattern, RegexTester.Test("(", "abc").Outcome);

        var slow = RegexTester.Test("(a+)+$", new string('a', 60) + "!", TimeSpan.FromMilliseconds(50));
        Assert.Equal(RegexTestOutcome.TimedOut, slow.Outcome);
    }

    [Fact]
    public void PatternTestBoxChecksTheSampleAgainstThePattern()
    {
        var (main, _) = Project();
        Select(main, "Customers", "Name");
        var designer = main.FieldDesigner;
        designer.Pattern = @"\d{3}-\d{4}";

        designer.PatternTestText = "555-1234";
        Assert.Equal(RegexTestOutcome.Match, designer.PatternTest.Outcome);

        designer.PatternTestText = "5551234";
        Assert.Equal(RegexTestOutcome.NoMatch, designer.PatternTest.Outcome);
    }

    [Fact]
    public void FormatPreviewShowsASampleForTheFieldType()
    {
        var (main, _) = Project();
        Select(main, "Orders", "Total");
        var designer = main.FieldDesigner;

        Assert.Equal("1,234.57", designer.FormatPreview);

        designer.FormatString = "N1";
        Assert.Equal("1,234.6", designer.FormatPreview);

        designer.Format = ViewFormat.Currency;
        designer.FormatString = string.Empty;
        Assert.Equal("$1,234.50", designer.FormatPreview);

        designer.Format = ViewFormat.Percent;
        Assert.Equal("25.6%", designer.FormatPreview);

        Select(main, "Customers", "Age");
        Assert.Equal("1,234,567", designer.FormatPreview);

        var active = Select(main, "Customers", "Active");
        Assert.Equal("Yes / No", designer.FormatPreview);
        designer.TrueText = "Active";
        designer.FalseText = "Inactive";
        Assert.Equal("Active / Inactive", designer.FormatPreview);
        Assert.Equal("Active", active.Ui.TrueText);

        Assert.Equal("3/14/2026", FormatPreviewer.Preview(FieldType.Date, new FieldUi()));
        Assert.Equal("2026-03-14", FormatPreviewer.Preview(FieldType.Date, new FieldUi { FormatString = "yyyy-MM-dd" }));
        Assert.Equal(FormatPreviewer.InvalidFormat, FormatPreviewer.Preview(FieldType.Date, new FieldUi { FormatString = "%" }));
    }

    [Fact]
    public void BadgePreviewUsesTheFirstRule()
    {
        var (main, _) = Project();
        var status = Select(main, "Orders", "Status");
        var designer = main.FieldDesigner;
        designer.Format = ViewFormat.Badge;
        Assert.Equal("No badge rules yet", designer.FormatPreview);

        designer.AddBadgeRuleCommand.Execute(null);
        designer.BadgeRules[0].Value = "Open";
        designer.BadgeRules[0].Style = "success";
        Assert.Single(status.Ui.BadgeRules);
        Assert.Equal("[success] Open", FormatPreviewer.Preview(FieldType.Text, status.Ui));

        designer.SelectedRule = designer.BadgeRules[0];
        designer.RemoveBadgeRuleCommand.Execute(null);
        Assert.Empty(status.Ui.BadgeRules);
    }

    [Fact]
    public void OptionsAreOnlyShownForSelectAndRadioAndWriteThrough()
    {
        var (main, _) = Project();
        var status = Select(main, "Orders", "Status");
        var designer = main.FieldDesigner;

        Assert.False(designer.ShowOptions);
        designer.Control = EditControl.Select;
        Assert.True(designer.ShowOptions);
        designer.Control = EditControl.Radio;
        Assert.True(designer.ShowOptions);
        designer.Control = EditControl.TextArea;
        Assert.False(designer.ShowOptions);
        Assert.True(designer.IsTextArea);

        designer.AddOptionCommand.Execute(null);
        designer.AddOptionCommand.Execute(null);
        designer.Options[0].Value = "O";
        designer.Options[0].Text = "Open";
        designer.Options[1].Value = "O";
        designer.RefreshOptionsMessage();
        Assert.Equal(2, status.Ui.Options.Count);
        Assert.Equal("Open", status.Ui.Options[0].Text);
        Assert.Contains("unique", designer.OptionsMessage);

        designer.SelectedOption = designer.Options[1];
        designer.RemoveOptionCommand.Execute(null);
        Assert.Single(status.Ui.Options);
    }

    [Fact]
    public void LookupPickersAreFilledFromTheProject()
    {
        var (main, _) = Project();
        var customerId = Select(main, "Orders", "CustomerId");
        var designer = main.FieldDesigner;

        Assert.Equal(["(none)", "Customers", "Orders"], designer.LookupTables.ToArray());
        Assert.False(designer.HasLookupTable);

        designer.LookupTable = "Customers";
        Assert.NotNull(customerId.Ui.Lookup);
        Assert.Equal("Customers", customerId.Ui.Lookup!.Table);
        Assert.Equal("Id", customerId.Ui.Lookup.KeyField);
        Assert.Equal("Name", customerId.Ui.Lookup.DisplayField);
        Assert.Equal(["Id", "Name", "Email", "Age", "Active", "Notes"], designer.LookupFields.ToArray());

        designer.LookupDisplayField = "Email";
        designer.LookupFilter = "Active = 1";
        designer.LookupOrderField = "Name";
        designer.LookupShowDisplayInView = false;
        Assert.Equal("Email", customerId.Ui.Lookup.DisplayField);
        Assert.Equal("Active = 1", customerId.Ui.Lookup.Filter);
        Assert.Equal("Name", customerId.Ui.Lookup.OrderField);
        Assert.False(customerId.Ui.Lookup.ShowDisplayInView);

        designer.LookupTable = "Orders";
        Assert.Equal(["Id", "CustomerId", "Total", "Status"], designer.LookupFields.ToArray());
        Assert.Equal("Id", customerId.Ui.Lookup.KeyField);
        Assert.Equal("Status", customerId.Ui.Lookup.DisplayField);
        Assert.Equal(string.Empty, customerId.Ui.Lookup.OrderField);

        designer.LookupTable = "(none)";
        Assert.Null(customerId.Ui.Lookup);
    }

    [Fact]
    public void WidthMustBeACssLength()
    {
        var (main, _) = Project();
        var name = Select(main, "Customers", "Name");
        main.FieldDesigner.Width = "20%";
        main.FieldDesigner.Width = "wide";
        Assert.Equal("20%", name.Ui.Width);
        Assert.Contains("CSS length", main.FieldDesigner.Message);
    }

    [Fact]
    public void SearchBoxFiltersTheFieldListByNameOrCaption()
    {
        var (main, _) = Project();
        Select(main, "Customers", "Name");
        var designer = main.FieldDesigner;
        Assert.Equal(6, designer.VisibleFields.Count);

        designer.SearchText = "mail";
        Assert.Equal(["Email"], designer.VisibleFields.Select(f => f.Name).ToArray());

        designer.SearchText = "customer name";
        Assert.Equal(["Name"], designer.VisibleFields.Select(f => f.Name).ToArray());

        designer.SearchText = string.Empty;
        Assert.Equal(6, designer.VisibleFields.Count);
    }

    [Fact]
    public void MoveUpAndDownReorderTheTableFields()
    {
        var (main, _) = Project();
        var email = Select(main, "Customers", "Email");
        var table = main.Tables.SelectedTable!;
        var designer = main.FieldDesigner;

        designer.MoveUp();
        Assert.Equal(["Id", "Email", "Name", "Age", "Active", "Notes"], table.Fields.Select(f => f.Name).ToArray());
        Assert.Equal(table.Fields.Select(f => f.Name), main.Tables.Fields.Select(f => f.Name));
        Assert.Equal(table.Fields.Select(f => f.Name), designer.VisibleFields.Select(f => f.Name));
        Assert.Same(email, main.Tables.SelectedField);

        designer.MoveDown();
        designer.MoveDown();
        Assert.Equal(["Id", "Name", "Age", "Email", "Active", "Notes"], table.Fields.Select(f => f.Name).ToArray());
        Assert.True(designer.CanMoveUp);
        Assert.True(designer.CanMoveDown);

        Select(main, "Customers", "Id");
        Assert.False(designer.CanMoveUp);
        designer.MoveUp();
        Assert.Equal("Id", table.Fields[0].Name);

        Select(main, "Customers", "Notes");
        Assert.False(designer.CanMoveDown);
        designer.MoveDown();
        Assert.Equal("Notes", table.Fields[^1].Name);
    }

    [Fact]
    public void MovingKeyFieldsKeepsTheCompositeKeyOrder()
    {
        var (main, _) = Project();
        var table = new TableInfo { Name = "Lines" };
        var orderId = new FieldInfo { Name = "OrderId", Type = FieldType.Integer, IsPrimaryKey = true };
        var note = new FieldInfo { Name = "Note", Type = FieldType.Text };
        var lineNo = new FieldInfo { Name = "LineNo", Type = FieldType.Integer, IsPrimaryKey = true };
        table.Fields.AddRange([orderId, note, lineNo]);
        main.Current.Tables.Add(table);
        main.Tables.Reload();

        Select(main, "Lines", "LineNo");
        Assert.Equal(["OrderId", "LineNo"], table.PrimaryKeys.Select(k => k.Name).ToArray());

        main.FieldDesigner.MoveUp();
        main.FieldDesigner.MoveUp();

        Assert.Equal(["LineNo", "OrderId", "Note"], table.Fields.Select(f => f.Name).ToArray());
        Assert.Equal(["OrderId", "LineNo"], table.PrimaryKeys.Select(k => k.Name).ToArray());
        Assert.Equal(1, orderId.KeyPosition);
        Assert.Equal(2, lineNo.KeyPosition);
    }

    [Fact]
    public async Task ResetFieldAsksFirstAndRestoresDefaults()
    {
        var (main, dialogs) = Project();
        var email = Select(main, "Customers", "Email");
        var designer = main.FieldDesigner;
        designer.HelpText = "help";
        designer.Control = EditControl.Select;
        designer.AddOptionCommand.Execute(null);
        designer.LookupTable = "Orders";

        dialogs.NextConfirm = false;
        await designer.ResetCommand.ExecuteAsync(null);
        Assert.Equal("help", email.Ui.HelpText);
        Assert.Equal(1, dialogs.ConfirmCount);

        dialogs.NextConfirm = true;
        await designer.ResetCommand.ExecuteAsync(null);
        Assert.Equal(string.Empty, email.Ui.HelpText);
        Assert.Equal(EditControl.Auto, email.Ui.Control);
        Assert.Empty(email.Ui.Options);
        Assert.Null(email.Ui.Lookup);
        Assert.Empty(designer.Options);
        Assert.Equal(EditControl.Auto, designer.Control);
    }

    [Fact]
    public async Task CopySettingsGoesToTheChosenFieldsOfTheSameType()
    {
        var (main, dialogs) = Project();
        var name = Select(main, "Customers", "Name");
        var designer = main.FieldDesigner;
        designer.HelpText = "Shared help";
        designer.Control = EditControl.Select;
        designer.AddOptionCommand.Execute(null);
        designer.Options[0].Value = "a";
        designer.Width = "50%";

        dialogs.NextChoices = ["Customers.Email", "Orders.Status"];
        await designer.CopySettingsCommand.ExecuteAsync(null);

        Assert.Equal(["Customers.Email", "Customers.Notes", "Orders.Status"], dialogs.LastChoicesOffered.ToArray());
        var email = main.Current.Tables[0].Fields.Single(f => f.Name == "Email");
        var notes = main.Current.Tables[0].Fields.Single(f => f.Name == "Notes");
        var status = main.Current.Tables[1].Fields.Single(f => f.Name == "Status");
        Assert.Equal("Shared help", email.Ui.HelpText);
        Assert.Equal(EditControl.Select, status.Ui.Control);
        Assert.Equal("50%", status.Ui.Width);
        Assert.Single(email.Ui.Options);
        Assert.NotSame(name.Ui.Options[0], email.Ui.Options[0]);
        Assert.Equal(string.Empty, notes.Ui.HelpText);
        Assert.Equal("Customer name", name.Caption);

        dialogs.NextChoices = null;
        await designer.CopySettingsCommand.ExecuteAsync(null);
        Assert.Equal(string.Empty, notes.Ui.HelpText);
    }

    // ---- page designer ----

    [Fact]
    public void PageTypeDecidesWhichSectionsAreEnabled()
    {
        var (main, _) = Project();
        main.Tables.SelectedTable = main.Current.Tables[0];
        var designer = main.PageDesigner;

        Assert.Equal(PageKind.List, designer.SelectedPage);
        Assert.True(designer.IsListPage);
        Assert.False(designer.IsFormPage);
        Assert.False(designer.IsContainerEnabled);

        foreach (var kind in new[] { PageKind.Add, PageKind.Copy, PageKind.Edit, PageKind.Search })
        {
            designer.SelectedPage = kind;
            Assert.True(designer.IsFormPage, kind.ToString());
            Assert.False(designer.IsListPage, kind.ToString());
            Assert.True(designer.IsContainerEnabled, kind.ToString());
        }

        foreach (var kind in new[] { PageKind.View, PageKind.Delete })
        {
            designer.SelectedPage = kind;
            Assert.False(designer.IsFormPage, kind.ToString());
            Assert.False(designer.IsListPage, kind.ToString());
            Assert.True(designer.IsContainerEnabled, kind.ToString());
        }
    }

    [Fact]
    public void PageDesignValuesGoToTheDesignOfTheSelectedPageType()
    {
        var (main, _) = Project();
        var table = main.Current.Tables[0];
        main.Tables.SelectedTable = table;
        var designer = main.PageDesigner;

        designer.Title = "All customers";
        designer.ListLayout = ListLayout.Cards;
        designer.ShowRowNumbers = true;
        designer.Striped = false;
        designer.AddButtonText = "New customer";
        designer.EmptyText = "Nobody here";
        designer.CustomCss = ".x { color: red }";
        Assert.Equal("All customers", table.Designs.List.Title);
        Assert.Equal(ListLayout.Cards, table.Designs.List.ListLayout);
        Assert.True(table.Designs.List.ShowRowNumbers);
        Assert.False(table.Designs.List.Striped);
        Assert.Equal("New customer", table.Designs.List.AddButtonText);
        Assert.Equal("Nobody here", table.Designs.List.EmptyText);
        Assert.Equal(string.Empty, table.Designs.Add.Title);

        designer.SelectedPage = PageKind.Edit;
        Assert.Equal(string.Empty, designer.Title);
        designer.Title = "Edit customer";
        designer.FormLayout = FormLayout.TwoColumns;
        designer.Container = PageContainer.Modal;
        designer.SubmitText = "Save changes";
        designer.CancelText = "Back";
        designer.ShowCancel = false;
        designer.SubmitCssClass = "btn-success";
        designer.ButtonPlacement = ButtonPlacement.Both;
        designer.SuccessMessage = "Saved";
        designer.ConfirmBeforeSave = true;
        designer.HeaderHtml = "<p>head</p>";
        designer.FooterHtml = "<p>foot</p>";
        designer.CustomScript = "alert(1)";
        designer.PageCssClass = "wide";
        designer.Subtitle = "sub";

        var edit = table.Designs.Edit;
        Assert.Equal("Edit customer", edit.Title);
        Assert.Equal(FormLayout.TwoColumns, edit.FormLayout);
        Assert.Equal(PageContainer.Modal, edit.Container);
        Assert.Equal("Save changes", edit.SubmitText);
        Assert.Equal("Back", edit.CancelText);
        Assert.False(edit.ShowCancel);
        Assert.Equal("btn-success", edit.SubmitCssClass);
        Assert.Equal(ButtonPlacement.Both, edit.Buttons);
        Assert.Equal("Saved", edit.SuccessMessage);
        Assert.True(edit.ConfirmBeforeSave);
        Assert.Equal("<p>head</p>", edit.HeaderHtml);
        Assert.Equal("<p>foot</p>", edit.FooterHtml);
        Assert.Equal("alert(1)", edit.CustomScript);
        Assert.Equal("wide", edit.CssClass);
        Assert.Equal("sub", edit.Subtitle);
        Assert.Equal("All customers", table.Designs.List.Title);
    }

    [Fact]
    public void TableUiValuesWriteThroughAndAreChecked()
    {
        var (main, _) = Project();
        var table = main.Current.Tables[0];
        main.Tables.SelectedTable = table;
        var designer = main.PageDesigner;

        designer.Description = "People we sell to";
        designer.Icon = "bi-people";
        designer.MenuGroup = "Sales";
        designer.ShowInMenu = false;
        designer.MenuOrderText = "7";
        designer.TableCssClass = "table-sm";
        Assert.Equal("People we sell to", table.Ui.Description);
        Assert.Equal("people", table.Ui.Icon);
        Assert.Equal("Sales", table.Ui.MenuGroup);
        Assert.False(table.Ui.ShowInMenu);
        Assert.Equal(7, table.Ui.MenuOrder);
        Assert.Equal("table-sm", table.Ui.CssClass);

        designer.MenuOrderText = "seven";
        Assert.Equal(7, table.Ui.MenuOrder);
        Assert.True(designer.HasMessage);

        designer.Icon = "Not An Icon!";
        Assert.Equal("people", table.Ui.Icon);
        Assert.True(designer.HasMessage);

        designer.Icon = "custom-icon";
        Assert.Equal("custom-icon", table.Ui.Icon);
        Assert.False(designer.HasMessage);
    }

    [Fact]
    public void RowStyleRulesAreEditedInTheTableUi()
    {
        var (main, _) = Project();
        var table = main.Current.Tables[0];
        main.Tables.SelectedTable = table;
        var designer = main.PageDesigner;

        designer.AddRowStyleCommand.Execute(null);
        designer.RowStyles[0].Field = "Active";
        designer.RowStyles[0].Operator = ConditionOperator.Equals;
        designer.RowStyles[0].Value = "0";
        designer.RowStyles[0].Style = "danger";

        Assert.Single(table.Ui.RowStyles);
        Assert.Same(designer.RowStyles[0].Model, table.Ui.RowStyles[0]);
        Assert.Equal("danger", table.Ui.RowStyles[0].Style);

        designer.SelectedRowStyle = designer.RowStyles[0];
        designer.RemoveRowStyleCommand.Execute(null);
        Assert.Empty(table.Ui.RowStyles);
    }

    [Fact]
    public void PageSizeOptionsAreAParsedCommaList()
    {
        var (main, _) = Project();
        var table = main.Current.Tables[0];
        main.Tables.SelectedTable = table;
        var designer = main.PageDesigner;

        designer.PageSizeOptionsText = "10, 25 ,50";
        Assert.Equal([10, 25, 50], table.Designs.List.PageSizeOptions);
        Assert.Equal("10, 25, 50", designer.PageSizeOptionsText);

        designer.PageSizeOptionsText = "10, x";
        Assert.Equal([10, 25, 50], table.Designs.List.PageSizeOptions);
        Assert.True(designer.HasMessage);

        designer.PageSizeOptionsText = "10, 10";
        Assert.Equal([10, 25, 50], table.Designs.List.PageSizeOptions);

        designer.PageSizeOptionsText = string.Empty;
        Assert.Empty(table.Designs.List.PageSizeOptions);
        Assert.False(designer.HasMessage);
    }

    [Fact]
    public void ColumnEditorAddsRemovesAndReordersListColumns()
    {
        var (main, _) = Project();
        var table = main.Current.Tables[0];
        main.Tables.SelectedTable = table;
        var designer = main.PageDesigner;
        var list = table.Designs.List;

        designer.AddColumnCommand.Execute(null);
        designer.AddColumnCommand.Execute(null);
        designer.AddColumnCommand.Execute(null);
        Assert.Equal(["Id", "Name", "Email"], list.Columns.Select(c => c.Field).ToArray());

        designer.SelectedColumn = designer.Columns[2];
        designer.MoveColumnUpCommand.Execute(null);
        Assert.Equal(["Id", "Email", "Name"], list.Columns.Select(c => c.Field).ToArray());
        designer.MoveColumnDownCommand.Execute(null);
        Assert.Equal(["Id", "Name", "Email"], list.Columns.Select(c => c.Field).ToArray());

        designer.Columns[1].Visible = false;
        designer.Columns[1].Width = "200px";
        Assert.False(list.Columns[1].Visible);
        Assert.Equal("200px", list.Columns[1].Width);

        designer.SelectedColumn = designer.Columns[0];
        designer.RemoveColumnCommand.Execute(null);
        Assert.Equal(["Name", "Email"], list.Columns.Select(c => c.Field).ToArray());

        designer.UseListFieldsCommand.Execute(null);
        Assert.Equal(6, list.Columns.Count);

        designer.ClearColumnsCommand.Execute(null);
        Assert.Empty(list.Columns);

        for (var i = 0; i < 6; i++)
        {
            designer.AddColumnCommand.Execute(null);
        }

        designer.AddColumnCommand.Execute(null);
        Assert.Equal(6, list.Columns.Count);
        Assert.Equal("Every field is already a column.", designer.Message);
    }

    [Fact]
    public async Task ResetPageDesignRestoresDefaultsAfterConfirmation()
    {
        var (main, dialogs) = Project();
        var table = main.Current.Tables[0];
        main.Tables.SelectedTable = table;
        var designer = main.PageDesigner;
        designer.SelectedPage = PageKind.Add;
        designer.Title = "Add one";
        designer.Container = PageContainer.Offcanvas;

        dialogs.NextConfirm = false;
        await designer.ResetPageCommand.ExecuteAsync(null);
        Assert.Equal("Add one", table.Designs.Add.Title);

        dialogs.NextConfirm = true;
        await designer.ResetPageCommand.ExecuteAsync(null);
        Assert.Equal(string.Empty, table.Designs.Add.Title);
        Assert.Equal(PageContainer.Page, table.Designs.Add.Container);
        Assert.Equal(string.Empty, designer.Title);
    }

    [Fact]
    public async Task CopyDesignFromAnotherPageTypeOrTable()
    {
        var (main, dialogs) = Project();
        var customers = main.Current.Tables[0];
        var orders = main.Current.Tables[1];
        main.Tables.SelectedTable = customers;
        var designer = main.PageDesigner;

        designer.SelectedPage = PageKind.Add;
        designer.Title = "Add customer";
        designer.FormLayout = FormLayout.ThreeColumns;
        designer.SubmitText = "Create";

        designer.SelectedPage = PageKind.Edit;
        dialogs.NextChoice = "Customers / Add";
        await designer.CopyDesignFromCommand.ExecuteAsync(null);
        Assert.DoesNotContain("Customers / Edit", dialogs.LastChoicesOffered);
        Assert.Contains("Orders / List", dialogs.LastChoicesOffered);
        Assert.Equal(14 - 1, dialogs.LastChoicesOffered.Count);
        Assert.Equal("Add customer", customers.Designs.Edit.Title);
        Assert.Equal(FormLayout.ThreeColumns, customers.Designs.Edit.FormLayout);
        Assert.Equal("Create", designer.SubmitText);

        main.Tables.SelectedTable = orders;
        designer.SelectedPage = PageKind.Search;
        dialogs.NextChoice = "Customers / Add";
        await designer.CopyDesignFromCommand.ExecuteAsync(null);
        Assert.Equal("Add customer", orders.Designs.Search.Title);

        dialogs.NextChoice = null;
        designer.Title = "Keep me";
        await designer.CopyDesignFromCommand.ExecuteAsync(null);
        Assert.Equal("Keep me", orders.Designs.Search.Title);
    }

    // ---- schematic model ----

    [Fact]
    public void SchematicOfTheListShowsHeadersAndSampleRows()
    {
        var (main, _) = Project();
        var table = main.Current.Tables[0];
        main.Tables.SelectedTable = table;
        var designer = main.PageDesigner;

        var schematic = designer.Schematic!;
        Assert.Equal(PageKind.List, schematic.Kind);
        Assert.Equal("Customers", schematic.Heading);
        Assert.Equal(["Id", "Customer name", "Email", "Age", "Active", "Notes", "Actions"], schematic.Headers.Take(7).ToArray());
        Assert.Equal(3, schematic.Rows.Count);
        Assert.Equal(["Add", "Search"], schematic.TopButtons.ToArray());

        designer.Title = "Our customers";
        designer.ShowRowNumbers = true;
        designer.ShowActionsFirst = true;
        designer.AddButtonText = "New";
        designer.UseListFieldsCommand.Execute(null);
        designer.Columns[1].Visible = false;
        designer.Columns[0].Field = "Email";

        schematic = designer.Schematic!;
        Assert.Equal("Our customers", schematic.Heading);
        Assert.Equal(["#", "Actions", "Email", "Email", "Age", "Active", "Notes"], schematic.Headers.ToArray());
        Assert.Equal("1", schematic.Rows[0][0]);
        Assert.Equal("New", schematic.TopButtons[0]);
    }

    [Fact]
    public void SchematicOfAFormFollowsLayoutButtonsAndFieldFlags()
    {
        var (main, _) = Project();
        var table = main.Current.Tables[0];
        main.Tables.SelectedTable = table;
        var designer = main.PageDesigner;
        table.Fields.Single(f => f.Name == "Notes").Ui.Control = EditControl.TextArea;
        table.Fields.Single(f => f.Name == "Email").ShowInAdd = false;

        designer.SelectedPage = PageKind.Add;
        designer.FormLayout = FormLayout.TwoColumns;
        designer.SubmitText = "Create";
        designer.ButtonPlacement = ButtonPlacement.Both;

        var schematic = designer.Schematic!;
        Assert.Equal("Add Customers", schematic.Heading);
        Assert.Equal(2, schematic.FormColumns);
        Assert.Equal(["Customer name", "Age", "Active", "Notes"], schematic.Fields.Select(f => f.Caption).ToArray());
        Assert.Equal(["TextBox", "Number", "Checkbox", "TextArea"], schematic.Fields.Select(f => f.Control).ToArray());
        Assert.Equal(["Create", "Cancel"], schematic.TopButtons.ToArray());
        Assert.Equal(["Create", "Cancel"], schematic.BottomButtons.ToArray());

        designer.ShowCancel = false;
        designer.ButtonPlacement = ButtonPlacement.Top;
        schematic = designer.Schematic!;
        Assert.Equal(["Create"], schematic.TopButtons.ToArray());
        Assert.Empty(schematic.BottomButtons);

        designer.SelectedPage = PageKind.Search;
        Assert.Equal(6, designer.Schematic!.Fields.Count);

        designer.SelectedPage = PageKind.Delete;
        Assert.Equal(["Delete", "Cancel"], designer.Schematic!.BottomButtons.ToArray());
        Assert.NotEmpty(designer.Schematic.Note);
    }

    [Fact]
    public void SchematicChangesRaiseAPropertyChange()
    {
        var (main, _) = Project();
        main.Tables.SelectedTable = main.Current.Tables[0];
        var raised = 0;
        main.PageDesigner.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PageDesignerViewModel.Schematic))
            {
                raised++;
            }
        };

        main.PageDesigner.Title = "Changed";
        main.PageDesigner.Striped = false;
        main.PageDesigner.AddColumnCommand.Execute(null);

        Assert.True(raised >= 3);
    }

    [Fact]
    public async Task EditingPageDesignMarksTheProjectDirty()
    {
        using var folder = new TempFolder();
        var (main, dialogs) = Project();
        main.Tables.SelectedTable = main.Current.Tables[0];
        await CleanAsync(main, dialogs, folder);
        main.PageDesigner.Subtitle = "x";
        Assert.True(main.IsDirty);
    }

    // ---- project tab ----

    [Fact]
    public void ProjectSettingsWriteThroughToTheProject()
    {
        var (main, _) = Project();
        var settings = main.ProjectSettings;
        var meta = main.Current.Meta;

        settings.Name = "Shop";
        settings.Description = "Desc";
        settings.Author = "Ann";
        settings.Company = "Acme";
        settings.Copyright = "(c) Acme";
        settings.Version = "2.1.0";
        settings.RootNamespace = "Acme.Shop";
        settings.AssemblyName = "Acme.Shop";
        settings.SiteTitle = "Shop admin";
        settings.Theme = "bootstrap";
        settings.IncludeHomePage = false;
        settings.FaviconPath = "assets/fav.ico";
        settings.LogoPath = "assets/logo.png";
        settings.FooterText = "Footer";
        settings.ColorMode = "dark";
        settings.NavbarStyle = "dark";
        settings.Environment = "Production";
        settings.HttpPortText = "8080";
        settings.HttpsPortText = "8443";
        settings.UseHttps = true;
        settings.EnableHsts = true;
        settings.EnableResponseCompression = false;
        settings.EnableHealthEndpoint = true;
        settings.LogLevel = "Warning";
        settings.GlobalCss = "body{}";
        settings.GlobalScript = "console.log(1)";

        Assert.Equal("Shop", main.Current.Name);
        Assert.Equal("Shop", main.ProjectName);
        Assert.Equal("Desc", meta.Description);
        Assert.Equal("Ann", meta.Author);
        Assert.Equal("Acme", meta.Company);
        Assert.Equal("(c) Acme", meta.Copyright);
        Assert.Equal("2.1.0", meta.Version);
        Assert.Equal("Acme.Shop", meta.RootNamespace);
        Assert.Equal("Acme.Shop", meta.AssemblyName);
        Assert.Equal("Shop admin", main.Current.AspNet.Title);
        Assert.False(main.Current.AspNet.IncludeHomePage);
        Assert.Equal("assets/fav.ico", meta.FaviconPath);
        Assert.Equal("assets/logo.png", meta.LogoPath);
        Assert.Equal("Footer", meta.FooterText);
        Assert.Equal("dark", meta.ColorMode);
        Assert.Equal("dark", meta.NavbarStyle);
        Assert.Equal("Production", meta.Environment);
        Assert.Equal(8080, meta.HttpPort);
        Assert.Equal(8443, meta.HttpsPort);
        Assert.True(meta.UseHttps);
        Assert.True(meta.EnableHsts);
        Assert.False(meta.EnableResponseCompression);
        Assert.True(meta.EnableHealthEndpoint);
        Assert.Equal("Warning", meta.LogLevel);
        Assert.Equal("body{}", meta.GlobalCss);
        Assert.Equal("console.log(1)", meta.GlobalScript);
        Assert.True(main.IsDirty);
    }

    [Fact]
    public void BuildAndDockerSettingsWriteThrough()
    {
        var (main, _) = Project();
        var settings = main.ProjectSettings;
        var build = main.Current.Build;

        settings.BuildAfterGenerate = true;
        settings.Configuration = "Release";
        settings.Publish = true;
        settings.PublishFolder = "out";
        settings.RuntimeIdentifier = "linux-x64";
        settings.SelfContained = true;
        settings.GenerateDockerfile = true;
        settings.DockerImageName = "shop";
        settings.DockerExposePortText = "9000";
        settings.DockerBaseSdkImage = "my/sdk:10";
        settings.DockerBaseRuntimeImage = "my/run:10";
        settings.GenerateCompose = true;
        settings.BuildDockerImage = true;

        Assert.True(build.BuildAfterGenerate);
        Assert.Equal("Release", build.Configuration);
        Assert.True(build.Publish);
        Assert.Equal("out", build.PublishFolder);
        Assert.Equal("linux-x64", build.RuntimeIdentifier);
        Assert.True(build.SelfContained);
        Assert.True(build.GenerateDockerfile);
        Assert.Equal("shop", build.DockerImageName);
        Assert.Equal(9000, build.DockerExposePort);
        Assert.Equal("my/sdk:10", build.DockerBaseSdkImage);
        Assert.Equal("my/run:10", build.DockerBaseRuntimeImage);
        Assert.True(build.GenerateCompose);
        Assert.True(build.BuildDockerImage);

        settings.DockerExposePortText = "70000";
        Assert.Equal(9000, build.DockerExposePort);
        Assert.True(settings.HasMessage);
        settings.DockerBaseSdkImage = "has space";
        Assert.Equal("my/sdk:10", build.DockerBaseSdkImage);
        settings.PublishFolder = "  ";
        Assert.Equal("out", build.PublishFolder);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("http")]
    [InlineData("")]
    public void PortsOutsideOneToSixtyFiveThousandAreRefused(string text)
    {
        var (main, _) = Project();
        var settings = main.ProjectSettings;
        settings.HttpPortText = "6000";

        settings.HttpPortText = text;
        settings.HttpsPortText = text;

        Assert.Equal(6000, main.Current.Meta.HttpPort);
        Assert.Equal(5001, main.Current.Meta.HttpsPort);
        Assert.True(settings.HasMessage);
        Assert.Equal(text, settings.HttpPortText);
    }

    [Fact]
    public void PortsAtTheEdgesAreAcceptedAndMustDiffer()
    {
        var (main, _) = Project();
        var settings = main.ProjectSettings;
        settings.HttpPortText = "1";
        settings.HttpsPortText = "65535";
        Assert.Equal(1, main.Current.Meta.HttpPort);
        Assert.Equal(65535, main.Current.Meta.HttpsPort);
        Assert.False(settings.HasMessage);

        settings.HttpPortText = "65535";
        Assert.Equal(1, main.Current.Meta.HttpPort);
        Assert.Contains("different", settings.Message);
    }

    [Fact]
    public void NamespaceAssemblyAndVersionAreChecked()
    {
        var (main, _) = Project();
        var settings = main.ProjectSettings;
        var meta = main.Current.Meta;

        settings.RootNamespace = "1Bad.Name";
        Assert.Equal(string.Empty, meta.RootNamespace);
        Assert.True(settings.HasMessage);
        settings.RootNamespace = "Good.Name";
        Assert.Equal("Good.Name", meta.RootNamespace);
        settings.RootNamespace = string.Empty;
        Assert.Equal(string.Empty, meta.RootNamespace);

        settings.AssemblyName = "bad name";
        Assert.Equal(string.Empty, meta.AssemblyName);
        settings.AssemblyName = "Good-Name_1";
        Assert.Equal("Good-Name_1", meta.AssemblyName);

        settings.Version = "abc";
        Assert.Equal("1.0.0", meta.Version);
        settings.Version = "3.2";
        Assert.Equal("3.2", meta.Version);
        settings.Version = "1.2.3-beta.1";
        Assert.Equal("1.2.3-beta.1", meta.Version);

        settings.Name = "   ";
        Assert.Equal("Project1", main.ProjectName);
    }

    [Fact]
    public async Task RegeneratingTheProjectIdNeedsConfirmation()
    {
        using var folder = new TempFolder();
        var (main, dialogs) = Project();
        await CleanAsync(main, dialogs, folder);
        var before = main.Current.ProjectId;

        dialogs.NextConfirm = false;
        await main.ProjectSettings.RegenerateProjectIdCommand.ExecuteAsync(null);
        Assert.Equal(before, main.Current.ProjectId);
        Assert.Equal(before.ToString(), main.ProjectSettings.ProjectId);
        Assert.False(main.IsDirty);

        dialogs.NextConfirm = true;
        await main.ProjectSettings.RegenerateProjectIdCommand.ExecuteAsync(null);
        Assert.NotEqual(before, main.Current.ProjectId);
        Assert.Equal(main.Current.ProjectId.ToString(), main.ProjectSettings.ProjectId);
        Assert.True(main.IsDirty);
    }

    [Fact]
    public async Task OutputFolderAndDatabasePathAreSharedWithTheirTabs()
    {
        var (main, dialogs) = Project();
        var settings = main.ProjectSettings;

        settings.OutputFolder = "site";
        Assert.Equal("site", main.Generate.OutputFolder);
        Assert.Equal("site", main.Current.OutputFolder);

        main.Generate.OutputFolder = "other";
        Assert.Equal("other", settings.OutputFolder);

        settings.OutputFolder = " ";
        Assert.Equal("other", main.Current.OutputFolder);
        Assert.True(settings.HasMessage);

        settings.DatabasePath = "data/app.db";
        Assert.Equal("data/app.db", main.Current.Database.Path);
        Assert.Equal("SQLite", settings.Provider);

        dialogs.NextFolder = Path.GetFullPath("elsewhere");
        await settings.BrowseOutputCommand.ExecuteAsync(null);
        Assert.Equal(Path.GetFullPath("elsewhere"), settings.OutputFolder);
    }

    [Fact]
    public async Task FaviconAndLogoPickersStoreTheChosenFile()
    {
        using var folder = new TempFolder();
        var main = await Harness.SavedProjectAsync(folder);
        var dialogs = (FakeDialogService)main.Dialogs;
        var settings = main.ProjectSettings;

        dialogs.NextImageFile = folder.File("img/fav.ico");
        await settings.BrowseFaviconCommand.ExecuteAsync(null);
        Assert.Equal("img/fav.ico", main.Current.Meta.FaviconPath);

        dialogs.NextImageFile = null;
        await settings.BrowseLogoCommand.ExecuteAsync(null);
        Assert.Equal(string.Empty, main.Current.Meta.LogoPath);

        settings.ClearFaviconCommand.Execute(null);
        Assert.Equal(string.Empty, main.Current.Meta.FaviconPath);
    }

    [Fact]
    public void SecurityAndLocalizationSummariesFollowTheProjectAndTheButtonsJumpToTabs()
    {
        var (main, _) = Project();
        var settings = main.ProjectSettings;

        Assert.StartsWith("Sign-in is off", settings.SecuritySummary);
        Assert.StartsWith("Multi-language support is off", settings.LocalizationSummary);

        main.Current.Security.Enabled = true;
        main.Current.Security.UserTable = "Customers";
        main.Current.Localization.Enabled = true;
        main.Current.Localization.Languages.Add("de");
        settings.RefreshSummaries();
        Assert.Contains("Customers", settings.SecuritySummary);
        Assert.Contains("en, de", settings.LocalizationSummary);

        settings.ShowSecurityTabCommand.Execute(null);
        Assert.Equal("Security", MainViewModel.TabNames[main.ActiveTabIndex]);

        main.ActiveTabIndex = MainViewModel.TabNames.ToList().IndexOf("Database");
        settings.ShowLocalizationTabCommand.Execute(null);
        Assert.Equal("Languages", MainViewModel.TabNames[main.ActiveTabIndex]);
    }

    [Fact]
    public void NewTabsAreNamedAndTheBreadcrumbFollowsThem()
    {
        var (main, _) = Project();
        Assert.Contains("Project", MainViewModel.TabNames);
        Assert.Contains("Pages", MainViewModel.TabNames);

        main.Tables.SelectedTable = main.Current.Tables[1];
        main.ActiveTabIndex = MainViewModel.TabNames.ToList().IndexOf("Pages");
        Assert.EndsWith("Pages > Orders", main.Breadcrumb);
        main.ActiveTabIndex = MainViewModel.TabNames.ToList().IndexOf("Project");
        Assert.EndsWith("Project", main.Breadcrumb);
    }

    [Fact]
    public void DeepCopyCopiesListsAndNestedObjectsWithoutSharing()
    {
        var source = new FieldUi { Control = EditControl.Select, Lookup = new FieldLookup { Table = "T", KeyField = "K" } };
        source.Options.Add(new FieldOption { Value = "1", Text = "One" });
        source.BadgeRules.Add(new ValueRule { Value = "x", Style = "info" });
        var target = new FieldUi();

        DeepCopy.Into(source, target);

        Assert.Equal(EditControl.Select, target.Control);
        Assert.Equal("T", target.Lookup!.Table);
        Assert.NotSame(source.Lookup, target.Lookup);
        Assert.NotSame(source.Options[0], target.Options[0]);
        Assert.Equal("One", target.Options[0].Text);
        Assert.Equal("info", target.BadgeRules[0].Style);

        DeepCopy.Into(new FieldUi(), target);
        Assert.Null(target.Lookup);
        Assert.Empty(target.Options);
    }
}
