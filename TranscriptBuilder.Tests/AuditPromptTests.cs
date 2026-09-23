using TranscriptBuilder.Models;

namespace TranscriptBuilder.Tests;

[TestClass]
public sealed class AuditPromptTests
{
    [TestMethod]
    public void Base_ContainsTheKeyRequirements_ButNotThePlaceholder()
    {
        StringAssert.Contains(AuditPrompt.Base, "Audit this codebase against CLAUDE.md");
        StringAssert.Contains(AuditPrompt.Base, "file:line citation");
        StringAssert.Contains(AuditPrompt.Base, "one line on the fix");
        // Not itself part of the saved base prompt — it's appended separately as the limit line.
        Assert.IsFalse(AuditPrompt.Base.Contains(AuditPrompt.AreaPlaceholder, StringComparison.Ordinal));
    }

    [TestMethod]
    public void Effective_PrefersTheCustomText_AndFallsBackToBase()
    {
        Assert.AreEqual("my wording", AuditPrompt.Effective("my wording"));
        Assert.AreEqual(AuditPrompt.Base, AuditPrompt.Effective(null));
    }

    [TestMethod]
    public void NormalizeCustom_StoresNullForEmptyOrDefaultText_SoTheDefaultStaysLive()
    {
        Assert.IsNull(AuditPrompt.NormalizeCustom(""));
        Assert.IsNull(AuditPrompt.NormalizeCustom("   \r\n"));
        Assert.IsNull(AuditPrompt.NormalizeCustom(AuditPrompt.Base));
        Assert.IsNull(AuditPrompt.NormalizeCustom($"  {AuditPrompt.Base}\n"));
    }

    [TestMethod]
    public void NormalizeCustom_KeepsGenuinelyCustomText_Trimmed()
    {
        Assert.AreEqual("Audit only the Services folder.", AuditPrompt.NormalizeCustom("  Audit only the Services folder.\n"));
    }
}
