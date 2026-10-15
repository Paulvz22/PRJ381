namespace BCOpendayApp.Services;

public static class MapGeometry
{
    // Coordinates refer to the full supplied image. No aspect-fit letterboxing is included.
    public static (double Left, double Top) PinPosition(double x, double y,
        double imageWidth, double imageHeight, double markerSize = 44)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || x is < 0 or > 1 || y is < 0 or > 1
            || !double.IsFinite(imageWidth) || !double.IsFinite(imageHeight)
            || imageWidth <= 0 || imageHeight <= 0 || !double.IsFinite(markerSize) || markerSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(x), "Invalid map geometry.");
        return (Math.Clamp(x * imageWidth - markerSize / 2, 0, Math.Max(0, imageWidth - markerSize)),
            Math.Clamp(y * imageHeight - markerSize / 2, 0, Math.Max(0, imageHeight - markerSize)));
    }
}
