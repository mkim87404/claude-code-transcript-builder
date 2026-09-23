using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using TranscriptBuilder.Models;
using TranscriptBuilder.Services;

namespace TranscriptBuilder;

// Single-window UI: pick a project, compose a prompt, and log it to that project's AI_TRANSCRIPT.txt.
public partial class MainWindow : Window
{
    // A user-facing outcome message; problems are shown in the error colour.
    private readonly record struct Note(string Text, bool IsProblem = false);

    private SessionState? _session;
    private AppThemePreference _themePreference = AppThemePreference.Light;

    // Guards the flash to once per project load, even though RefreshEntryDependentUi (which triggers
    // it) runs on every keystroke in the compose box while this window is open.
    private bool _hasShownModelNudgeThisLoad;

    public MainWindow()
    {
        InitializeComponent();
    }

    // Thin null-safe wrapper: the actual condition lives on SessionState.NeedsModelNudge (Core, tested),
    // shared here by both the status-text nudge (DescribeProgress) and the one-off button flash so the
    // two can never disagree about when this moment is.
    private bool NeedsModelNudge => _session?.NeedsModelNudge ?? false;

    // The earliest point this window's HWND exists, so the title bar is themed before it's shown
    // (App.OnStartup already applied the saved theme to the Fluent styling before construction).
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TitleBarTheme.Apply(this);
    }

    // Deferred to Loaded so the CLAUDE.md picker (if needed) has a visible owner window.
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = SettingsService.Load();

        _themePreference = ThemePreferenceService.Parse(settings.ThemePreference);
        UpdateThemeToggleButtonLabel();

        // Width/Height hold the XAML-declared default here (sized so every top-bar button fits on
        // one line), used whenever nothing valid is remembered.
        Width = WindowSizing.Resolve(settings.WindowWidth, Width, MinWidth, SystemParameters.WorkArea.Width);
        Height = WindowSizing.Resolve(settings.WindowHeight, Height, MinHeight, SystemParameters.WorkArea.Height);

        TryResumeLastProject();
    }

    // Position isn't remembered, only size — a saved position can land off-screen after a monitor
    // change or resolution swap, a failure mode size alone doesn't have.
    private void Window_Closing(object sender, CancelEventArgs e)
    {
        var settings = SettingsService.Load();

        // RestoreBounds, not Width/Height, while maximized — those report the full-screen size in
        // that state, which isn't a meaningful "normal" size to bring back on the next launch.
        var maximized = WindowState == WindowState.Maximized;
        settings.WindowWidth = maximized ? RestoreBounds.Width : Width;
        settings.WindowHeight = maximized ? RestoreBounds.Height : Height;
        SettingsService.Save(settings);
    }

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _themePreference = ThemePreferenceService.Toggle(_themePreference);
        App.ApplyTheme(_themePreference);
        UpdateThemeToggleButtonLabel();
        ResetModelNudgeVisualState();

        var settings = SettingsService.Load();
        settings.ThemePreference = _themePreference.ToString();
        SettingsService.Save(settings);
    }

    private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select the project root directory" };
        if (_session is not null)
        {
            dialog.InitialDirectory = _session.ProjectRoot;
        }

        if (dialog.ShowDialog(this) == true && !string.IsNullOrEmpty(dialog.FolderName))
        {
            LoadProject(dialog.FolderName);
        }
    }

    private void ComposeBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            LogCurrentComposeBoxText();
            return;
        }

        // WPF's default is a no-op once the caret can't move further vertically — it stays at its
        // current column on the boundary line. Pressing Up/Down again instead jumps to the very
        // start/end of the text, matching the behavior of most text editors. Plain Up/Down only, so
        // Shift-extend and other modified variants keep their default behavior.
        if (e.Key == Key.Up && Keyboard.Modifiers == ModifierKeys.None
            && ComposeBox.GetLineIndexFromCharacterIndex(ComposeBox.CaretIndex) == 0)
        {
            e.Handled = true;
            ComposeBox.CaretIndex = 0;
        }
        else if (e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.None
                 && ComposeBox.GetLineIndexFromCharacterIndex(ComposeBox.CaretIndex) == ComposeBox.LineCount - 1)
        {
            e.Handled = true;
            ComposeBox.CaretIndex = ComposeBox.Text.Length;
        }
    }

    private void ComposeBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshEntryDependentUi();

    private void LogButton_Click(object sender, RoutedEventArgs e) => LogCurrentComposeBoxText();

    // Logs the selected/typed model as its own entry and copies the exact command to switch to it.
    private void LogModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        var modelValue = ModelComboBox.Text.Trim();
        if (modelValue.Length == 0)
        {
            SetFooter(new Note("Enter or select a model first.", IsProblem: true));
            return;
        }

        if (!LogCommandEntry(_session, EntryHeadings.Model, $"/model {modelValue}", " Your model selection is still in the box.",
                session => session.AfterModelLogged(), $"Logged Model: {modelValue}."))
        {
            return;
        }

        // Remembered across all projects, mirroring /model's own cross-session default persistence,
        // so the combo box can be pre-filled next time instead of the user needing to know the value.
        var settings = SettingsService.Load();
        settings.LastKnownModel = modelValue;
        SettingsService.Save(settings);
    }

    // /compact and /clear are exact, argument-free Claude Code commands (verified against Claude
    // Code's own docs), picked from a closed list since there's nothing for the user to get right or
    // wrong themselves. Not gated on CLAUDE.md being logged, for the same reason Model isn't: it uses
    // its own dedicated heading, never the shared paste-classification path, so it carries none of
    // the "might overwrite the real CLAUDE.md" risk that gates Audit Prompt.
    private void LogSessionCommandButton_Click(object sender, RoutedEventArgs e)
    {
        var command = SessionCommandComboBox.Text;
        if (_session is null || EntryHeadings.ForSessionCommand(command) is not { } heading)
        {
            return;
        }

        LogCommandEntry(_session, heading, command, retrySuffix: "", session => session.AfterMarkerLogged(), $"Logged {heading}.");
    }

    // Only ever edits the compose box — no logging, no clipboard, no session change. Replaces empty/
    // whitespace content; otherwise appends after one blank line so an existing draft is preserved.
    // A blank line also isolates the limit line from the base prompt paragraph above it, so it stays
    // a clean, self-contained block even if the selection below is lost (e.g. the user clicks away)
    // and it needs a manual Shift+Up+Delete instead.
    private void InsertAuditPromptButton_Click(object sender, RoutedEventArgs e)
    {
        var template = $"{AuditPrompt.Effective(SettingsService.Load().CustomAuditPromptText)}\n\n{AuditPrompt.LimitLine}";

        var existingDraft = ComposeBox.Text;
        ComposeBox.Text = string.IsNullOrWhiteSpace(existingDraft)
            ? template
            : $"{existingDraft.TrimEnd()}\n\n{template}";

        ComposeBox.Focus();

        // Selects the whole limit line, not just <area> — one Backspace removes the entire optional
        // line for someone who doesn't want it, no Shift+Up+Delete knowledge required; a user who
        // does want it can press Right once (lands right after <area>, WPF's default for collapsing
        // a selection) and delete just that token. ComposeBox.Text is known to end with exactly
        // LimitLine by construction above, so no search is needed to find it.
        ComposeBox.Select(ComposeBox.Text.Length - AuditPrompt.LimitLine.Length, AuditPrompt.LimitLine.Length);
        ComposeBox.ScrollToEnd();
    }

    // Opens the modal editor pre-filled with whatever Insert Audit Prompt would currently insert
    // (custom if saved, else the built-in default), and persists whatever comes back from it.
    private void EditAuditPromptButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = SettingsService.Load();

        var dialog = new EditAuditPromptWindow(AuditPrompt.Effective(settings.CustomAuditPromptText)) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        settings.CustomAuditPromptText = dialog.SavedCustomPrompt;
        SettingsService.Save(settings);
        SetFooter(new Note(settings.CustomAuditPromptText is null
            ? "Audit prompt reset to the built-in default."
            : "Custom audit prompt saved."));
    }

    // Reveals AI_TRANSCRIPT.txt in a File Explorer window with it pre-selected, rather than opening
    // the file directly — Explorer's /select is a core shell feature with nothing to be unassociated,
    // unlike opening a .txt with whatever (if anything) the user has set as their default text editor.
    private void RevealTranscriptButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || !File.Exists(_session.TranscriptPath))
        {
            return;
        }

        try
        {
            // Disposing releases only this app's handle to the launched process, not Explorer itself.
            using var explorer = Process.Start("explorer.exe", $"/select,\"{_session.TranscriptPath}\"");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            SetFooter(new Note($"Could not open File Explorer: {ex.Message}", IsProblem: true));
        }
    }

    // Re-reads CLAUDE.md fresh from disk and logs it, unless it's unchanged since the last time it
    // (or an update) was logged — avoiding transcript noise from an accidental repeat click.
    private void LogClaudeMdUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        if (!ClaudeMdService.TryReadAtRoot(_session.ProjectRoot, out var currentContent))
        {
            SetFooter(new Note("CLAUDE.md no longer exists at the project root.", IsProblem: true));
            RefreshEntryDependentUi();
            return;
        }

        if (!TranscriptService.HasClaudeMdChangedSinceLastLogged(_session.ProjectRoot, currentContent))
        {
            SetFooter(new Note("No changes since the last logged CLAUDE.md — nothing to log."));
            return;
        }

        if (!TryAppendEntry(_session, EntryHeadings.ClaudeMdUpdate, currentContent, retrySuffix: "", out var writeErrorNote))
        {
            SetFooter(writeErrorNote);
            return;
        }

        _session = _session.AfterMarkerLogged();
        StatusText.Text = DescribeProgress("AI_TRANSCRIPT.txt");
        SetFooter(new Note("Logged CLAUDE.md Update."));
        RefreshEntryDependentUi();
    }

    private void TryResumeLastProject()
    {
        var settings = SettingsService.Load();
        if (!string.IsNullOrWhiteSpace(settings.LastProjectPath) && Directory.Exists(settings.LastProjectPath))
        {
            LoadProject(settings.LastProjectPath);
        }
    }

    // Selects a project: remembers it, then resumes or starts its transcript, seeding CLAUDE.md if missing.
    private void LoadProject(string projectRoot)
    {
        // Load-modify-save so remembered settings other than the project path are preserved.
        var settings = SettingsService.Load();
        settings.LastProjectPath = projectRoot;
        SettingsService.Save(settings);

        ProjectPathText.Text = projectRoot;

        try
        {
            OpenProject(projectRoot, settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without a readable/writable transcript there is nothing safe to log to, so every
            // logging control stays disabled.
            _session = null;
            ComposeBox.IsEnabled = false;
            LogButton.IsEnabled = false;
            ModelComboBox.IsEnabled = false;
            ModelComboBox.Text = string.Empty;
            LogModelButton.IsEnabled = false;
            SessionCommandComboBox.IsEnabled = false;
            LogSessionCommandButton.IsEnabled = false;
            InsertAuditPromptButton.IsEnabled = false;
            EditAuditPromptButton.IsEnabled = false;
            LogClaudeMdUpdateButton.IsEnabled = false;
            RevealTranscriptButton.IsEnabled = false;
            NextEntryText.Text = "";
            StatusText.Text = $"Could not open this project's files: {ex.Message}";
            SetFooter(new Note("Fix the problem or choose another folder.", IsProblem: true));
            return;
        }

        ComposeBox.IsEnabled = true;
        LogButton.IsEnabled = true;
        // Model logging carries no CLAUDE.md-specific risk, so unlike Audit Prompt and CLAUDE.md
        // Update (gated in RefreshEntryDependentUi) it's available as soon as a project is loaded.
        // Pre-filled with the last model logged anywhere, never forced — just a starting suggestion
        // the user can override if they deliberately switched models since (see NOTES.md).
        ModelComboBox.IsEnabled = true;
        ModelComboBox.Text = settings.LastKnownModel ?? string.Empty;
        LogModelButton.IsEnabled = true;
        // Same reasoning as Model: /compact and /clear carry no CLAUDE.md-specific risk either.
        SessionCommandComboBox.IsEnabled = true;
        LogSessionCommandButton.IsEnabled = true;
        ComposeBox.Focus();
    }

    private void OpenProject(string projectRoot, AppSettings settings)
    {
        _session = TranscriptService.InspectTranscript(projectRoot);
        _hasShownModelNudgeThisLoad = false;

        // Copy first, log second: a crash in between self-heals on the next launch, because the
        // copied CLAUDE.md is then found at the root while the transcript still needs its entry.
        var copyNote = ClaudeMdService.ExistsAtRoot(projectRoot) ? (Note?)null : OfferClaudeMdCopy(projectRoot, settings);
        Note? logNote = null;

        if (_session.NextEntryKind != NextEntryKind.ClaudeMd)
        {
            StatusText.Text = DescribeProgress("AI_TRANSCRIPT.txt found");
        }
        else if (ClaudeMdService.TryReadAtRoot(projectRoot, out var claudeMdContent))
        {
            TranscriptService.AppendEntry(_session, _session.NextHeading, claudeMdContent);
            _session = _session.AfterEntryLogged();
            StatusText.Text = DescribeProgress("CLAUDE.md auto-logged from disk");
            logNote = new Note("Logged CLAUDE.md automatically from disk.");
        }
        else
        {
            StatusText.Text = "CLAUDE.md not found at the project root. Paste its content below and log it first.";
        }

        SetFooter(copyNote, logNote);
        RefreshEntryDependentUi();
    }

    // Shows a native picker (preselecting the last CLAUDE.md used) and copies the choice into the
    // project root; returns null if the user cancelled.
    private Note? OfferClaudeMdCopy(string projectRoot, AppSettings settings)
    {
        var dialog = new OpenFileDialog
        {
            Title = $"CLAUDE.md not found in {projectRoot} — select the CLAUDE.md to copy into it",
            Filter = "CLAUDE.md|CLAUDE.md|Markdown files (*.md)|*.md|All files (*.*)|*.*",
            CheckFileExists = true
        };

        var lastSource = settings.LastClaudeMdSourcePath;
        if (!string.IsNullOrWhiteSpace(lastSource) && File.Exists(lastSource))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(lastSource);
            dialog.FileName = Path.GetFileName(lastSource);

            // The file only appears (and gets highlighted) if the active filter includes it.
            dialog.FilterIndex = string.Equals(dialog.FileName, "CLAUDE.md", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return null;
        }

        try
        {
            ClaudeMdService.CopyIntoProject(dialog.FileName, projectRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Note($"Could not copy CLAUDE.md: {ex.Message}", IsProblem: true);
        }

        settings.LastClaudeMdSourcePath = dialog.FileName;
        SettingsService.Save(settings);
        return new Note("Copied CLAUDE.md into the project root.");
    }

    // Updates everything that depends on _session's current state: the live "next entry" label,
    // gating the audit-prompt and CLAUDE.md Update buttons on CLAUDE.md already being logged, and
    // Reveal on the transcript file existing. Model and Session carry no such risk and stay enabled
    // from project load (set in LoadProject).
    private void RefreshEntryDependentUi()
    {
        if (_session is null)
        {
            return;
        }

        NextEntryText.Text = $"Next entry: {EntryClassifier.Classify(_session, ComposeBox.Text).Heading}";

        var claudeMdLogged = _session.NextEntryKind == NextEntryKind.Prompt;
        InsertAuditPromptButton.IsEnabled = claudeMdLogged;
        EditAuditPromptButton.IsEnabled = claudeMdLogged;
        LogClaudeMdUpdateButton.IsEnabled = claudeMdLogged;

        // The file only exists once the first entry is written, whichever type that is (Model can
        // come before CLAUDE.md), so this can't reuse claudeMdLogged. A cheap existence check, redone
        // on every refresh rather than tracked, since any log action can be the one that creates it.
        RevealTranscriptButton.IsEnabled = File.Exists(_session.TranscriptPath);

        if (NeedsModelNudge)
        {
            FlashLogModelButtonHint();
        }
    }

    // A brief, self-terminating color pulse — not a sustained or repeating blink. Guarded to fire once
    // per project load (see _hasShownModelNudgeThisLoad); this method runs on every keystroke via
    // RefreshEntryDependentUi, so re-arming per call would replay the animation on every character typed.
    private void FlashLogModelButtonHint()
    {
        if (_hasShownModelNudgeThisLoad)
        {
            return;
        }

        _hasShownModelNudgeThisLoad = true;

        // Theme-dependent, so looked up fresh rather than cached — see App.ApplyTheme.
        var restColor = (Color)Application.Current.Resources["ModelNudgeRestColor"];
        var highlightColor = (Color)Application.Current.Resources["ModelNudgeHighlightColor"];

        // Always a fresh, private, unfrozen brush starting from the current theme's rest color —
        // never a reused or theme-shared one (see ResetModelNudgeVisualState for why that matters).
        var brush = new SolidColorBrush(restColor);
        LogModelButton.Background = brush;

        // Warm, WCAG-safe highlight: a few slow pulses (well under the 3-per-second flash threshold
        // that risks triggering seizures), never a rapid or sustained blink.
        var animation = new ColorAnimation
        {
            To = highlightColor,
            Duration = TimeSpan.FromMilliseconds(450),
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(3)
        };

        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    // The flash leaves a local Background on the button, which overrides the Fluent style until
    // cleared — without this, toggling the theme after a flash left the button stuck on the old
    // theme's rest color (hover still looked right, since Fluent draws hover on a separate layer).
    private void ResetModelNudgeVisualState() => LogModelButton.ClearValue(Button.BackgroundProperty);

    private void UpdateThemeToggleButtonLabel()
    {
        var isDark = _themePreference == AppThemePreference.Dark;
        ThemeToggleButton.Content = isDark ? "☀️ Light Mode" : "🌙 Dark Mode";

        // Kept in step with the visible label; a fixed name would announce "dark mode" while the
        // button actually switches to light.
        AutomationProperties.SetName(ThemeToggleButton, isDark ? "Switch to light mode" : "Switch to dark mode");
    }

    // Shared by the Model and Session groups: logs a non-numbered entry whose body is a pasteable
    // command, copies that command, and advances the session. Returns false (with the failure
    // already shown) if the write failed, so the caller does nothing further.
    private bool LogCommandEntry(SessionState session, string heading, string command, string retrySuffix,
        Func<SessionState, SessionState> advance, string loggedMessage)
    {
        if (!TryAppendEntry(session, heading, command, retrySuffix, out var writeErrorNote))
        {
            SetFooter(writeErrorNote);
            return false;
        }

        var clipboardNote = ClipboardNote(command, "Copied to clipboard — paste into the Claude Code terminal.");

        _session = advance(session);
        StatusText.Text = DescribeProgress("AI_TRANSCRIPT.txt");
        SetFooter(new Note(loggedMessage), clipboardNote);
        RefreshEntryDependentUi();
        return true;
    }

    // Logs the compose box as a Prompt, the CLAUDE.md entry, or a Session Exit, then copies the result to the clipboard.
    private void LogCurrentComposeBoxText()
    {
        if (_session is null)
        {
            return;
        }

        var text = ComposeBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetFooter(new Note("Nothing to log — the compose box is empty.", IsProblem: true));
            return;
        }

        var entry = EntryClassifier.Classify(_session, text);

        // A manually pasted CLAUDE.md becomes the project's CLAUDE.md too; created before logging,
        // and a failure never blocks the transcript entry, which is the primary record.
        var fileNote = _session.NextEntryKind == NextEntryKind.ClaudeMd
            ? CreateClaudeMdFromPaste(_session.ProjectRoot, entry.Body)
            : (Note?)null;

        // Nothing was logged and the compose box is untouched on failure, so the entry can simply be retried.
        if (!TryAppendEntry(_session, entry.Heading, entry.Body, " Your text is still in the compose box.", out var writeErrorNote))
        {
            SetFooter(writeErrorNote);
            return;
        }

        // Copy only after the append is durably on disk, so the next action is a paste into the VS Code
        // terminal (prompt) or a resume (Session Exit) — no reselecting text in this app.
        var clipboardNote = ClipboardNote(entry.Body,
            entry.IsSessionExit ? "Resume command copied to clipboard." : "Copied to clipboard — paste into the VS Code terminal.");

        ComposeBox.Clear();
        _session = entry.IsSessionExit ? _session.AfterMarkerLogged() : _session.AfterEntryLogged();

        StatusText.Text = DescribeProgress("AI_TRANSCRIPT.txt");
        SetFooter(new Note($"Logged {entry.Heading}."), clipboardNote, fileNote);
        RefreshEntryDependentUi();
        ComposeBox.Focus();
    }

    private string DescribeProgress(string label)
    {
        var text = $"{label} — {_session!.EntryCount} entr{(_session.EntryCount == 1 ? "y" : "ies")} logged. Next: Prompt #{_session.NextPromptNumber}.";

        // Soft nudge only, never a gate: shown solely in the window between CLAUDE.md and Prompt #1
        // (NextPromptNumber == 1 stops being true the moment that first prompt is logged), so it never
        // nags on later prompts if that window was deliberately skipped.
        if (NeedsModelNudge)
        {
            text += " Model not yet logged for this project — consider logging it before your first prompt.";
        }

        return text;
    }

    // Joins the notes into the footer and colours it as a problem if any note is one.
    private void SetFooter(params Note?[] notes)
    {
        var present = notes.OfType<Note>().ToList();
        FooterText.Text = string.Join(" ", present.Select(note => note.Text));
        FooterText.SetResourceReference(TextBlock.ForegroundProperty, present.Any(note => note.IsProblem) ? "ProblemBrush" : "SuccessBrush");
    }

    private static Note CreateClaudeMdFromPaste(string projectRoot, string content)
    {
        try
        {
            ClaudeMdService.CreateAtRoot(projectRoot, content);
            return new Note("Created CLAUDE.md in the project root.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ClaudeMdService.ExistsAtRoot(projectRoot)
                ? new Note("CLAUDE.md already exists in the project root; left untouched.")
                : new Note($"Could not create CLAUDE.md in the project root: {ex.Message}", IsProblem: true);
        }
    }

    // Copies text and describes the outcome; the failure wording is the same wherever a copy happens.
    private static Note ClipboardNote(string text, string successMessage) =>
        TrySetClipboard(text)
            ? new Note(successMessage)
            : new Note("Could not copy to the clipboard (another app is using it).", IsProblem: true);

    // The clipboard can be held by another process (CLIPBRD_E_CANT_OPEN, surfaced as a COMException).
    private static bool TrySetClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    // Wraps AppendEntry with the IO/permission-failure handling shared by every click handler that logs
    // an entry: on failure, nothing was written, and retrySuffix names whatever the caller's own input
    // is still safe in (a combo box, a compose box) so the user knows it's fine to just try again.
    private static bool TryAppendEntry(SessionState session, string heading, string body, string retrySuffix, out Note? errorNote)
    {
        try
        {
            TranscriptService.AppendEntry(session, heading, body);
            errorNote = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errorNote = new Note($"Could not write AI_TRANSCRIPT.txt: {ex.Message}{retrySuffix}", IsProblem: true);
            return false;
        }
    }
}
