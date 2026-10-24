namespace WebAppForge.Core.Localization;

/// <summary>One fixed text of the generated pages. The key in the translation store is "ui." + Id.</summary>
public sealed record UiString(string Id, string English)
{
    public string Key => UiStrings.KeyPrefix + Id;
}

/// <summary>Catalog of the fixed UI strings of the generated pages, in English. English is also the fallback text of every page.</summary>
public static class UiStrings
{
    public const string KeyPrefix = "ui.";

    public static IReadOnlyList<UiString> All { get; } =
    [
        new("add", "Add"),
        new("edit", "Edit"),
        new("delete", "Delete"),
        new("search", "Search"),
        new("save", "Save"),
        new("cancel", "Cancel"),
        new("view", "View"),
        new("copy", "Copy"),
        new("backToList", "Back to list"),
        new("quickSearch", "Quick search"),
        new("home", "Home"),
        new("yes", "Yes"),
        new("no", "No"),
        new("confirmDelete", "Delete this record? This cannot be undone."),
        new("required", "{0} is required."),
        new("notNumber", "{0} must be a number."),
        new("notDate", "{0} is not a valid date."),
        new("logIn", "Log in"),
        new("logOut", "Log out"),
        new("register", "Register"),
        new("userName", "User name"),
        new("password", "Password"),
        new("confirmPassword", "Confirm password"),
        new("backToLogIn", "Back to log in"),
        new("changePassword", "Change password"),
        new("currentPassword", "Current password"),
        new("newPassword", "New password"),
        new("confirmNewPassword", "Confirm new password"),
        new("invalidLogin", "Invalid username or password."),
        new("tooManyAttempts", "Too many failed attempts. Try again later."),
        new("registrationComplete", "Registration complete. You can now log in."),
        new("manualSignUp", "This site needs a manual sign-up for this table."),
        new("enterUserName", "Enter a user name."),
        new("userNameTaken", "That user name is already taken."),
        new("enterPassword", "Enter a password."),
        new("passwordTooShort", "The password must be at least {0} characters."),
        new("passwordsDiffer", "The passwords do not match."),
        new("enterCurrentPassword", "Enter your current password."),
        new("currentPasswordWrong", "The current password is not correct."),
        new("passwordChanged", "Your password has been changed."),
    ];

    /// <summary>
    /// Fixed texts of the report pages. They are kept apart from <see cref="All"/> so that the translation store of a project without
    /// reports stays as it was; <see cref="LocalizationKeys.Enumerate"/> adds them only when a report is generated.
    /// </summary>
    public static IReadOnlyList<UiString> Reports { get; } =
    [
        new("reportTotal", "Total"),
        new("reportGrandTotal", "Grand total"),
        new("reportEmpty", "(empty)"),
        new("reportNoData", "No data."),
        new("reportMoreColumns", "Only the first {0} columns are shown; the totals include every column."),
        new("reportMoreRows", "Only the first {0} rows are listed; the totals include every row."),
        new("reportPrevious", "Previous"),
        new("reportNext", "Next"),
        new("reportThisMonth", "This month"),
        new("reportValue", "Value"),
        new("reportCount", "Count"),
        new("reportSum", "Sum"),
        new("reportAverage", "Average"),
        new("reportMin", "Min"),
        new("reportMax", "Max"),
        new("reportOf", "{0} of {1}"),
        new("reports", "Reports"),
        new("reportFrom", "From"),
        new("reportTo", "To"),
        new("reportApply", "Apply"),
        new("reportClear", "Clear"),
        new("reportAll", "(all)"),
        new("reportRowCount", "{0} rows"),
        new("reportBack", "Back to the report"),
    ];

