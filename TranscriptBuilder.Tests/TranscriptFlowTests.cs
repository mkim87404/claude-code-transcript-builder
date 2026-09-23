using System.Text.RegularExpressions;
using TranscriptBuilder.Models;
using TranscriptBuilder.Services;

namespace TranscriptBuilder.Tests;

// Integration tests: real files in a temp project folder, driven through the same calls the UI makes.
[TestClass]
public sealed partial class TranscriptFlowTests
{
    private const string ResumeCommand = "claude --resume 3f7a9c1e-8b2d-4e6f-a1c3-9d5b7e2f4a06";

    [GeneratedRegex(@"^=+\r?\n(?<heading>[^\r\n]+)\r?\nUTC: \d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ\r?\nLocal: \d{4}-\d\d-\d\d \d\d:\d\d:\d\d [+-]\d\d:\d\d\r?\n=+\r?\n", RegexOptions.Multiline)]
    private static partial Regex EntryHeaderRegex();

    [TestMethod]
    public void FreshProject_WithoutTranscript_NeedsClaudeMdFirst()
    {
        using var project = new TempProject();

        var session = TranscriptService.InspectTranscript(project.Root);

        Assert.AreEqual(NextEntryKind.ClaudeMd, session.NextEntryKind);
        Assert.AreEqual(0, session.EntryCount);
        Assert.IsFalse(session.HasUnrecognizedContent);
    }

    [TestMethod]
    public void PastedClaudeMd_IsLoggedAndCreatedAtRoot_ThenPromptsFollowFromOne()
    {
        using var project = new TempProject();
        var session = TranscriptService.InspectTranscript(project.Root);

        session = Flow.Log(session, "# Pasted rules\r\nbe nice\r\n");

        Assert.IsTrue(ClaudeMdService.TryReadAtRoot(project.Root, out var onDisk));
        Assert.AreEqual("# Pasted rules\r\nbe nice\r\n", onDisk);
        Assert.AreEqual("Prompt #1", session.NextHeading);
        StringAssert.StartsWith(project.ReadTranscript(), "====");
    }

    [TestMethod]
    public void CopiedClaudeMd_IsLoggedThroughTheNormalAutoLogPath()
    {
        using var project = new TempProject();
        var source = project.PathOf("template.md");
        File.WriteAllText(source, "# Template rules");

        var session = TranscriptService.InspectTranscript(project.Root);
        ClaudeMdService.CopyIntoProject(source, project.Root);
        Assert.IsTrue(ClaudeMdService.TryReadAtRoot(project.Root, out var content));
        session = Flow.Log(session, content);

        Assert.AreEqual("Prompt #1", session.NextHeading);
        StringAssert.Contains(project.ReadTranscript(), "# Template rules");
    }

    [TestMethod]
    public void EveryEntry_HasTheExpectedHeaderShape()
    {
        using var project = new TempProject();
        var session = Flow.Log(TranscriptService.InspectTranscript(project.Root), "rules");
        session = Flow.Log(session, "first prompt");
        Flow.Log(session, ResumeCommand);

        var headings = EntryHeaderRegex().Matches(project.ReadTranscript()).Select(m => m.Groups["heading"].Value).ToArray();

        CollectionAssert.AreEqual(new[] { "CLAUDE.md", "Prompt #1", "Session Exit" }, headings);
    }

    [TestMethod]
    public void SessionExitMarkers_CountAsEntriesButNeverConsumePromptNumbers()
    {
        using var project = new TempProject();
        var session = Flow.Log(TranscriptService.InspectTranscript(project.Root), "rules");
        session = Flow.Log(session, "first");
        session = Flow.Log(session, ResumeCommand);
        session = Flow.Log(session, "second");
        session = Flow.Log(session, "3F7A9C1E-8B2D-4E6F-A1C3-9D5B7E2F4A06");
        session = Flow.Log(session, ResumeCommand);

        Assert.AreEqual("Prompt #3", session.NextHeading);
        Assert.AreEqual(6, session.EntryCount);
        Assert.HasCount(3, Regex.Matches(project.ReadTranscript(), $"^{Regex.Escape(ResumeCommand)}\\r?$", RegexOptions.Multiline));
    }

    [TestMethod]
    public void Relaunch_RederivesTheSameStateFromDiskAlone()
    {
        using var project = new TempProject();
        var inMemory = Flow.Log(TranscriptService.InspectTranscript(project.Root), "rules");
        inMemory = Flow.Log(inMemory, "first");
        inMemory = Flow.Log(inMemory, ResumeCommand);
        inMemory = Flow.Log(inMemory, "second");

        var fromDisk = TranscriptService.InspectTranscript(project.Root);

        Assert.AreEqual(inMemory.NextEntryKind, fromDisk.NextEntryKind);
        Assert.AreEqual(inMemory.NextPromptNumber, fromDisk.NextPromptNumber);
        Assert.AreEqual(inMemory.EntryCount, fromDisk.EntryCount);
    }

