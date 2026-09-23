using TranscriptBuilder.Models;

namespace TranscriptBuilder.Services;

public readonly record struct ClassifiedEntry(string Heading, string Body, bool IsSessionExit);

// Decides what a compose-box paste will be logged as; shared by the live label and the log action.
public static class EntryClassifier
{
    // A resume line only counts as a Session Exit once CLAUDE.md is logged, so it can never be
    // mistaken for the CLAUDE.md entry; its body is the normalized command, everything else is verbatim.
    public static ClassifiedEntry Classify(SessionState session, string text)
    {
        if (session.NextEntryKind == NextEntryKind.Prompt && SessionMarker.TryParse(text, out var resumeCommand))
        {
            return new ClassifiedEntry(EntryHeadings.SessionExit, resumeCommand, IsSessionExit: true);
        }

        return new ClassifiedEntry(session.NextHeading, text, IsSessionExit: false);
    }
}
