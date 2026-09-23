using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TranscriptBuilder.Models;

namespace TranscriptBuilder.Services;

// Inspects a project's AI_TRANSCRIPT.txt to resume from it, and appends new entries to it.
public static class TranscriptService
{
    private const string TranscriptFileName = "AI_TRANSCRIPT.txt";
    private const string Separator = "================================================================================";

    // Pattern form of EntryHeadings.Prompt.
    private static readonly Regex PromptHeadingRegex = new(@"^Prompt #(\d+)$", RegexOptions.Compiled);

    // How to handle whatever is already in the file when writing the very first (CLAUDE.md) entry.
    private enum ExistingContentAction
    {
        None,
        ClearWhitespaceOnly,
        SeparateWithBlankLine
    }

    // Tracks the rolling 3-line window used to confirm a heading (separator, heading, "UTC: " line)
    // while streaming a transcript file — shared by InspectTranscript and TryGetLastClaudeMdBody so the
    // detection rule (and the reason it's safe against pasted body text) lives in exactly one place.
    private sealed class HeadingWindow
    {
        private string? _twoLinesAgo;
        private string? _oneLineAgo;

        // Call once per line, in order. Returns the heading text if this line completes a confirmed
        // heading (the "UTC: " line immediately following <separator>, <heading>), else null.
        public string? Observe(string line)
        {
            var confirmedHeading = _twoLinesAgo == Separator && line.StartsWith("UTC: ", StringComparison.Ordinal)
                ? _oneLineAgo ?? string.Empty
                : null;

            _twoLinesAgo = _oneLineAgo;
            _oneLineAgo = line;
            return confirmedHeading;
        }
    }

    // Single streaming pass (File.ReadLines is lazy) — never loads the whole transcript into memory,
    // and only treats a line as a heading when it sits between a separator and a "UTC: " line, so
    // pasted prompt bodies can never be mistaken for real headings.
    public static SessionState InspectTranscript(string projectRoot)
    {
        var transcriptPath = Path.Combine(projectRoot, TranscriptFileName);

        if (!File.Exists(transcriptPath))
        {
            return new SessionState(projectRoot, transcriptPath, NextEntryKind.ClaudeMd, NextPromptNumber: 0, EntryCount: 0);
        }

        var maxPromptNumber = 0;
        var entryCount = 0;
        var hasNonWhitespaceContent = false;
        var hasLoggedModel = false;
        var window = new HeadingWindow();

        foreach (var line in File.ReadLines(transcriptPath))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                hasNonWhitespaceContent = true;
            }

