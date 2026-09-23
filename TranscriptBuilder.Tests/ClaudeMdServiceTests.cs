using System.Text;
using TranscriptBuilder.Services;

namespace TranscriptBuilder.Tests;

[TestClass]
public sealed class ClaudeMdServiceTests
{
    [TestMethod]
    public void CopyIntoProject_IsByteIdentical_IncludingBomAndLineEndings_AndRenamesToClaudeMd()
    {
        using var project = new TempProject();
        var source = project.PathOf("my-template.md");
        File.WriteAllBytes(source, [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("# Rules\r\nünïcode – line\r\n")]);

        Assert.IsFalse(ClaudeMdService.ExistsAtRoot(project.Root));
        ClaudeMdService.CopyIntoProject(source, project.Root);

        Assert.IsTrue(ClaudeMdService.ExistsAtRoot(project.Root));
        CollectionAssert.AreEqual(File.ReadAllBytes(source), File.ReadAllBytes(project.PathOf("CLAUDE.md")));
    }

    [TestMethod]
    public void CopyIntoProject_NeverOverwritesAnExistingClaudeMd()
    {
        using var project = new TempProject();
        var source = project.PathOf("source.md");
        File.WriteAllText(source, "new");
        File.WriteAllText(project.PathOf("CLAUDE.md"), "original");

        Assert.Throws<IOException>(() => ClaudeMdService.CopyIntoProject(source, project.Root));
        Assert.AreEqual("original", File.ReadAllText(project.PathOf("CLAUDE.md")));
    }

    [TestMethod]
    public void CopyIntoProject_WithMissingSource_ThrowsAndCreatesNothing()
    {
        using var project = new TempProject();

        Assert.Throws<IOException>(() => ClaudeMdService.CopyIntoProject(project.PathOf("nope.md"), project.Root));
        Assert.IsFalse(ClaudeMdService.ExistsAtRoot(project.Root));
    }

    [TestMethod]
    public void CreateAtRoot_WritesTheExactTextAsUtf8WithoutBom()
    {
        using var project = new TempProject();
        const string pasted = "# Pasted\r\nbody ✓\r\n";

        ClaudeMdService.CreateAtRoot(project.Root, pasted);

        var bytes = File.ReadAllBytes(project.PathOf("CLAUDE.md"));
        Assert.AreEqual(pasted, Encoding.UTF8.GetString(bytes));
        Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "unexpected BOM");
    }

    [TestMethod]
    public void CreateAtRoot_NeverOverwritesAnExistingClaudeMd()
    {
        using var project = new TempProject();
        File.WriteAllText(project.PathOf("CLAUDE.md"), "original");

        Assert.Throws<IOException>(() => ClaudeMdService.CreateAtRoot(project.Root, "other"));
        Assert.AreEqual("original", File.ReadAllText(project.PathOf("CLAUDE.md")));
    }

    [TestMethod]
    public void TryReadAtRoot_ReturnsContentWhenPresent_AndFalseWhenAbsent()
    {
        using var project = new TempProject();
        Assert.IsFalse(ClaudeMdService.TryReadAtRoot(project.Root, out var missing));
        Assert.AreEqual(string.Empty, missing);

        File.WriteAllText(project.PathOf("CLAUDE.md"), "# rules");
        Assert.IsTrue(ClaudeMdService.TryReadAtRoot(project.Root, out var content));
        Assert.AreEqual("# rules", content);
    }
}
