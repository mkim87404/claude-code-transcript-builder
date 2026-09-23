using TranscriptBuilder.Services;

namespace TranscriptBuilder.Tests;

[TestClass]
public sealed class WindowSizingTests
{
    private const double Fallback = 1040;
    private const double Minimum = 620;
    private const double LargeScreen = 1920;

    [TestMethod]
    public void NothingRemembered_UsesTheFallback()
    {
        Assert.AreEqual(Fallback, WindowSizing.Resolve(null, Fallback, Minimum, LargeScreen));
    }

    [TestMethod]
    public void AValidRememberedSize_IsUsed()
    {
        Assert.AreEqual(1131, WindowSizing.Resolve(1131, Fallback, Minimum, LargeScreen));
    }

    [TestMethod]
    [DataRow(100.0)]
    [DataRow(-5.0)]
    [DataRow(double.NaN)]
    public void ARememberedSizeBelowTheMinimumOrNotANumber_FallsBack(double remembered)
    {
        Assert.AreEqual(Fallback, WindowSizing.Resolve(remembered, Fallback, Minimum, LargeScreen));
    }

    [TestMethod]
    public void ARememberedSizeFromABiggerMonitor_IsCappedToTheCurrentScreen()
    {
        Assert.AreEqual(1366, WindowSizing.Resolve(2500, Fallback, Minimum, 1366));
    }

    [TestMethod]
    public void TheFallbackItself_IsCappedOnAScreenSmallerThanIt()
    {
        // e.g. a 1366px-wide laptop at 150% scaling has only ~910 DIPs of work area.
        Assert.AreEqual(910, WindowSizing.Resolve(null, Fallback, Minimum, 910));
    }
}
