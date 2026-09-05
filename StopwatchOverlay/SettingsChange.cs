using System;

namespace StopwatchOverlay;

[Flags]
internal enum SettingsChangeKind
{
    None = 0,
    Theme = 1 << 0,
    OverlayScreen = 1 << 1,
    OverlayPosition = 1 << 2,
    OverlayAppearance = 1 << 3,
    OverlayGeometry = 1 << 4,
    BackgroundSelection = 1 << 5,
    BackgroundStrength = 1 << 6,
    OverlayInteraction = 1 << 7,
    LightRingVisibility = 1 << 8,
    LightRingAppearance = 1 << 9,
    Behavior = 1 << 10,
    Startup = 1 << 11,
    OverlayTheme = 1 << 12
}

internal static class SettingsChangePolicy
{
    private const SettingsChangeKind ContinuousChanges =
        SettingsChangeKind.OverlayAppearance
        | SettingsChangeKind.OverlayGeometry
        | SettingsChangeKind.BackgroundStrength
        | SettingsChangeKind.LightRingAppearance;

    internal static bool IsContinuous(SettingsChangeKind change)
        => (change & ContinuousChanges) != 0;

    internal static bool RequiresThemeApply(SettingsChangeKind change)
        => (change & SettingsChangeKind.Theme) != 0;

    internal static bool RequiresBackgroundApply(SettingsChangeKind change)
        => (change & (SettingsChangeKind.Theme
                      | SettingsChangeKind.BackgroundSelection
                      | SettingsChangeKind.BackgroundStrength)) != 0;

    internal static bool RequiresLightRingRebuild(SettingsChangeKind change)
        => (change & (SettingsChangeKind.LightRingVisibility | SettingsChangeKind.OverlayScreen)) != 0;

    internal static int ResolveScreenComboIndex(int persistedIndex, int screenCount)
    {
        int maximumIndex = Math.Max(0, screenCount);
        int defaultIndex = screenCount > 1 ? 1 : 0;
        return persistedIndex < 0 || persistedIndex > maximumIndex
            ? defaultIndex
            : persistedIndex;
    }
}
