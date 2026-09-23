using TranscriptBuilder.Models;
using TranscriptBuilder.Services;

namespace TranscriptBuilder.Tests;

[TestClass]
public sealed class ThemePreferenceServiceTests
{
    [TestMethod]
    public void Parse_DefaultsToLight_ForNullMissingOrUnrecognizedValues()
    {
        Assert.AreEqual(AppThemePreference.Light, ThemePreferenceService.Parse(null));
        Assert.AreEqual(AppThemePreference.Light, ThemePreferenceService.Parse(string.Empty));
        Assert.AreEqual(AppThemePreference.Light, ThemePreferenceService.Parse("Blue"));
    }

    [TestMethod]
    [DataRow("1")]
    [DataRow("5")]
    [DataRow("-1")]
    public void Parse_RejectsNumericStrings_EvenOnesMatchingAnEnumValue(string value)
    {
        // Enum.TryParse accepts numbers by default; the settings file stores names, so a number
        // means the file was hand-edited or corrupted and should fall back like any other bad value.
        Assert.AreEqual(AppThemePreference.Light, ThemePreferenceService.Parse(value));
    }

    [TestMethod]
    [DataRow("Dark")]
    [DataRow("dark")]
    [DataRow("DARK")]
    public void Parse_AcceptsDarkCaseInsensitively(string value)
    {
        Assert.AreEqual(AppThemePreference.Dark, ThemePreferenceService.Parse(value));
    }

    [TestMethod]
    public void Toggle_SwapsBetweenLightAndDark()
    {
        Assert.AreEqual(AppThemePreference.Dark, ThemePreferenceService.Toggle(AppThemePreference.Light));
        Assert.AreEqual(AppThemePreference.Light, ThemePreferenceService.Toggle(AppThemePreference.Dark));
    }
}
