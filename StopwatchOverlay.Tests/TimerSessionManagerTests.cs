using Xunit;

namespace StopwatchOverlay.Tests
{
    public class TimerSessionManagerTests
    {
        [Fact]
        public void EmptyManager_AllowsZeroSessions()
        {
            var manager = new TimerSessionManager();

            Assert.Empty(manager.Sessions);
            Assert.Equal(0, manager.Count);
            Assert.Null(manager.Active);
            Assert.Null(manager.CycleNext());
            Assert.False(manager.CloseActive());
        }

        [Fact]
        public void Create_AssignsMonotonicNumbersAndActivatesNewest()
        {
            var manager = new TimerSessionManager();

            var first = manager.Create();
            var second = manager.Create();
            var third = manager.Create();

            Assert.Equal(new[] { 1, 2, 3 }, new[] { first.Number, second.Number, third.Number });
            Assert.Same(third, manager.Active);
        }

        [Fact]
        public void ClosedTimerNumber_IsNeverReused()
        {
            var manager = new TimerSessionManager();
            manager.Create();
            var second = manager.Create();
            manager.Create();

            Assert.True(manager.Close(second));
            var fourth = manager.Create();

            Assert.Equal(4, fourth.Number);
            Assert.Equal(new[] { 1, 3, 4 }, new[]
            {
                manager.Sessions[0].Number,
                manager.Sessions[1].Number,
                manager.Sessions[2].Number
            });
        }

        [Fact]
        public void CycleNext_UsesCreationOrderAndWraps()
        {
            var manager = new TimerSessionManager();
            var first = manager.Create();
            var second = manager.Create();
            var third = manager.Create();

            Assert.Same(first, manager.CycleNext());
            Assert.Same(second, manager.CycleNext());
            Assert.Same(third, manager.CycleNext());
        }

        [Fact]
        public void Activate_RejectsForeignSessionWithoutChangingActive()
        {
            var manager = new TimerSessionManager();
            var owned = manager.Create();
            var foreign = new TimerSession(99);

            Assert.False(manager.Activate(foreign));
            Assert.False(manager.Activate(null));
            Assert.Same(owned, manager.Active);
        }

        [Fact]
        public void CloseActive_SelectsNextNeighborAndEventuallyAllowsZero()
        {
            var manager = new TimerSessionManager();
            var first = manager.Create();
            var second = manager.Create();
            var third = manager.Create();
            manager.Activate(second);

            Assert.True(manager.CloseActive());
            Assert.Same(third, manager.Active);

            Assert.True(manager.CloseActive());
            Assert.Same(first, manager.Active);

            Assert.True(manager.CloseActive());
            Assert.Null(manager.Active);
            Assert.Empty(manager.Sessions);
        }

        [Fact]
        public void ClosingInactiveTimer_PreservesActiveTimer()
        {
            var manager = new TimerSessionManager();
            var first = manager.Create();
            var active = manager.Create();

            Assert.True(manager.Close(first));

            Assert.Same(active, manager.Active);
            Assert.Single(manager.Sessions);
        }

        [Fact]
        public void Sessions_KeepIndependentRuntimeAndPresentationState()
        {
            var manager = new TimerSessionManager();
            var first = manager.Create();
            var second = manager.Create();

            first.IsRunning = true;
            first.Mode = 2;
            first.CountdownRemaining = System.TimeSpan.FromSeconds(30);
            first.Name = "Tea";
            first.LapTimes.Add("Lap 1: 00:01");

            Assert.False(second.IsRunning);
            Assert.Equal(0, second.Mode);
            Assert.Equal(System.TimeSpan.Zero, second.CountdownRemaining);
            Assert.Equal(string.Empty, second.Name);
            Assert.Empty(second.LapTimes);
        }
    }
}
