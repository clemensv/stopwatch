using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StopwatchOverlay.Tests
{
    public sealed class TimerWorkspaceStoreTests
    {
        private static readonly DateTime SavedUtc = new(
            2026, 8, 31, 8, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime RestoredUtc = new(
            2026, 8, 31, 8, 2, 0, DateTimeKind.Utc);
        private static readonly DateTime RestoredLocal = new(
            2026, 8, 31, 11, 32, 0, DateTimeKind.Local);

        [Fact]
        public void CaptureAndRestore_PreservesOrderActiveIdentityNextNumberAndPresentation()
        {
            var source = new TimerSessionManager();
            var first = source.Create();
            var second = source.Create();
            var third = source.Create();
            source.Close(second);
            source.Activate(first);

            first.Name = "Deep work";
            first.Mode = 3;
            first.LastNonClockMode = 3;
            first.CountdownDuration = TimeSpan.FromMinutes(17);
            first.CountdownRemaining = TimeSpan.FromMinutes(12);
            first.CountdownInitialized = true;
            first.CountdownMinutesText = "17";
            first.CountdownSecondsText = "45";
            first.ClockTargetHoursText = "14";
            first.ClockTargetMinutesText = "05";
            first.ClockTargetSecondsText = "09";
            first.SmartInputText = "next monday at 9am";
            first.LapTimes.Add("Lap 1: 00:01.2");
            first.LapCount = 1;
            first.OverlayVisible = true;
            first.HasCustomPosition = true;
            first.CustomLeft = 123.5;
            first.CustomTop = 456.25;
            first.CustomPositionsByScreen["DISPLAY-A"] = (23.25, 91.75);
            first.LastPresetPosition = "Bottom Right";
            first.CascadeIndex = 4;
            first.RestoreElapsed(TimeSpan.FromSeconds(9), start: false);

            var snapshot = TimerWorkspaceStore.Capture(source, SavedUtc);
            var restored = new TimerSessionManager();
            TimerWorkspaceStore.Restore(snapshot, restored, SavedUtc, RestoredLocal);

            Assert.Equal(new[] { first.Id, third.Id }, restored.Sessions.Select(timer => timer.Id));
            Assert.Equal(first.Id, restored.Active!.Id);
            Assert.Equal(4, restored.NextNumber);

            var actual = restored.Sessions[0];
            Assert.Equal("Deep work", actual.Name);
            Assert.Equal(3, actual.Mode);
            Assert.Equal(TimeSpan.FromSeconds(9), actual.Elapsed);
            Assert.Equal("next monday at 9am", actual.SmartInputText);
            Assert.Equal("Lap 1: 00:01.2", Assert.Single(actual.LapTimes));
            Assert.True(actual.OverlayVisible);
            Assert.True(actual.HasCustomPosition);
            Assert.Equal(123.5, actual.CustomLeft);
            Assert.Equal((23.25, 91.75), actual.CustomPositionsByScreen["DISPLAY-A"]);
            Assert.Equal("Bottom Right", actual.LastPresetPosition);
            Assert.Equal(4, actual.CascadeIndex);
        }

        [Fact]
        public void Restore_RunningStopwatchAddsOfflineTimeAndResumes()
        {
            var snapshot = Snapshot(Timer(
                running: true,
                elapsed: TimeSpan.FromSeconds(10),
                mode: 0));
            var manager = new TimerSessionManager();

            TimerWorkspaceStore.Restore(snapshot, manager, RestoredUtc, RestoredLocal);

            var timer = Assert.Single(manager.Sessions);
            Assert.True(timer.IsRunning);
            Assert.True(timer.Stopwatch.IsRunning);
            Assert.InRange(
                timer.Elapsed,
                TimeSpan.FromSeconds(130),
                TimeSpan.FromSeconds(130.5));
        }

        [Fact]
        public void Restore_PausedStopwatchKeepsExactElapsedAndDoesNotStart()
        {
            var snapshot = Snapshot(Timer(
                running: false,
                elapsed: TimeSpan.FromSeconds(10),
                mode: 0));
            var manager = new TimerSessionManager();

            TimerWorkspaceStore.Restore(snapshot, manager, RestoredUtc, RestoredLocal);

            var timer = Assert.Single(manager.Sessions);
            Assert.False(timer.IsRunning);
            Assert.False(timer.Stopwatch.IsRunning);
            Assert.Equal(TimeSpan.FromSeconds(10), timer.Elapsed);
        }

        [Fact]
        public void Restore_RunningFixedCountdownSubtractsOfflineTime()
        {
            var saved = Timer(running: true, elapsed: TimeSpan.Zero, mode: 2);
            saved.CountdownRemaining = TimeSpan.FromMinutes(5);
            saved.CountdownInitialized = true;
            saved.UseClockTarget = false;
            var manager = new TimerSessionManager();

            TimerWorkspaceStore.Restore(Snapshot(saved), manager, RestoredUtc, RestoredLocal);

            var timer = Assert.Single(manager.Sessions);
            Assert.Equal(TimeSpan.FromMinutes(3), timer.CountdownRemaining);
            Assert.Equal(RestoredUtc, timer.LastCountdownUpdateUtc);
            Assert.True(timer.Stopwatch.IsRunning);
        }

        [Fact]
        public void Restore_PausedFixedCountdownKeepsExactRemainingTime()
        {
            var saved = Timer(running: false, elapsed: TimeSpan.Zero, mode: 2);
            saved.CountdownRemaining = TimeSpan.FromMinutes(5);
            saved.CountdownInitialized = true;
            saved.LastCountdownUpdateUtc = SavedUtc.AddSeconds(-1);
            var manager = new TimerSessionManager();

            TimerWorkspaceStore.Restore(Snapshot(saved), manager, RestoredUtc, RestoredLocal);

            var timer = Assert.Single(manager.Sessions);
            Assert.Equal(TimeSpan.FromMinutes(5), timer.CountdownRemaining);
            Assert.Equal(saved.LastCountdownUpdateUtc, timer.LastCountdownUpdateUtc);
            Assert.False(timer.Stopwatch.IsRunning);
        }

        [Fact]
        public void Restore_RunningWallTargetDerivesRemainingFromTargetAndLocalNow()
        {
            var saved = Timer(running: true, elapsed: TimeSpan.Zero, mode: 2);
            saved.UseClockTarget = true;
            saved.CountdownInitialized = true;
            saved.CountdownRemaining = TimeSpan.FromHours(99); // Must be ignored.
            saved.ClockTarget = RestoredLocal.AddMinutes(8);
            var manager = new TimerSessionManager();

            TimerWorkspaceStore.Restore(Snapshot(saved), manager, RestoredUtc, RestoredLocal);

            Assert.Equal(TimeSpan.FromMinutes(8), Assert.Single(manager.Sessions).CountdownRemaining);
        }

        [Fact]
        public void Restore_ClockToggleOverCountdownContinuesCountdown()
        {
            var saved = Timer(running: true, elapsed: TimeSpan.Zero, mode: 1);
            saved.LastNonClockMode = 2;
            saved.CountdownInitialized = true;
            saved.CountdownRemaining = TimeSpan.FromMinutes(5);
            var manager = new TimerSessionManager();

            TimerWorkspaceStore.Restore(Snapshot(saved), manager, RestoredUtc, RestoredLocal);

            var timer = Assert.Single(manager.Sessions);
            Assert.Equal(1, timer.Mode);
            Assert.Equal(2, timer.LastNonClockMode);
            Assert.Equal(TimeSpan.FromMinutes(3), timer.CountdownRemaining);
        }

        [Fact]
        public void Restore_ClockToggleOverStopwatchContinuesElapsedTime()
        {
            var saved = Timer(running: true, elapsed: TimeSpan.FromSeconds(4), mode: 1);
            saved.LastNonClockMode = 0;
            var manager = new TimerSessionManager();

            TimerWorkspaceStore.Restore(Snapshot(saved), manager, RestoredUtc, RestoredLocal);

            Assert.InRange(
                Assert.Single(manager.Sessions).Elapsed,
                TimeSpan.FromSeconds(124),
                TimeSpan.FromSeconds(124.5));
        }

        [Fact]
        public void Restore_ExpiredRunningCountdownKeepsCountingNegative()
        {
            var saved = Timer(running: true, elapsed: TimeSpan.Zero, mode: 2);
            saved.CountdownRemaining = TimeSpan.FromSeconds(30);
            saved.CountdownInitialized = true;
            var manager = new TimerSessionManager();

            TimerWorkspaceStore.Restore(Snapshot(saved), manager, RestoredUtc, RestoredLocal);

            Assert.Equal(TimeSpan.FromSeconds(-90), Assert.Single(manager.Sessions).CountdownRemaining);
        }

        [Fact]
        public void Restore_ZeroTimerWorkspaceRemainsEmpty()
        {
            var manager = new TimerSessionManager();
            var snapshot = new TimerWorkspaceSnapshot
            {
                SavedAtUtc = SavedUtc,
                NextNumber = 7,
                ActiveTimerId = null
            };

            TimerWorkspaceStore.Restore(snapshot, manager, RestoredUtc, RestoredLocal);

            Assert.Empty(manager.Sessions);
            Assert.Null(manager.Active);
            Assert.Equal(7, manager.NextNumber);
            Assert.Equal(7, manager.Create().Number);
        }

        [Fact]
        public void TryLoad_CorruptPrimaryFallsBackToPreviousValidGeneration()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "workspace.json");
            var store = new TimerWorkspaceStore(path);
            var manager = new TimerSessionManager();
            manager.Create().Name = "previous";
            Assert.True(store.Save(manager, SavedUtc));

            manager.Create().Name = "latest";
            Assert.True(store.Save(manager, SavedUtc.AddMinutes(1)));
            File.WriteAllText(path, "{ definitely not json");

            var restored = new TimerSessionManager();
            Assert.True(store.TryLoad(restored, RestoredUtc, RestoredLocal));
            Assert.Single(restored.Sessions);
            Assert.Equal("previous", restored.Sessions[0].Name);
            Assert.Equal(SavedUtc, store.LastLoadedSavedAtUtc);
            Assert.True(store.LastLoadUsedBackup);
        }

        [Fact]
        public void TryLoad_PrimarySuccessExposesExactSnapshotMetadata()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "workspace.json");
            var store = new TimerWorkspaceStore(path);
            var source = new TimerSessionManager();
            source.Create().Name = "primary";
            DateTime exactSavedAtUtc = SavedUtc.AddTicks(1234);
            Assert.True(store.Save(source, exactSavedAtUtc));

            var restored = new TimerSessionManager();
            Assert.True(store.TryLoad(restored, RestoredUtc, RestoredLocal));

            Assert.Equal(exactSavedAtUtc, store.LastLoadedSavedAtUtc);
            Assert.False(store.LastLoadUsedBackup);
        }

        [Fact]
        public void TryLoad_PreservesProjectHistoryReconciliationGuard()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "workspace.json");
            var store = new TimerWorkspaceStore(path);
            var source = new TimerSessionManager();
            source.Create();
            TimerWorkspaceSnapshot snapshot = TimerWorkspaceStore.Capture(source, SavedUtc);
            snapshot.SkipProjectHistoryReconciliation = true;
            Assert.True(store.Save(snapshot));

            Assert.True(store.TryLoad(
                new TimerSessionManager(), RestoredUtc, RestoredLocal));

            Assert.True(store.LastLoadedSkipProjectHistoryReconciliation);
        }

        [Fact]
        public void TryLoad_PreservesCombinedOverlayPresentation()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "workspace.json");
            var store = new TimerWorkspaceStore(path);
            var source = new TimerSessionManager();
            source.Create();
            TimerWorkspaceSnapshot snapshot = TimerWorkspaceStore.Capture(source, SavedUtc);
            snapshot.CombinedOverlayMode = true;
            snapshot.CombinedOverlayVisible = false;
            snapshot.CombinedHasCustomPosition = true;
            snapshot.CombinedPositionsByScreen["DISPLAY-A"] = new TimerScreenPositionSnapshot
            {
                Left = 123.5,
                Top = 456.25
            };
            Assert.True(store.Save(snapshot));

            Assert.True(store.TryLoad(
                new TimerSessionManager(), RestoredUtc, RestoredLocal));

            Assert.True(store.LastLoadedCombinedOverlayMode);
            Assert.False(store.LastLoadedCombinedOverlayVisible);
            Assert.True(store.LastLoadedCombinedHasCustomPosition);
            Assert.Equal(123.5, store.LastLoadedCombinedPositionsByScreen["DISPLAY-A"].Left);
            Assert.Equal(456.25, store.LastLoadedCombinedPositionsByScreen["DISPLAY-A"].Top);
        }

        [Fact]
        public void TryLoad_PreCombinedWorkspaceDefaultsToSeparatePresentation()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "workspace.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                Version = TimerWorkspaceStore.CurrentVersion,
                SavedAtUtc = SavedUtc,
                ActiveTimerId = (Guid?)null,
                NextNumber = 1,
                Timers = Array.Empty<object>()
            }));
            var store = new TimerWorkspaceStore(path);

            Assert.True(store.TryLoad(
                new TimerSessionManager(), RestoredUtc, RestoredLocal));

            Assert.False(store.LastLoadedCombinedOverlayMode);
            Assert.True(store.LastLoadedCombinedOverlayVisible);
            Assert.False(store.LastLoadedCombinedHasCustomPosition);
            Assert.Empty(store.LastLoadedCombinedPositionsByScreen);
        }

        [Fact]
        public void TryLoad_FailureClearsPreviousSuccessfulLoadMetadata()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "workspace.json");
            var store = new TimerWorkspaceStore(path);
            var source = new TimerSessionManager();
            source.Create();
            Assert.True(store.Save(source, SavedUtc));
            Assert.True(store.TryLoad(
                new TimerSessionManager(), RestoredUtc, RestoredLocal));
            Assert.Equal(SavedUtc, store.LastLoadedSavedAtUtc);

            File.WriteAllText(path, "not json");
            Assert.False(store.TryLoad(
                new TimerSessionManager(), RestoredUtc, RestoredLocal));

            Assert.Null(store.LastLoadedSavedAtUtc);
            Assert.False(store.LastLoadUsedBackup);
            Assert.False(store.LastLoadedSkipProjectHistoryReconciliation);
            Assert.False(store.LastLoadedCombinedOverlayMode);
            Assert.True(store.LastLoadedCombinedOverlayVisible);
            Assert.False(store.LastLoadedCombinedHasCustomPosition);
            Assert.Empty(store.LastLoadedCombinedPositionsByScreen);
        }

        [Fact]
        public void SaveAttemptsClearPreviousSuccessfulLoadMetadata()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "workspace.json");
            var store = new TimerWorkspaceStore(path);
            var source = new TimerSessionManager();
            source.Create();
            Assert.True(store.Save(source, SavedUtc));
            Assert.True(store.TryLoad(
                new TimerSessionManager(), RestoredUtc, RestoredLocal));

            Assert.True(store.Save(source, SavedUtc.AddMinutes(1)));

            Assert.Null(store.LastLoadedSavedAtUtc);
            Assert.False(store.LastLoadUsedBackup);

            Assert.True(store.TryLoad(
                new TimerSessionManager(), RestoredUtc, RestoredLocal));
            Assert.False(store.Save(new TimerWorkspaceSnapshot()));

            Assert.Null(store.LastLoadedSavedAtUtc);
            Assert.False(store.LastLoadUsedBackup);
        }

        [Fact]
        public void Save_AfterBackupRecoveryRepairsPrimaryWithoutReplacingGoodBackup()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "workspace.json");
            var store = new TimerWorkspaceStore(path);

            var firstGeneration = new TimerSessionManager();
            firstGeneration.Create().Name = "known good backup";
            Assert.True(store.Save(firstGeneration, SavedUtc));

            var secondGeneration = new TimerSessionManager();
            secondGeneration.Create().Name = "will become corrupt";
            Assert.True(store.Save(secondGeneration, SavedUtc.AddMinutes(1)));
            File.WriteAllText(path, "{ corrupt primary");

            var recovered = new TimerSessionManager();
            Assert.True(store.TryLoad(recovered, RestoredUtc, RestoredLocal));
            Assert.Equal("known good backup", Assert.Single(recovered.Sessions).Name);

            recovered.Active!.Name = "repaired primary";
            Assert.True(store.Save(recovered, RestoredUtc));

            var primaryReader = new TimerWorkspaceStore(path);
            Assert.True(primaryReader.TryReadSnapshot(out var repairedPrimary));
            Assert.Equal(
                "repaired primary",
                Assert.Single(repairedPrimary!.Timers).Name);

            var backupReader = new TimerWorkspaceStore(path + ".bak");
            Assert.True(backupReader.TryReadSnapshot(out var preservedBackup));
            Assert.Equal(
                "known good backup",
                Assert.Single(preservedBackup!.Timers).Name);

            // The preserved backup must remain usable for another recovery, not
            // merely be parseable through the direct reader above.
            File.WriteAllText(path, "corrupt again");
            var secondRecovery = new TimerSessionManager();
            Assert.True(primaryReader.TryLoad(secondRecovery, RestoredUtc, RestoredLocal));
            Assert.Equal("known good backup", Assert.Single(secondRecovery.Sessions).Name);
        }

        [Fact]
        public void TryLoad_CorruptFilesReturnFalseWithoutMutatingManager()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "workspace.json");
            File.WriteAllText(path, "not json");
            File.WriteAllText(path + ".bak", "also not json");
            var store = new TimerWorkspaceStore(path);
            var manager = new TimerSessionManager();
            var existing = manager.Create();

            Assert.False(store.TryLoad(manager, RestoredUtc, RestoredLocal));
            Assert.Same(existing, manager.Active);
            Assert.Single(manager.Sessions);
        }

        [Fact]
        public void Restore_FutureSaveTimestampNeverMovesRunningTimerBackward()
        {
            var saved = Timer(running: true, elapsed: TimeSpan.FromSeconds(5), mode: 2);
            saved.CountdownRemaining = TimeSpan.FromSeconds(20);
            saved.CountdownInitialized = true;
            var snapshot = Snapshot(saved);
            snapshot.SavedAtUtc = RestoredUtc.AddMinutes(1);
            var manager = new TimerSessionManager();

            TimerWorkspaceStore.Restore(snapshot, manager, RestoredUtc, RestoredLocal);

            var timer = Assert.Single(manager.Sessions);
            Assert.InRange(timer.Elapsed, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5.5));
            Assert.Equal(TimeSpan.FromSeconds(20), timer.CountdownRemaining);
        }

        [Theory]
        [InlineData(-1, 0)]
        [InlineData(4, 0)]
        [InlineData(0, -1)]
        [InlineData(0, 4)]
        public void Restore_RejectsInvalidModesWithoutMutatingManager(int mode, int lastMode)
        {
            var saved = Timer(running: false, elapsed: TimeSpan.Zero, mode: 0);
            saved.Mode = mode;
            saved.LastNonClockMode = lastMode;
            var target = ManagerWithExistingTimer(out var existing);

            Assert.Throws<InvalidDataException>(() =>
                TimerWorkspaceStore.Restore(Snapshot(saved), target, RestoredUtc, RestoredLocal));
            Assert.Same(existing, target.Active);
            Assert.Single(target.Sessions);
        }

        [Fact]
        public void Restore_RejectsNegativeLapCountWithoutMutatingManager()
        {
            var saved = Timer(running: false, elapsed: TimeSpan.Zero, mode: 0);
            saved.LapCount = -1;
            var target = ManagerWithExistingTimer(out var existing);

            Assert.Throws<InvalidDataException>(() =>
                TimerWorkspaceStore.Restore(Snapshot(saved), target, RestoredUtc, RestoredLocal));
            Assert.Same(existing, target.Active);
        }

        [Theory]
        [InlineData("custom-left")]
        [InlineData("custom-top")]
        [InlineData("screen-left")]
        [InlineData("screen-top")]
        public void Restore_RejectsNonFiniteCoordinatesWithoutMutatingManager(string coordinate)
        {
            var saved = Timer(running: false, elapsed: TimeSpan.Zero, mode: 0);
            saved.CustomPositionsByScreen["DISPLAY-A"] = new TimerScreenPositionSnapshot
            {
                Left = 1,
                Top = 2
            };
            switch (coordinate)
            {
                case "custom-left": saved.CustomLeft = double.NaN; break;
                case "custom-top": saved.CustomTop = double.PositiveInfinity; break;
                case "screen-left": saved.CustomPositionsByScreen["DISPLAY-A"].Left = double.NegativeInfinity; break;
                case "screen-top": saved.CustomPositionsByScreen["DISPLAY-A"].Top = double.NaN; break;
            }
            var target = ManagerWithExistingTimer(out var existing);

            Assert.Throws<InvalidDataException>(() =>
                TimerWorkspaceStore.Restore(Snapshot(saved), target, RestoredUtc, RestoredLocal));
            Assert.Same(existing, target.Active);
        }

        [Fact]
        public void Restore_RejectsMissingActiveTimerWithoutMutatingManager()
        {
            var snapshot = Snapshot(Timer(false, TimeSpan.Zero, 0));
            snapshot.ActiveTimerId = Guid.NewGuid();
            var target = ManagerWithExistingTimer(out var existing);

            Assert.Throws<InvalidDataException>(() =>
                TimerWorkspaceStore.Restore(snapshot, target, RestoredUtc, RestoredLocal));
            Assert.Same(existing, target.Active);
        }

        [Fact]
        public void TryLoad_NewerVersionIsReportedAndFileIsNotChangedOrDowngraded()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "workspace.json");
            string newerJson = JsonSerializer.Serialize(new TimerWorkspaceSnapshot
            {
                Version = TimerWorkspaceStore.CurrentVersion + 1,
                SavedAtUtc = SavedUtc,
                NextNumber = 1
            });
            File.WriteAllText(path, newerJson);

            // Even a valid older backup must not be silently substituted for a
            // primary file written by a newer application.
            var oldManager = new TimerSessionManager();
            oldManager.Create().Name = "old backup";
            var backupStore = new TimerWorkspaceStore(path + ".bak");
            Assert.True(backupStore.Save(oldManager, SavedUtc));

            var store = new TimerWorkspaceStore(path);
            var target = ManagerWithExistingTimer(out var existing);
            Assert.False(store.TryLoad(target, RestoredUtc, RestoredLocal));

            Assert.Equal(TimerWorkspaceReadStatus.UnsupportedVersion, store.LastReadStatus);
            Assert.Equal(newerJson, File.ReadAllText(path));
            Assert.Same(existing, target.Active);
            Assert.Single(target.Sessions);
        }

        [Fact]
        public void TryLoad_UnavailablePrimaryDoesNotLoadOrOverwriteStaleBackup()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "workspace.json");

            var staleManager = new TimerSessionManager();
            staleManager.Create().Name = "stale backup";
            var backupStore = new TimerWorkspaceStore(path + ".bak");
            Assert.True(backupStore.Save(staleManager, SavedUtc));

            // A directory at the primary file path reliably represents an
            // inaccessible file without depending on platform-specific locks.
            Directory.CreateDirectory(path);
            var store = new TimerWorkspaceStore(path);
            var target = ManagerWithExistingTimer(out var existing);

            Assert.False(store.TryLoad(target, RestoredUtc, RestoredLocal));

            Assert.Equal(TimerWorkspaceReadStatus.Unavailable, store.LastReadStatus);
            Assert.Equal(TimerWorkspaceReadStatus.Unavailable, store.LastPrimaryReadStatus);
            Assert.Equal(TimerWorkspaceReadStatus.None, store.LastBackupReadStatus);
            Assert.Null(store.LastLoadedSavedAtUtc);
            Assert.False(store.LastLoadUsedBackup);
            Assert.Same(existing, target.Active);
            Assert.Single(target.Sessions);
        }

        [Fact]
        public async Task TryLoad_TransientExclusiveLockRetriesPrimaryBeforeReportingUnavailable()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "workspace.json");
            var writer = new TimerWorkspaceStore(path);
            var expected = new TimerSessionManager();
            expected.Create().Name = "must survive a transient sharing violation";
            Assert.True(writer.Save(expected, SavedUtc));

            var store = new TimerWorkspaceStore(path);
            var restored = new TimerSessionManager();
            using var locked = new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            using var loadStarted = new ManualResetEventSlim();
            Task<bool> loadTask = Task.Run(() =>
            {
                loadStarted.Set();
                return store.TryLoad(restored, RestoredUtc, RestoredLocal);
            });

            Assert.True(loadStarted.Wait(TimeSpan.FromSeconds(2)));
            await Task.Delay(TimeSpan.FromMilliseconds(40));
            Assert.False(loadTask.IsCompleted);
            locked.Dispose();

            Assert.True(await loadTask.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(
                "must survive a transient sharing violation",
                Assert.Single(restored.Sessions).Name);
            Assert.Equal(TimerWorkspaceReadStatus.Success, store.LastReadStatus);
            Assert.Equal(TimerWorkspaceReadStatus.Success, store.LastPrimaryReadStatus);
            Assert.Equal(TimerWorkspaceReadStatus.None, store.LastBackupReadStatus);
            Assert.Equal(SavedUtc, store.LastLoadedSavedAtUtc);
            Assert.False(store.LastLoadUsedBackup);
        }

        [Fact]
        public void TryLoad_ExclusiveLockThatOutlivesRetriesPreservesPrimaryAndManager()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "workspace.json");
            var writer = new TimerWorkspaceStore(path);
            var expected = new TimerSessionManager();
            expected.Create().Name = "new primary";
            Assert.True(writer.Save(expected, SavedUtc.AddMinutes(1)));

            var staleManager = new TimerSessionManager();
            staleManager.Create().Name = "stale backup";
            var backupStore = new TimerWorkspaceStore(path + ".bak");
            Assert.True(backupStore.Save(staleManager, SavedUtc));

            string primaryBefore = File.ReadAllText(path);
            var store = new TimerWorkspaceStore(path);
            var target = ManagerWithExistingTimer(out var existing);
            using var locked = new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);

            Assert.False(store.TryLoad(target, RestoredUtc, RestoredLocal));

            Assert.Equal(TimerWorkspaceReadStatus.Unavailable, store.LastReadStatus);
            Assert.Equal(TimerWorkspaceReadStatus.Unavailable, store.LastPrimaryReadStatus);
            Assert.Equal(TimerWorkspaceReadStatus.None, store.LastBackupReadStatus);
            Assert.Null(store.LastLoadedSavedAtUtc);
            Assert.False(store.LastLoadUsedBackup);
            Assert.Same(existing, target.Active);
            Assert.Single(target.Sessions);
            locked.Dispose();
            Assert.Equal(primaryBefore, File.ReadAllText(path));
        }

        [Fact]
        public void TryLoad_UnavailableBackupTakesPrecedenceOverCorruptPrimary()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "workspace.json");
            File.WriteAllText(path, "corrupt");
            Directory.CreateDirectory(path + ".bak");
            var store = new TimerWorkspaceStore(path);
            var target = ManagerWithExistingTimer(out var existing);

            Assert.False(store.TryLoad(target, RestoredUtc, RestoredLocal));

            Assert.Equal(TimerWorkspaceReadStatus.Unavailable, store.LastReadStatus);
            Assert.Equal(TimerWorkspaceReadStatus.Corrupt, store.LastPrimaryReadStatus);
            Assert.Equal(TimerWorkspaceReadStatus.Unavailable, store.LastBackupReadStatus);
            Assert.Null(store.LastLoadedSavedAtUtc);
            Assert.False(store.LastLoadUsedBackup);
            Assert.Same(existing, target.Active);
        }

        [Fact]
        public void TryLoad_MissingPrimaryStillRecoversValidBackup()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "workspace.json");
            var expected = new TimerSessionManager();
            expected.Create().Name = "backup only";
            var backupStore = new TimerWorkspaceStore(path + ".bak");
            Assert.True(backupStore.Save(expected, SavedUtc));

            var store = new TimerWorkspaceStore(path);
            var restored = new TimerSessionManager();
            Assert.True(store.TryLoad(restored, RestoredUtc, RestoredLocal));

            Assert.Equal("backup only", Assert.Single(restored.Sessions).Name);
            Assert.Equal(TimerWorkspaceReadStatus.Success, store.LastReadStatus);
            Assert.Equal(TimerWorkspaceReadStatus.NotFound, store.LastPrimaryReadStatus);
            Assert.Equal(TimerWorkspaceReadStatus.Success, store.LastBackupReadStatus);
            Assert.Equal(SavedUtc, store.LastLoadedSavedAtUtc);
            Assert.True(store.LastLoadUsedBackup);
        }

        [Fact]
        public void SaveNew_CreatesPrimaryButNeverReplacesIt()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "workspace.json");
            var first = new TimerSessionManager();
            first.Create().Name = "first";
            var store = new TimerWorkspaceStore(path);

            Assert.True(store.SaveNew(first, SavedUtc));
            Assert.False(store.LastSaveNewConflictDetected);
            string firstGeneration = File.ReadAllText(path);
            Assert.Equal(firstGeneration, File.ReadAllText(path + ".bak"));

            var second = new TimerSessionManager();
            second.Create().Name = "second";
            Assert.False(store.SaveNew(second, SavedUtc.AddMinutes(1)));
            Assert.True(store.LastSaveNewConflictDetected);
            Assert.Equal(firstGeneration, File.ReadAllText(path));
        }

        [Fact]
        public void SaveNew_ExistingBackupWinsWithoutCreatingPrimary()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "workspace.json");
            byte[] backup = [1, 3, 3, 7];
            File.WriteAllBytes(path + ".bak", backup);
            var manager = new TimerSessionManager();
            manager.Create().Name = "new";
            var store = new TimerWorkspaceStore(path);

            Assert.False(store.SaveNew(manager, SavedUtc));
            Assert.True(store.LastSaveNewConflictDetected);

            Assert.False(File.Exists(path));
            Assert.Equal(backup, File.ReadAllBytes(path + ".bak"));
        }

        private static TimerWorkspaceSnapshot Snapshot(TimerSessionSnapshot timer)
            => new()
            {
                SavedAtUtc = SavedUtc,
                ActiveTimerId = timer.Id,
                NextNumber = timer.Number + 1,
                Timers = new() { timer }
            };

        private static TimerSessionSnapshot Timer(bool running, TimeSpan elapsed, int mode)
            => new()
            {
                Id = Guid.NewGuid(),
                Number = 1,
                IsRunning = running,
                Elapsed = elapsed,
                Mode = mode,
                LastNonClockMode = mode == 1 ? 0 : mode,
                CountdownDuration = TimeSpan.FromMinutes(5),
                CountdownRemaining = TimeSpan.FromMinutes(5),
                CountdownMinutesText = "5",
                CountdownSecondsText = "00",
                LapTimes = new(),
                CustomPositionsByScreen = new()
            };

        private static TimerSessionManager ManagerWithExistingTimer(out TimerSession existing)
        {
            var manager = new TimerSessionManager();
            existing = manager.Create();
            return manager;
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "StopwatchOverlayTests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public void Dispose()
            {
                try { Directory.Delete(Path, recursive: true); }
                catch { }
            }
        }
    }
}
