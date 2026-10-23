using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebAppForge.Desktop.Services;

/// <summary>Reads and writes <see cref="AppSettings"/> as JSON. Failures are handled quietly: defaults on read, false on write.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public SettingsStore(string filePath)
    {
        FilePath = filePath;
    }

    /// <summary>%LOCALAPPDATA%\WebAppForge\settings.json on Windows.</summary>
    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WebAppForge", "settings.json");

    public string FilePath { get; }

    /// <summary>Returns the stored settings, or the defaults when the file is missing, unreadable or not valid JSON.</summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options);
            return (loaded ?? new AppSettings()).Normalized();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Writes the settings, creating the folder when needed. Returns false when the file could not be written.</summary>
    public bool Save(AppSettings settings)
    {
        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(FilePath));
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings.Normalized(), Options));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
