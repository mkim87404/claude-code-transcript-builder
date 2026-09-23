using TranscriptBuilder.Models;

namespace TranscriptBuilder.Tests;

[TestClass]
public sealed class EntryHeadingsTests
{
    [TestMethod]
    public void ForSessionCommand_MapsEachExactCommandToItsHeading()
    {
        Assert.AreEqual(EntryHeadings.Compact, EntryHeadings.ForSessionCommand("/compact"));
        Assert.AreEqual(EntryHeadings.Clear, EntryHeadings.ForSessionCommand("/clear"));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("/rewind")]
    [DataRow("/Compact")]
    [DataRow("/compact now")]
    public void ForSessionCommand_ReturnsNullForAnythingElse(string command)
    {
        Assert.IsNull(EntryHeadings.ForSessionCommand(command));
    }
}
