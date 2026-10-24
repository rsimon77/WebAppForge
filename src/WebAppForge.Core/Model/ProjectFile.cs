using System.Xml.Linq;

namespace WebAppForge.Core.Model;

/// <summary>Reads and writes the project XML file (UTF-8). Settings are stored as attributes, one node per object.</summary>
public static class ProjectFile
{
    public const string RootElement = "project";

    public static void Save(Project project, string path)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrEmpty(path);
        Write(project).Save(path);
    }

    public static Project Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Read(XDocument.Load(path));
    }

    public static XDocument Write(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var root = new XElement(RootElement,
            new XAttribute("name", project.Name),
            new XAttribute("id", project.ProjectId.ToString("D")),
            new XAttribute("outputFolder", project.OutputFolder),
            new XElement("database",
                new XAttribute("provider", project.Database.Provider),
                new XAttribute("path", project.Database.Path)),
            new XElement("aspnet",
                new XAttribute("title", project.AspNet.Title),
                new XAttribute("theme", project.AspNet.Theme),
                new XAttribute("homePage", project.AspNet.IncludeHomePage)),
            new XElement("security",
                new XAttribute("enabled", project.Security.Enabled),
                new XAttribute("userTable", project.Security.UserTable),
                new XAttribute("userNameField", project.Security.UserNameField),
                new XAttribute("passwordField", project.Security.PasswordField),
                new XAttribute("trackFailedAttempts", project.Security.TrackFailedAttempts),
                new XAttribute("maxFailedAttempts", project.Security.MaxFailedAttempts),
                new XAttribute("failedAttemptsWindowMinutes", project.Security.FailedAttemptsWindowMinutes),
                new XAttribute("allowRegistration", project.Security.AllowRegistration),
                new XAttribute("allowChangePassword", project.Security.AllowChangePassword),
                new XAttribute("userLevelField", project.Security.UserLevelField)));

        foreach (var table in project.Tables)
        {
            var tableElement = new XElement("table",
                new XAttribute("name", table.Name),
                new XAttribute("caption", table.Caption),
                new XAttribute("generate", table.Generate),
                new XAttribute("filter", table.Filter),
                new XAttribute("ownerField", table.OwnerField),
                new XAttribute("viewMinLevel", table.ViewMinLevel),
                new XAttribute("editMinLevel", table.EditMinLevel),
                new XAttribute("list", table.Pages.List),
                new XAttribute("view", table.Pages.View),
                new XAttribute("add", table.Pages.Add),
                new XAttribute("copy", table.Pages.Copy),
                new XAttribute("edit", table.Pages.Edit),
                new XAttribute("delete", table.Pages.Delete),
                new XAttribute("search", table.Pages.Search),
                new XAttribute("pageSize", table.Pages.PageSize));

            foreach (var sort in table.Sort)
            {
                tableElement.Add(new XElement("sort",
                    new XAttribute("field", sort.Field),
                    new XAttribute("descending", sort.Descending)));
            }

            foreach (var field in table.Fields)
            {
                var fieldElement = new XElement("field",
                    new XAttribute("name", field.Name),
                    new XAttribute("caption", field.Caption),
                    new XAttribute("type", field.Type.ToString()),
                    new XAttribute("pk", field.IsPrimaryKey),
                    field.IsPrimaryKey ? new XAttribute("keyPosition", field.KeyPosition) : null,
                    new XAttribute("autoinc", field.IsAutoIncrement),
                    new XAttribute("required", field.IsRequired),
                    new XAttribute("hasDefault", field.HasDefault),
                    new XAttribute("inList", field.ShowInList),
                    new XAttribute("inView", field.ShowInView),
                    new XAttribute("inAdd", field.ShowInAdd),
                    new XAttribute("inEdit", field.ShowInEdit),
                    new XAttribute("searchable", field.Searchable));
                CustomizationXml.AppendFieldUi(fieldElement, field.Ui);
                tableElement.Add(fieldElement);
            }

            CustomizationXml.AppendTable(tableElement, table);
            MasterDetailXml.Append(tableElement, table);
            root.Add(tableElement);
        }

        root.Add(new XElement("build",
            new XAttribute("buildAfterGenerate", project.Build.BuildAfterGenerate),
            new XAttribute("configuration", project.Build.Configuration),
            new XAttribute("publish", project.Build.Publish),
            new XAttribute("publishFolder", project.Build.PublishFolder),
            new XAttribute("runtimeIdentifier", project.Build.RuntimeIdentifier),
            new XAttribute("selfContained", project.Build.SelfContained),
            new XAttribute("generateDockerfile", project.Build.GenerateDockerfile),
            new XAttribute("dockerImageName", project.Build.DockerImageName),
            new XAttribute("dockerExposePort", project.Build.DockerExposePort),
            new XAttribute("dockerBaseSdkImage", project.Build.DockerBaseSdkImage),
            new XAttribute("dockerBaseRuntimeImage", project.Build.DockerBaseRuntimeImage),
            new XAttribute("generateCompose", project.Build.GenerateCompose),
            new XAttribute("buildDockerImage", project.Build.BuildDockerImage)));

        LocalizationXml.Append(root, project.Localization);
        CustomizationXml.AppendMeta(root, project.Meta);
        ReportXml.Append(root, project.Reports);

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    public static Project Read(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Root
            ?? throw new InvalidDataException("Project file has no root element.");
        if (root.Name.LocalName != RootElement)
        {
            throw new InvalidDataException($"Expected root element '{RootElement}' but found '{root.Name.LocalName}'.");
        }

        var project = new Project
        {
            Name = RequiredAttribute(root, "name"),
            ProjectId = Guid.Parse(RequiredAttribute(root, "id")),
            OutputFolder = Optional(root, "outputFolder", "generated"),
        };

        if (root.Element("database") is { } database)
        {
            project.Database = new DatabaseSettings
            {
                Provider = Optional(database, "provider", "sqlite"),
                Path = Optional(database, "path", string.Empty),
            };
        }

        if (root.Element("aspnet") is { } aspnet)
        {
            project.AspNet = new AspNetSettings
            {
                Title = Optional(aspnet, "title", project.AspNet.Title),
                Theme = Optional(aspnet, "theme", project.AspNet.Theme),
                IncludeHomePage = ReadBool(aspnet, "homePage", true),
            };
        }

        // Older project files have no <security> element; the defaults keep login off.
        if (root.Element("security") is { } security)
        {
            project.Security = new SecuritySettings
            {
                Enabled = ReadBool(security, "enabled", false),
                UserTable = Optional(security, "userTable", string.Empty),
                UserNameField = Optional(security, "userNameField", string.Empty),
                PasswordField = Optional(security, "passwordField", string.Empty),
                TrackFailedAttempts = ReadBool(security, "trackFailedAttempts", true),
                MaxFailedAttempts = ReadInt(security, "maxFailedAttempts", 5),
                FailedAttemptsWindowMinutes = ReadInt(security, "failedAttemptsWindowMinutes", 10),
                AllowRegistration = ReadBool(security, "allowRegistration", false),
                AllowChangePassword = ReadBool(security, "allowChangePassword", true),
                UserLevelField = Optional(security, "userLevelField", string.Empty),
            };
        }

        // Older project files have no <build> element; the defaults keep building and Docker off.
        if (root.Element("build") is { } build)
        {
            project.Build = new BuildSettings
            {
                BuildAfterGenerate = ReadBool(build, "buildAfterGenerate", false),
                Configuration = Optional(build, "configuration", "Debug"),
                Publish = ReadBool(build, "publish", false),
                PublishFolder = Optional(build, "publishFolder", "publish"),
                RuntimeIdentifier = Optional(build, "runtimeIdentifier", string.Empty),
                SelfContained = ReadBool(build, "selfContained", false),
                GenerateDockerfile = ReadBool(build, "generateDockerfile", false),
                DockerImageName = Optional(build, "dockerImageName", string.Empty),
                DockerExposePort = ReadInt(build, "dockerExposePort", 8080),
                DockerBaseSdkImage = Optional(build, "dockerBaseSdkImage", BuildSettings.DefaultSdkImage),
                DockerBaseRuntimeImage = Optional(build, "dockerBaseRuntimeImage", BuildSettings.DefaultRuntimeImage),
                GenerateCompose = ReadBool(build, "generateCompose", false),
                BuildDockerImage = ReadBool(build, "buildDockerImage", false),
            };
        }

        LocalizationXml.Read(root, project);
        CustomizationXml.ReadMeta(root, project);

        foreach (var tableElement in root.Elements("table"))
        {
            var table = new TableInfo
            {
                Name = RequiredAttribute(tableElement, "name"),
                Caption = Optional(tableElement, "caption", string.Empty),
                Generate = ReadBool(tableElement, "generate", true),
                Filter = Optional(tableElement, "filter", string.Empty),
                OwnerField = Optional(tableElement, "ownerField", string.Empty),
                ViewMinLevel = ReadInt(tableElement, "viewMinLevel", 0),
                EditMinLevel = ReadInt(tableElement, "editMinLevel", 0),
                Pages = new PageSettings
                {
                    List = ReadBool(tableElement, "list", true),
                    View = ReadBool(tableElement, "view", true),
                    Add = ReadBool(tableElement, "add", true),
                    Copy = ReadBool(tableElement, "copy", true),
                    Edit = ReadBool(tableElement, "edit", true),
                    Delete = ReadBool(tableElement, "delete", true),
                    Search = ReadBool(tableElement, "search", true),
                    PageSize = int.TryParse(tableElement.Attribute("pageSize")?.Value, out var size) ? size : 20,
                },
            };

            foreach (var sortElement in tableElement.Elements("sort"))
            {
                table.Sort.Add(new SortField
                {
                    Field = RequiredAttribute(sortElement, "field"),
                    Descending = ReadBool(sortElement, "descending", false),
                });
            }

            foreach (var fieldElement in tableElement.Elements("field"))
            {
                table.Fields.Add(new FieldInfo
                {
                    Name = RequiredAttribute(fieldElement, "name"),
                    Caption = Optional(fieldElement, "caption", string.Empty),
                    Type = Enum.Parse<FieldType>(RequiredAttribute(fieldElement, "type")),
                    IsPrimaryKey = ReadBool(fieldElement, "pk", false),
                    KeyPosition = ReadInt(fieldElement, "keyPosition", 0),
                    IsAutoIncrement = ReadBool(fieldElement, "autoinc", false),
                    IsRequired = ReadBool(fieldElement, "required", false),
                    HasDefault = ReadBool(fieldElement, "hasDefault", false),
                    ShowInList = ReadBool(fieldElement, "inList", true),
                    ShowInView = ReadBool(fieldElement, "inView", true),
                    ShowInAdd = ReadBool(fieldElement, "inAdd", true),
                    ShowInEdit = ReadBool(fieldElement, "inEdit", true),
                    Searchable = ReadBool(fieldElement, "searchable", true),
                    Ui = CustomizationXml.ReadFieldUi(fieldElement),
                });
            }

            CustomizationXml.ReadTable(tableElement, table);
            MasterDetailXml.Read(tableElement, table);

            project.Tables.Add(table);
        }

        ReportXml.Read(root, project.Reports);

        return project;
    }

    private static string RequiredAttribute(XElement element, string name)
    {
        return element.Attribute(name)?.Value
            ?? throw new InvalidDataException($"Element '{element.Name.LocalName}' is missing attribute '{name}'.");
    }

    private static string Optional(XElement element, string name, string fallback)
    {
        return element.Attribute(name)?.Value ?? fallback;
    }

    private static bool ReadBool(XElement element, string name, bool fallback)
    {
        return bool.TryParse(element.Attribute(name)?.Value, out var value) ? value : fallback;
    }

    private static int ReadInt(XElement element, string name, int fallback)
    {
        return int.TryParse(element.Attribute(name)?.Value, out var value) ? value : fallback;
    }
}
