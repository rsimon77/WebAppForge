using WebAppForge.Core.Model;
using WebAppForge.Core.Schema;

namespace WebAppForge.Core.Generation;

/// <summary>The rules of the ASP.NET Core Identity user store, shared by the generator and the editor.</summary>
public static class IdentityRules
{
    /// <summary>
    /// Every setting the generator refuses, as a message. Empty when login is off or the users are kept in a project table.
    /// </summary>
    public static IReadOnlyList<string> Problems(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var problems = new List<string>();
        if (!project.Security.UsesIdentity)
        {
            return problems;
        }

        var identity = project.Security.Identity;
        if (!IdentitySchema.IsValidPrefix(identity.TablePrefix))
        {
            problems.Add($"The identity table prefix '{identity.TablePrefix}' is not valid: use 1 to 30 letters, digits and underscores, starting with a letter.");
        }

        if (identity.MinPasswordLength is < 1 or > 128)
        {
            problems.Add("The minimum password length must be between 1 and 128.");
        }

        if (string.IsNullOrWhiteSpace(identity.AdminUserName))
        {
            problems.Add("Name the administrator the site creates when it has no users.");
        }

        if (!IsVariableName(identity.AdminPasswordVariable))
        {
            problems.Add($"'{identity.AdminPasswordVariable}' is not a valid environment variable name for the administrator's password: use letters, digits and underscores, not starting with a digit.");
        }

        if (identity.AdminLevel < 1)
        {
            problems.Add("The administrator level must be at least 1.");
        }

        if (identity.RegisteredUserLevel < 0 || identity.RegisteredUserLevel >= identity.AdminLevel)
        {
            problems.Add("The level of users who sign up must be 0 or more and lower than the administrator level.");
        }

        if (project.Security.MaxFailedAttempts < 1 || project.Security.FailedAttemptsWindowMinutes < 1)
        {
            problems.Add("The lockout needs at least one failed attempt and a lockout time of at least one minute.");
        }

        return problems;
    }

    /// <summary>Settings that have no effect with the identity store. These do not stop the generator.</summary>
    public static IReadOnlyList<string> Notes(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var notes = new List<string>();
        if (!project.Security.UsesIdentity)
        {
            return notes;
        }

        var security = project.Security;
        if (security.UserTable.Length > 0 || security.UserNameField.Length > 0 || security.PasswordField.Length > 0 || security.UserLevelField.Length > 0)
        {
            notes.Add("The user table, user name, password and user level fields are not used: ASP.NET Core Identity keeps the users.");
        }

        if (!security.Identity.CreateTablesAtStartup)
        {
            notes.Add("The site does not create the identity tables: they must exist in the database before it starts.");
        }

        return notes;
    }

    /// <summary>Throws on the first of <see cref="Problems"/>.</summary>
    public static void Validate(Project project)
    {
        if (Problems(project) is [var first, ..])
        {
            throw new InvalidOperationException(first);
        }
    }

    private static bool IsVariableName(string? name) =>
        name is { Length: > 0 } && !char.IsAsciiDigit(name[0]) && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
