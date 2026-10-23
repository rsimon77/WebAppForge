using System.Threading.Tasks;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Tests.TestSupport;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Tests;

public class SecurityTests
{
    /// <summary>An in-memory project with a generated Users table (UserName, PasswordHash) and a table that is not generated.</summary>
    private static MainViewModel ProjectWithUsers()
    {
        var main = new MainViewModel(new FakeDialogService());

        var users = new TableInfo { Name = "Users", Generate = true };
        users.Fields.Add(new FieldInfo { Name = "UserName" });
        users.Fields.Add(new FieldInfo { Name = "PasswordHash" });
        main.Current.Tables.Add(users);

        var audit = new TableInfo { Name = "Audit", Generate = false };
        audit.Fields.Add(new FieldInfo { Name = "Id", IsPrimaryKey = true });
        main.Current.Tables.Add(audit);

        main.Tables.Reload();
        return main;
    }

    [Fact]
    public void EnabledWithGeneratedUserTableAndFieldsHasNoError()
    {
        var main = ProjectWithUsers();

        main.Security.Enabled = true;
        main.Security.UserTable = "Users";
        main.Security.UserNameField = "UserName";
        main.Security.PasswordField = "PasswordHash";

        Assert.Null(main.Security.ValidationMessage);
        Assert.True(main.Security.CanGenerate);
        Assert.Equal(new[] { "Users" }, main.Security.UserTables);
        Assert.Equal(new[] { "UserName", "PasswordHash" }, main.Security.UserNameFields);
        Assert.Equal(new[] { "UserName", "PasswordHash" }, main.Security.PasswordFields);
    }

    [Fact]
    public void EnabledWithNonGeneratedTableNamesTheTable()
    {
        var main = ProjectWithUsers();

        main.Security.Enabled = true;
        main.Security.UserTable = "Audit";
        main.Security.UserNameField = "Id";
        main.Security.PasswordField = "Id";

        Assert.Contains("'Audit'", main.Security.ValidationMessage);
        Assert.Contains("not one of the generated tables", main.Security.ValidationMessage);
        Assert.False(main.Security.CanGenerate);
        Assert.DoesNotContain("Audit", main.Security.UserTables);
    }

    [Fact]
    public void EnabledWithMissingPasswordFieldNamesTheField()
    {
        var main = ProjectWithUsers();

        main.Security.Enabled = true;
        main.Security.UserTable = "Users";
        main.Security.UserNameField = "UserName";
        main.Security.PasswordField = "Secret";

        Assert.Equal(
            "Security is enabled but table 'Users' has no password field 'Secret'.",
            main.Security.ValidationMessage);
        Assert.False(main.Security.CanGenerate);
    }

    [Fact]
    public void EnabledWithMissingUserNameFieldNamesTheField()
    {
        var main = ProjectWithUsers();

        main.Security.Enabled = true;
        main.Security.UserTable = "Users";
        main.Security.UserNameField = "Login";
        main.Security.PasswordField = "PasswordHash";

        Assert.Equal(
            "Security is enabled but table 'Users' has no user name field 'Login'.",
            main.Security.ValidationMessage);
    }

    [Fact]
    public void FieldAndTableNamesMatchIgnoringCase()
    {
        var main = ProjectWithUsers();

        main.Security.Enabled = true;
        main.Security.UserTable = "users";
        main.Security.UserNameField = "username";
        main.Security.PasswordField = "PASSWORDHASH";

        Assert.Null(main.Security.ValidationMessage);
    }

    [Fact]
    public void DisabledLoginNeverReportsAnError()
    {
        var main = ProjectWithUsers();

        main.Security.UserTable = "Audit";
        main.Security.PasswordField = "Missing";

        Assert.False(main.Current.Security.Enabled);
        Assert.Null(main.Security.ValidationMessage);
        Assert.True(main.Security.CanGenerate);
    }

    [Fact]
    public void ValidateReturnsTheSameMessageAsTheGeneratorRule()
    {
        var tables = new[]
        {
            new TableInfo { Name = "Users", Generate = true, Fields = { new FieldInfo { Name = "UserName" } } },
        };
        var security = new SecuritySettings { Enabled = true, UserTable = "Users", UserNameField = "UserName", PasswordField = "" };

        Assert.Equal(
            "Choose the password field. Login is enabled but no field is set.",
            SecurityViewModel.Validate(security, tables));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("2.5")]
    public void MaxFailedAttemptsRejectsNonPositiveInput(string input)
    {
        var main = ProjectWithUsers();

        main.Security.MaxFailedAttemptsText = input;

        Assert.Equal(5, main.Current.Security.MaxFailedAttempts);
        Assert.Contains("Max failed attempts must be a positive whole number", main.Status);
        Assert.True(main.Security.HasInputMessage);
    }

