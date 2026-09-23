namespace TranscriptBuilder.Models;

// Single source of truth for the heading text written to (and recognized in) AI_TRANSCRIPT.txt.
public static class EntryHeadings
{
    public const string ClaudeMd = "CLAUDE.md";
    public const string ClaudeMdUpdate = "CLAUDE.md Update";
    public const string SessionExit = "Session Exit";
    public const string Model = "Model";
    public const string Compact = "Compact";
    public const string Clear = "Clear";

    public static string Prompt(int number) => $"Prompt #{number}";

    // Heading for a session command picked in the Session group; null for anything unrecognized.
    public static string? ForSessionCommand(string command) => command switch
    {
        "/compact" => Compact,
        "/clear" => Clear,
        _ => null
    };
}
