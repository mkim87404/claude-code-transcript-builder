using System.Text.Json;
using TranscriptBuilder.Models;

namespace TranscriptBuilder.Services;

// Loads and saves the user's remembered choices (see AppSettings) as JSON under %AppData%.
public static class SettingsService
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TranscriptBuilder");

    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    // Missing, unreadable or corrupt settings simply mean "nothing remembered yet" — this covers a
    // first-time user (no file), an upgrade from a version that didn't have a given field (that field
    // deserializes to its default, since AppSettings is a plain mutable class, not a required-args
    // record), and a hand-corrupted or truncated file.
    // path overrides the real %AppData% location — for tests only; production callers omit it.
    public static AppSettings Load(string? path = null)
    {
        path ??= SettingsPath;

        try
        {
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    // Best-effort: remembering a choice is a convenience and must never block logging a prompt.
    public static void Save(AppSettings settings, string? path = null)
    {
        path ??= SettingsPath;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Intentionally ignored; see above.
        }
    }
}
