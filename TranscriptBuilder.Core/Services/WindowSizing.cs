namespace TranscriptBuilder.Services;

// Decides the window's launch size from a remembered value, the declared default, and the screen.
public static class WindowSizing
{
    // A remembered value is used only if it's a real number no smaller than the window's minimum
    // (NaN fails that comparison too); otherwise the declared default is used. Either way the result
    // is capped at the screen's work area, since WPF doesn't clamp an explicit size itself — a small
    // display, or high DPI scaling shrinking the effective work area, would leave the window partly
    // off-screen.
    public static double Resolve(double? remembered, double fallback, double minimum, double available)
    {
        var candidate = remembered is double value && value >= minimum ? value : fallback;
        return Math.Min(candidate, available);
    }
}