    /// <summary>
    /// Fixed texts of the filter builder, quick filter and reset button of the list pages. Like <see cref="Reports"/> they are kept
    /// apart from <see cref="All"/> and enumerated only when a generated list uses them.
    /// </summary>
    public static IReadOnlyList<UiString> Grid { get; } =
    [
        new("gridFilter", "Filter and sort"),
        new("gridQuick", "Quick filter"),
        new("gridQuickHint", "Every word must match. Use \"a phrase\", column:value or -word to leave rows out."),
        new("gridMatch", "Match"),
        new("gridMatchAll", "all conditions"),
        new("gridMatchAny", "any condition"),
        new("gridColumn", "(column)"),
        new("gridOperator", "Operator"),
        new("gridValue", "Value"),
        new("gridAnd", "and"),
        new("gridOr", "or"),
        new("gridApply", "Apply"),
        new("gridReset", "Reset"),
        new("gridRemove", "Remove"),
        new("gridSortBy", "Sort by"),
        new("gridThenBy", "then by"),
        new("gridNoSort", "(default order)"),
        new("gridDirection", "Direction"),
        new("gridAscending", "ascending"),
        new("gridDescending", "descending"),
        new("gridContains", "contains"),
        new("gridNotContains", "does not contain"),
        new("gridStartsWith", "starts with"),
        new("gridEndsWith", "ends with"),
        new("gridBetween", "between"),
        new("gridIsYes", "is yes"),
        new("gridIsNo", "is no"),
        new("gridIsEmpty", "is empty"),
        new("gridNotEmpty", "is not empty"),
    ];

    /// <summary>Texts of the audit trail page, enumerated only when the project has an audit trail.</summary>
    public static IReadOnlyList<UiString> Audit { get; } =
    [
        new("auditTrail", "Audit trail"),
        new("auditWhen", "When"),
        new("auditUser", "User"),
        new("auditTable", "Table"),
        new("auditAction", "Action"),
        new("auditRecord", "Record"),
        new("auditChanges", "Changes"),
        new("auditInsert", "Added"),
        new("auditUpdate", "Changed"),
        new("auditDelete", "Deleted"),
        new("auditAll", "(all)"),
        new("auditApply", "Apply"),
        new("auditClear", "Clear"),
        new("auditFrom", "From"),
        new("auditTo", "To"),
        new("auditEmpty", "No entries match."),
    ];

    /// <summary>
    /// Fixed texts of the import and export pages. Kept apart from <see cref="All"/> like <see cref="Reports"/>; enumerated only
    /// when a generated table exports or imports.
    /// </summary>
    public static IReadOnlyList<UiString> ImportExport { get; } =
    [
        new("impExport", "Export"),
        new("impImport", "Import"),
        new("impTitle", "Import {0}"),
        new("impFile", "File"),
        new("impHeaderRow", "The first row contains column names"),
        new("impSkipInvalid", "Skip invalid rows and import the rest"),
        new("impUpload", "Upload and preview"),
        new("impTemplate", "Download a CSV template"),
        new("impFormats", "Accepted files: {0}."),
        new("impNoFile", "Choose a file to import."),
        new("impTooLarge", "The file is larger than {0} MB."),
        new("impBadType", "This type of file cannot be imported."),
        new("impUnreadable", "The file could not be read as {0}."),
        new("impNoRows", "The file has no rows to import."),
        new("impTooMany", "The file has more than {0} rows."),
        new("impPreview", "Preview"),
        new("impMapping", "Which column goes into which field"),
        new("impMapNone", "(do not import)"),
        new("impField", "Field"),
        new("impColumn", "Column in the file"),
        new("impRowsFound", "{0} rows found in {1}."),
        new("impCheck", "Check"),
        new("impExpired", "The uploaded file is no longer available. Upload it again."),
        new("impNotMapped", "{0} is required, but no column is mapped to it."),
        new("impKeyNotMapped", "{0} is part of the key of the table: map a column to it to update existing rows."),
        new("impNothingMapped", "Map at least one column."),
        new("impSummary", "{0} rows are valid and {1} rows have errors."),
        new("impRowError", "Row {0}: {1}"),
        new("impMoreErrors", "{0} more errors are not shown."),
        new("impDone", "Import finished"),
        new("impResult", "{0} rows added, {1} rows updated, {2} rows skipped."),
        new("impNothing", "Nothing was imported because some rows have errors. Correct the file, or choose to skip invalid rows."),
        new("impFailed", "The import failed at row {0} and was rolled back. Nothing was changed."),
        new("impBool", "{0} must be yes or no."),
        new("impLookup", "{0}: '{1}' is not an allowed value."),
        new("impDuplicate", "The key appears more than once in the file."),
        new("impKeyExists", "A row with this key already exists."),
        new("impKeyMissing", "The key {0} is empty."),
        new("impAnother", "Import another file"),
        new("impTooLong", "{0} is longer than {1} characters."),
        new("impTooShort", "{0} needs at least {1} characters."),
        new("impPattern", "{0} does not have the expected format."),
        new("impMin", "{0} must be at least {1}."),
        new("impMax", "{0} must be at most {1}."),
    ];

