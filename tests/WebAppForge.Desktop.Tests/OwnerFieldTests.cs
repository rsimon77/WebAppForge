using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Tests.TestSupport;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Tests;

public class OwnerFieldTests
{
    /// <summary>Users (generated, UserName and PasswordHash) and Notes (generated, Id, Title text, Score integer, Owner text).</summary>
    private static MainViewModel ProjectWithNotes()
    {
        var main = new MainViewModel(new FakeDialogService());

        var users = new TableInfo { Name = "Users", Generate = true };
        users.Fields.Add(new FieldInfo { Name = "UserName", Type = FieldType.Text });
        users.Fields.Add(new FieldInfo { Name = "PasswordHash", Type = FieldType.Text });
        main.Current.Tables.Add(users);

        var notes = new TableInfo { Name = "Notes", Generate = true };
        notes.Fields.Add(new FieldInfo { Name = "Id", Type = FieldType.Integer, IsPrimaryKey = true });
        notes.Fields.Add(new FieldInfo { Name = "Title", Type = FieldType.Text });
        notes.Fields.Add(new FieldInfo { Name = "Score", Type = FieldType.Integer });
        notes.Fields.Add(new FieldInfo { Name = "Owner", Type = FieldType.Text });
        main.Current.Tables.Add(notes);

        main.Tables.Reload();
        return main;
    }

    private static void EnableLogin(MainViewModel main)
    {
        main.Security.Enabled = true;
        main.Security.UserTable = "Users";
        main.Security.UserNameField = "UserName";
        main.Security.PasswordField = "PasswordHash";
    }

    [Fact]
    public void PickerListsNoneThenTextFieldsOnly()
    {
        var main = ProjectWithNotes();
        var notes = main.Current.Tables.Single(t => t.Name == "Notes");

        Assert.Equal(new[] { "(none)", "Title", "Owner" }, OwnerFieldPicker.Choices(notes));
    }

    [Fact]
    public void ChoosingAFieldWritesOwnerField()
    {
        var main = ProjectWithNotes();
        var notes = main.Current.Tables.Single(t => t.Name == "Notes");

        main.Tables.SetOwnerField(notes, "Owner");

        Assert.Equal("Owner", notes.OwnerField);
        Assert.Equal("Owner", OwnerFieldPicker.Display(notes.OwnerField));
    }

    [Fact]
    public void ChoosingNoneClearsOwnerField()
    {
        var main = ProjectWithNotes();
        var notes = main.Current.Tables.Single(t => t.Name == "Notes");
        main.Tables.SetOwnerField(notes, "Owner");

        main.Tables.SetOwnerField(notes, OwnerFieldPicker.NoneLabel);

        Assert.Equal(string.Empty, notes.OwnerField);
        Assert.Equal(OwnerFieldPicker.NoneLabel, OwnerFieldPicker.Display(notes.OwnerField));
    }

    [Fact]
    public void NullChoiceAlsoClearsOwnerField()
    {
        var main = ProjectWithNotes();
        var notes = main.Current.Tables.Single(t => t.Name == "Notes");
        main.Tables.SetOwnerField(notes, "Owner");

        main.Tables.SetOwnerField(notes, null);

        Assert.Equal(string.Empty, notes.OwnerField);
    }

    [Fact]
    public void NonTextFieldIsNotOfferedAndIsRejectedWhenSecurityIsOn()
    {
        var main = ProjectWithNotes();
        var notes = main.Current.Tables.Single(t => t.Name == "Notes");
        EnableLogin(main);

        main.Tables.SetOwnerField(notes, "Score");

        Assert.DoesNotContain("Score", OwnerFieldPicker.Choices(notes));
        Assert.Equal(
            "The owner field 'Score' in table 'Notes' must be a text column to hold the user name, but it is Integer.",
            main.Security.ValidationMessage);
        Assert.False(main.Security.CanGenerate);
    }

