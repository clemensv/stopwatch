using Xunit;

namespace StopwatchOverlay.Tests
{
    public class ShortcutSettingsTests
    {
        [Theory]
        [InlineData(ShortcutAction.NewTimer, 0x71u)]
        [InlineData(ShortcutAction.NextTimer, 0x72u)]
        [InlineData(ShortcutAction.CloseTimer, 0x73u)]
        [InlineData(ShortcutAction.StartStop, 0x74u)]
        [InlineData(ShortcutAction.Reset, 0x75u)]
        [InlineData(ShortcutAction.ToggleOverlay, 0x76u)]
        [InlineData(ShortcutAction.Lap, 0x77u)]
        [InlineData(ShortcutAction.ToggleClock, 0x78u)]
        [InlineData(ShortcutAction.RenameTimer, 0x79u)]
        [InlineData(ShortcutAction.OpenDashboard, 0x7Au)]
        [InlineData(ShortcutAction.ToggleCombinedOverlay, 0x7Bu)]
        public void DefaultShortcuts_UsesExpectedWinFunctionKey(ShortcutAction action, uint virtualKey)
        {
            var shortcuts = AppSettings.DefaultShortcuts();

            var shortcut = shortcuts[action];
            Assert.Equal(Shortcut.MOD_WIN, shortcut.Modifiers);
            Assert.Equal(virtualKey, shortcut.VirtualKey);
        }

        [Fact]
        public void EnsureAllActions_AddsNewActionsWithoutChangingExistingAssignments()
        {
            var customStartStop = new Shortcut(Shortcut.MOD_CONTROL | Shortcut.MOD_SHIFT, 0x53u);
            var settings = new AppSettings
            {
                Shortcuts = new()
                {
                    [ShortcutAction.StartStop] = customStartStop,
                    [ShortcutAction.Reset] = new Shortcut(Shortcut.MOD_WIN, 0x75u),
                    [ShortcutAction.ToggleOverlay] = new Shortcut(Shortcut.MOD_WIN, 0x76u),
                    [ShortcutAction.Lap] = new Shortcut(Shortcut.MOD_WIN, 0x77u),
                    [ShortcutAction.ToggleClock] = new Shortcut(Shortcut.MOD_WIN, 0x78u),
                }
            };

            settings.EnsureAllActions();

            Assert.Equal(customStartStop, settings.Shortcuts[ShortcutAction.StartStop]);
            Assert.Equal(AppSettings.DefaultShortcuts()[ShortcutAction.NewTimer], settings.Shortcuts[ShortcutAction.NewTimer]);
            Assert.Equal(AppSettings.DefaultShortcuts()[ShortcutAction.NextTimer], settings.Shortcuts[ShortcutAction.NextTimer]);
            Assert.Equal(AppSettings.DefaultShortcuts()[ShortcutAction.CloseTimer], settings.Shortcuts[ShortcutAction.CloseTimer]);
            Assert.Equal(AppSettings.DefaultShortcuts()[ShortcutAction.RenameTimer], settings.Shortcuts[ShortcutAction.RenameTimer]);
            Assert.Equal(AppSettings.DefaultShortcuts()[ShortcutAction.OpenDashboard], settings.Shortcuts[ShortcutAction.OpenDashboard]);
            Assert.Equal(AppSettings.DefaultShortcuts()[ShortcutAction.ToggleCombinedOverlay], settings.Shortcuts[ShortcutAction.ToggleCombinedOverlay]);
            Assert.Equal(11, settings.Shortcuts.Count);
        }

        [Fact]
        public void ShortcutAction_ExistingIdsRemainStableAndNewIdsAreAppended()
        {
            Assert.Equal(1, (int)ShortcutAction.StartStop);
            Assert.Equal(2, (int)ShortcutAction.Reset);
            Assert.Equal(3, (int)ShortcutAction.ToggleOverlay);
            Assert.Equal(4, (int)ShortcutAction.Lap);
            Assert.Equal(5, (int)ShortcutAction.ToggleClock);
            Assert.Equal(6, (int)ShortcutAction.NewTimer);
            Assert.Equal(7, (int)ShortcutAction.NextTimer);
            Assert.Equal(8, (int)ShortcutAction.CloseTimer);
            Assert.Equal(9, (int)ShortcutAction.RenameTimer);
            Assert.Equal(10, (int)ShortcutAction.OpenDashboard);
            Assert.Equal(11, (int)ShortcutAction.ToggleCombinedOverlay);
        }
    }
}
