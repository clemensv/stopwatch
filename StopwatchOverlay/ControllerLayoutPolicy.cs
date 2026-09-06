namespace StopwatchOverlay;

public static class ControllerLayoutPolicy
{
    public const double CompactWidthThreshold = 820;

    public static bool UseCompactLayout(double availableWidth)
        => !double.IsFinite(availableWidth) || availableWidth < CompactWidthThreshold;
}
