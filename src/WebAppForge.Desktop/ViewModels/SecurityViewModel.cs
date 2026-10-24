using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;
using WebAppForge.Core.Schema;
using WebAppForge.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>
/// The Security tab: login settings for the generated site. Edits write straight into
/// <see cref="Project.Security"/> of the open project, so Save persists them.
/// </summary>
public partial class SecurityViewModel : ViewModelBase
{
    private readonly MainViewModel _main;

    private bool _enabled;
    private string _userTable = string.Empty;
    private string _userNameField = string.Empty;
    private string _passwordField = string.Empty;
    private string _userLevelField = string.Empty;
    private bool _trackFailedAttempts = true;
    private bool _allowRegistration;
    private bool _allowChangePassword = true;
    private string _maxFailedAttemptsText = "5";
    private string _failedAttemptsWindowText = "10";
    private string? _inputMessage;
    private string? _validationMessage;
    private string? _identityNotes;
    private string? _identityTablesMessage;
    private bool _identityBusy;
    private string _minPasswordLengthText = "8";
    private string _adminLevelText = "100";
    private string _registeredUserLevelText = "0";

    public SecurityViewModel(MainViewModel main)
    {
        _main = main;
    }

    /// <summary>Generated tables that can hold users (Generate = true).</summary>
    public ObservableCollection<string> UserTables { get; } = [];

    /// <summary>Fields of the selected user table.</summary>
    public ObservableCollection<string> UserNameFields { get; } = [];

    /// <summary>Fields of the selected user table.</summary>
    public ObservableCollection<string> PasswordFields { get; } = [];

    /// <summary>"(none)" followed by the Integer fields of the selected user table.</summary>
    public ObservableCollection<string> UserLevelFields { get; } = [];

    /// <summary>Display name of <see cref="UserStore.ProjectTable"/>.</summary>
    public const string ProjectTableLabel = "Project table";

    /// <summary>Display name of <see cref="UserStore.AspNetIdentity"/>.</summary>
    public const string AspNetIdentityLabel = "ASP.NET Core Identity";

    public IReadOnlyList<string> UserStoreChoices { get; } = [ProjectTableLabel, AspNetIdentityLabel];

    private SecuritySettings Model => _main.Current.Security;

    private IdentitySettings Identity => Model.Identity;

