using System.Linq;
using System.Threading.Tasks;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Tests.TestSupport;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Tests;

public class LevelSecurityTests
{
    /// <summary>Users (generated): UserName and PasswordHash text, Level and Credits integer. Notes (generated) has no login fields.</summary>
    private static MainViewModel ProjectWithUsers()
    {
        var main = new MainViewModel(new FakeDialogService());

        var users = new TableInfo { Name = "Users", Generate = true };
        users.Fields.Add(new FieldInfo { Name = "UserName", Type = FieldType.Text });
        users.Fields.Add(new FieldInfo { Name = "PasswordHash", Type = FieldType.Text });
        users.Fields.Add(new FieldInfo { Name = "Level", Type = FieldType.Integer });
        users.Fields.Add(new FieldInfo { Name = "Credits", Type = FieldType.Integer });
        main.Current.Tables.Add(users);

        var notes = new TableInfo { Name = "Notes", Generate = true };
        notes.Fields.Add(new FieldInfo { Name = "Id", Type = FieldType.Integer, IsPrimaryKey = true });
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
    public void LevelPickerListsNoneThenIntegerFieldsOnly()
    {
        var main = ProjectWithUsers();

        main.Security.UserTable = "Users";

        Assert.Equal(new[] { "(none)", "Level", "Credits" }, main.Security.UserLevelFields);
        Assert.Equal(new[] { "(none)", "Level", "Credits" }, SecurityViewModel.LevelFieldChoices(main.Current.Tables[0]));
    }

    [Fact]
    public void LevelPickerIsNoneOnlyWithoutAUserTable()
    {
        var main = ProjectWithUsers();

        Assert.Equal(new[] { "(none)" }, main.Security.UserLevelFields);
    }

    [Fact]
    public void ChoosingAnIntegerFieldWritesUserLevelField()
    {
        var main = ProjectWithUsers();
        EnableLogin(main);

        main.Security.LevelField = "Level";

        Assert.Equal("Level", main.Current.Security.UserLevelField);
        Assert.Equal("Level", main.Security.LevelField);
        Assert.Null(main.Security.ValidationMessage);
    }

    [Fact]
    public void ChoosingNoneClearsUserLevelField()
    {
        var main = ProjectWithUsers();
        EnableLogin(main);
        main.Security.LevelField = "Level";

        main.Security.LevelField = "(none)";

        Assert.Equal(string.Empty, main.Current.Security.UserLevelField);
        Assert.Equal("(none)", main.Security.LevelField);
        Assert.Null(main.Security.ValidationMessage);
    }

    [Fact]
    public void NonIntegerLevelFieldIsRejectedWithTheGeneratorMessage()
    {
        var main = ProjectWithUsers();
        EnableLogin(main);

        // A project file can name a field that the picker would not offer; the validation still has to catch it.
        main.Current.Security.UserLevelField = "UserName";
        main.Security.Refresh();

        Assert.Equal(
            "The user level field 'UserName' in table 'Users' must be an integer column, but it is Text.",
            main.Security.ValidationMessage);
        Assert.False(main.Security.CanGenerate);
    }

    [Fact]
    public void MissingLevelFieldNamesTableAndField()
    {
        var main = ProjectWithUsers();
        EnableLogin(main);

        main.Security.LevelField = "Rank";

        Assert.Equal(
            "User level security is enabled but table 'Users' has no user level field 'Rank'.",
            main.Security.ValidationMessage);
        Assert.False(main.Security.CanGenerate);
    }

    [Fact]
    public void LevelFieldIsIgnoredWhenLoginIsOff()
    {
        var main = ProjectWithUsers();
        main.Security.UserTable = "Users";
        main.Security.LevelField = "Rank";

        Assert.Null(main.Security.ValidationMessage);
        Assert.True(main.Security.CanGenerate);
    }

    [Fact]
    public void LevelFieldIsCheckedAgainstTheChosenUserTable()
    {
        var main = ProjectWithUsers();
        var notes = main.Current.Tables.Single(t => t.Name == "Notes");
        notes.Fields.Add(new FieldInfo { Name = "UserName", Type = FieldType.Text });
        notes.Fields.Add(new FieldInfo { Name = "PasswordHash", Type = FieldType.Text });
        EnableLogin(main);
        main.Security.LevelField = "Level";

        main.Security.UserTable = "Notes";

        Assert.Equal(
            "User level security is enabled but table 'Notes' has no user level field 'Level'.",
            main.Security.ValidationMessage);
    }

    [Theory]
    [InlineData("0", true, 0)]
    [InlineData("7", true, 7)]
    [InlineData(" 12 ", true, 12)]
    [InlineData("-1", false, 0)]
    [InlineData("2.5", false, 0)]
    [InlineData("abc", false, 0)]
    [InlineData("", false, 0)]
    [InlineData(null, false, 0)]
    public void LevelParseAcceptsWholeNumbersZeroOrMore(string? input, bool expected, int value)
    {
        var ok = TablesViewModel.TryParseLevel(input, out var parsed);

        Assert.Equal(expected, ok);
        if (expected)
        {
            Assert.Equal(value, parsed);
        }
    }

    [Fact]
    public void ViewLevelRejectsInvalidInputAndKeepsLastValidValue()
    {
        var main = ProjectWithUsers();
        var notes = main.Current.Tables.Single(t => t.Name == "Notes");
        Assert.True(main.Tables.SetViewLevel(notes, "2"));

        var stored = main.Tables.SetViewLevel(notes, "-1");

        Assert.False(stored);
        Assert.Equal(2, notes.ViewMinLevel);
        Assert.Equal("View level must be a whole number, 0 or more. The last valid value was kept.", main.Status);
    }

    [Fact]
    public void EditLevelRejectsInvalidInputAndKeepsLastValidValue()
    {
        var main = ProjectWithUsers();
        var notes = main.Current.Tables.Single(t => t.Name == "Notes");
        Assert.True(main.Tables.SetEditLevel(notes, "5"));

        Assert.False(main.Tables.SetEditLevel(notes, "x"));

        Assert.Equal(5, notes.EditMinLevel);
        Assert.Equal("Edit level must be a whole number, 0 or more. The last valid value was kept.", main.Status);
    }

    [Fact]
    public void ZeroIsAcceptedAsAnyLoggedInUser()
    {
        var main = ProjectWithUsers();
        var notes = main.Current.Tables.Single(t => t.Name == "Notes");
        Assert.True(main.Tables.SetViewLevel(notes, "3"));

        Assert.True(main.Tables.SetViewLevel(notes, "0"));

        Assert.Equal(0, notes.ViewMinLevel);
    }

    [Fact]
    public async Task UserLevelFieldAndTableLevelsSurviveSaveAndLoad()
    {
        using var folder = new TempFolder();
        var main = await Harness.SavedProjectAsync(folder);
        main.Current.Tables.Add(new TableInfo { Name = "Users", Generate = true, Fields = { new FieldInfo { Name = "UserName", Type = FieldType.Text }, new FieldInfo { Name = "PasswordHash", Type = FieldType.Text }, new FieldInfo { Name = "Level", Type = FieldType.Integer } } });
        main.Current.Tables.Add(new TableInfo { Name = "Notes", Generate = true, Fields = { new FieldInfo { Name = "Id", Type = FieldType.Integer, IsPrimaryKey = true } } });
        main.Tables.Reload();

        EnableLogin(main);
        main.Security.LevelField = "Level";
        var notes = main.Current.Tables.Single(t => t.Name == "Notes");
        main.Tables.SetViewLevel(notes, "3");
        main.Tables.SetEditLevel(notes, "5");

        await main.SaveProjectCommand.ExecuteAsync(null);
        var loaded = ProjectFile.Load(folder.File("project.xml"));

        Assert.Equal("Level", loaded.Security.UserLevelField);
        var loadedNotes = loaded.Tables.Single(t => t.Name == "Notes");
        Assert.Equal(3, loadedNotes.ViewMinLevel);
        Assert.Equal(5, loadedNotes.EditMinLevel);
    }
}
