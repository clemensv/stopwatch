using Xunit;

namespace StopwatchOverlay.Tests
{
    public class ShortcutSettingsTests
    {
        [Fact]
        public void DefaultShortcuts_UsesWinF9ForToggleClock()
        {
            var shortcuts = AppSettings.DefaultShortcuts();

            var shortcut = shortcuts[ShortcutAction.ToggleClock];
            Assert.Equal(Shortcut.MOD_WIN, shortcut.Modifiers);
            Assert.Equal(0x78u, shortcut.VirtualKey);
        }

        [Fact]
        public void EnsureAllActions_AddsToggleClockToOlderSettings()
        {
            var settings = new AppSettings();

            settings.EnsureAllActions();

            Assert.Contains(ShortcutAction.ToggleClock, settings.Shortcuts.Keys);
        }
    }
}
