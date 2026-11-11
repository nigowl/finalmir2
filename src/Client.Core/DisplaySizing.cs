namespace Crystal.Native;

public static class DisplaySizing
{
    public static (int Width, int Height) Fit(int usableWidth, int usableHeight, float displayScale)
    {
        if (usableWidth <= 0 || usableHeight <= 0 || !float.IsFinite(displayScale) || displayScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(displayScale), "Display dimensions and scale must be positive.");
        var scale = Math.Min(1.25 * displayScale, Math.Min(usableWidth * .9 / 1024, usableHeight * .9 / 768));
        var width = Math.Max(4, (int)Math.Floor(1024 * scale / 4) * 4);
        return (width, width * 3 / 4);
    }
}
