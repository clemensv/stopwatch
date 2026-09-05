using System.Collections.Generic;
using System.Linq;

namespace StopwatchOverlay;

/// <summary>
/// Pure presentation decisions shared by the floating overlay and its tests.
/// Keeping these rules outside the window prevents the compact and combined
/// paths from drifting into different structures.
/// </summary>
public static class OverlayPresentationPolicy
{
    public static double ClampBackgroundOpacity(double opacity)
        => double.IsFinite(opacity) ? System.Math.Clamp(opacity, 0, 1) : 0.5;

    public static bool ShouldShowProjectName(string? projectName)
        => !string.IsNullOrWhiteSpace(projectName);

    public static TimerSession? SelectCombinedTimer(
        IEnumerable<TimerSession> sessions,
        TimerSession? activeTimer)
        => activeTimer != null && sessions.Contains(activeTimer) ? activeTimer : null;
}