    [Fact]
    public void WindowMinutesRejectsZeroAndAcceptsPositive()
    {
        var main = ProjectWithUsers();

        main.Security.FailedAttemptsWindowText = "0";
        Assert.Equal(10, main.Current.Security.FailedAttemptsWindowMinutes);
        Assert.Contains("positive whole number", main.Status);

        main.Security.FailedAttemptsWindowText = "15";
        Assert.Equal(15, main.Current.Security.FailedAttemptsWindowMinutes);
        Assert.False(main.Security.HasInputMessage);
    }

    [Fact]
    public async Task GenerateIsBlockedWhileLoginSettingsAreInvalid()
    {
        using var folder = new TempFolder();
        var main = await Harness.SavedProjectAsync(folder);
        main.Current.Tables.Add(new TableInfo { Name = "Users", Generate = true, Fields = { new FieldInfo { Name = "UserName" }, new FieldInfo { Name = "PasswordHash" } } });
        main.Tables.Reload();
        main.Security.Enabled = true;
        main.Security.UserTable = "Users";
        main.Security.UserNameField = "UserName";
        main.Security.PasswordField = "Secret";

        await main.Generate.GenerateCommand.ExecuteAsync(null);

        Assert.Equal(
            "Generation blocked: Security is enabled but table 'Users' has no password field 'Secret'.",
            main.Status);
        Assert.False(main.Security.CanGenerate);
        Assert.False(Directory.Exists(folder.File("generated")));
    }

    [Fact]
    public async Task SecuritySettingsPersistThroughSaveAndLoad()
    {
        using var folder = new TempFolder();
        var main = await Harness.SavedProjectAsync(folder);
        main.Current.Tables.Add(new TableInfo { Name = "Users", Generate = true, Fields = { new FieldInfo { Name = "UserName" }, new FieldInfo { Name = "PasswordHash" } } });
        main.Tables.Reload();

        main.Security.Enabled = true;
        main.Security.UserTable = "Users";
        main.Security.UserNameField = "UserName";
        main.Security.PasswordField = "PasswordHash";
        main.Security.TrackFailedAttempts = false;
        main.Security.MaxFailedAttemptsText = "3";
        main.Security.FailedAttemptsWindowText = "30";

        await main.SaveProjectCommand.ExecuteAsync(null);
        var loaded = ProjectFile.Load(folder.File("project.xml")).Security;

        Assert.True(loaded.Enabled);
        Assert.Equal("Users", loaded.UserTable);
        Assert.Equal("UserName", loaded.UserNameField);
        Assert.Equal("PasswordHash", loaded.PasswordField);
        Assert.False(loaded.TrackFailedAttempts);
        Assert.Equal(3, loaded.MaxFailedAttempts);
        Assert.Equal(30, loaded.FailedAttemptsWindowMinutes);
    }

    [Fact]
    public void RegistrationAndChangePasswordTogglesWriteThroughToTheModel()
    {
        var main = ProjectWithUsers();

        main.Security.AllowRegistration = true;
        main.Security.AllowChangePassword = false;

        Assert.True(main.Current.Security.AllowRegistration);
        Assert.False(main.Current.Security.AllowChangePassword);

        main.Security.AllowRegistration = false;
        main.Security.AllowChangePassword = true;

        Assert.False(main.Current.Security.AllowRegistration);
        Assert.True(main.Current.Security.AllowChangePassword);
    }

    [Fact]
    public async Task RegistrationAndChangePasswordSurviveSaveAndLoad()
    {
        using var folder = new TempFolder();
        var main = await Harness.SavedProjectAsync(folder);
        main.Current.Tables.Add(new TableInfo { Name = "Users", Generate = true, Fields = { new FieldInfo { Name = "UserName" }, new FieldInfo { Name = "PasswordHash" } } });
        main.Tables.Reload();

        main.Security.Enabled = true;
        main.Security.UserTable = "Users";
        main.Security.UserNameField = "UserName";
        main.Security.PasswordField = "PasswordHash";
        main.Security.AllowRegistration = true;
        main.Security.AllowChangePassword = false;

        await main.SaveProjectCommand.ExecuteAsync(null);
        var loaded = ProjectFile.Load(folder.File("project.xml")).Security;

        Assert.True(loaded.AllowRegistration);
        Assert.False(loaded.AllowChangePassword);
    }

    [Fact]
    public void DisabledSecurityIgnoresRegistrationAndChangePasswordForValidation()
    {
        var main = ProjectWithUsers();

        main.Security.Enabled = false;
        main.Security.UserTable = "Audit";
        main.Security.PasswordField = "Missing";

        main.Security.AllowRegistration = true;
        main.Security.AllowChangePassword = false;

        Assert.Null(main.Security.ValidationMessage);
        Assert.True(main.Security.CanGenerate);
        Assert.Null(SecurityViewModel.Validate(main.Current.Security, main.Current.Tables));
    }
}
