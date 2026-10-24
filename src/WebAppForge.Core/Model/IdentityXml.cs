using System.Xml.Linq;

namespace WebAppForge.Core.Model;

/// <summary>
/// The user store and the ASP.NET Core Identity options inside the &lt;security&gt; element. Nothing is written while they keep
/// their defaults, so older project files and files of projects with a user table stay as they were.
/// </summary>
internal static class IdentityXml
{
    public static void Append(XElement security, SecuritySettings settings)
    {
        if (settings.UserStore != UserStore.ProjectTable)
        {
            security.Add(new XAttribute("userStore", settings.UserStore.ToString()));
        }

        var identity = settings.Identity;
        if (identity.IsDefault())
        {
            return;
        }

        security.Add(new XElement("identity",
            new XAttribute("createTablesAtStartup", identity.CreateTablesAtStartup),
            new XAttribute("tablePrefix", identity.TablePrefix),
            new XAttribute("minPasswordLength", identity.MinPasswordLength),
            new XAttribute("requireDigit", identity.RequireDigit),
            new XAttribute("requireUppercase", identity.RequireUppercase),
            new XAttribute("requireLowercase", identity.RequireLowercase),
            new XAttribute("requireNonAlphanumeric", identity.RequireNonAlphanumeric),
            new XAttribute("requireConfirmedEmail", identity.RequireConfirmedEmail),
            new XAttribute("adminUserName", identity.AdminUserName),
            new XAttribute("adminPasswordVariable", identity.AdminPasswordVariable),
            new XAttribute("adminLevel", identity.AdminLevel),
            new XAttribute("registeredUserLevel", identity.RegisteredUserLevel)));
    }

    public static void Read(XElement security, SecuritySettings settings)
    {
        settings.UserStore = Enum.TryParse<UserStore>((string?)security.Attribute("userStore"), true, out var store) ? store : UserStore.ProjectTable;
        var d = new IdentitySettings();
        if (security.Element("identity") is not { } e)
        {
            settings.Identity = d;
            return;
        }

        settings.Identity = new IdentitySettings
        {
            CreateTablesAtStartup = Bool(e, "createTablesAtStartup", d.CreateTablesAtStartup),
            TablePrefix = (string?)e.Attribute("tablePrefix") ?? d.TablePrefix,
            MinPasswordLength = Int(e, "minPasswordLength", d.MinPasswordLength),
            RequireDigit = Bool(e, "requireDigit", d.RequireDigit),
            RequireUppercase = Bool(e, "requireUppercase", d.RequireUppercase),
            RequireLowercase = Bool(e, "requireLowercase", d.RequireLowercase),
            RequireNonAlphanumeric = Bool(e, "requireNonAlphanumeric", d.RequireNonAlphanumeric),
            RequireConfirmedEmail = Bool(e, "requireConfirmedEmail", d.RequireConfirmedEmail),
            AdminUserName = (string?)e.Attribute("adminUserName") ?? d.AdminUserName,
            AdminPasswordVariable = (string?)e.Attribute("adminPasswordVariable") ?? d.AdminPasswordVariable,
            AdminLevel = Int(e, "adminLevel", d.AdminLevel),
            RegisteredUserLevel = Int(e, "registeredUserLevel", d.RegisteredUserLevel),
        };
    }

    private static bool Bool(XElement e, string name, bool fallback) => bool.TryParse((string?)e.Attribute(name), out var value) ? value : fallback;

    private static int Int(XElement e, string name, int fallback) => int.TryParse((string?)e.Attribute(name), out var value) ? value : fallback;
}