    [TestMethod]
    public void WhitespaceOnlyTranscript_IsClearedSoTheFirstEntryStartsTheFile()
    {
        using var project = new TempProject();
        File.WriteAllText(project.TranscriptPath, "  \n\t\n");

        var session = TranscriptService.InspectTranscript(project.Root);
        Assert.AreEqual(NextEntryKind.ClaudeMd, session.NextEntryKind);
        Assert.IsFalse(session.HasUnrecognizedContent);

        Flow.Log(session, "rules");

        StringAssert.StartsWith(project.ReadTranscript(), "====");
    }

    [TestMethod]
    [DataRow("asd")]
    [DataRow("asd\n")]
    [DataRow("asd\r\n")]
    public void UnrecognizedContent_IsKept_SeparatedByExactlyOneBlankLine(string existing)
    {
        using var project = new TempProject();
        File.WriteAllText(project.TranscriptPath, existing);

        var session = TranscriptService.InspectTranscript(project.Root);
        Assert.AreEqual(NextEntryKind.ClaudeMd, session.NextEntryKind);
        Assert.IsTrue(session.HasUnrecognizedContent);

        Flow.Log(session, "rules");

        var lines = File.ReadAllLines(project.TranscriptPath);
        Assert.AreEqual("asd", lines[0]);
        Assert.AreEqual(string.Empty, lines[1]);
        StringAssert.StartsWith(lines[2], "====");
    }

    [TestMethod]
    public void BodyTextThatLooksLikeAHeading_IsNeverMistakenForOne()
    {
        using var project = new TempProject();
        var session = Flow.Log(TranscriptService.InspectTranscript(project.Root), "rules");

        Flow.Log(session, "mentions\nPrompt #99\nUTC: not a heading\n" + new string('=', 80) + "\nPrompt #500");

        Assert.AreEqual(2, TranscriptService.InspectTranscript(project.Root).NextPromptNumber);
    }

    [TestMethod]
    [DataRow("Prompt #99999999999999999999")]
    [DataRow("Prompt #0")]
    [DataRow("Prompt #2147483647")]
    [DataRow("Prompt #١٢٣")]
    public void HostileOrCorruptHeadingNumbers_AreIgnoredWithoutCrashing(string heading)
    {
        using var project = new TempProject();
        var separator = new string('=', 80);
        File.WriteAllText(project.TranscriptPath,
            $"{separator}\nCLAUDE.md\nUTC: 2026-01-01T00:00:00Z\nLocal: 2026-01-01 13:00:00 +13:00\n{separator}\nrules\n\n" +
            $"{separator}\n{heading}\nUTC: 2026-01-01T00:00:01Z\nLocal: 2026-01-01 13:00:01 +13:00\n{separator}\nbody\n");

        var session = TranscriptService.InspectTranscript(project.Root);

        Assert.AreEqual(NextEntryKind.Prompt, session.NextEntryKind);
        Assert.AreEqual(1, session.EntryCount);
        Assert.AreEqual(1, session.NextPromptNumber);
    }

    [TestMethod]
    public void AppendEntry_ReleasesTheFileHandle_SoTheFileCanBeDeletedImmediately()
    {
        using var project = new TempProject();
        Flow.Log(TranscriptService.InspectTranscript(project.Root), "rules");

        File.Delete(project.TranscriptPath);

        Assert.IsFalse(File.Exists(project.TranscriptPath));
    }

    [TestMethod]
    public void ModelAndClaudeMdUpdate_AreRecognizedAsEntries_AndNeverConsumePromptNumbers()
    {
        using var project = new TempProject();
        var session = Flow.Log(TranscriptService.InspectTranscript(project.Root), "rules");
        session = Flow.Log(session, "first");

        TranscriptService.AppendEntry(session, EntryHeadings.Model, "/model opus");
        session = session.AfterMarkerLogged();
        TranscriptService.AppendEntry(session, EntryHeadings.ClaudeMdUpdate, "# updated rules");
        session = session.AfterMarkerLogged();
        TranscriptService.AppendEntry(session, EntryHeadings.Compact, "/compact");
        session = session.AfterMarkerLogged();
        TranscriptService.AppendEntry(session, EntryHeadings.Clear, "/clear");
        session = session.AfterMarkerLogged();

        Assert.AreEqual("Prompt #2", session.NextHeading);
        Assert.AreEqual(6, session.EntryCount);

        var fromDisk = TranscriptService.InspectTranscript(project.Root);
        Assert.AreEqual(session.NextPromptNumber, fromDisk.NextPromptNumber);
        Assert.AreEqual(session.EntryCount, fromDisk.EntryCount);
    }

