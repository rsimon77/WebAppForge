using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

public class MasterDetailValidationTests
{
    private static Project Sample(Action<Project>? change = null)
    {
        var project = new Project { Name = "V" };
        project.Database.Path = "v.db";
        var a = new TableInfo { Name = "a" };
        a.Fields.Add(new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true, IsAutoIncrement = true, IsRequired = true });
        a.Fields.Add(new FieldInfo { Name = "code", Type = FieldType.Text });
        a.Fields.Add(new FieldInfo { Name = "stamp", Type = FieldType.Date });
        var b = new TableInfo { Name = "b" };
        b.Fields.Add(new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true, IsAutoIncrement = true, IsRequired = true });
        b.Fields.Add(new FieldInfo { Name = "a_id", Type = FieldType.Integer });
        b.Fields.Add(new FieldInfo { Name = "a_req", Type = FieldType.Integer, IsRequired = true });
        b.Fields.Add(new FieldInfo { Name = "up_id", Type = FieldType.Integer });
        project.Tables.AddRange([a, b]);
        a.Details.Add(MdSupport.Link("b", ("id", "a_id")));
        change?.Invoke(project);
        return project;
    }

    private static string Fail(Project project)
    {
        var root = MdSupport.TempFolder();
        try
        {
            return Assert.Throws<InvalidOperationException>(() => new ProjectGenerator().Generate(project, Path.Combine(root, "o"), root)).Message;
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }

    private static void Ok(Project project)
    {
        var root = MdSupport.TempFolder();
        try
        {
            new ProjectGenerator().Generate(project, Path.Combine(root, "o"), root);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }

    [Fact]
    public void AValidLinkGenerates() => Ok(Sample());

    [Fact]
    public void ADetailTableThatDoesNotExistIsRejected() =>
        Assert.Equal("Master/detail a -> zz: the detail table 'zz' does not exist.", Fail(Sample(p => p.Tables[0].Details[0].DetailTable = "zz")));

    [Fact]
    public void ADetailTableThatIsNotGeneratedIsRejected() =>
        Assert.Equal("Master/detail a -> b: the detail table 'b' is not generated (Generate is off).", Fail(Sample(p => p.Tables[1].Generate = false)));

    [Fact]
    public void AnEmptyDetailTableIsRejected() =>
        Assert.Equal("Master/detail a -> : choose the detail table.", Fail(Sample(p => p.Tables[0].Details[0].DetailTable = "")));

    [Fact]
    public void ALinkWithoutKeyLinksIsRejected() =>
        Assert.Equal("Master/detail a -> b: add at least one key link (master field = detail field).", Fail(Sample(p => p.Tables[0].Details[0].Links.Clear())));

    [Fact]
    public void ALinkFieldThatDoesNotExistIsRejected()
    {
        Assert.Equal("Master/detail a -> b: the master field 'nope' does not exist in table 'a'.", Fail(Sample(p => p.Tables[0].Details[0].Links[0].MasterField = "nope")));
        Assert.Equal("Master/detail a -> b: the detail field 'nope' does not exist in table 'b'.", Fail(Sample(p => p.Tables[0].Details[0].Links[0].DetailField = "nope")));
        Assert.Equal("Master/detail a -> b: key link 1 has no master field.", Fail(Sample(p => p.Tables[0].Details[0].Links[0].MasterField = "")));
        Assert.Equal("Master/detail a -> b: key link 1 has no detail field.", Fail(Sample(p => p.Tables[0].Details[0].Links[0].DetailField = "")));
    }

    [Fact]
    public void ADifferingFieldTypeDoesNotBlockGeneration() =>
        Ok(Sample(p => p.Tables[0].Details[0].Links[0].MasterField = "code"));

    [Fact]
    public void ARepeatedKeyLinkIsRejected() =>
        Assert.Equal("Master/detail a -> b: the key link 'id = a_id' is listed twice.", Fail(Sample(p => p.Tables[0].Details[0].Links.Add(new KeyLink { MasterField = "id", DetailField = "a_id" }))));

    [Fact]
    public void ADuplicateLinkIsRejected() =>
        Assert.Equal("Master/detail a -> b: duplicate of another link of 'a' to 'b' with the same key fields.",
            Fail(Sample(p => p.Tables[0].Details.Add(MdSupport.Link("b", ("id", "a_id"))))));

    [Fact]
    public void ADifferentKeyPairToTheSameDetailIsAllowed() =>
        Ok(Sample(p => p.Tables[0].Details.Add(MdSupport.Link("b", ("id", "up_id")))));

    [Fact]
    public void AColumnThatDoesNotExistIsRejected() =>
        Assert.Equal("Master/detail a -> b: the grid column 'zz' does not exist in table 'b'.", Fail(Sample(p => p.Tables[0].Details[0].Columns.Add("zz"))));

    [Fact]
    public void APageSizeOutsideTheRangeIsRejected()
    {
        Assert.Equal("Master/detail a -> b: the page size 0 must be from 1 to 200.", Fail(Sample(p => p.Tables[0].Details[0].PageSize = 0)));
        Assert.Equal("Master/detail a -> b: the page size 201 must be from 1 to 200.", Fail(Sample(p => p.Tables[0].Details[0].PageSize = 201)));
    }

    [Fact]
    public void AFormPageThatTheMasterDoesNotHaveIsRejected() =>
        Assert.Equal("Master/detail a -> b: the grid is placed on form page 'Lines', which 'a' does not have.", Fail(Sample(p => p.Tables[0].Details[0].FormPage = "Lines")));

    [Fact]
    public void SetNullNeedsNullableLinkFields()
    {
        Assert.Equal("Master/detail a -> b: on delete 'Set null' needs nullable link fields, but 'a_req' of 'b' is required.",
            Fail(Sample(p =>
            {
                p.Tables[0].Details[0].Links[0].DetailField = "a_req";
                p.Tables[0].Details[0].OnDelete = DeleteRule.SetNull;
            })));
        Assert.Contains("'id' of 'b' is required", Fail(Sample(p =>
        {
            p.Tables[0].Details[0].Links[0].DetailField = "id";
            p.Tables[0].Details[0].OnDelete = DeleteRule.SetNull;
        })));
        Ok(Sample(p => p.Tables[0].Details[0].OnDelete = DeleteRule.SetNull));
    }

    [Fact]
    public void ACascadeCycleIsRejected() =>
        Assert.StartsWith("Cascade delete cycle: a -> b -> a.", Fail(Sample(p =>
        {
            p.Tables[0].Details[0].OnDelete = DeleteRule.Cascade;
            var back = MdSupport.Link("a", ("up_id", "id"));
            back.OnDelete = DeleteRule.Cascade;
            p.Tables[1].Details.Add(back);
        })));

    [Fact]
    public void ASelfCascadeIsRejectedButASelfSetNullIsAllowed()
    {
        Assert.Contains("a table cannot cascade delete to itself", Fail(Sample(p =>
        {
            var self = MdSupport.Link("a", ("id", "id"));
            self.OnDelete = DeleteRule.Cascade;
            p.Tables[0].Details.Add(self);
        })));
        Ok(Sample(p =>
        {
            var self = MdSupport.Link("b", ("id", "up_id"));
            self.OnDelete = DeleteRule.SetNull;
            p.Tables[1].Details.Add(self);
        }));
    }

    [Fact]
    public void ACascadeIntoACycleThroughANonCascadeRuleIsAllowed() =>
        Ok(Sample(p =>
        {
            p.Tables[0].Details[0].OnDelete = DeleteRule.Cascade;
            var back = MdSupport.Link("a", ("up_id", "id"));
            back.OnDelete = DeleteRule.Restrict;
            p.Tables[1].Details.Add(back);
        }));

    [Fact]
    public void ShowOnAddNeedsTheEditPage() =>
        Assert.Contains("(ShowOnAdd) but the edit page of 'a' is not generated", Fail(Sample(p =>
        {
            p.Tables[0].Details[0].ShowOnAdd = true;
            p.Tables[0].Pages.Edit = false;
        })));

    [Fact]
    public void AMasterWithoutAPrimaryKeyIsRejected() =>
        Assert.Contains("the master table 'a' has no primary key", Fail(Sample(p => p.Tables[0].Fields[0].IsPrimaryKey = false)));

    [Fact]
    public void FormPagesNeedUniqueNames()
    {
        Assert.Equal("Table 'a': a form page has no name.", Fail(Sample(p => p.Tables[0].FormPages.Add(new FormPageDef { Name = " " }))));
        Assert.Equal("Table 'a': the form page 'p' exists twice.", Fail(Sample(p =>
        {
            p.Tables[0].FormPages.Add(new FormPageDef { Name = "P" });
            p.Tables[0].FormPages.Add(new FormPageDef { Name = "p" });
        })));
    }

    [Fact]
    public void LinksOfATableThatIsNotGeneratedAreNotChecked() =>
        Ok(Sample(p =>
        {
            p.Tables[0].Details[0].DetailTable = "zz";
            p.Tables[0].Generate = false;
        }));
}
