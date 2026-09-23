using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using TranscriptBuilder.Models;
using TranscriptBuilder.Services;

namespace TranscriptBuilder;

// Application entry point; applies the saved theme and installs global exception logging.
public partial class App : Application
{
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TranscriptBuilder", "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        // Applied before base.OnStartup creates the StartupUri window, so there is no visible flash
        // between the default theme and a saved dark preference.
        ApplyTheme(ThemePreferenceService.Parse(SettingsService.Load().ThemePreference));

        base.OnStartup(e);

        // Failures are only logged, never answered with another write to AI_TRANSCRIPT.txt: each
        // append is already a single flushed, self-contained operation, so there is nothing to salvage.
        DispatcherUnhandledException += (_, args) => LogCrash(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                LogCrash(ex);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogCrash(args.Exception);
            args.SetObserved();
        };
    }

    // Maps the persisted preference onto WPF's own Fluent ThemeMode (.NET 9+, still marked
    // experimental — WPF0001, suppressed in the .csproj), which restyles every standard control
    // (buttons, text box, combo box) automatically. Preferred over hand-rolling a
    // ResourceDictionary-swap theme, since the platform's own mechanism wins: a from-scratch
    // theme would still have needed custom styles for every control to look right in dark mode,
    // where Fluent already does that correctly — see NOTES.md for the full trade-off.
    // Also sets the handful of app-specific colors Fluent has no opinion on (footer, status text,
    // group borders, and the Model nudge's rest/highlight colors), since those are this app's own
    // semantics, not standard-control chrome, and re-themes the title bar of every open window.
    public static void ApplyTheme(AppThemePreference preference)
    {
        var isDark = preference == AppThemePreference.Dark;
        Current.ThemeMode = isDark ? ThemeMode.Dark : ThemeMode.Light;

        // Decorative grouping lines around the Model and Session controls — not text, so no 4.5:1
        // requirement, just visible against each theme's background without being loud.
        Current.Resources["GroupBorderBrush"] = new SolidColorBrush(isDark
            ? Color.FromRgb(0x5A, 0x5A, 0x5A)
            : Color.FromRgb(0xCC, 0xCC, 0xCC));

        // Both pairs verified at >= 4.5:1 (WCAG AA) against their respective background: the light
        // pair against white (as before), the dark pair against a near-black Fluent dark surface.
        Current.Resources["SuccessBrush"] = new SolidColorBrush(isDark
            ? Color.FromRgb(0x66, 0xBB, 0x6A)
            : Color.FromRgb(0x2A, 0x7A, 0x2A));
        Current.Resources["ProblemBrush"] = new SolidColorBrush(isDark
            ? Color.FromRgb(0xEF, 0x53, 0x50)
            : Color.FromRgb(0xB0, 0x00, 0x20));

        // StatusText/NextEntryText: a primary/secondary text pair, not just one flat color, so the
        // status line reads as more prominent than the italic hint below it — same relationship in
        // both themes, not independently eyeballed per-theme. Light keeps its original two grays;
        // dark uses two light grays holding roughly the same relative step (secondary sits about one
        // WCAG contrast band dimmer than primary), both still comfortably above 4.5:1 against a
        // near-black Fluent dark surface. If either pair ever needs adjusting, shift both together to
        // keep that step consistent, rather than tweaking one line in isolation.
        Current.Resources["PrimaryStatusTextBrush"] = new SolidColorBrush(isDark
            ? Color.FromRgb(0xE6, 0xE6, 0xE6)
            : Color.FromRgb(0x44, 0x44, 0x44));
        Current.Resources["SecondaryStatusTextBrush"] = new SolidColorBrush(isDark
            ? Color.FromRgb(0xAF, 0xAF, 0xAF)
            : Color.FromRgb(0x66, 0x66, 0x66));

        // The Model nudge's rest color (the Log button's look before it starts pulsing) and
        // highlight color (the pulse's peak) both need a dark-mode variant, not just the highlight —
        // otherwise the animation would flash from a bright light-mode rest color even in dark mode.
        // The dark highlight is deliberately dimmer/less saturated than the light one, so the pulse
        // stays easy on the eyes against a dark background rather than glaring.
        Current.Resources["ModelNudgeRestColor"] = isDark
            ? Color.FromRgb(0x2D, 0x2D, 0x30)
            : Colors.WhiteSmoke;
        Current.Resources["ModelNudgeHighlightColor"] = isDark
            ? Color.FromRgb(0xC7, 0x9A, 0x2E)
            : Color.FromRgb(0xFF, 0xCA, 0x28);

        // Empty at startup (called before any window exists); on a runtime toggle, this covers the
        // main window and any open dialog alike.
        foreach (Window window in Current.Windows)
        {
            TitleBarTheme.Apply(window);
        }
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath, $"{DateTime.UtcNow:O} - {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception logEx) when (logEx is IOException or UnauthorizedAccessException)
        {
            // Best-effort logging only - a failure here must never mask the original crash.
        }
    }
}
