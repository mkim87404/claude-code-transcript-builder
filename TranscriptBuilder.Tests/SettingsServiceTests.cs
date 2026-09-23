using TranscriptBuilder.Models;
using TranscriptBuilder.Services;

namespace TranscriptBuilder.Tests;

// Exercises SettingsService against a temp path (via the test-only path override), not the real
// %AppData% location — covers the "first-time user" scenarios every AppSettings field read must
// tolerate: no file yet, an old file missing newer fields, and a corrupt file.
[TestClass]
public sealed class SettingsServiceTests
{
    [TestMethod]
    public void Load_WithNoFileAtAll_ReturnsAllNullFields_LikeAFirstTimeUser()
    {
        AssertAllNull(SettingsService.Load(TempSettingsPath()));
    }

    [TestMethod]
    public void Load_WithCorruptJson_FallsBackToAllNullFields_RatherThanThrowing()
    {
        var path = TempSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is not valid json");

        AssertAllNull(SettingsService.Load(path));
    }

    [TestMethod]
    public void Load_WithOldSchemaJsonMissingNewerFields_LeavesThemNull_WithoutThrowing()
    {
        // Simulates upgrading from the first version of the app, which only remembered the project.
        var path = TempSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"LastProjectPath": "C:\\projects\\old"}""");

        var settings = SettingsService.Load(path);

        Assert.AreEqual(@"C:\projects\old", settings.LastProjectPath);
        AssertAllNull(settings, ignoreProjectPath: true);
    }

    [TestMethod]
    public void Load_WithNonNumericWindowSize_FallsBackRatherThanThrowing()
    {
        // settings.json is user-editable; a non-number in a numeric field is a JsonException like any
        // other corruption, so it must surface as "nothing remembered", never a crash.
        var path = TempSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"WindowWidth": "NaN", "WindowHeight": 600}""");

        Assert.IsNull(SettingsService.Load(path).WindowWidth);
    }

    [TestMethod]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var path = TempSettingsPath();
        var original = new AppSettings
        {
            LastProjectPath = @"C:\projects\example",
            LastClaudeMdSourcePath = @"C:\templates\CLAUDE.md",
            LastKnownModel = "opus",
            ThemePreference = "Dark",
            CustomAuditPromptText = "Audit only the Services folder.",
            WindowWidth = 1131,
            WindowHeight = 622
        };

        SettingsService.Save(original, path);
        var restored = SettingsService.Load(path);

        Assert.AreEqual(original.LastProjectPath, restored.LastProjectPath);
        Assert.AreEqual(original.LastClaudeMdSourcePath, restored.LastClaudeMdSourcePath);
        Assert.AreEqual(original.LastKnownModel, restored.LastKnownModel);
        Assert.AreEqual(original.ThemePreference, restored.ThemePreference);
        Assert.AreEqual(original.CustomAuditPromptText, restored.CustomAuditPromptText);
        Assert.AreEqual(original.WindowWidth, restored.WindowWidth);
        Assert.AreEqual(original.WindowHeight, restored.WindowHeight);
    }

    [TestMethod]
    public void SaveThenLoad_RoundTripsAFirstTimeUsersMostlyEmptySettings()
    {
        // What Save() actually receives the first time a user ever sets one field (e.g. just picks a
        // project) while everything else is still unset.
        var path = TempSettingsPath();

        SettingsService.Save(new AppSettings { LastProjectPath = @"C:\projects\example" }, path);
        var restored = SettingsService.Load(path);

        Assert.AreEqual(@"C:\projects\example", restored.LastProjectPath);
        AssertAllNull(restored, ignoreProjectPath: true);
    }

    [TestMethod]
    public void Save_CreatesTheDirectoryIfItDoesNotExistYet()
    {
        var path = TempSettingsPath(); // parent directory deliberately not created beforehand

        SettingsService.Save(new AppSettings { LastKnownModel = "sonnet" }, path);

        Assert.IsTrue(File.Exists(path));
    }

    private static string TempSettingsPath() =>
        Path.Combine(Path.GetTempPath(), "TranscriptBuilderTests", Guid.NewGuid().ToString("N") + ".json");

    // Every remembered field, so a newly added one that isn't covered here shows up as a gap.
    private static void AssertAllNull(AppSettings settings, bool ignoreProjectPath = false)
    {
        if (!ignoreProjectPath)
        {
            Assert.IsNull(settings.LastProjectPath);
        }

        Assert.IsNull(settings.LastClaudeMdSourcePath);
        Assert.IsNull(settings.LastKnownModel);
        Assert.IsNull(settings.ThemePreference);
        Assert.IsNull(settings.CustomAuditPromptText);
        Assert.IsNull(settings.WindowWidth);
        Assert.IsNull(settings.WindowHeight);
    }
}