    /// <summary>The user store picked in the combo box.</summary>
    public string UserStoreChoice
    {
        get => Model.UserStore == UserStore.AspNetIdentity ? AspNetIdentityLabel : ProjectTableLabel;
        set
        {
            var store = value == AspNetIdentityLabel ? UserStore.AspNetIdentity : UserStore.ProjectTable;
            if (Model.UserStore != store)
            {
                Model.UserStore = store;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsIdentity));
                OnPropertyChanged(nameof(IsProjectTable));
                Refresh();
            }
        }
    }

    /// <summary>True when the site keeps its users with ASP.NET Core Identity; the identity settings are shown.</summary>
    public bool IsIdentity => Model.UserStore == UserStore.AspNetIdentity;

    /// <summary>True when the users are in a project table; the user table settings are shown.</summary>
    public bool IsProjectTable => !IsIdentity;

    public bool CreateTablesAtStartup
    {
        get => Identity.CreateTablesAtStartup;
        set => SetIdentity(Identity.CreateTablesAtStartup, value, v => Identity.CreateTablesAtStartup = v);
    }

    public string TablePrefix
    {
        get => Identity.TablePrefix;
        set => SetIdentity(Identity.TablePrefix, (value ?? string.Empty).Trim(), v => Identity.TablePrefix = v);
    }

    public bool RequireDigit
    {
        get => Identity.RequireDigit;
        set => SetIdentity(Identity.RequireDigit, value, v => Identity.RequireDigit = v);
    }

    public bool RequireUppercase
    {
        get => Identity.RequireUppercase;
        set => SetIdentity(Identity.RequireUppercase, value, v => Identity.RequireUppercase = v);
    }

    public bool RequireLowercase
    {
        get => Identity.RequireLowercase;
        set => SetIdentity(Identity.RequireLowercase, value, v => Identity.RequireLowercase = v);
    }

    public bool RequireNonAlphanumeric
    {
        get => Identity.RequireNonAlphanumeric;
        set => SetIdentity(Identity.RequireNonAlphanumeric, value, v => Identity.RequireNonAlphanumeric = v);
    }

    public bool RequireConfirmedEmail
    {
        get => Identity.RequireConfirmedEmail;
        set => SetIdentity(Identity.RequireConfirmedEmail, value, v => Identity.RequireConfirmedEmail = v);
    }

    public string AdminUserName
    {
        get => Identity.AdminUserName;
        set => SetIdentity(Identity.AdminUserName, (value ?? string.Empty).Trim(), v => Identity.AdminUserName = v);
    }

    /// <summary>Name of the environment variable with the first administrator's password. The editor never asks for the password itself.</summary>
    public string AdminPasswordVariable
    {
        get => Identity.AdminPasswordVariable;
        set => SetIdentity(Identity.AdminPasswordVariable, (value ?? string.Empty).Trim(), v => Identity.AdminPasswordVariable = v);
    }

    /// <summary>Bound to a text box. Only positive whole numbers reach the model; anything else keeps the last valid value.</summary>
    public string MinPasswordLengthText
    {
        get => _minPasswordLengthText;
        set
        {
            if (SetProperty(ref _minPasswordLengthText, value ?? string.Empty))
            {
                ApplyCount(_minPasswordLengthText, "Minimum password length", n => Identity.MinPasswordLength = n);
            }
        }
    }

    /// <summary>Bound to a text box. Only positive whole numbers reach the model; anything else keeps the last valid value.</summary>
    public string AdminLevelText
    {
        get => _adminLevelText;
        set
        {
            if (SetProperty(ref _adminLevelText, value ?? string.Empty))
            {
                ApplyCount(_adminLevelText, "Administrator level", n => Identity.AdminLevel = n);
            }
        }
    }

    /// <summary>Bound to a text box. Only whole numbers of 0 or more reach the model; anything else keeps the last valid value.</summary>
    public string RegisteredUserLevelText
    {
        get => _registeredUserLevelText;
        set
        {
            if (SetProperty(ref _registeredUserLevelText, value ?? string.Empty))
            {
                if (int.TryParse(_registeredUserLevelText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) && level >= 0)
                {
                    Identity.RegisteredUserLevel = level;
                    InputMessage = null;
                    Refresh();
                }
                else
                {
                    InputMessage = "Level of new users must be a whole number of 0 or more. The last valid value was kept.";
                    _main.Status = InputMessage;
                }
            }
        }
    }

    /// <summary>Identity settings that have no effect (they do not block Generate), one per line, or null.</summary>
    public string? IdentityNotes
    {
        get => _identityNotes;
        private set
        {
            if (SetProperty(ref _identityNotes, value))
            {
                OnPropertyChanged(nameof(HasIdentityNotes));
            }
        }
    }

    public bool HasIdentityNotes => IdentityNotes is not null;

    /// <summary>The result of the last check or creation of the identity tables, or null.</summary>
    public string? IdentityTablesMessage
    {
        get => _identityTablesMessage;
        private set
        {
            if (SetProperty(ref _identityTablesMessage, value))
            {
                OnPropertyChanged(nameof(HasIdentityTablesMessage));
            }
        }
    }

    public bool HasIdentityTablesMessage => IdentityTablesMessage is not null;

    /// <summary>True while the identity tables are checked or created.</summary>
    public bool IdentityBusy
    {
        get => _identityBusy;
        private set => SetProperty(ref _identityBusy, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetProperty(ref _enabled, value))
            {
                Model.Enabled = value;
                Refresh();
            }
        }
    }

    public string UserTable
    {
        get => _userTable;
        set
        {
            if (SetProperty(ref _userTable, value ?? string.Empty))
            {
                Model.UserTable = _userTable;
                Refresh();
            }
        }
    }

    public string UserNameField
    {
        get => _userNameField;
        set
        {
            if (SetProperty(ref _userNameField, value ?? string.Empty))
            {
                Model.UserNameField = _userNameField;
                Refresh();
            }
        }
    }

    public string PasswordField
    {
        get => _passwordField;
        set
        {
            if (SetProperty(ref _passwordField, value ?? string.Empty))
            {
                Model.PasswordField = _passwordField;
                Refresh();
            }
        }
    }

    /// <summary>The level field picked in the combo box. "(none)" stands for an empty <see cref="SecuritySettings.UserLevelField"/>.</summary>
    public string LevelField
    {
        get => OwnerFieldPicker.Display(_userLevelField);
        set
        {
            var stored = OwnerFieldPicker.Value(value);
            if (SetProperty(ref _userLevelField, stored))
            {
                Model.UserLevelField = stored;
                Refresh();
            }
        }
    }

    public bool TrackFailedAttempts
    {
        get => _trackFailedAttempts;
        set
        {
            if (SetProperty(ref _trackFailedAttempts, value))
            {
                Model.TrackFailedAttempts = value;
                Refresh();
            }
        }
    }

    public bool AllowRegistration
    {
        get => _allowRegistration;
        set
        {
            if (SetProperty(ref _allowRegistration, value))
            {
                Model.AllowRegistration = value;
                Refresh();
            }
        }
    }

    public bool AllowChangePassword
    {
        get => _allowChangePassword;
        set
        {
            if (SetProperty(ref _allowChangePassword, value))
            {
                Model.AllowChangePassword = value;
                Refresh();
            }
        }
    }

    /// <summary>Bound to a text box. Only positive whole numbers reach the model; anything else keeps the last valid value.</summary>
    public string MaxFailedAttemptsText
    {
        get => _maxFailedAttemptsText;
        set
        {
            if (SetProperty(ref _maxFailedAttemptsText, value ?? string.Empty))
            {
                ApplyCount(_maxFailedAttemptsText, "Max failed attempts", n => Model.MaxFailedAttempts = n);
            }
        }
    }

    /// <summary>Bound to a text box. Only positive whole numbers reach the model; anything else keeps the last valid value.</summary>
    public string FailedAttemptsWindowText
    {
        get => _failedAttemptsWindowText;
        set
        {
            if (SetProperty(ref _failedAttemptsWindowText, value ?? string.Empty))
            {
                ApplyCount(_failedAttemptsWindowText, "Failed attempts window (minutes)", n => Model.FailedAttemptsWindowMinutes = n);
            }
        }
    }

    /// <summary>Message about the last rejected number input, or null.</summary>
    public string? InputMessage
    {
        get => _inputMessage;
        private set
        {
            if (SetProperty(ref _inputMessage, value))
            {
                OnPropertyChanged(nameof(HasInputMessage));
            }
        }
    }

    public bool HasInputMessage => InputMessage is not null;

    /// <summary>
    /// The first problem that the generator would reject, or null. Only set while login is enabled,
    /// because the generator ignores these settings when it is disabled.
    /// </summary>
    public string? ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (SetProperty(ref _validationMessage, value))
            {
                OnPropertyChanged(nameof(HasValidationMessage));
                OnPropertyChanged(nameof(CanGenerate));
            }
        }
    }

    public bool HasValidationMessage => ValidationMessage is not null;

    /// <summary>False while the settings would make the generator fail. The Generate button is bound to this.</summary>
    public bool CanGenerate => ValidationMessage is null;

    /// <summary>
    /// The checks the generator applies in ProjectGenerator.AddLoginFiles, with the same matching rules
    /// (table and field names compare case-insensitively, and only generated tables count).
    /// Returns null when the settings are valid or login is disabled.
    /// </summary>
    public static string? Validate(SecuritySettings security, IEnumerable<TableInfo> tables)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(tables);

        if (!security.Enabled)
        {
            return null;
        }

        // The identity store keeps its users in the AspNet* tables: the user table settings are not used.
        if (security.UserStore == UserStore.AspNetIdentity)
        {
            return tables.Where(t => t.Generate && !string.IsNullOrEmpty(t.OwnerField)).Select(ValidateOwnerField).FirstOrDefault(p => p is not null);
        }

        if (string.IsNullOrWhiteSpace(security.UserTable))
        {
            return "Choose the user table. Login is enabled but no table is set.";
        }

        var users = FindGeneratedTable(tables, security.UserTable);
        if (users is null)
        {
            return $"Security is enabled but the user table '{security.UserTable}' is not one of the generated tables.";
        }

        if (!HasField(users, security.UserNameField))
        {
            return string.IsNullOrWhiteSpace(security.UserNameField)
                ? "Choose the user name field. Login is enabled but no field is set."
                : $"Security is enabled but table '{users.Name}' has no user name field '{security.UserNameField}'.";
        }

        if (!HasField(users, security.PasswordField))
        {
            return string.IsNullOrWhiteSpace(security.PasswordField)
                ? "Choose the password field. Login is enabled but no field is set."
                : $"Security is enabled but table '{users.Name}' has no password field '{security.PasswordField}'.";
        }

        if (!string.IsNullOrEmpty(security.UserLevelField))
        {
            var level = users.Fields.FirstOrDefault(f => string.Equals(f.Name, security.UserLevelField, StringComparison.OrdinalIgnoreCase));
            if (level is null)
            {
                return $"User level security is enabled but table '{users.Name}' has no user level field '{security.UserLevelField}'.";
            }

            if (level.Type != FieldType.Integer)
            {
                return $"The user level field '{level.Name}' in table '{users.Name}' must be an integer column, but it is {level.Type}.";
            }
        }

        foreach (var table in tables.Where(t => t.Generate && !string.IsNullOrEmpty(t.OwnerField)))
        {
            var ownerProblem = ValidateOwnerField(table);
            if (ownerProblem is not null)
            {
                return ownerProblem;
            }
        }

        return null;
    }

    /// <summary>
    /// The owner-field rule of ProjectGenerator (User ID security): the owner field must exist in the table and be a
    /// Text field. Same messages as the generator's InvalidOperationException. Returns null when the table has no
    /// owner field or the field is valid. Login settings are not checked here; <see cref="Validate"/> does that.
    /// </summary>
    public static string? ValidateOwnerField(TableInfo table)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (string.IsNullOrEmpty(table.OwnerField))
        {
            return null;
        }

        var owner = table.Fields.FirstOrDefault(f => string.Equals(f.Name, table.OwnerField, StringComparison.OrdinalIgnoreCase));
        if (owner is null)
        {
            return $"User ID security is enabled for table '{table.Name}' but it has no owner field '{table.OwnerField}'.";
        }

        return owner.Type == FieldType.Text
            ? null
            : $"The owner field '{owner.Name}' in table '{table.Name}' must be a text column to hold the user name, but it is {owner.Type}.";
    }

    /// <summary>"(none)" followed by the Integer fields of <paramref name="users"/>. Null means no user table is chosen.</summary>
    public static IReadOnlyList<string> LevelFieldChoices(TableInfo? users)
    {
        return new[] { OwnerFieldPicker.NoneLabel }
            .Concat(users?.Fields.Where(f => f.Type == FieldType.Integer).Select(f => f.Name) ?? [])
            .ToList();
    }

    /// <summary>True for a whole number greater than zero.</summary>
    public static bool TryParsePositive(string? text, out int value)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;
    }

    /// <summary>Copies the security settings of the open project into the tab. Called whenever a project is loaded.</summary>
    public void LoadFrom(Project project)
    {
        var security = project.Security;
        Enabled = security.Enabled;
        UserTable = security.UserTable;
        UserNameField = security.UserNameField;
        PasswordField = security.PasswordField;
        LevelField = security.UserLevelField;
        TrackFailedAttempts = security.TrackFailedAttempts;
        AllowRegistration = security.AllowRegistration;
        AllowChangePassword = security.AllowChangePassword;
        MaxFailedAttemptsText = security.MaxFailedAttempts.ToString(CultureInfo.InvariantCulture);
        FailedAttemptsWindowText = security.FailedAttemptsWindowMinutes.ToString(CultureInfo.InvariantCulture);
        var identity = security.Identity;
        MinPasswordLengthText = identity.MinPasswordLength.ToString(CultureInfo.InvariantCulture);
        AdminLevelText = identity.AdminLevel.ToString(CultureInfo.InvariantCulture);
        RegisteredUserLevelText = identity.RegisteredUserLevel.ToString(CultureInfo.InvariantCulture);
        IdentityTablesMessage = null;
        foreach (var name in new[]
        {
            nameof(UserStoreChoice), nameof(IsIdentity), nameof(IsProjectTable), nameof(CreateTablesAtStartup), nameof(TablePrefix),
            nameof(RequireDigit), nameof(RequireUppercase), nameof(RequireLowercase), nameof(RequireNonAlphanumeric),
            nameof(RequireConfirmedEmail), nameof(AdminUserName), nameof(AdminPasswordVariable),
        })
        {
            OnPropertyChanged(name);
        }

        Refresh();
    }

    /// <summary>
    /// Rebuilds the table and field lists from the open project and recomputes the validation message.
    /// Call it after the table list changes (Generate flags, database connect).
    /// </summary>
    public void Refresh()
    {
        var tables = _main.Current.Tables;

        SetItems(UserTables, tables.Where(t => t.Generate).Select(t => t.Name));

        var users = FindGeneratedTable(tables, UserTable);
        SetItems(UserNameFields, users?.Fields.Select(f => f.Name) ?? []);
        SetItems(PasswordFields, users?.Fields.Select(f => f.Name) ?? []);
        SetItems(UserLevelFields, LevelFieldChoices(users));

        // Master/detail errors block generation like the login checks. The login problem is reported first.
        var project = _main.Current;
        var notes = IdentityRules.Notes(project);
        IdentityNotes = project.Security.UsesIdentity && notes.Count > 0 ? string.Join(Environment.NewLine, notes) : null;
        ValidationMessage = Validate(Model, tables) ?? IdentityRules.Problems(project).FirstOrDefault() ?? RelationValidator.FirstError(tables) ?? AuditViewModel.Validate(_main.Current) ?? ImportExportViewModel.Validate(_main.Current);
    }

    /// <summary>
    /// Checks the project database for the identity tables, and after a confirmation creates the missing ones. The database is
    /// then read again, as the Database tab's Connect does, so the new tables appear (they get no pages of their own).
    /// </summary>
    [RelayCommand]
    private async Task CreateIdentityTablesAsync()
    {
        var prefix = Identity.TablePrefix;
        if (!IdentitySchema.IsValidPrefix(prefix))
        {
            Report($"'{prefix}' is not a valid identity table prefix: use 1 to 30 letters, digits and underscores, starting with a letter.");
            return;
        }

        var (connectionString, problem) = _main.Database.Resolve();
        if (connectionString is null)
        {
            Report(problem ?? "Connect the database first.");
            return;
        }

        var provider = _main.Current.Database.Provider;
        IdentityBusy = true;
        try
        {
            var missing = await _main.IdentityTables.MissingTablesAsync(provider, connectionString, prefix);
            if (missing.Count == 0)
            {
                Report($"All {IdentitySchema.TableNames(prefix).Count} identity tables exist in the database.");
                return;
            }

            var confirmed = await _main.Dialogs.ConfirmAsync(
                "Create identity tables",
                $"These tables are missing from the project database and will be created:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}",
                "Create tables");
            if (!confirmed)
            {
                Report($"Missing identity tables: {string.Join(", ", missing)}. Nothing was created.");
                return;
            }

            var created = await _main.IdentityTables.CreateMissingTablesAsync(provider, connectionString, prefix);
            var discovered = await _main.IdentityTables.ReadTablesAsync(provider, connectionString);
            SchemaMerger.Merge(_main.Current, discovered);
            foreach (var table in _main.Current.Tables.Where(t => IdentitySchema.IsIdentityTable(Model, t.Name)))
            {
                // The site manages these tables itself; they get no list, edit or import pages.
                table.Generate = false;
            }

            _main.Tables.Reload();
            Refresh();
            Report($"Created {created.Count} identity table(s): {string.Join(", ", created)}.");
        }
        catch (Exception ex)
        {
            Report($"The identity tables could not be created: {ex.Message}");
        }
        finally
        {
            IdentityBusy = false;
        }
    }

    private void Report(string message)
    {
        IdentityTablesMessage = message;
        _main.Status = message;
    }

    private void SetIdentity<T>(T current, T value, Action<T> store, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return;
        }

        store(value);
        OnPropertyChanged(name);
        Refresh();
    }

    private void ApplyCount(string text, string label, Action<int> store)
    {
        if (TryParsePositive(text, out var value))
        {
            store(value);
            InputMessage = null;
            Refresh();
            return;
        }

        InputMessage = $"{label} must be a positive whole number. The last valid value was kept.";
        _main.Status = InputMessage;
    }

    private static TableInfo? FindGeneratedTable(IEnumerable<TableInfo> tables, string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        return tables.FirstOrDefault(t => t.Generate && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasField(TableInfo table, string name)
    {
        return !string.IsNullOrEmpty(name)
            && table.Fields.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Replaces the items only when they differ, so the combo boxes keep their selection on an unchanged refresh.</summary>
    private static void SetItems(ObservableCollection<string> target, IEnumerable<string> items)
    {
        var list = items.ToList();
        if (target.SequenceEqual(list))
        {
            return;
        }

        target.Clear();
        foreach (var item in list)
        {
            target.Add(item);
        }
    }
}
