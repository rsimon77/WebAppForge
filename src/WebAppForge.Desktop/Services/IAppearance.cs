namespace WebAppForge.Desktop.Services;

/// <summary>Applies the appearance settings (accent, light or dark mode, custom styles) to the running application.</summary>
public interface IAppearance
{
    /// <summary>Applies the settings. Returns a message when a custom styles file exists but cannot be loaded; otherwise null.</summary>
    string? Apply(AppSettings settings);
}
