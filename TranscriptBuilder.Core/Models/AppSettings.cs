namespace TranscriptBuilder.Models;

// User choices remembered between launches.
public sealed class AppSettings
{
    public string? LastProjectPath { get; set; }

    // Last CLAUDE.md the user copied into a project; the file picker opens on it next time.
    public string? LastClaudeMdSourcePath { get; set; }

    // Last model logged via the Model group, across all projects; mirrors /model's own cross-session default
    // persistence, so the model combo box can be pre-filled instead of asking the user to know it.
    public string? LastKnownModel { get; set; }

    // "Light" or "Dark"; parsed via ThemePreferenceService.Parse, which falls back to Light for a
    // missing, corrupted, or unrecognized value rather than throwing.
    public string? ThemePreference { get; set; }

    // Overrides AuditPrompt.Base when set. Null means "use the built-in default" rather than a
    // stored copy of it, so a future change to the built-in wording is picked up automatically for
    // anyone who never customized it.
    public string? CustomAuditPromptText { get; set; }

    // Remembered window size (not position — a saved position can end up off-screen after a monitor
    // change; size alone has no such failure mode). Null on a first launch, or if either value ever
    // failed to parse, falls back to the XAML-declared default size.
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
}