            var headingLine = window.Observe(line);
            if (headingLine is not null)
            {
                var match = PromptHeadingRegex.Match(headingLine);
                if (match.Success)
                {
                    // File content is untrusted: an out-of-range or non-ASCII number is not a real heading.
                    if (int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var promptNumber)
                        && promptNumber is >= 1 and < int.MaxValue)
                    {
                        entryCount++;
                        maxPromptNumber = Math.Max(maxPromptNumber, promptNumber);
                    }
                }
                else if (headingLine is EntryHeadings.ClaudeMd or EntryHeadings.ClaudeMdUpdate
                         or EntryHeadings.SessionExit or EntryHeadings.Model
                         or EntryHeadings.Compact or EntryHeadings.Clear)
                {
                    // These headings count as entries but never consume a prompt number.
                    entryCount++;
                    if (headingLine == EntryHeadings.Model)
                    {
                        hasLoggedModel = true;
                    }
                }
            }
        }

        // The source of truth is "did we find a recognized entry", not "does the file have any
        // bytes". Whitespace-only content is treated like an empty file; genuine unrecognized text
        // is flagged so the first CLAUDE.md entry can be separated from it by a blank line.
        if (entryCount == 0)
        {
            return new SessionState(
                projectRoot, transcriptPath, NextEntryKind.ClaudeMd, NextPromptNumber: 0, EntryCount: 0,
                HasUnrecognizedContent: hasNonWhitespaceContent);
        }

        return new SessionState(
            projectRoot, transcriptPath, NextEntryKind.Prompt, NextPromptNumber: maxPromptNumber + 1, EntryCount: entryCount,
            HasLoggedModel: hasLoggedModel);
    }

    // Appends one timestamped entry; the very first entry in the file also normalizes any content
    // already there. "First" is judged by EntryCount rather than heading type, since CLAUDE.md was
    // originally the only possible first entry but Model logging can now also open a fresh transcript.
    public static void AppendEntry(SessionState session, string heading, string body)
    {
        var (utcLine, localLine) = TimestampService.GetTimestampPair();

        var existingContentAction = session.EntryCount != 0
            ? ExistingContentAction.None
            : session.HasUnrecognizedContent
                ? ExistingContentAction.SeparateWithBlankLine
                : ExistingContentAction.ClearWhitespaceOnly;

        var transcriptPath = session.TranscriptPath;

        if (existingContentAction == ExistingContentAction.ClearWhitespaceOnly && File.Exists(transcriptPath))
        {
            // Whitespace-only leftovers carry no information — clear them so the transcript starts cleanly.
            File.WriteAllText(transcriptPath, string.Empty);
        }

        var blockBuilder = new StringBuilder();

        if (existingContentAction == ExistingContentAction.SeparateWithBlankLine
            && File.Exists(transcriptPath) && new FileInfo(transcriptPath).Length > 0)
        {
            // Leave exactly one blank line between pre-existing unrecognized content and this entry,
            // regardless of whether that content already happens to end with a trailing newline.
            blockBuilder.Append(FileEndsWithNewline(transcriptPath) ? Environment.NewLine : Environment.NewLine + Environment.NewLine);
        }

        blockBuilder
            .AppendLine(Separator)
            .AppendLine(heading)
            .AppendLine(utcLine)
            .AppendLine(localLine)
            .AppendLine(Separator)
            .AppendLine(body)
            .AppendLine();

        // File.AppendAllText opens, writes, and closes/disposes the underlying FileStream within its
        // own try/finally before returning — no handle is held open between log actions.
        File.AppendAllText(transcriptPath, blockBuilder.ToString(), Encoding.UTF8);
    }

    // True if the transcript has never logged a CLAUDE.md entry, or if currentContent differs from
    // the most recently logged CLAUDE.md/CLAUDE.md Update body (line endings normalized, since the
    // file on disk and the transcript's copy of it may use different conventions for identical text).
    public static bool HasClaudeMdChangedSinceLastLogged(string projectRoot, string currentContent)
    {
        if (!TryGetLastClaudeMdBody(projectRoot, out var lastBody))
        {
            return true;
        }

        return NormalizeLineEndings(lastBody) != NormalizeLineEndings(currentContent);
    }

    // Single streaming pass, sharing HeadingWindow with InspectTranscript: once a CLAUDE.md/CLAUDE.md
    // Update heading is confirmed, skip the local-time line and the separator before the body, then collect
    // lines until the next separator (which opens the following entry) — keeping only the last such
    // body found, since scanning in file order means "last found" is "most recent".
    private static bool TryGetLastClaudeMdBody(string projectRoot, out string lastBody)
    {
        var transcriptPath = Path.Combine(projectRoot, TranscriptFileName);
        lastBody = string.Empty;

        if (!File.Exists(transcriptPath))
        {
            return false;
        }

        string? found = null;
        var window = new HeadingWindow();
        var capturing = false;
        var skipCount = 0;
        var bodyLines = new List<string>();

        foreach (var line in File.ReadLines(transcriptPath))
        {
            if (capturing)
            {
                if (skipCount > 0)
                {
                    skipCount--;
                }
                else if (line == Separator)
                {
                    found = FinalizeBody(bodyLines);
                    capturing = false;
                    bodyLines.Clear();
                }
                else
                {
                    bodyLines.Add(line);
                }
            }

            var headingLine = window.Observe(line);
            if (headingLine is EntryHeadings.ClaudeMd or EntryHeadings.ClaudeMdUpdate)
            {
                capturing = true;
                skipCount = 2; // The local-time line, then the separator before the body.
                bodyLines.Clear();
            }
        }

        // The file may end while still capturing the last entry's body (no trailing separator after it).
        if (capturing && bodyLines.Count > 0)
        {
            found = FinalizeBody(bodyLines);
        }

        if (found is null)
        {
            return false;
        }

        lastBody = found;
        return true;
    }

    // Joins the captured body lines back into one string, trimming the one trailing blank line.
    // AppendEntry always writes that blank line after the body; strip it rather than treat it as content.
    private static string FinalizeBody(List<string> bodyLines)
    {
        if (bodyLines.Count > 0 && bodyLines[^1].Length == 0)
        {
            bodyLines.RemoveAt(bodyLines.Count - 1);
        }

        return string.Join("\n", bodyLines);
    }

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n").Replace("\r", "\n");

    private static bool FileEndsWithNewline(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length == 0)
        {
            return true;
        }

        stream.Seek(-1, SeekOrigin.End);
        return stream.ReadByte() == '\n';
    }
}
