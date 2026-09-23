namespace TranscriptBuilder.Models;

public enum NextEntryKind
{
    ClaudeMd,
    Prompt
}

// Where a project's transcript stands: what the next entry is, and how many are logged.
public sealed record SessionState(
    string ProjectRoot,
    string TranscriptPath,
    NextEntryKind NextEntryKind,
    int NextPromptNumber,
    int EntryCount,
    // True when the file holds text that is not a recognized entry (the first entry is then separated from it).
    bool HasUnrecognizedContent = false,
    // True once any Model entry has been logged for this project, at any point in the transcript.
    bool HasLoggedModel = false)
{
    public string NextHeading => NextEntryKind == NextEntryKind.ClaudeMd
        ? EntryHeadings.ClaudeMd
        : EntryHeadings.Prompt(NextPromptNumber);

    // State after the CLAUDE.md or a Prompt entry is logged: prompts always follow, numbered from 1.
    public SessionState AfterEntryLogged() => this with
    {
        NextEntryKind = NextEntryKind.Prompt,
        NextPromptNumber = NextEntryKind == NextEntryKind.ClaudeMd ? 1 : NextPromptNumber + 1,
        EntryCount = EntryCount + 1
    };

    // State after any non-numbered entry (Session Exit, CLAUDE.md Update, Compact, Clear): counts as
    // an entry but never consumes a prompt number.
    public SessionState AfterMarkerLogged() => this with { EntryCount = EntryCount + 1 };

    // State after a Model entry: same non-consuming shape as AfterMarkerLogged, but also records that
    // a model has now been logged, so the "not yet logged" nudge stops showing for this session.
    public SessionState AfterModelLogged() => this with { EntryCount = EntryCount + 1, HasLoggedModel = true };

    // True only in the window between CLAUDE.md resolving and Prompt #1 being logged — the one moment
    // the app nudges toward logging a Model entry (status text and a brief button highlight).
    public bool NeedsModelNudge => NextEntryKind == NextEntryKind.Prompt && NextPromptNumber == 1 && !HasLoggedModel;
}
