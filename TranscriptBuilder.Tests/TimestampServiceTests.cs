using TranscriptBuilder.Services;

namespace TranscriptBuilder.Tests;

[TestClass]
public sealed class TimestampServiceTests
{
    // A fixed zone, not TimeZoneInfo.Local, so these tests are deterministic on any machine that
    // runs them — Windows ships this zone's rules built into the OS regardless of what zone the
    // host itself is actually configured to (this app is Windows-only, so that's always available).
    private static readonly TimeZoneInfo FixedZone = TimeZoneInfo.FindSystemTimeZoneById("New Zealand Standard Time");

    private static (string UtcLine, string LocalLine) At(int year, int month, int day, int hour, int minute, int second) =>
        TimestampService.GetTimestampPair(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc), FixedZone);

    [TestMethod]
    public void WinterInTheFixedZone_UsesItsStandardOffset()
    {
        var (utc, local) = At(2026, 7, 15, 0, 0, 0);

        Assert.AreEqual("UTC: 2026-07-15T00:00:00Z", utc);
        Assert.AreEqual("Local: 2026-07-15 12:00:00 +12:00", local);
    }

    [TestMethod]
    public void SummerInTheFixedZone_UsesItsDaylightOffset()
    {
        var (_, local) = At(2026, 1, 15, 0, 0, 0);

        Assert.AreEqual("Local: 2026-01-15 13:00:00 +13:00", local);
    }

    [TestMethod]
    public void OffsetSwitchesExactlyAtTheDaylightSavingBoundary()
    {
        // This zone's 2026 daylight saving starts Sunday 27 Sep at 02:00 standard time (= 26 Sep 14:00 UTC).
        Assert.AreEqual("Local: 2026-09-27 01:59:59 +12:00", At(2026, 9, 26, 13, 59, 59).LocalLine);
        Assert.AreEqual("Local: 2026-09-27 03:00:00 +13:00", At(2026, 9, 26, 14, 0, 0).LocalLine);
    }

    [TestMethod]
    public void GetTimestampPair_DefaultsToTheHostMachinesOwnLocalZone()
    {
        // The one test that deliberately exercises the real default (TimeZoneInfo.Local) instead of
        // FixedZone — only the shape is checked, never a specific offset, so it can't be flaky on a
        // machine configured to a different zone than the one this was written on.
        var (_, local) = TimestampService.GetTimestampPair();

        StringAssert.Matches(local, new System.Text.RegularExpressions.Regex(@"^Local: \d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} [+-]\d{2}:\d{2}$"));
    }
}
