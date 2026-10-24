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

    private static readonly Dictionary<string, UiString> ById = All.Concat(Reports).ToDictionary(s => s.Id);

    /// <summary>The English text of a UI string. Throws for an unknown id, which is a bug in the generator.</summary>
    public static string English(string id) =>
        ById.TryGetValue(id, out var s) ? s.English : throw new ArgumentException($"Unknown UI string id '{id}'.", nameof(id));

    public static string KeyOf(string id) => KeyPrefix + id;
}
