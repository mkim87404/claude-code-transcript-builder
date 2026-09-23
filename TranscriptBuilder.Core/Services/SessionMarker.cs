using System.Text.RegularExpressions;

namespace TranscriptBuilder.Services;

// Recognizes a pasted Claude Code exit "resume" line and normalizes it to its canonical command.
public static class SessionMarker
{
    // A real resume paste is at most a few short lines; bailing early keeps per-keystroke
    // detection cheap when a large prompt is pasted.
    private const int MaxDetectableLength = 500;

    private static readonly Regex ResumeLineRegex = new(
        @"^(?:claude\s+--resume\s+)?(?<id>[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ResumeBannerRegex = new(
        @"^Resume this session with:?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ShellPromptRegex = new(@"^PS\s.*>$", RegexOptions.Compiled);

    // Matches only when the WHOLE paste is the resume line (optionally with the exit banner and a
    // trailing PowerShell prompt), so a real prompt that merely contains a UUID is never captured.
    public static bool TryParse(string text, out string canonicalCommand)
    {
        canonicalCommand = string.Empty;
        if (text.Length > MaxDetectableLength)
        {
            return false;
        }

        string? resumeLine = null;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || ResumeBannerRegex.IsMatch(line) || ShellPromptRegex.IsMatch(line))
            {
                continue;
            }

            if (resumeLine is not null)
            {
                return false;
            }

            resumeLine = line;
        }

        if (resumeLine is null)
        {
            return false;
        }

        var match = ResumeLineRegex.Match(resumeLine);
        if (!match.Success)
        {
            return false;
        }

        canonicalCommand = $"claude --resume {match.Groups["id"].Value.ToLowerInvariant()}";
        return true;
    }
}
