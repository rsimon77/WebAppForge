using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

/// <summary>Generated text for field, table and project customization, and proof that defaults change nothing.</summary>
public class CustomizationTests
{
    // ---- defaults are byte-identical ------------------------------------------------------------

    [Theory]
    [InlineData("plain")]
    [InlineData("secure")]
    [InlineData("localized")]
    public void ProjectsThatCustomizeNothingGenerateTheSameFilesAsBefore(string sample)
    {
        var project = CustomizationSamples.Create(sample);

        var files = Generate(project);

        var golden = CustomizationGolden.Hashes
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(' '))
            .Where(parts => parts[0].StartsWith(sample + "/", StringComparison.Ordinal))
            .ToDictionary(parts => parts[0][(sample.Length + 1)..], parts => parts[1]);
        var actual = files.Where(f => f.Key != "appsettings.json").ToDictionary(f => f.Key, f => Hash(f.Value));
        Assert.Equal(golden.Keys.OrderBy(k => k, StringComparer.Ordinal), actual.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (path, hash) in golden)
        {
            Assert.True(hash == actual[path], $"{sample}/{path} differs from the output generated before customization existed (expected {hash}, actual {actual[path]})");
        }

        Assert.DoesNotContain("Kestrel", files["appsettings.json"]);
        Assert.False(files.ContainsKey("Web/Ui.cs"));
        Assert.DoesNotContain(files.Keys, k => k.StartsWith("wwwroot", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitlyDefaultCustomizationObjectsChangeNothing()
    {
        var plain = Generate(CustomizationSamples.Create("plain"));
        var project = CustomizationSamples.Create("plain");
        foreach (var table in project.Tables)
        {
            table.Ui = new TableUi();
            table.Designs = new PageDesigns();
            foreach (var field in table.Fields)
            {
                field.Ui = new FieldUi();
            }
        }

        project.Meta = new ProjectMetadata();

        Assert.Equal(plain, Generate(project));
    }

    [Fact]
    public void CompressionAloneDoesNotChangeTheDefaultOutput()
    {
        // EnableResponseCompression is true by default, so it only takes effect together with another metadata change.
        var project = CustomizationSamples.Create("plain");
        project.Meta.EnableResponseCompression = true;

        Assert.DoesNotContain("Compression", Generate(project)["Program.cs"]);
    }

    // ---- controls -------------------------------------------------------------------------------

    [Theory]
    [InlineData(EditControl.Auto, FieldType.Text, "text")]
    [InlineData(EditControl.Auto, FieldType.Integer, "number")]
    [InlineData(EditControl.Auto, FieldType.Date, "date")]
    [InlineData(EditControl.Auto, FieldType.DateTime, "datetime-local")]
    [InlineData(EditControl.Auto, FieldType.Boolean, "checkbox")]
    [InlineData(EditControl.TextBox, FieldType.Text, "text")]
    [InlineData(EditControl.TextArea, FieldType.Text, "textarea")]
    [InlineData(EditControl.Password, FieldType.Text, "password")]
    [InlineData(EditControl.Email, FieldType.Text, "email")]
    [InlineData(EditControl.Url, FieldType.Text, "url")]
    [InlineData(EditControl.Phone, FieldType.Text, "tel")]
    [InlineData(EditControl.Number, FieldType.Text, "number")]
    [InlineData(EditControl.Date, FieldType.Date, "date")]
    [InlineData(EditControl.DateTime, FieldType.DateTime, "datetime-local")]
    [InlineData(EditControl.Time, FieldType.DateTime, "time")]
    [InlineData(EditControl.Checkbox, FieldType.Boolean, "checkbox")]
    [InlineData(EditControl.Checkbox, FieldType.Text, "text")]
    [InlineData(EditControl.Select, FieldType.Text, "select")]
    [InlineData(EditControl.Radio, FieldType.Text, "radio")]
    [InlineData(EditControl.Color, FieldType.Text, "color")]
    [InlineData(EditControl.Hidden, FieldType.Text, "hidden")]
    [InlineData(EditControl.ReadOnly, FieldType.Text, "readonly")]
    [InlineData(EditControl.ReadOnly, FieldType.Boolean, "readonly")]
    public void EveryEditControlGeneratesItsInputType(EditControl control, FieldType type, string expected)
    {
        var project = OneTable(table =>
        {
            var field = Field("f", type);
            field.Ui.Control = control;
            field.Ui.HelpText = "x";
            table.Fields.Add(field);
        });

        var pages = Pages(Generate(project));

        Assert.Contains($"Name = \"f\", Label = \"f\", Control = \"{expected}\"", pages);
    }

    [Fact]
    public void OptionsWithoutAnExplicitControlBecomeASelect()
    {
        var project = OneTable(table =>
        {
            var field = Field("size", FieldType.Text);
            field.Ui.Options.Add(new FieldOption { Value = "s", Text = "Small" });
            field.Ui.Options.Add(new FieldOption { Value = "l", Text = "Large \"XL\"" });
            table.Fields.Add(field);
        });

        var pages = Pages(Generate(project));

        Assert.Contains("Control = \"select\"", pages);
        Assert.Contains("Options_Size = [new(\"s\", \"Small\"), new(\"l\", \"Large \\\"XL\\\"\")];", pages);
        Assert.Contains("Options = Options_Size", pages);
        Assert.Contains("Array.Exists(Options_Size, o => o.Key == value)", pages);
    }

    [Fact]
    public void TextAreaPasswordAndHintsAreEmitted()
    {
        var project = OneTable(table =>
        {
            var notes = Field("notes", FieldType.Text);
            notes.Ui.Control = EditControl.TextArea;
            notes.Ui.Rows = 6;
            notes.Ui.HelpText = "Say \"more\" <b>";
            notes.Ui.Placeholder = "Type here";
            notes.Ui.Tooltip = "Tip";
            notes.Ui.AutoFocus = true;
            notes.Ui.CssClass = "extra";
            notes.Ui.Style = "color:red";
            var secret = Field("secret", FieldType.Text);
            secret.Ui.Control = EditControl.Password;
            table.Fields.AddRange([notes, secret]);
        });

        var files = Generate(project);
        var pages = Pages(files);

        Assert.Contains("Rows = 6", pages);
        Assert.Contains("Help = \"Say \\\"more\\\" <b>\"", pages);
        Assert.Contains("Placeholder = \"Type here\"", pages);
        Assert.Contains("Tooltip = \"Tip\"", pages);
        Assert.Contains("AutoFocus = true", pages);
        Assert.Contains("CssClass = \"extra\"", pages);
        Assert.Contains("Style = \"color:red\"", pages);
        // A password box is never filled in and an empty value on edit keeps the stored password.
        Assert.Contains("Control = \"password\", Value = string.Empty", pages);
        Assert.Contains("if (!edit || value.Length > 0)", pages);
        // The text is encoded when the page is rendered.
        var runtime = files["Web/Ui.cs"];
        Assert.Contains("Html.Encode(s.Help)", runtime);
        Assert.Contains("Attribute(\"title\", s.Tooltip)", runtime);
        Assert.Contains("Html.Attr(value)", runtime);
    }

    [Fact]
    public void DefaultValueSeedsTheAddFormOnly()
    {
        var project = OneTable(table =>
        {
            var qty = Field("qty", FieldType.Integer);
            qty.Ui.DefaultValue = "5";
            var name = Field("name", FieldType.Text);
            name.Ui.DefaultValue = "a \"b\"";
            var price = Field("price", FieldType.Decimal);
            price.Ui.DefaultValue = "9.5";
            var flag = Field("flag", FieldType.Boolean);
            flag.Ui.DefaultValue = "yes";
            var due = Field("due", FieldType.Date);
            due.Ui.DefaultValue = "today";
            var bad = Field("bad", FieldType.Integer);
            bad.Ui.DefaultValue = "not a number";
            table.Fields.AddRange([qty, name, price, flag, due, bad]);
        });

        var pages = Pages(Generate(project));

        Assert.Contains("new Items { Qty = 5L, Name = \"a \\\"b\\\"\", Price = 9.5m, Flag = true, Due = DateTime.Today }", pages);
        Assert.DoesNotContain("Bad =", pages.Split('\n').Single(l => l.Contains("new Items {")));
    }

    [Fact]
    public void ReadOnlyOnEditShowsTextOnEditAndIsNotBoundThere()
    {
        var project = OneTable(table =>
        {
            var code = Field("code", FieldType.Text, required: true);
            code.Ui.ReadOnlyOnEdit = true;
            table.Fields.Add(code);
        });

        var pages = Pages(Generate(project));

        Assert.Contains("body.Append(edit ? FieldEditor.Render(new FieldEditor.Spec { Name = \"code\", Label = \"code\", Control = \"readonly\"", pages);
        Assert.Equal("if (!edit)", BindGuard(pages, "item.Code = value;"));
    }

    [Fact]
    public void ReadOnlyControlIsNeverBound()
    {
        var project = OneTable(table =>
        {
            var code = Field("code", FieldType.Text);
            code.Ui.Control = EditControl.ReadOnly;
            table.Fields.Add(code);
        });

        var pages = Pages(Generate(project));

        Assert.DoesNotContain("form[\"code\"]", pages);
    }

    [Fact]
    public void GroupWrapsConsecutiveFieldsInAFieldset()
    {
        var project = OneTable(table =>
        {
            var a = Field("a", FieldType.Text);
            a.Ui.Group = "Contact \"info\"";
            var b = Field("b", FieldType.Text);
            b.Ui.Group = "Contact \"info\"";
            var c = Field("c", FieldType.Text);
            table.Fields.AddRange([a, b, c]);
        });

        var lines = Pages(Generate(project)).Split('\n').Select(l => l.Trim()).ToList();

        var start = lines.FindIndex(l => l.Contains("FieldEditor.GroupStart(\"Contact \\\"info\\\"\")"));
        var end = lines.FindIndex(l => l.Contains("FieldEditor.GroupEnd"));
        Assert.True(start >= 0 && end > start);
        Assert.Contains("Name = \"a\"", lines[start + 1]);
        Assert.Contains("Name = \"b\"", lines[start + 2]);
        Assert.Contains("Html.Input(\"c\"", lines[end + 1]);
    }

    // ---- validation -----------------------------------------------------------------------------

    [Fact]
    public void TextValidationIsServerSideAndMirroredAsHtmlAttributes()
    {
        var project = OneTable(table =>
        {
            var name = Field("name", FieldType.Text, required: true);
            name.Ui.MaxLength = 20;
            name.Ui.MinLength = 2;
            name.Ui.Pattern = "[a-z ]+";
            name.Ui.PatternMessage = "Lower case \"only\"";
            name.Ui.RequiredMessage = "Please give a name";
            table.Fields.Add(name);
        });

        var files = Generate(project);
        var pages = Pages(files);

        Assert.Contains("MaxLength = 20, MinLength = 2", pages);
        Assert.Contains("Pattern = \"[a-z ]+\"", pages);
        Assert.Contains("Required = true", pages);
        Assert.Contains("errors.Add(\"Please give a name\")", pages);
        Assert.Contains("if (value.Length > 20) errors.Add(\"name\" + \" must be at most 20 characters.\");", pages);
        Assert.Contains("if (value.Length < 2) errors.Add(\"name\" + \" must be at least 2 characters.\");", pages);
        Assert.Contains("if (!FieldEditor.Matches(value, \"[a-z ]+\")) errors.Add(\"Lower case \\\"only\\\"\");", pages);

        var runtime = files["Web/Ui.cs"];
        foreach (var attribute in new[] { "maxlength=", "minlength=", "pattern", "required" })
        {
            Assert.Contains(attribute, runtime);
        }

        // The pattern is anchored so the whole value has to match, as in the browser.
        Assert.Contains("\"^(?:\" + pattern + \")$\"", runtime);
    }

    [Fact]
    public void NumberAndDateRangesAreCheckedAfterParsing()
    {
        var project = OneTable(table =>
        {
            var qty = Field("qty", FieldType.Integer);
            qty.Ui.Min = "1";
            qty.Ui.Max = "99";
            qty.Ui.Step = "1";
            var price = Field("price", FieldType.Decimal);
            price.Ui.Min = "0.5";
            var due = Field("due", FieldType.Date);
            due.Ui.Min = "2020-01-01";
            due.Ui.Max = "2030-12-31";
            table.Fields.AddRange([qty, price, due]);
        });

        var pages = Pages(Generate(project));

        Assert.Contains("Min = \"1\", Max = \"99\", Step = \"1\"", pages);
        Assert.Contains("if (parsed < 1m) errors.Add(\"qty\" + \" must be at least 1.\");", pages);
        Assert.Contains("if (parsed > 99m) errors.Add(\"qty\" + \" must be at most 99.\");", pages);
        Assert.Contains("if (parsed < 0.5m) errors.Add(\"price\" + \" must be at least 0.5.\");", pages);
        // A decimal field gets step=any so the browser accepts fractions.
        Assert.Contains("Name = \"price\", Label = \"price\", Control = \"number\", Value = item.Price?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, Min = \"0.5\", Step = \"any\"", pages);
        Assert.Contains($"if (parsed < new DateTime({new DateTime(2020, 1, 1).Ticks}L))", pages);
        Assert.Contains($"if (parsed > new DateTime({new DateTime(2030, 12, 31).Ticks}L))", pages);
    }

    [Fact]
    public void AnInvalidPatternIsIgnoredEverywhere()
    {
        var project = OneTable(table =>
        {
            var name = Field("name", FieldType.Text);
            name.Ui.Pattern = "([unclosed";
            table.Fields.Add(name);
        });

        var pages = Pages(Generate(project));

        Assert.DoesNotContain("Matches(", pages);
        Assert.DoesNotContain("Pattern =", pages);
    }

    // ---- lookups --------------------------------------------------------------------------------

    [Fact]
    public void LookupReadsKeyAndDisplayRowsWithParameterisedSql()
    {
        var project = OneTable(table =>
        {
            var category = Field("category_id", FieldType.Integer);
            category.Ui.Lookup = new FieldLookup
            {
                Table = "Categories",
                KeyField = "CategoryID",
                DisplayField = "CategoryName",
                Filter = "Active = 1",
                OrderField = "SortNo",
            };
            table.Fields.Add(category);
        });

        var files = Generate(project);
        var pages = Pages(files);
        var runtime = files["Web/Ui.cs"];

        Assert.Contains("Lookup_CategoryId = new(\"\\\"Categories\\\"\", \"\\\"CategoryID\\\"\", \"\\\"CategoryName\\\"\", \"Active = 1\", \"\\\"SortNo\\\"\");", pages);
        Assert.Contains("Control = \"select\"", pages);
        Assert.Contains("Options = opt_CategoryId", pages);
        Assert.Contains("var opt_CategoryId = await Lookup_CategoryId.OptionsAsync(connectionString);", pages);
        Assert.Contains("private static async Task<IResult> RenderForm(", pages);
        Assert.Contains("return await RenderForm(", pages);
        Assert.Contains("AddForm(context, antiforgery, connectionString)", pages);
        // The list and view pages resolve the stored keys to display text.
        Assert.Contains("var lk_CategoryId = await Lookup_CategoryId.TextsAsync(connection, items.Select(x => (object?)x.CategoryId));", pages);
        Assert.Contains("var lk_CategoryId = await Lookup_CategoryId.TextsAsync(connection, new object?[] { item.CategoryId });", pages);
        Assert.Contains("LookupSource.Pick(lk_CategoryId, item.CategoryId)", pages);

        // Stored keys are bound as parameters, never added to the SQL text. The filter is part of the project.
        Assert.Contains("args.Add(\"@k\" + i, chunk[i]);", runtime);
        Assert.Contains("\" IN (\" + string.Join(\", \", names) + \")\"", runtime);
        Assert.Contains("(filter.Length > 0 ? \" WHERE (\" + filter + \")\" : \"\") + \" ORDER BY \" + order", runtime);
        Assert.DoesNotContain("chunk[i].ToString()", runtime);
    }

    [Fact]
    public void LookupOrdersByTheDisplayFieldAndCanHideTheDisplayTextInViews()
    {
        var project = OneTable(table =>
        {
            var category = Field("category_id", FieldType.Integer);
            category.Ui.Lookup = new FieldLookup { Table = "Categories", KeyField = "Id", DisplayField = "Name", ShowDisplayInView = false };
            category.Ui.Control = EditControl.Radio;
            table.Fields.Add(category);
        });

        var pages = Pages(Generate(project));

        Assert.Contains("\"\\\"Name\\\"\");", pages); // order field defaults to the display field
        Assert.Contains("Control = \"radio\"", pages);
        Assert.DoesNotContain("lk_CategoryId", pages);
    }

    [Fact]
    public void LookupIdentifiersWithQuotesAreRejected()
    {
        var project = OneTable(table =>
        {
            var category = Field("category_id", FieldType.Integer);
            category.Ui.Lookup = new FieldLookup { Table = "Cat\"; DROP TABLE x; --", KeyField = "Id", DisplayField = "Name" };
            table.Fields.Add(category);
        });

        Assert.Throws<NotSupportedException>(() => Generate(project));
    }

    [Fact]
    public void IncompleteLookupIsIgnored()
    {
        var project = OneTable(table =>
        {
            var category = Field("category_id", FieldType.Integer);
            category.Ui.Lookup = new FieldLookup { Table = "Categories" };
            table.Fields.Add(category);
        });

        var pages = Pages(Generate(project));

        Assert.DoesNotContain("LookupSource", pages);
    }

    // ---- formats --------------------------------------------------------------------------------

    [Theory]
    [InlineData(ViewFormat.Text)]
    [InlineData(ViewFormat.MultilineText)]
    [InlineData(ViewFormat.Number)]
    [InlineData(ViewFormat.Currency)]
    [InlineData(ViewFormat.Percent)]
    [InlineData(ViewFormat.Date)]
    [InlineData(ViewFormat.DateTime)]
    [InlineData(ViewFormat.Time)]
    [InlineData(ViewFormat.Boolean)]
    [InlineData(ViewFormat.Image)]
    [InlineData(ViewFormat.Link)]
    [InlineData(ViewFormat.EmailLink)]
    [InlineData(ViewFormat.Badge)]
    public void EveryViewFormatIsUsedOnTheListAndViewPages(ViewFormat format)
    {
        var project = OneTable(table =>
        {
            var field = Field("v", FieldType.Text);
            field.Ui.Format = format;
            table.Fields.Add(field);
        });

        var pages = Pages(Generate(project));

        Assert.Contains($"Fmt_V = new() {{ Kind = \"{format}\" }};", pages);
        Assert.Equal(2, CountOf(pages, "Fmt_V.Render(item.V)"));
    }

    [Fact]
    public void FormatStringAndBooleanTextsAreEmitted()
    {
        var project = OneTable(table =>
        {
            var price = Field("price", FieldType.Decimal);
            price.Ui.Format = ViewFormat.Currency;
            price.Ui.FormatString = "C0";
            var flag = Field("flag", FieldType.Boolean, required: true);
            flag.Ui.TrueText = "On \"now\"";
            flag.Ui.FalseText = "Off";
            table.Fields.AddRange([price, flag]);
        });

        var pages = Pages(Generate(project));

        Assert.Contains("Fmt_Price = new() { Kind = \"Currency\", FormatString = \"C0\" };", pages);
        // Texts for true and false imply the Boolean format.
        Assert.Contains("Fmt_Flag = new() { Kind = \"Boolean\", TrueText = \"On \\\"now\\\"\", FalseText = \"Off\" };", pages);
    }

    [Fact]
    public void BadgeRulesKeepOperatorStyleAndText()
    {
        var project = OneTable(table =>
        {
            var status = Field("status", FieldType.Text);
            status.Ui.Format = ViewFormat.Badge;
            status.Ui.BadgeRules.Add(new ValueRule { Operator = ConditionOperator.Equals, Value = "1", Style = "danger", Text = "Gone" });
            status.Ui.BadgeRules.Add(new ValueRule { Operator = ConditionOperator.Contains, Value = "wa", Style = "wa rn\"<ing", Text = "" });
            status.Ui.BadgeRules.Add(new ValueRule { Operator = ConditionOperator.IsEmpty, Value = "", Style = "", Text = "none" });
            table.Fields.Add(status);
        });

        var files = Generate(project);
        var pages = Pages(files);

        Assert.Contains("Rules = new FieldView.Rule[] { new(\"Equals\", \"1\", \"danger\", \"Gone\"), new(\"Contains\", \"wa\", \"warning\", \"\"), new(\"IsEmpty\", \"\", \"secondary\", \"none\") }", pages);
        // The evaluator knows every operator of the contract.
        var runtime = files["Web/Ui.cs"];
        foreach (var op in Enum.GetNames<ConditionOperator>().Where(n => n != "Equals"))
        {
            Assert.Contains($"case \"{op}\":", runtime);
        }

        Assert.Contains("text-bg-", runtime);
        Assert.Contains("Token(rule.Style, \"secondary\")", runtime);
    }

    [Fact]
    public void BadgeRulesWithoutAFormatImplyTheBadgeFormat()
    {
        var project = OneTable(table =>
        {
            var status = Field("status", FieldType.Text);
            status.Ui.BadgeRules.Add(new ValueRule { Value = "x" });
            table.Fields.Add(status);
        });

        Assert.Contains("Fmt_Status = new() { Kind = \"Badge\"", Pages(Generate(project)));
    }

    [Fact]
    public void BooleanRuleValuesAreNormalisedToOneAndZero()
    {
        var project = OneTable(table =>
        {
            var active = Field("active", FieldType.Boolean, required: true);
            active.Ui.Format = ViewFormat.Badge;
            active.Ui.BadgeRules.Add(new ValueRule { Value = "True", Style = "success" });
            active.Ui.BadgeRules.Add(new ValueRule { Value = "no", Style = "danger" });
            table.Fields.Add(active);
        });

        var pages = Pages(Generate(project));

        Assert.Contains("new(\"Equals\", \"1\", \"success\", \"\"), new(\"Equals\", \"0\", \"danger\", \"\")", pages);
    }

    [Fact]
    public void WidthAlignCssClassAndStyleGoOnHeaderAndCells()
    {
        var project = OneTable(table =>
        {
            var price = Field("price", FieldType.Decimal);
            price.Ui.Width = "90px";
            price.Ui.Align = TextAlign.Right;
            price.Ui.CssClass = "fw-bold \"x\"";
            price.Ui.Style = "color:red;";
            price.Ui.Tooltip = "Net <price>";
            table.Fields.Add(price);
        });

        var pages = Pages(Generate(project));

        Assert.Contains("\" class=\\\"text-end fw-bold &quot;x&quot;\\\" style=\\\"width:90px;color:red\\\" title=\\\"Net &lt;price&gt;\\\"\"", pages);
        Assert.Contains("\"<td class=\\\"text-end fw-bold &quot;x&quot;\\\" style=\\\"color:red\\\">\"", pages);
    }

    // ---- sorting --------------------------------------------------------------------------------

    [Fact]
    public void OnlySortableListedNonBinaryFieldsAreInTheSortWhitelist()
    {
        var project = OneTable(table =>
        {
            var a = Field("a", FieldType.Text);
            a.Ui.HelpText = "makes the table customized";
            var b = Field("b", FieldType.Text);
            b.Ui.Sortable = false;
            var hidden = Field("hidden", FieldType.Text);
            hidden.ShowInList = false;
            var blob = Field("blob", FieldType.Binary);
            table.Fields.AddRange([a, b, hidden, blob]);
        });

        var pages = Pages(Generate(project));

        Assert.Contains("SortFields = new[] { \"id\", \"a\" };", pages);
        Assert.Contains("SortColumns = new[] { \"\\\"id\\\"\", \"\\\"a\\\"\" };", pages);
        Assert.Contains("var sortIndex = Array.IndexOf(SortFields, request.Query[\"sort\"].ToString());", pages);
        // Only a whitelisted, quoted column from the array reaches the SQL; the request text never does.
        Assert.Contains("order = \" ORDER BY \" + SortColumns[sortIndex] + (request.Query[\"dir\"].ToString() == \"desc\" ? \" DESC\" : \" ASC\") + SortTieBreak;", pages);
        Assert.Contains("FieldView.SortHeader(request, Route, \"a\"", pages);
        Assert.Contains("body.Append(\"<th>\").Append(Html.Encode(\"b\")).Append(\"</th>\");", pages);
        Assert.DoesNotContain("SortHeader(request, Route, \"b\"", pages);
        Assert.DoesNotContain("SortHeader(request, Route, \"blob\"", pages);
    }

    [Fact]
    public void TablesThatCustomizeNothingGetNoSorting()
    {
        var project = OneTable(table => table.Fields.Add(Field("a", FieldType.Text)));

        var pages = Pages(Generate(project));

        Assert.DoesNotContain("SortFields", pages);
        Assert.DoesNotContain("SortHeader", pages);
    }

    [Fact]
    public void TableLevelSettingsAlsoTurnSortingOn()
    {
        var project = OneTable(table =>
        {
            table.Fields.Add(Field("a", FieldType.Text));
            table.Ui.Description = "d";
        });

        Assert.Contains("SortHeader(request, Route, \"a\"", Pages(Generate(project)));
    }

    // ---- table settings -------------------------------------------------------------------------

    [Fact]
    public void RowStyleRulesAddATableClassToMatchingRows()
    {
        var project = OneTable(table =>
        {
            table.Fields.Add(Field("active", FieldType.Boolean, required: true));
            table.Fields.Add(Field("stock", FieldType.Integer));
            table.Ui.RowStyles.Add(new RowStyleRule { Field = "active", Operator = ConditionOperator.Equals, Value = "true", Style = "danger" });
            table.Ui.RowStyles.Add(new RowStyleRule { Field = "STOCK", Operator = ConditionOperator.LessThan, Value = "5", Style = "warn\"ing" });
            table.Ui.RowStyles.Add(new RowStyleRule { Field = "missing", Operator = ConditionOperator.Equals, Value = "1", Style = "info" });
        });

        var pages = Pages(Generate(project));

        Assert.Contains("body.Append(\"<tr\").Append(RowClass(item)).Append(\">\");", pages);
        Assert.Contains("if (FieldView.Test(\"Equals\", FieldView.Raw(item.Active), \"1\")) return \" class=\\\"table-danger\\\"\";", pages);
        Assert.Contains("if (FieldView.Test(\"LessThan\", FieldView.Raw(item.Stock), \"5\")) return \" class=\\\"table-warning\\\"\";", pages);
        Assert.DoesNotContain("table-info", pages);
    }

    [Fact]
    public void WithoutRowStylesRowsStayPlain()
    {
        var project = OneTable(table =>
        {
            table.Fields.Add(Field("a", FieldType.Text));
            table.Ui.Description = "d";
            table.Ui.RowStyles.Add(new RowStyleRule { Field = "nothing", Value = "1" });
        });

        var pages = Pages(Generate(project));

        Assert.DoesNotContain("RowClass", pages);
        Assert.Contains("body.Append(\"<tr>\");", pages);
    }

    [Fact]
    public void DescriptionIconAndCssClassAreEncodedAndShownOnTheListPage()
    {
        var project = OneTable(table =>
        {
            table.Fields.Add(Field("a", FieldType.Text));
            table.Ui.Description = "All <items> & \"more\"";
            table.Ui.Icon = "box-seam\"><script";
            table.Ui.CssClass = "my-table \"x\"";
        });

        var files = Generate(project);
        var pages = Pages(files);

        Assert.Contains("<p class=\\\"text-muted\\\"><i class=\\\"bi bi-box-seamscript me-2\\\"></i>All &lt;items&gt; &amp; &quot;more&quot;</p>", pages);
        Assert.Contains("table table-striped table-hover my-table &quot;x&quot;\\\"><thead>", pages);
        Assert.Contains("bootstrap-icons", files["Web/Html.cs"]);
    }

    [Fact]
    public void IconLinkIsOnlyInTheLayoutWhenSomeTableUsesAnIcon()
    {
        var withIcon = OneTable(table => table.Ui.Icon = "box");
        var without = OneTable(table => table.Ui.Description = "x");

        Assert.Contains("bootstrap-icons", Generate(withIcon)["Web/Html.cs"]);
        Assert.DoesNotContain("bootstrap-icons", Generate(without)["Web/Html.cs"]);
    }

    [Fact]
    public void HomePageGroupsTablesByMenuGroupInMenuOrder()
    {
        var project = MenuProject();

        var files = Generate(project);
        var program = files["Program.cs"];

        Assert.Contains("Html.GroupedMenu(new (string Group, string Caption, string Href, string Icon)[]", program);
        var lines = program.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("(\"", StringComparison.Ordinal)).ToList();
        // Menu order first (stable for equal orders), hidden tables left out.
        Assert.Equal(
            new[]
            {
                "(\"\", \"Alpha\", \"/alpha\", \"\"),",
                "(\"Sales\", \"Bravo\", \"/bravo\", \"cart\"),",
                "(\"Sales\", \"Charlie\", \"/charlie\", \"\"),",
                "(\"Admin\", \"Delta\", \"/delta\", \"\"),",
            },
            lines.ToArray());
        Assert.DoesNotContain("Echo", program.Split("MapGet(\"/\"")[1].Split("app.MapDeltaEndpoints")[0]);
        // Hidden from the menu does not mean not generated.
        Assert.Contains("app.MapEchoPages(connectionString);", program);
    }

    [Fact]
    public void NavbarMenuIsAddedToTheLayoutOnlyWhenAGroupOrHiddenTableExists()
    {
        var withGroups = Generate(MenuProject())["Web/Html.cs"];

        Assert.Contains("private static string Nav()", withGroups);
        Assert.Contains("(\"Sales\", \"Bravo\", \"/bravo\")", withGroups);
        Assert.DoesNotContain("Echo", withGroups);
        Assert.Contains("+ Nav() +", withGroups);
        Assert.Contains("bootstrap.bundle.min.js", withGroups);

        var onlyOrder = OneTable(table => table.Ui.MenuOrder = 3);
        var layout = Generate(onlyOrder)["Web/Html.cs"];
        Assert.DoesNotContain("Nav()", layout);
        Assert.DoesNotContain("bootstrap.bundle", layout);
        Assert.Contains("GroupedMenu", layout);
    }

    [Fact]
    public void LayoutMenuAndLocalizationWorkTogether()
    {
        var project = MenuProject();
        project.Localization.Enabled = true;
        project.Localization.DefaultLanguage = "en";
        project.Localization.Languages.Add("de");

        var layout = Generate(project)["Web/Html.cs"];

        Assert.Contains("Strings.T(\"table.bravo\", \"Bravo\")", layout);
        Assert.Contains("+ Nav() + Strings.Switcher()", layout);
    }

    // ---- project metadata -----------------------------------------------------------------------

    [Fact]
    public void LogLevelAndPortsGoToAppSettings()
    {
        var project = OneTable(_ => { });
        project.Meta.LogLevel = "warning";
        project.Meta.HttpPort = 5123;

        var settings = Generate(project)["appsettings.json"];

        Assert.Contains("\"Default\": \"Warning\"", settings);
        Assert.Contains("\"Url\": \"http://localhost:5123\"", settings);
        Assert.DoesNotContain("\"Https\"", settings);
        System.Text.Json.JsonDocument.Parse(settings).Dispose();

        project.Meta.UseHttps = true;
        project.Meta.HttpsPort = 5124;
        settings = Generate(project)["appsettings.json"];
        Assert.Contains("\"Url\": \"https://localhost:5124\"", settings);
        System.Text.Json.JsonDocument.Parse(settings).Dispose();
    }

    [Fact]
    public void UnknownLogLevelFallsBackToInformation()
    {
        var project = OneTable(_ => { });
        project.Meta.LogLevel = "Loud\"; evil";
        project.Meta.HttpPort = 6000;

        var settings = Generate(project)["appsettings.json"];

        Assert.Contains("\"Default\": \"Information\"", settings);
        Assert.DoesNotContain("evil", settings);
    }

    [Fact]
    public void HttpsHstsCompressionAndHealthAreAddedToProgram()
    {
        var project = OneTable(_ => { });
        project.Meta.UseHttps = true;
        project.Meta.EnableHsts = true;
        project.Meta.EnableHealthEndpoint = true;

        var program = Generate(project)["Program.cs"];

        Assert.Contains("app.UseHsts();", program);
        Assert.Contains("app.UseHttpsRedirection();", program);
        Assert.Contains("builder.Services.AddResponseCompression();", program);
        Assert.Contains("app.UseResponseCompression();", program);
        Assert.Contains("app.MapGet(\"/health\"", program);
        Assert.Contains("using Microsoft.Data.Sqlite;", program);
        Assert.Contains("SELECT 1", program);

        project.Meta.EnableResponseCompression = false;
        Assert.DoesNotContain("Compression", Generate(project)["Program.cs"]);
    }

    [Fact]
    public void HealthEndpointStaysAnonymousWithLogin()
    {
        var project = CustomizationSamples.Create("secure");
        project.Meta.EnableHealthEndpoint = true;

        var program = Generate(project)["Program.cs"];

        Assert.Contains("app.MapGet(\"/health\"", program);
        Assert.DoesNotContain("secured.MapGet(\"/health\"", program);
    }

    [Fact]
    public void AssemblyNameNamespaceVersionAndDescriptionGoToTheCsproj()
    {
        var project = OneTable(_ => { });
        project.Meta.AssemblyName = "Acme.Shop";
        project.Meta.RootNamespace = "Acme.Web";
        project.Meta.Version = "2.1.0";
        project.Meta.Description = "A & B <shop>";
        project.Meta.Author = "Ann";
        project.Meta.Company = "Acme";
        project.Meta.Copyright = "(c) Acme";

        var csproj = Generate(project)["Shop.csproj"];

        Assert.Contains("<AssemblyName>Acme.Shop</AssemblyName>", csproj);
        Assert.Contains("<RootNamespace>Acme.Web</RootNamespace>", csproj);
        Assert.Contains("<Version>2.1.0</Version>", csproj);
        Assert.Contains("<Description>A &amp; B &lt;shop&gt;</Description>", csproj);
        Assert.Contains("<Authors>Ann</Authors>", csproj);
        Assert.Contains("<Company>Acme</Company>", csproj);
        Assert.Contains("<Copyright>(c) Acme</Copyright>", csproj);
        System.Xml.Linq.XDocument.Parse(csproj);
    }

    [Fact]
    public void DockerEntryPointUsesTheConfiguredAssemblyName()
    {
        var project = OneTable(_ => { });
        project.Meta.AssemblyName = "Acme.Shop";

        Assert.Contains("ENTRYPOINT [\"dotnet\", \"Acme.Shop.dll\"]", WebAppForge.Core.Build.DockerGenerator.Dockerfile(project));
        project.Meta.AssemblyName = string.Empty;
        Assert.Contains("ENTRYPOINT [\"dotnet\", \"Shop.dll\"]", WebAppForge.Core.Build.DockerGenerator.Dockerfile(project));
    }

    [Fact]
    public void FaviconAndLogoAreCopiedToWwwrootAndReferenced()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"cust-{Guid.NewGuid():N}");
        var output = Path.Combine(folder, "out");
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "assets"));
            File.WriteAllBytes(Path.Combine(folder, "assets", "icon.ICO"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(folder, "assets", "brand.png"), [4, 5, 6]);
            var project = OneTable(_ => { });
            project.Meta.FaviconPath = "assets/icon.ICO";
            project.Meta.LogoPath = "assets/brand.png";

            var written = new ProjectGenerator().Generate(project, output, folder);

            Assert.Contains("wwwroot/favicon.ico", written);
            Assert.Contains("wwwroot/logo.png", written);
            Assert.Equal<byte>([1, 2, 3], File.ReadAllBytes(Path.Combine(output, "wwwroot", "favicon.ico")));
            Assert.Equal<byte>([4, 5, 6], File.ReadAllBytes(Path.Combine(output, "wwwroot", "logo.png")));
            var layout = File.ReadAllText(Path.Combine(output, "Web", "Html.cs"));
            Assert.Contains("<link rel=\\\"icon\\\" href=\\\"/favicon.ico\\\">", layout);
            Assert.Contains("<img src=\\\"/logo.png\\\"", layout);
            Assert.Contains("app.UseStaticFiles();", File.ReadAllText(Path.Combine(output, "Program.cs")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void MissingFaviconFileIsReported()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"cust-{Guid.NewGuid():N}");
        try
        {
            var project = OneTable(_ => { });
            project.Meta.FaviconPath = "nope.ico";

            var ex = Assert.Throws<FileNotFoundException>(() => new ProjectGenerator().Generate(project, Path.Combine(folder, "out"), folder));

            Assert.Contains("nope.ico", ex.Message);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void FooterGlobalCssGlobalScriptColorModeAndNavbarStyleGoToTheLayout()
    {
        var project = OneTable(_ => { });
        project.Meta.FooterText = "(c) <Acme> \"Ltd\"";
        project.Meta.GlobalCss = "body { color: \"red\"; }";
        project.Meta.GlobalScript = "console.log(\"hi\");";
        project.Meta.ColorMode = "dark";
        project.Meta.NavbarStyle = "primary";

        var layout = Generate(project)["Web/Html.cs"];

        // The footer text is encoded; CSS and script are raw by contract, and escaped only as C# string literals.
        Assert.Contains("<footer class=\\\"container text-muted small py-4\\\">(c) &lt;Acme&gt; &quot;Ltd&quot;</footer>", layout);
        Assert.Contains("<style>body { color: \\\"red\\\"; }</style>", layout);
        Assert.Contains("<script>console.log(\\\"hi\\\");</script>", layout);
        Assert.Contains("<html lang=\\\"en\\\" data-bs-theme=\\\"dark\\\">", layout);
        Assert.Contains("navbar navbar-expand bg-primary mb-4\\\" data-bs-theme=\\\"dark\\\"", layout);
    }

    [Fact]
    public void AutoColorModeAndLightNavbarKeepTheOriginalLayout()
    {
        var project = OneTable(_ => { });
        project.Meta.Author = "Ann"; // any metadata change, but nothing that touches the layout

        var layout = Generate(project)["Web/Html.cs"];

        Assert.Contains("<html lang=\\\"en\\\"><head>", layout);
        Assert.Contains("navbar navbar-expand bg-body-tertiary mb-4", layout);
        Assert.DoesNotContain("<footer", layout);
    }

    // ---- the whole thing compiles ---------------------------------------------------------------

    [Fact]
    public async Task AFullyCustomizedSecureLocalizedSiteBuilds()
    {
        var project = CustomizationSamples.Create("localized");
        project.Name = "CustomShop";
        CustomizationSamples.Customize(project);
        var root = Path.Combine(Path.GetTempPath(), $"cust-{Guid.NewGuid():N}");
        try
        {
            new ProjectGenerator().Generate(project, Path.Combine(root, "site"), root);

            var info = new ProcessStartInfo("dotnet", "build --nologo -v q -nodeReuse:false")
            {
                WorkingDirectory = Path.Combine(root, "site"),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            await process.WaitForExitAsync(timeout.Token);
            Assert.True(process.ExitCode == 0, await output + await error);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static Dictionary<string, string> Generate(Project project, string folder = "C:/pf")
    {
        var output = Path.Combine(Path.GetTempPath(), $"cust-{Guid.NewGuid():N}");
        try
        {
            new ProjectGenerator().Generate(project, output, folder);
            return Directory.GetFiles(output, "*", SearchOption.AllDirectories)
                .ToDictionary(f => Path.GetRelativePath(output, f).Replace('\\', '/'), File.ReadAllText);
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n")))).ToLowerInvariant()[..16];

    private static string Pages(Dictionary<string, string> files) => files["Pages/ItemsPages.cs"];

    private static int CountOf(string text, string part) => text.Split(part).Length - 1;

    private static FieldInfo Field(string name, FieldType type, bool required = false) =>
        new() { Name = name, Type = type, IsRequired = required };

    /// <summary>A project "shop" with one table "items" that has an auto-increment key plus whatever the test adds.</summary>
    private static Project OneTable(Action<TableInfo> configure)
    {
        var project = new Project { Name = "shop", ProjectId = Guid.Parse("095F6728-DF53-4763-A372-D8F2EAC959F2") };
        project.Database.Path = "shop.db";
        var table = new TableInfo { Name = "items" };
        table.Fields.Add(new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true, IsAutoIncrement = true, IsRequired = true });
        configure(table);
        project.Tables.Add(table);
        return project;
    }

    private static Project MenuProject()
    {
        var project = new Project { Name = "menu", ProjectId = Guid.Parse("095F6728-DF53-4763-A372-D8F2EAC959F2") };
        project.Database.Path = "menu.db";
        TableInfo Make(string name, string group, int order, bool show = true, string icon = "")
        {
            var table = new TableInfo { Name = name };
            table.Fields.Add(new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true, IsAutoIncrement = true, IsRequired = true });
            table.Ui.MenuGroup = group;
            table.Ui.MenuOrder = order;
            table.Ui.ShowInMenu = show;
            table.Ui.Icon = icon;
            return table;
        }

        project.Tables.Add(Make("delta", "Admin", 5));
        project.Tables.Add(Make("alpha", string.Empty, 0));
        project.Tables.Add(Make("bravo", "Sales", 1, icon: "cart"));
        project.Tables.Add(Make("echo", "Sales", 0, show: false));
        project.Tables.Add(Make("charlie", "Sales", 1));
        foreach (var table in project.Tables)
        {
            table.Caption = char.ToUpperInvariant(table.Name[0]) + table.Name[1..];
        }

        return project;
    }

    /// <summary>The innermost guard (for example "if (!edit)") around a statement in BindForm, or empty when there is none.</summary>
    private static string BindGuard(string pages, string statement)
    {
        var lines = pages[pages.IndexOf("private static void BindForm", StringComparison.Ordinal)..].Split('\n');
        var index = Array.FindIndex(lines, l => l.Contains(statement));
        Assert.True(index >= 0, "statement not found: " + statement);
        for (var i = index - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("if (", StringComparison.Ordinal) && !line.EndsWith(';') && !line.Contains("errors.Add"))
            {
                return line;
            }
        }

        return string.Empty;
    }
}

/// <summary>The three sample projects whose pre-customization output is recorded in <see cref="CustomizationGolden"/>.</summary>
internal static class CustomizationSamples
{
    public static Project Create(string name) => name switch
    {
        "plain" => Plain(),
        "secure" => Secure(),
        "localized" => Localized(),
        _ => throw new ArgumentException(name),
    };

    private static FieldInfo F(string name, FieldType type, bool pk = false, bool auto = false, bool required = false) =>
        new() { Name = name, Type = type, IsPrimaryKey = pk, IsAutoIncrement = auto, IsRequired = required };

    private static Project Plain()
    {
        var project = new Project { Name = "Shop Demo", ProjectId = Guid.Parse("095F6728-DF53-4763-A372-D8F2EAC959F2") };
        project.Database.Path = "shop.db";
        var items = new TableInfo { Name = "order_items", Caption = "Order \"Items\"", Filter = "qty > 0" };
        items.Fields.Add(F("id", FieldType.Integer, true, true, true));
        items.Fields.Add(F("sku", FieldType.Text, required: true));
        items.Fields.Add(F("qty", FieldType.Integer, required: true));
        items.Fields.Add(F("price", FieldType.Decimal));
        items.Fields.Add(F("shipped", FieldType.Boolean, required: true));
        items.Fields.Add(F("due", FieldType.Date));
        items.Fields.Add(F("created", FieldType.DateTime, required: true));
        items.Fields.Add(F("blob", FieldType.Binary));
        items.Sort.Add(new SortField { Field = "sku", Descending = true });
        var codes = new TableInfo { Name = "codes" };
        codes.Fields.Add(F("code", FieldType.Text, true, required: true));
        codes.Fields.Add(F("label", FieldType.Text));
        var lines = new TableInfo { Name = "order_lines" };
        lines.Fields.Add(F("order_id", FieldType.Integer, true, required: true));
        lines.Fields.Add(F("line_no", FieldType.Integer, true, required: true));
        lines.Fields.Add(F("note", FieldType.Text));
        var log = new TableInfo { Name = "audit_log" };
        log.Fields.Add(F("msg", FieldType.Text));
        project.Tables.AddRange([items, codes, lines, log]);
        return project;
    }

    private static Project Secure()
    {
        var project = Plain();
        project.Name = "SecureShop";
        var users = new TableInfo { Name = "users" };
        users.Fields.Add(F("id", FieldType.Integer, true, true, true));
        users.Fields.Add(F("username", FieldType.Text, required: true));
        users.Fields.Add(F("password_hash", FieldType.Text, required: true));
        users.Fields.Add(F("level", FieldType.Integer, required: true));
        project.Tables.Insert(0, users);
        var notes = new TableInfo { Name = "notes", OwnerField = "owner", ViewMinLevel = 1, EditMinLevel = 2 };
        notes.Fields.Add(F("id", FieldType.Integer, true, true, true));
        notes.Fields.Add(F("owner", FieldType.Text, required: true));
        notes.Fields.Add(F("body", FieldType.Text));
        project.Tables.Add(notes);
        project.Security.Enabled = true;
        project.Security.UserTable = "users";
        project.Security.UserNameField = "username";
        project.Security.PasswordField = "password_hash";
        project.Security.UserLevelField = "level";
        project.Security.AllowRegistration = true;
        return project;
    }

    private static Project Localized()
    {
        var project = Secure();
        project.Name = "LocShop";
        project.Localization.Enabled = true;
        project.Localization.DefaultLanguage = "en";
        project.Localization.Languages.Add("de");
        return project;
    }

    /// <summary>Sets a bit of everything on a project built from the samples, for the build test.</summary>
    public static void Customize(Project project)
    {
        var categories = new TableInfo { Name = "categories", Caption = "Categories" };
        categories.Fields.Add(F("id", FieldType.Integer, true, true, true));
        categories.Fields.Add(F("name", FieldType.Text, required: true));
        categories.Ui.MenuGroup = "Catalog";
        categories.Ui.Icon = "tags";
        var products = new TableInfo { Name = "products", Caption = "Products" };
        var name = F("name", FieldType.Text, required: true);
        name.Ui.MaxLength = 20;
        name.Ui.MinLength = 2;
        name.Ui.Pattern = "[A-Za-z ]+";
        name.Ui.PatternMessage = "Letters only";
        name.Ui.RequiredMessage = "Name needed";
        name.Ui.Group = "Basics";
        name.Ui.Align = TextAlign.Center;
        var category = F("category_id", FieldType.Integer);
        category.Ui.Lookup = new FieldLookup { Table = "categories", KeyField = "id", DisplayField = "name", Filter = "id > 0" };
        category.Ui.Group = "Basics";
        var status = F("status", FieldType.Text);
        status.Ui.Control = EditControl.Radio;
        status.Ui.Options.Add(new FieldOption { Value = "n", Text = "New" });
        status.Ui.Options.Add(new FieldOption { Value = "u", Text = "Used" });
        status.Ui.DefaultValue = "n";
        status.Ui.BadgeRules.Add(new ValueRule { Value = "n", Style = "success", Text = "Brand new" });
        var price = F("price", FieldType.Decimal);
        price.Ui.Format = ViewFormat.Currency;
        price.Ui.Min = "0";
        price.Ui.DefaultValue = "9.5";
        var qty = F("qty", FieldType.Integer, required: true);
        qty.Ui.Min = "1";
        qty.Ui.ReadOnlyOnEdit = true;
        qty.Ui.Sortable = false;
        var email = F("email", FieldType.Text);
        email.Ui.Control = EditControl.Email;
        email.Ui.Format = ViewFormat.EmailLink;
        var notes = F("notes", FieldType.Text);
        notes.Ui.Control = EditControl.TextArea;
        notes.Ui.Format = ViewFormat.MultilineText;
        var due = F("due", FieldType.Date);
        due.Ui.Format = ViewFormat.Date;
        due.Ui.Min = "2020-01-01";
        due.Ui.DefaultValue = "today";
        var active = F("active", FieldType.Boolean, required: true);
        active.Ui.TrueText = "On";
        active.Ui.FalseText = "Off";
        var secret = F("secret", FieldType.Text);
        secret.Ui.Control = EditControl.Password;
        var shade = F("shade", FieldType.Text);
        shade.Ui.Control = EditControl.Color;
        var hidden = F("hid", FieldType.Text);
        hidden.Ui.Control = EditControl.Hidden;
        var readOnly = F("ro", FieldType.Text);
        readOnly.Ui.Control = EditControl.ReadOnly;
        var at = F("at", FieldType.DateTime);
        at.Ui.Control = EditControl.Time;
        at.Ui.Format = ViewFormat.Time;
        var rating = F("rating", FieldType.Integer);
        rating.Ui.Control = EditControl.Select;
        rating.Ui.Options.Add(new FieldOption { Value = "1", Text = "One" });
        products.Fields.Add(F("id", FieldType.Integer, true, true, true));
        products.Fields.AddRange([name, category, status, price, qty, email, notes, due, active, secret, shade, hidden, readOnly, at, rating]);
        products.Ui.Description = "All <products>";
        products.Ui.Icon = "box";
        products.Ui.MenuOrder = 2;
        products.Ui.RowStyles.Add(new RowStyleRule { Field = "active", Value = "1", Style = "danger" });
        project.Tables.Insert(0, categories);
        project.Tables.Insert(1, products);
        project.Tables.First(t => t.Name == "codes").Ui.MenuGroup = "Sales";
        project.Tables.First(t => t.Name == "audit_log").Ui.ShowInMenu = false;

        var meta = project.Meta;
        meta.Version = "2.1.0";
        meta.AssemblyName = "MyShop";
        meta.HttpPort = 5123;
        meta.UseHttps = true;
        meta.EnableHsts = true;
        meta.EnableHealthEndpoint = true;
        meta.LogLevel = "Warning";
        meta.FooterText = "Footer";
        meta.GlobalCss = "body{color:#111}";
        meta.GlobalScript = "console.log(1)";
        meta.ColorMode = "dark";
        meta.NavbarStyle = "dark";
    }
}