    /// <summary>
    /// Texts of the ASP.NET Core Identity pages (user and role management, sign-in messages). Enumerated only when the project keeps
    /// its users with ASP.NET Core Identity.
    /// </summary>
    public static IReadOnlyList<UiString> Identity { get; } =
    [
        new("idUsers", "Users"),
        new("idRoles", "Roles"),
        new("idEmail", "E-mail"),
        new("idEmailConfirmed", "E-mail confirmed"),
        new("idLevel", "User level"),
        new("idRoleName", "Role name"),
        new("idLocked", "Locked"),
        new("idLock", "Lock"),
        new("idUnlock", "Unlock"),
        new("idSetPassword", "Set password"),
        new("idAddUser", "Add user"),
        new("idAddRole", "Add role"),
        new("idEditUser", "Edit user {0}"),
        new("idEditRole", "Edit role {0}"),
        new("idNoUsers", "No users match."),
        new("idNoRoles", "There are no roles yet."),
        new("idCreated", "The record has been created."),
        new("idSaved", "The changes have been saved."),
        new("idDeleted", "The record has been deleted."),
        new("idPasswordSet", "The password has been set."),
        new("idNotSelf", "You cannot do this to your own account."),
        new("idKeepAdmin", "You cannot lower your own level below the administrator level."),
        new("idNotFound", "The user or role was not found."),
        new("idNotNumber", "The user level must be a whole number."),
        new("idNotAllowed", "You cannot log in until your account has been confirmed."),
        new("idRegistrationPending", "Registration complete. An administrator must confirm your account before you can log in."),
        new("idAdminOnly", "Only administrators can open this page."),
        new("idInvalidUserName", "The user name '{0}' is not valid."),
        new("idRoleTaken", "The role name '{0}' is already taken."),
        new("idInvalidRoleName", "The role name '{0}' is not valid."),
        new("idInvalidEmail", "The e-mail address '{0}' is not valid."),
        new("idPasswordDigit", "The password must contain a digit."),
        new("idPasswordUpper", "The password must contain an uppercase letter."),
        new("idPasswordLower", "The password must contain a lowercase letter."),
        new("idPasswordSymbol", "The password must contain a character that is not a letter or a digit."),
        new("idConcurrency", "The record was changed by someone else. Try again."),
        new("idError", "The change could not be saved."),
    ];

    /// <summary>Texts of designed pages: the confirmations before saving and deleting, the close button of modals and the page size.</summary>
    public static IReadOnlyList<UiString> PageKit { get; } =
    [
        new("pageConfirmSave", "Save changes?"),
        new("pageConfirmDelete", "Are you sure?"),
        new("pageClose", "Close"),
        new("pageRowsPerPage", "Rows per page"),
    ];

    /// <summary>Texts of multi-page forms: the wizard buttons, its step label and its progress bar.</summary>
    public static IReadOnlyList<UiString> FormPages { get; } =
    [
        new("formPrevious", "Previous"),
        new("formNext", "Next"),
        new("formStep", "Step {0} of {1}"),
        new("formProgress", "Progress"),
    ];

    /// <summary>Texts of detail grids and of the delete confirmation of a master with dependents.</summary>
    public static IReadOnlyList<UiString> Details { get; } =
    [
        new("detailNoRecords", "No records."),
        new("detailBlocked", "Cannot delete: {0} related {1} record(s) exist."),
        new("detailAlsoDeletes", "This will also delete {0} record(s) in {1}."),
    ];

    /// <summary>The answers of a route that needs a higher user level.</summary>
    public static IReadOnlyList<UiString> Denied { get; } =
    [
        new("deniedView", "You do not have permission to view this table."),
        new("deniedEdit", "You do not have permission to change this table."),
        new("deniedReport", "You do not have permission to view this report."),
    ];

    private static readonly Dictionary<string, UiString> ById = All.Concat(Reports).Concat(Grid).Concat(Audit).Concat(ImportExport).Concat(Identity)
        .Concat(PageKit).Concat(FormPages).Concat(Details).Concat(Denied).ToDictionary(s => s.Id);

    /// <summary>The English text of a UI string. Throws for an unknown id, which is a bug in the generator.</summary>
    public static string English(string id) =>
        ById.TryGetValue(id, out var s) ? s.English : throw new ArgumentException($"Unknown UI string id '{id}'.", nameof(id));

    public static string KeyOf(string id) => KeyPrefix + id;
}
