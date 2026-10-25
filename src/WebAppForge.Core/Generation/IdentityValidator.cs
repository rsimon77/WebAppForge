using WebAppForge.Core.Model;
using WebAppForge.Core.Schema;
using M = WebAppForge.Core.Localization.CoreMessages;

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
            problems.Add(M.Format("Identity_Prefix", identity.TablePrefix));
        }

        if (identity.MinPasswordLength is < 1 or > 128)
        {
            problems.Add(M.Text("Identity_PasswordLength"));
        }

        if (string.IsNullOrWhiteSpace(identity.AdminUserName))
        {
            problems.Add(M.Text("Identity_AdminName"));
        }

        if (!IsVariableName(identity.AdminPasswordVariable))
        {
            problems.Add(M.Format("Identity_AdminVariable", identity.AdminPasswordVariable));
        }

        if (identity.AdminLevel < 1)
        {
            problems.Add(M.Text("Identity_AdminLevel"));
        }

        if (identity.RegisteredUserLevel < 0 || identity.RegisteredUserLevel >= identity.AdminLevel)
        {
            problems.Add(M.Text("Identity_RegisteredLevel"));
        }

        if (project.Security.MaxFailedAttempts < 1 || project.Security.FailedAttemptsWindowMinutes < 1)
        {
            problems.Add(M.Text("Identity_Lockout"));
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
            notes.Add(M.Text("Identity_NoteUserTable"));
        }

        if (!security.Identity.CreateTablesAtStartup)
        {
            notes.Add(M.Text("Identity_NoteNoCreate"));
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
