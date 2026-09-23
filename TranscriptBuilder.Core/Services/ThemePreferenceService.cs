using TranscriptBuilder.Models;

namespace TranscriptBuilder.Services;

// Parses the persisted theme-preference string safely, and computes the other half of a toggle.
public static class ThemePreferenceService
{
    // Null, missing (a first-time user, or a settings.json predating this feature), or an
    // unrecognized value (a hand-corrupted file, or a future value this build doesn't know about)
    // all fall back to Light — the app's stated default — rather than throwing. Matched by name, not
    // Enum.TryParse, which also accepts numeric strings ("5" → an undefined value, "1" → Dark).
    public static AppThemePreference Parse(string? preference)
    {
        foreach (var mode in Enum.GetValues<AppThemePreference>())
        {
            if (string.Equals(mode.ToString(), preference, StringComparison.OrdinalIgnoreCase))
            {
                return mode;
            }
        }

        return AppThemePreference.Light;
    }

    public static AppThemePreference Toggle(AppThemePreference current) =>
        current == AppThemePreference.Light ? AppThemePreference.Dark : AppThemePreference.Light;
}