    [Fact]
    public void MissingOwnerFieldNamesTableAndField()
    {
        var main = ProjectWithNotes();
        var notes = main.Current.Tables.Single(t => t.Name == "Notes");
        EnableLogin(main);

        main.Tables.SetOwnerField(notes, "Account");

        Assert.Equal(
            "User ID security is enabled for table 'Notes' but it has no owner field 'Account'.",
            main.Security.ValidationMessage);
        Assert.False(main.Security.CanGenerate);
    }

    [Fact]
    public void OwnerFieldProblemIsIgnoredWhenSecurityIsOff()
    {
        var main = ProjectWithNotes();
        var notes = main.Current.Tables.Single(t => t.Name == "Notes");

        main.Tables.SetOwnerField(notes, "Score");

        Assert.False(main.Current.Security.Enabled);
        Assert.Null(main.Security.ValidationMessage);
        Assert.True(main.Security.CanGenerate);
    }

    [Fact]
    public void ValidOwnerFieldHasNoMessageWhenSecurityIsOn()
    {
        var main = ProjectWithNotes();
        var notes = main.Current.Tables.Single(t => t.Name == "Notes");
        EnableLogin(main);

        main.Tables.SetOwnerField(notes, "Owner");

        Assert.Null(main.Security.ValidationMessage);
        Assert.True(main.Security.CanGenerate);
    }

    [Fact]
    public async Task GenerateIsBlockedWhileOwnerFieldIsInvalid()
    {
        using var folder = new TempFolder();
        var main = await Harness.SavedProjectAsync(folder);
        main.Current.Tables.Add(new TableInfo { Name = "Users", Generate = true, Fields = { new FieldInfo { Name = "UserName" }, new FieldInfo { Name = "PasswordHash" } } });
        var notes = new TableInfo { Name = "Notes", Generate = true, Fields = { new FieldInfo { Name = "Id", Type = FieldType.Integer, IsPrimaryKey = true }, new FieldInfo { Name = "Score", Type = FieldType.Integer } } };
        main.Current.Tables.Add(notes);
        main.Tables.Reload();
        EnableLogin(main);
        main.Tables.SetOwnerField(notes, "Score");

        await main.Generate.GenerateCommand.ExecuteAsync(null);

        Assert.Equal(
            "Generation blocked: The owner field 'Score' in table 'Notes' must be a text column to hold the user name, but it is Integer.",
            main.Status);
        Assert.False(Directory.Exists(folder.File("generated")));
    }

    [Fact]
    public async Task OwnerFieldRoundTripsThroughProjectFile()
    {
        using var folder = new TempFolder();
        var main = await Harness.SavedProjectAsync(folder);
        main.Current.Tables.Add(new TableInfo { Name = "Users", Generate = true, Fields = { new FieldInfo { Name = "UserName" }, new FieldInfo { Name = "PasswordHash" } } });
        var notes = new TableInfo { Name = "Notes", Generate = true, Fields = { new FieldInfo { Name = "Id", Type = FieldType.Integer, IsPrimaryKey = true }, new FieldInfo { Name = "Owner", Type = FieldType.Text } } };
        var other = new TableInfo { Name = "Other", Generate = true, Fields = { new FieldInfo { Name = "Id", Type = FieldType.Integer, IsPrimaryKey = true } } };
        main.Current.Tables.Add(notes);
        main.Current.Tables.Add(other);
        main.Tables.Reload();

        main.Tables.SetOwnerField(notes, "Owner");
        main.Tables.SetOwnerField(other, "Id");
        main.Tables.SetOwnerField(other, OwnerFieldPicker.NoneLabel);

        await main.SaveProjectCommand.ExecuteAsync(null);
        var loaded = ProjectFile.Load(folder.File("project.xml"));

        Assert.Equal("Owner", loaded.Tables.Single(t => t.Name == "Notes").OwnerField);
        Assert.Equal(string.Empty, loaded.Tables.Single(t => t.Name == "Other").OwnerField);
    }
}
