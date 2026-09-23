using TranscriptBuilder.Models;
using TranscriptBuilder.Services;

namespace TranscriptBuilder.Tests;

[TestClass]
public sealed class SessionModelTests
{
    private const string Id = "3f7a9c1e-8b2d-4e6f-a1c3-9d5b7e2f4a06";

    private static SessionState NeedsClaudeMd() => new("root", "root/t.txt", NextEntryKind.ClaudeMd, 0, 0);

    private static SessionState AtPrompt(int next, int entries) => new("root", "root/t.txt", NextEntryKind.Prompt, next, entries);

    [TestMethod]
    public void NextHeading_NamesTheNextEntry()
    {
        Assert.AreEqual("CLAUDE.md", NeedsClaudeMd().NextHeading);
        Assert.AreEqual("Prompt #7", AtPrompt(7, 8).NextHeading);
    }

    [TestMethod]
    public void AfterEntryLogged_MovesFromClaudeMdToPromptOne_ThenCounts()
    {
        var afterClaudeMd = NeedsClaudeMd().AfterEntryLogged();
        Assert.AreEqual(NextEntryKind.Prompt, afterClaudeMd.NextEntryKind);
        Assert.AreEqual(1, afterClaudeMd.NextPromptNumber);
        Assert.AreEqual(1, afterClaudeMd.EntryCount);

        var afterPrompt = afterClaudeMd.AfterEntryLogged();
        Assert.AreEqual(2, afterPrompt.NextPromptNumber);
        Assert.AreEqual(2, afterPrompt.EntryCount);
    }

    [TestMethod]
    public void AfterMarkerLogged_CountsAnEntryButKeepsThePromptNumber()
    {
        var after = AtPrompt(5, 6).AfterMarkerLogged();
        Assert.AreEqual(5, after.NextPromptNumber);
        Assert.AreEqual(7, after.EntryCount);
    }

    [TestMethod]
    public void NeedsModelNudge_IsTrueOnlyBetweenClaudeMdAndPromptOne_WithNoModelLoggedYet()
    {
        Assert.IsFalse(NeedsClaudeMd().NeedsModelNudge, "still needs CLAUDE.md — not the nudge window yet");
        Assert.IsTrue(AtPrompt(1, 1).NeedsModelNudge, "right after CLAUDE.md, before Prompt #1");
        Assert.IsFalse(AtPrompt(2, 2).NeedsModelNudge, "past Prompt #1 — window closed for good");
        Assert.IsFalse((AtPrompt(1, 1) with { HasLoggedModel = true }).NeedsModelNudge, "a model was already logged");
    }

    [TestMethod]
    public void AfterModelLogged_CountsAnEntry_KeepsThePromptNumber_AndRecordsThatAModelWasLogged()
    {
        var before = AtPrompt(5, 6);
        Assert.IsFalse(before.HasLoggedModel);

        var after = before.AfterModelLogged();

        Assert.AreEqual(5, after.NextPromptNumber);
        Assert.AreEqual(7, after.EntryCount);
        Assert.IsTrue(after.HasLoggedModel);
    }

    [TestMethod]
    public void Classify_LeavesANormalPromptVerbatim()
    {
        var entry = EntryClassifier.Classify(AtPrompt(3, 4), "just a prompt\r\nline two ");

        Assert.IsFalse(entry.IsSessionExit);
        Assert.AreEqual("Prompt #3", entry.Heading);
        Assert.AreEqual("just a prompt\r\nline two ", entry.Body);
    }

    [TestMethod]
    public void Classify_TurnsAResumeLineIntoANormalizedSessionExit()
    {
        var entry = EntryClassifier.Classify(AtPrompt(3, 4), Id.ToUpperInvariant());

        Assert.IsTrue(entry.IsSessionExit);
        Assert.AreEqual("Session Exit", entry.Heading);
        Assert.AreEqual($"claude --resume {Id}", entry.Body);
    }

    [TestMethod]
    public void Classify_NeverTreatsAResumeLineAsClaudeMdWhileClaudeMdIsStillNeeded()
    {
        var entry = EntryClassifier.Classify(NeedsClaudeMd(), $"claude --resume {Id}");

        Assert.IsFalse(entry.IsSessionExit);
        Assert.AreEqual("CLAUDE.md", entry.Heading);
        Assert.AreEqual($"claude --resume {Id}", entry.Body);
    }
}
