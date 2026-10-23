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

    private static readonly Dictionary<string, UiString> ById = All.ToDictionary(s => s.Id);

    /// <summary>The English text of a UI string. Throws for an unknown id, which is a bug in the generator.</summary>
    public static string English(string id) =>
        ById.TryGetValue(id, out var s) ? s.English : throw new ArgumentException($"Unknown UI string id '{id}'.", nameof(id));

    public static string KeyOf(string id) => KeyPrefix + id;
}
