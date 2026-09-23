# Notes

Design decisions and their reasoning, newest first.

## 2026-09-23 (UTC)

### Dark mode: WPF's native Fluent `ThemeMode`, not a hand-rolled ResourceDictionary swap
- **Why Fluent:** WPF ships a first-party Fluent theme with a `ThemeMode` property (`Light`/`Dark`/`System`/`None`) since .NET 9, continuing in .NET 10 ([What's new in WPF for .NET 9](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net90), [using-fluent.md](https://github.com/dotnet/wpf/blob/main/Documentation/docs/using-fluent.md)). It restyles every standard control (Button/TextBox/ComboBox chrome) for both themes; a hand-rolled `ResourceDictionary` swap would have needed a custom `ControlTemplate` per control to look right in dark mode. It adds no NuGet dependency, keeping the app dependency-free.
- **Trade-off accepted:** setting `ThemeMode` from code (needed for a runtime toggle, not just a fixed XAML value) is marked experimental (`WPF0001`) as of .NET 10 and "subject to breaking changes in future .NET releases." It's suppressed via `<NoWarn>` in the `.csproj`; the TFM (`net10.0-windows`) is pinned, so the risk is bounded to revisiting this one spot on a deliberate future TFM bump.
- **What Fluent doesn't cover:** the app's own semantic colors — `SuccessBrush`/`ProblemBrush` (footer), the status-text pair, `GroupBorderBrush`, and the Model nudge's rest/highlight colors — are set per theme in `App.ApplyTheme`. The nudge's *rest* color has a dark variant too, not just its highlight, so the pulse never flashes from a bright light-mode color in dark mode; the dark highlight is deliberately less saturated so it stays easy on the eyes.
- **Title bar:** `ThemeMode` restyles window content but not OS-drawn chrome, so `TitleBarTheme` calls `DwmSetWindowAttribute` with `DWMWA_USE_IMMERSIVE_DARK_MODE` (`20`, Windows 11 Build 22000+, per [Microsoft's DWMWINDOWATTRIBUTE reference](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)). Every window applies it in `OnSourceInitialized` (the earliest point its HWND exists, so it's themed before being shown), and `App.ApplyTheme` re-applies it to every open window on a toggle, dialogs included. Older Windows 10 keeps a default-colored title bar; the rest of dark mode is unaffected.
- **Architecture:** a Core-only `AppThemePreference` enum plus `ThemePreferenceService` (parse, toggle), named differently from WPF's own `System.Windows.ThemeMode` because Core can't reference WPF types and the two would otherwise collide in scope. `AppSettings.ThemePreference` persists the choice; it's applied in `App.OnStartup` *before* `base.OnStartup` creates the window, so there's no flash of the wrong theme at launch.
- **Parsing is by name only.** `settings.json` is user-editable, and `Enum.TryParse` also accepts numeric strings (`"5"` → an undefined value, `"1"` → Dark). Anything that isn't exactly a theme name, case-insensitive, falls back to Light.
- **No Window-level brush definitions:** WPF resource lookup checks the Window before the Application, so a local copy of any themed brush would permanently shadow the runtime theme swap.

### Dark-mode status text: a primary/secondary pair, adjusted together
`StatusText`/`NextEntryText` use `DynamicResource`-bound brushes (`PrimaryStatusTextBrush`/`SecondaryStatusTextBrush`) rather than fixed hex, since an explicit local `Foreground` always wins over Fluent's dark default. They're defined as a pair with one rule: the secondary (italic hint) sits about one WCAG contrast band dimmer than the primary, in both themes, both comfortably above 4.5:1. Any future adjustment should shift both together. Fluent's own semantic text tokens were considered instead, but no documented, first-party key could be confirmed, and a `DynamicResource` on a missing key fails silently rather than throwing.

### Model nudge flash: always reset on theme toggle
The nudge flash sets a local `Background` on the Model group's Log button and animates it. A local value
overrides the Fluent style until explicitly cleared, and `App.ApplyTheme` only updates application resources,
so toggling the theme after a flash left the button stuck on the previous theme's rest color (hover still looked
right, since Fluent draws hover on a separate template layer). Two-part fix: every theme toggle calls
`ResetModelNudgeVisualState()` (`ClearValue` on the button's `Background`), and every flash starts from a
fresh brush in the *current* theme's rest color rather than reusing one. It's the only control whose
`Background` is set in code; every other themed color is consumed via `DynamicResource`/`SetResourceReference`,
which can't go stale this way.

### Timezone: auto-detected local zone with a numeric UTC offset
- **No configuration:** the second timestamp line exists for human readability next to the UTC line, so it uses the host's own zone (`TimeZoneInfo.Local`) — correct for every user with nothing to set. No on/off toggle: the rough location it implies is already present in ordinary `git commit` metadata, which records the committer's UTC offset by default.
- **A numeric offset (`+13:00`), not a named abbreviation:** abbreviations collide across zones (`IST` alone is India, Israel, or Ireland), and Windows exposes only verbose display names, not short ones.
- **Host-independent tests:** `TimestampService.GetTimestampPair` takes an optional `TimeZoneInfo` (default `TimeZoneInfo.Local`); the exact daylight-saving-boundary test injects a fixed zone, which Windows ships regardless of the host's configured zone. One test deliberately exercises the real default, checking only the line's shape, so it can't be flaky on another machine.

### Session commands: `/compact` and `/clear`
- **Fixed strings:** both work bare with no argument ([slash-commands.md](https://code.claude.com/docs/en/slash-commands.md)), so they're logged and copied verbatim, the same one-click flow as Model.
- **A button, not detection from typed text:** nothing is typed into Claude Code first; the app originates the command (the same direction as Model), so there's no text to detect. It's the same reasoning that rules out detecting `/model`.
- **Layout:** a bordered `Session:` group (closed two-item combo + Log), visually matching the Model group and placed beside it rather than merged into it, which would blur which controls belong to which feature. A combo rather than two separate buttons keeps the footprint small and leaves room for more commands without more window space.
- **Not gated on CLAUDE.md being logged**, like Model: gating exists only where a shared paste path could write into the real `CLAUDE.md` (Audit Prompt, CLAUDE.md Update), and this uses its own heading and button.
- **Deliberately excluded:** `/rewind` opens an interactive checkpoint picker rather than completing as one fixed command, so logging it wouldn't capture which checkpoint was chosen. Informational commands (`/usage`, `/cost`) don't affect session content. Token/cost tracking would need either manual pasting or breaking the app's fully-offline design, and tracking subdirectory/user-level CLAUDE.md files is a larger scope change than this app's current model.

### Button labels: "Log" for everything that writes to the transcript
A label describes what the app does, not what happens in Claude Code once the copied command is pasted. The
app never runs a command or sets a model; it writes an entry and copies the command. So every button that
writes to the transcript says **Log** (Log Entry, the Model and Session groups' Log buttons, Log CLAUDE.md
Update), and the ones that don't (Insert Audit Prompt, Edit…, Reveal in Explorer) don't. Two adjacent "Log"
buttons read unambiguously because each sits beside its own group label. The visible labels are short; each
has a fuller `AutomationProperties.Name` ("Log model", "Log session command") for screen readers.

### Audit prompt: whole-line highlight on the limit line, and user-editable wording
- **Insertion:** the saved prompt (`AuditPrompt.Effective`: the user's custom text, else `AuditPrompt.Base`) is appended to the compose box after one blank line, followed by `AuditPrompt.LimitLine` (`Limit this to <area>`, no trailing punctuation). The **whole** limit line is selected: one Backspace removes it for an unscoped audit, and → once lands the caret right after `<area>` to scope it. The blank line above it stays, so if the selection is lost the line is still a clean block for a manual Shift+Up+Delete.
- **Editable wording:** **Edit…** opens `EditAuditPromptWindow`, pre-filled with the effective text, with Save / Reset to Default / Cancel. `AuditPrompt.NormalizeCustom` stores `null` for empty or default-identical text, so "using the default" is one state and a future change to the built-in wording reaches anyone who never customized it.
- **The limit line itself isn't editable** in the dialog: the whole-line selection relies on it being a known, fixed string, and it's already one keystroke to remove or change once inserted.

### Reveal in Explorer
`explorer.exe /select,"<path>"` shows `AI_TRANSCRIPT.txt` pre-selected in a File Explorer window. It's a core
shell feature, unlike opening the `.txt` with whatever (if anything) is the default handler, which can
interrupt with an OS file-type chooser. It's enabled only once the file exists, which happens at the first
written entry of any type (Model can come before CLAUDE.md), so it has its own existence check rather than
reusing the CLAUDE.md gate. The returned `Process` is disposed immediately; that releases only this app's
handle, not Explorer.

### Window size: remembered, clamped to the screen, position excluded
- **Remembered size with a clean default:** `Window_Closing` saves the size (`RestoreBounds` while maximized, since `Width`/`Height` then report the full-screen size). At launch, `WindowSizing.Resolve` uses the remembered value if it's a real number no smaller than the window's minimum, otherwise the XAML default (`Width="1040"`, wide enough for the Model/Session/audit-prompt row to fit on one line).
- **Always clamped to the screen's work area:** WPF doesn't clamp an explicit size itself, and even the default can exceed a small screen (a 1366px-wide laptop at 150% scaling has roughly 910 DIPs of work area). The one hard floor is the window's own `MinWidth="620"`.
- **Position deliberately not remembered:** a saved position can land off-screen after a monitor or resolution change, with no visible way back; size has no such failure mode, and `WrapPanel` turns "too small" into a reflow, never a broken layout.

### Exe/product naming: "Claude Code Transcript Builder"
`AssemblyName`/`Product`/`AssemblyTitle` in `TranscriptBuilder.csproj` make the built `.exe` and every
Windows-surfaced label (Explorer, taskbar, title bar) read as the product name, without renaming the
`TranscriptBuilder` project/namespace/solution, which would ripple through every file for no visible benefit.
The `%AppData%\TranscriptBuilder` settings folder is also unchanged: it's an internal path, and renaming it
would silently reset existing users' remembered settings.

## 2026-09-22 (UTC)

### Error-handling, resource & crash safety: which concerns apply
A single-threaded WPF desktop app with no network, no background threads, and no async/await.
- **Deterministic disposal** — applies, satisfied: explicit streams (`ClaudeMdService.CreateAtRoot`, `TranscriptService.FileEndsWithNewline`) are in `using`; the `Process` from Reveal in Explorer is disposed immediately; everything else goes through BCL convenience methods that dispose internally.
- **Global crash safety net** — applies, satisfied: `App.OnStartup` registers `DispatcherUnhandledException`, `AppDomain.UnhandledException`, and `TaskScheduler.UnobservedTaskException`.
- **Bound the damage on abrupt termination** — applies, satisfied: every `AppendEntry` is one small atomic `File.AppendAllText`, never batched or buffered.
- **No leaked subscriptions, listeners, or timers** — applies, satisfied: event handlers are XAML-wired and torn down with their controls; the three app-level exception hooks are intentionally subscribed for the app's whole lifetime.
- **Graceful shutdown** — applies, satisfied: `Window_Closing` persists the window size with a single best-effort settings write. Transcript writes are already flushed and closed as they happen, so nothing else is pending at exit.
- **Shared-state & race safety, lock discipline, idempotency, async/streaming cleanup** — not applicable: nothing runs off the UI thread; no locks, retries, network calls, or async pipelines (`File.ReadLines` is a lazy synchronous enumerable).

### Model nudge: a brief visual flash on top of the text nudge
- **Why:** the text nudge sits at the end of an already-informative status line, so a one-off visual cue marks the single moment it matters.
- **WCAG:** content flashing more than 3 times per second is a seizure risk; this is a slow color pulse (450ms per half-cycle, 3 cycles ≈ 2.7s), finite and never repeating.
- **Fires once per project load:** `RefreshEntryDependentUi`, which triggers it, runs on every keystroke in the compose box, so `_hasShownModelNudgeThisLoad` (reset only in `OpenProject`) stops the pulse restarting on each character.
- **One condition:** `SessionState.NeedsModelNudge` (`NextEntryKind == Prompt && NextPromptNumber == 1 && !HasLoggedModel`) drives both the text nudge and the flash, so they can't disagree; it's pure Core logic with its own test.
- **Frozen-brush safety:** a style-provided `Background` is typically a frozen shared brush, and `BeginAnimation` throws on a frozen `Freezable`; the flash always animates its own fresh, unfrozen brush (see the theme-toggle reset above).

### Persisted settings: safe for a first-time user and across upgrades
- **Every read tolerates absence:** `SettingsService.Load()` returns a fresh all-null `AppSettings` for a missing file and for corrupt JSON (including a non-number in a numeric field); every consumer guards its field. `AppSettings` is a plain mutable class, not a record with required constructor arguments, so a file from an older version missing newer fields deserializes those fields to `null` rather than throwing.
- **Launching alone never writes:** a first launch creates no `settings.json` until the user picks a project, changes a setting, or closes the window (which saves its size).
- **Testable path:** `Load`/`Save` take an optional path (defaulting to the real `%AppData%` location), so `SettingsServiceTests` exercises the real code against temp files; a shared `AssertAllNull` helper covers every remembered field, so a newly added one missing from the tests is visible.

### Model logging at project start: nudge and pre-fill, deliberately not a gate
- **Two distinct problems:** "I don't know which model is configured" and "I forget to log it" need different fixes; a gate addresses only the second, and forcing an answer the user can't give just produces a wrong one.
- **Pre-fill:** `/model <value>` saves as Claude Code's default for new sessions, so the app mirrors that: `AppSettings.LastKnownModel` is updated on every Model log (any project) and pre-fills the combo box on every project load.
- **Rejected — a gate:** CLAUDE.md is gated because Claude Code won't load the project's rules without it; Model is informational, and the session runs identically either way.
- **Rejected — auto-logging a guessed Model entry:** the remembered default is wrong in exactly the case that matters (a deliberate switch before the first prompt), and a silently wrong entry is worse than a visible gap.
- **Built instead:** a soft, self-clearing nudge, shown only between CLAUDE.md resolving and Prompt #1 being logged; once Prompt #1 is logged it stops appearing, so skipping it is a quiet, accepted choice. `HasLoggedModel` is tracked in the same streaming pass `InspectTranscript` already performs, and `SessionState.AfterModelLogged()` sets it in memory on a successful log.
- **Sequencing:** Model states which model is about to run under the rules CLAUDE.md just established, so it follows CLAUDE.md rather than preceding it.

### Model group, compose-box navigation
- **Grouping:** the model combo box and its Log button sit inside a labelled `Border`, which fixes a visual grouping problem without a reveal-on-click popup (that would defeat free-text entry and turn one click into three).
- **Up/Down at the boundary line:** WPF's `TextBox` does nothing when the caret can't move further vertically. Pressing Up on the first display line jumps to position 0, and Down on the last jumps to the end, using the wrap-aware `GetLineIndexFromCharacterIndex`/`LineCount`. Plain Up/Down only; Shift+Up/Down (extend selection) isn't built, since `TextBox` doesn't expose which end of a selection is the anchor.
- **Not built:** placeholder text in the empty compose box; an "undo last entry" (it would have to truncate a durably written file, against the append-only, crash-safe design).

### Model, Audit Prompt, and CLAUDE.md Update logging
- **Model:** heading `Model`, body normalized to the exact pasteable `/model <value>` command, as Session Exit normalizes `claude --resume <id>`. One control serves both a session's first model and any later switch, since the app can't detect either (no IPC into a running `claude` process). Presets are the documented aliases (opus, sonnet, haiku, opusplan, best, fable, default; code.claude.com/docs/en/model-config.md) plus free text, so a full model ID or `opus[1m]` works too.
- **Rejected — detecting `/model`'s confirmation text:** it has no documented, stable output format, unlike a resume line's guaranteed UUID shape.
- **Audit Prompt:** only edits the compose box (no clipboard, no logging, no session change); the normal Log Entry path logs it as a Prompt, since an audit request is a genuine instruction to Claude Code. Gated on CLAUDE.md already being logged: before that, the compose box is in CLAUDE.md mode, and logging would write the audit prompt into the project's real `CLAUDE.md`.
- **CLAUDE.md Update:** heading `CLAUDE.md Update`; re-reads the file fresh from disk and logs it, gated on CLAUDE.md already being logged, with no clipboard copy (nothing to paste elsewhere).
- **Rejected — a `FileSystemWatcher`** for CLAUDE.md Update: it would fire on every intermediate save or formatter touch, with no way to tell when an edit is final.
- **Skip if unchanged:** `TranscriptService.HasClaudeMdChangedSinceLastLogged` reuses the single streaming pass and the shared `HeadingWindow` heading detector, keeps only the most recent CLAUDE.md/Update body, and compares with line endings normalized (`\r\n`/`\r` → `\n`), since the file and the transcript's copy can differ only in line endings.
- **First entry of any type:** `AppendEntry`'s handling of existing content (clear whitespace-only, separate unrecognized text) keys on `EntryCount == 0`, not on the heading being CLAUDE.md, since Model can now be the first entry written.

## 2026-09-20 (UTC)

### CLAUDE.md seeding (picker copy + paste-created file)
- **Why:** Claude Code only auto-loads `CLAUDE.md` from the project root, so a project missing one runs without the user's rules. When it's missing on launch or project change, the app offers a native picker (preselecting the last-used file via `InitialDirectory`/`FileName`, with `FilterIndex` set so a non-`CLAUDE.md`-named source is still visible and highlighted) and copies the choice into the root.
- **Behavior matrix:** picker runs whenever root CLAUDE.md is missing. Selected → exact copy, then logged only if the transcript still needs the CLAUDE.md entry. Cancelled + entry needed → manual paste, and the pasted text is also created as `CLAUDE.md` at the root. Cancelled + entry already logged → proceed, nothing created. The paste-created file is tied strictly to the CLAUDE.md entry, so prompts and Session Exit markers never create it.
- **Safety:** copy is `File.Copy(overwrite:false)` (byte-identical including BOM/line endings, always named `CLAUDE.md`); paste creation uses `FileMode.CreateNew`, BOM-less UTF-8. Neither can overwrite a file that appears in the meantime. The file is written before the transcript entry, so a crash between the two self-heals on next launch. A write failure never blocks logging the pasted entry, since the transcript is the primary record.
- **Settings:** `LoadProject` does load-modify-save, so choosing a project never erases other remembered settings. Resuming the last project happens on the window's `Loaded` event, so the picker has a visible owner.

### Architecture
- **Core/UI split:** logic lives in `TranscriptBuilder.Core` (UI-free, `net10.0`), referenced by the WPF app and the tests, so tests never rebuild or lock a running app. CLAUDE.md file operations live in `ClaudeMdService`, transcript parsing/appending in `TranscriptService`, heading strings once in `EntryHeadings`, and session-state transitions (`AfterEntryLogged`/`AfterMarkerLogged`/`AfterModelLogged`, `NextHeading`) on `SessionState`.
- **One classification path:** what a compose-box paste logs as is decided by the pure `EntryClassifier`, which drives both the live "Next entry" label and the log action, so the two can never disagree. It also makes that decision directly testable: an early refactor that passed the same variable as both input and `out` argument to `SessionMarker.TryParse` would have blanked every non-resume prompt, and would have compiled cleanly.
- **Shared UI helpers:** the Model and Session groups share `LogCommandEntry`; every clipboard copy reports its outcome through `ClipboardNote`; every log action handles write failures through `TryAppendEntry`, which keeps the user's input and names where it's still safe to retry from.
- **Trust boundaries:** transcript content is untrusted, so a heading number is parsed with `TryParse` and a range check (`>= 1`, `< int.MaxValue`) and non-ASCII digits are ignored; `Prompt #99999999999999999999` can't crash the app on launch. `settings.json` is user-editable and parsed defensively (see Persisted settings, and theme parsing above).
- **Error handling:** a locked/unwritable transcript, a busy clipboard (`ExternalException`), and unwritable settings/crash-log locations never crash the app or lose the typed prompt; problems are shown in a distinct error color with explicit text.
- **Accessibility:** the compose box has an accessible name and help text; status/footer regions are UI Automation live regions; every short-labelled button carries a fuller accessible name; text colors meet WCAG AA (>= 4.5:1) in both themes.
- **Implicit usings:** the WPF SDK removes `System.IO`/`System.Threading` from implicit usings (avoiding a `Path` clash with `System.Windows.Shapes`), so those `using`s are required in the WPF project but redundant in the plain-SDK Core library.
- **Testing:** MSTest, xUnit v3 and NUnit all support .NET 10 ([Microsoft Learn: testing in .NET](https://learn.microsoft.com/en-us/dotnet/core/testing/)); MSTest was chosen as Microsoft's first-party framework, with no extra runner packages. Mock-free unit tests cover the pure helpers; real-filesystem integration tests cover the transcript/CLAUDE.md flows (first launch, copy, paste-create, resume-from-disk equals in-memory, garbage/whitespace first entries, decoy and hostile headings, no file handle left open).
- **Dependencies:** the app and Core use no third-party packages; the only NuGet dependency is `MSTest`, in the test project. The `.csproj` files are the dependency manifests.
- **Known gap:** the WPF windows themselves (dialogs, rendering, clipboard) have no automated tests; they're kept deliberately thin, with pure logic moved to Core where it can be tested.
- **This repository's own transcript:** `AI_TRANSCRIPT.txt` and `CLAUDE.md` from developing this app hold personal prompt history, so both are excluded via `.gitignore`.
- **Sources:** [.NET download page](https://dotnet.microsoft.com/download/dotnet/10.0), [Claude Code sessions](https://code.claude.com/docs/en/sessions.md), [Microsoft Learn: testing in .NET](https://learn.microsoft.com/en-us/dotnet/core/testing/).

## 2026-09-19 (UTC)

### Session Exit markers (`claude --resume <id>`)
- **Verified facts** (code.claude.com/docs/en/sessions.md): a session ID stays the same across `--resume`/`--continue`/`/resume`, and changes on `/clear`, `/branch` and `--fork-session`. The exit line always prints the *current* ID, so a marker is logged at every exit rather than once at the end of the file, and one transcript can hold several IDs. Limitation: an ID replaced by a mid-session `/clear` never appears in any exit line, so it can't be captured. Sessions live locally under `~/.claude/projects/<project>/<id>.jsonl` and are kept 30 days by default (`cleanupPeriodDays`), so older markers can go stale.
- **Privacy:** the ID is a local filesystem identifier, not a credential, and gives no remote access. The sensitive artifacts are the local `.jsonl` transcript and `AI_TRANSCRIPT.txt` itself (full prompts + CLAUDE.md).
- **Detection design:** strict whole-paste matching (`SessionMarker.TryParse`) — accepts `claude --resume <uuid>`, a bare UUID, or the full exit banner with a trailing PowerShell prompt; rejects a UUID embedded in longer text, so real prompts are never misclassified. Detection is only active once CLAUDE.md is logged, and logging still requires an explicit Log action, consistent with the rejection of clipboard-watching. Rejected alternatives: a dedicated button (an extra click for no safety gain over the visible label) and watching `~/.claude/projects` (couples the app to Claude Code's internal storage layout).
- **Format:** heading `Session Exit`, body normalized to `claude --resume <lowercase-uuid>` (the one deliberate deviation from verbatim, so markers stay greppable). Markers count toward the entry total but never consume a prompt number. The command is also copied to the clipboard on logging, for an immediate resume.

## 2026-09-18 (UTC)

### Decision: standalone .NET 10 WPF app instead of a CLAUDE.md-maintained transcript
**Context:** an accurate, verbatim, chronologically-ordered record of every prompt typed into Claude Code for a project, from start to finish, without spending tokens on every turn to maintain it via CLAUDE.md instructions.

**Decision:** a standalone offline Windows desktop app (.NET 10, WPF) that runs alongside the terminal. It never talks to Claude Code or any network — it only reads and writes local files.

**Trade-offs considered:**
- **.NET 10 WPF** (chosen) vs. Tauri/Electron/Python: the app only needs a folder picker, a multiline text box, clipboard access, and file append — all native, mature APIs in WPF. Tauri/Electron are justified only if cross-platform support is needed. .NET 10 is the current LTS.
- **Manual compose-and-log vs. clipboard-watch auto-detect:** rejected clipboard-watching — the risk of silently logging an unrelated copy as a prompt. Chose an explicit compose box + "Log Entry", with the app copying the verbatim text back to the clipboard after logging, so the only manual step is pasting into the terminal.
- **Heading-detection safety:** resume logic must never mistake pasted prompt text for a real heading (e.g. a prompt containing the line "Prompt #99"). A line only counts as a heading when it's immediately preceded by the fixed 80-`=` separator and immediately followed by a line starting with `UTC: ` — a 3-line sliding window (`HeadingWindow`) checked during a single streaming pass over the file.
- **Timestamps:** each entry gets two timestamp lines — UTC (ISO 8601) and local time (see the timezone entry dated 2026-09-23).
- **Resource safety:** file operations use the BCL convenience methods (`File.AppendAllText` / `ReadLines` / `ReadAllText` / `WriteAllText` / `Copy`), each of which opens, acts, and disposes its `FileStream` before returning; the two places that need an explicit stream wrap it in `using`. No file handle is held open between UI actions, so an abrupt exit can't leak a handle or leave a lock on `AI_TRANSCRIPT.txt`. Global exception hooks log to a local crash file and let the process terminate cleanly rather than attempting a second, possibly-corrupting write.

### Resume and append logic
- **Recognized entries are the source of truth:** whether the CLAUDE.md entry exists is decided by "did we recognize at least one real entry heading" (`entryCount == 0`), not "does the file have any bytes" — so unrelated leftover text is never mistaken for a completed CLAUDE.md entry, and an empty file needs no special case.
- **Pre-existing unrecognized content** is kept, and the first entry is appended after exactly one blank line (regardless of whether the content already ends in a newline, checked with a single-byte end-of-file seek, not a full file load).
- **Whitespace-only content** carries no information, so it's cleared before the first entry is written.