    [TestMethod]
    public void ModelLoggedAsTheFirstEntry_StillClearsWhitespaceOnlyContent()
    {
        using var project = new TempProject();
        File.WriteAllText(project.TranscriptPath, "   \n\t\n");
        var session = TranscriptService.InspectTranscript(project.Root);

        TranscriptService.AppendEntry(session, EntryHeadings.Model, "/model sonnet");

        StringAssert.StartsWith(project.ReadTranscript(), "====");
    }

    [TestMethod]
    public void ModelLoggedAsTheFirstEntry_SeparatesFromUnrecognizedContent()
    {
        using var project = new TempProject();
        File.WriteAllText(project.TranscriptPath, "asd");
        var session = TranscriptService.InspectTranscript(project.Root);
        Assert.IsTrue(session.HasUnrecognizedContent);

        TranscriptService.AppendEntry(session, EntryHeadings.Model, "/model sonnet");

        var lines = File.ReadAllLines(project.TranscriptPath);
        Assert.AreEqual("asd", lines[0]);
        Assert.AreEqual(string.Empty, lines[1]);
        StringAssert.StartsWith(lines[2], "====");
    }

    [TestMethod]
    public void HasClaudeMdChanged_IsTrueWhenNoClaudeMdEverLogged()
    {
        using var project = new TempProject();

        Assert.IsTrue(TranscriptService.HasClaudeMdChangedSinceLastLogged(project.Root, "anything"));
    }

    [TestMethod]
    public void HasClaudeMdChanged_IsFalseForIdenticalContent()
    {
        using var project = new TempProject();
        Flow.Log(TranscriptService.InspectTranscript(project.Root), "# rules\r\nline two\r\n");

        Assert.IsFalse(TranscriptService.HasClaudeMdChangedSinceLastLogged(project.Root, "# rules\r\nline two\r\n"));
    }

    [TestMethod]
    public void HasClaudeMdChanged_IgnoresLineEndingDifferences()
    {
        using var project = new TempProject();
        Flow.Log(TranscriptService.InspectTranscript(project.Root), "# rules\r\nline two\r\n");

        // Same content, LF only instead of CRLF — a file re-saved with different line endings
        // shouldn't be reported as changed.
        Assert.IsFalse(TranscriptService.HasClaudeMdChangedSinceLastLogged(project.Root, "# rules\nline two\n"));
    }

    [TestMethod]
    public void HasClaudeMdChanged_IsTrueForDifferentContent()
    {
        using var project = new TempProject();
        Flow.Log(TranscriptService.InspectTranscript(project.Root), "# original rules");

        Assert.IsTrue(TranscriptService.HasClaudeMdChangedSinceLastLogged(project.Root, "# different rules"));
    }

    [TestMethod]
    public void HasClaudeMdChanged_ComparesAgainstTheMostRecentUpdate_NotTheOriginal()
    {
        using var project = new TempProject();
        var session = Flow.Log(TranscriptService.InspectTranscript(project.Root), "# v1");
        TranscriptService.AppendEntry(session, EntryHeadings.ClaudeMdUpdate, "# v2");

        Assert.IsFalse(TranscriptService.HasClaudeMdChangedSinceLastLogged(project.Root, "# v2"));
        Assert.IsTrue(TranscriptService.HasClaudeMdChangedSinceLastLogged(project.Root, "# v1"));
    }

    [TestMethod]
    public void HasClaudeMdChanged_HandlesMultiLineBodiesCorrectly()
    {
        using var project = new TempProject();
        var multiline = "# Rules\r\n\r\nSection one.\r\nSection two.\r\n";
        Flow.Log(TranscriptService.InspectTranscript(project.Root), multiline);

        Assert.IsFalse(TranscriptService.HasClaudeMdChangedSinceLastLogged(project.Root, multiline));
    }

    [TestMethod]
    public void FreshProject_HasNeverLoggedAModel()
    {
        using var project = new TempProject();

        Assert.IsFalse(TranscriptService.InspectTranscript(project.Root).HasLoggedModel);
    }

    [TestMethod]
    public void InspectTranscript_DetectsAModelEntryAnywhereInTheFile()
    {
        using var project = new TempProject();
        var session = Flow.Log(TranscriptService.InspectTranscript(project.Root), "rules");
        session = Flow.Log(session, "first prompt");
        Assert.IsFalse(TranscriptService.InspectTranscript(project.Root).HasLoggedModel);

        TranscriptService.AppendEntry(session, EntryHeadings.Model, "/model opus");

        Assert.IsTrue(TranscriptService.InspectTranscript(project.Root).HasLoggedModel);
    }
}
