using System.Globalization;

namespace TranscriptBuilder.Services;

// Produces the UTC and local-time header lines written for every transcript entry.
public static class TimestampService
{
    public static (string UtcLine, string LocalLine) GetTimestampPair() =>
        GetTimestampPair(DateTime.UtcNow, TimeZoneInfo.Local);

    // zone defaults to the host machine's own configured zone; tests pass an explicit one instead,
    // so the exact-DST-boundary test stays deterministic regardless of what machine runs it.
    // utcNow must have Kind == Utc.
    public static (string UtcLine, string LocalLine) GetTimestampPair(DateTime utcNow, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone);

        // A numeric UTC offset, not a named abbreviation (NZST, IST, ...): abbreviations collide
        // across zones (IST alone is India, Israel, or Ireland) and Windows only exposes verbose
        // display names, not short ones. An offset is unambiguous and needs no lookup table for any
        // zone on earth, so the local line auto-detects correctly with zero configuration.
        var offset = zone.GetUtcOffset(localNow);
        var offsetText = $"{(offset < TimeSpan.Zero ? "-" : "+")}{offset.Duration():hh\\:mm}";

        var utcLine = $"UTC: {utcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}";
        var localLine = $"Local: {localNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} {offsetText}";

        return (utcLine, localLine);
    }
}
