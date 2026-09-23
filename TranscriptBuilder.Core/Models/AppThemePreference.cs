namespace TranscriptBuilder.Models;

// The two color profiles the app can display. Deliberately distinct from WPF's own
// System.Windows.ThemeMode: this lives in the UI-free Core project, which cannot reference WPF
// types, and is mapped onto System.Windows.ThemeMode only at the UI layer.
public enum AppThemePreference
{
    Light,
    Dark
}
