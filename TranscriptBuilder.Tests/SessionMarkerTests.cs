using TranscriptBuilder.Services;

namespace TranscriptBuilder.Tests;

[TestClass]
public sealed class SessionMarkerTests
{
    private const string Id = "3f7a9c1e-8b2d-4e6f-a1c3-9d5b7e2f4a06";
    private const string Canonical = $"claude --resume {Id}";

    [TestMethod]
    [DataRow($"claude --resume {Id}")]
    [DataRow(Id)]
    [DataRow("  CLAUDE  --resume  3F7A9C1E-8B2D-4E6F-A1C3-9D5B7E2F4A06  \r\n")]
    [DataRow($"Resume this session with:\r\nclaude --resume {Id}\r\nPS C:\\Users\\me\\repo> ")]
    public void TryParse_AcceptsResumeLineInAnyReasonableForm_AndNormalizesIt(string input)
    {
        Assert.IsTrue(SessionMarker.TryParse(input, out var canonical));
        Assert.AreEqual(Canonical, canonical);
    }

    [TestMethod]
    [DataRow($"please look at session {Id} for me")]
    [DataRow($"claude --resume {Id}\nand then do X")]
    [DataRow($"claude --resume {Id}\nclaude --resume {Id}")]
    [DataRow("claude --resume 3f7a9c1e-8b2d-4e6f-a1c3")]
    [DataRow("   ")]
    [DataRow("a normal prompt")]
    public void TryParse_RejectsAnythingThatIsNotOnlyTheResumeLine(string input)
    {
        Assert.IsFalse(SessionMarker.TryParse(input, out var canonical));
        Assert.AreEqual(string.Empty, canonical);
    }

    [TestMethod]
    public void TryParse_RejectsLongTextWithoutScanningIt()
    {
        Assert.IsFalse(SessionMarker.TryParse(new string('a', 600), out _));
    }
}
