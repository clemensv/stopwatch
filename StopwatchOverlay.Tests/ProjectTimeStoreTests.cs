using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using StopwatchOverlay;
using Xunit;

namespace StopwatchOverlay.Tests
{
    public sealed class ProjectTimeStoreTests
    {
        private static readonly DateTime StartUtc =
            new(2026, 8, 31, 11, 33, 00, DateTimeKind.Utc);

        [Fact]
        public void RegisterProject_IsCaseInsensitiveAndPreservesFirstDisplayCasing()
        {
            var history = new ProjectTimeHistory();

            Assert.Equal("Navid", history.RegisterProject("  Navid  "));
            Assert.Equal("Navid", history.RegisterProject("navid"));
            Assert.Equal("Other", history.RegisterProject("Other"));

            Assert.Equal(new[] { "Navid", "Other" }, history.ProjectNames);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("line\nbreak")]
        public void RegisterProject_RejectsInvalidNames(string projectName)
        {
            var history = new ProjectTimeHistory();

            Assert.Throws<ArgumentException>(() => history.RegisterProject(projectName));
            Assert.Empty(history.ProjectNames);
        }

        [Fact]
        public void AddManualInterval_CreatesCanonicalClosedIndependentRecord()
        {
            var history = new ProjectTimeHistory();
            Assert.Equal("Navid", history.RegisterProject("Navid"));

            ProjectWorkIntervalView first = history.AddManualInterval(
                "NAVID",
                StartUtc,
                StartUtc.AddMinutes(45));
            ProjectWorkIntervalView second = history.AddManualInterval(
                "navid",
                StartUtc.AddMinutes(15),
                StartUtc.AddHours(1));

            Assert.NotEqual(Guid.Empty, first.Id);
            Assert.NotEqual(Guid.Empty, first.TimerSessionId);
            Assert.NotEqual(first.Id, second.Id);
            Assert.NotEqual(first.TimerSessionId, second.TimerSessionId);
            Assert.Equal("NAVID", first.ProjectKey);
            Assert.Equal("Navid", first.ProjectName);
            Assert.Equal(StartUtc, first.StartUtc);
            Assert.Equal(StartUtc.AddMinutes(45), first.EndUtc);
            Assert.False(first.IsOpen);
            Assert.Equal(new[] { "Navid" }, history.ProjectNames);
            Assert.Equal(2, history.CreateView(StartUtc.AddHours(2)).Intervals.Count);
        }

        [Fact]
        public void AddManualInterval_InvalidInputHasNoSideEffects()
        {
            var history = new ProjectTimeHistory();

            Assert.Throws<ArgumentOutOfRangeException>(() => history.AddManualInterval(
                "Default start",
                default,
                StartUtc));
            Assert.Throws<ArgumentOutOfRangeException>(() => history.AddManualInterval(
                "Default end",
                StartUtc,
                default));
            Assert.Throws<ArgumentException>(() => history.AddManualInterval(
                "Equal",
                StartUtc,
                StartUtc));
            Assert.Throws<ArgumentException>(() => history.AddManualInterval(
                "Backward",
                StartUtc,
                StartUtc.AddSeconds(-1)));
            Assert.Throws<ArgumentException>(() => history.AddManualInterval(
                "   ",
                StartUtc,
                StartUtc.AddMinutes(1)));

            ProjectHistoryView view = history.CreateView(StartUtc.AddHours(1));
            Assert.Empty(view.Projects);
            Assert.Empty(view.Intervals);
        }

        [Fact]
        public void UpdateClosedInterval_PreservesIdentityAndUsesCanonicalProject()
        {
            var history = new ProjectTimeHistory();
            ProjectWorkIntervalView original = history.AddManualInterval(
                "Navid",
                StartUtc,
                StartUtc.AddMinutes(30));
            history.RegisterProject("Website");

            ProjectRecordMutationResult result = history.UpdateClosedInterval(
                original.Id,
                "WEBSITE",
                StartUtc.AddMinutes(5),
                StartUtc.AddMinutes(50));

            Assert.Equal(ProjectRecordMutationStatus.Success, result.Status);
            ProjectWorkIntervalView updated = Assert.IsType<ProjectWorkIntervalView>(result.Record);
            Assert.Equal(original.Id, updated.Id);
            Assert.Equal(original.TimerSessionId, updated.TimerSessionId);
            Assert.Equal("WEBSITE", updated.ProjectKey);
            Assert.Equal("Website", updated.ProjectName);
            Assert.Equal(StartUtc.AddMinutes(5), updated.StartUtc);
            Assert.Equal(StartUtc.AddMinutes(50), updated.EndUtc);
            Assert.Equal(updated, Assert.Single(history.CreateView(StartUtc.AddHours(1)).Intervals));
        }

        [Fact]
        public void UpdateClosedInterval_MissingOrOpenRecordIsRejectedAtomically()
        {
            var history = new ProjectTimeHistory();
            Guid runningTimer = Guid.NewGuid();
            history.StartTracking(runningTimer, "Running", StartUtc);
            ProjectWorkIntervalView open = history.GetOpenInterval(runningTimer)!;
            ProjectHistoryView before = history.CreateView(StartUtc.AddMinutes(5));

            ProjectRecordMutationResult missing = history.UpdateClosedInterval(
                Guid.NewGuid(),
                "Missing project",
                StartUtc,
                StartUtc.AddMinutes(1));
            ProjectRecordMutationResult openResult = history.UpdateClosedInterval(
                open.Id,
                "Changed project",
                StartUtc.AddMinutes(1),
                StartUtc.AddMinutes(2));

            Assert.Equal(ProjectRecordMutationStatus.NotFound, missing.Status);
            Assert.Null(missing.Record);
            Assert.Equal(ProjectRecordMutationStatus.OpenInterval, openResult.Status);
            Assert.Null(openResult.Record);
            Assert.Equal(new[] { "Running" }, history.ProjectNames);
            Assert.Equal(before.Intervals, history.CreateView(StartUtc.AddMinutes(5)).Intervals);
        }

        [Fact]
        public void UpdateClosedInterval_RejectsSameTimerOverlapWithoutRegisteringProject()
        {
            var history = new ProjectTimeHistory();
            Guid timerId = Guid.NewGuid();
            history.StartTracking(timerId, "First", StartUtc);
            history.StopTracking(timerId, StartUtc.AddHours(1));
            history.StartTracking(timerId, "Second", StartUtc.AddHours(2));
            history.StopTracking(timerId, StartUtc.AddHours(3));
            ProjectWorkIntervalView first = history.CreateView(StartUtc.AddHours(4)).Intervals[0];

            ProjectRecordMutationResult overlap = history.UpdateClosedInterval(
                first.Id,
                "Should not register",
                StartUtc.AddMinutes(30),
                StartUtc.AddHours(2).AddMinutes(30));

            Assert.Equal(ProjectRecordMutationStatus.Overlap, overlap.Status);
            Assert.Null(overlap.Record);
            Assert.Equal(new[] { "First", "Second" }, history.ProjectNames);
            Assert.Equal(first, history.CreateView(StartUtc.AddHours(4)).Intervals[0]);

            ProjectRecordMutationResult touching = history.UpdateClosedInterval(
                first.Id,
                "First",
                StartUtc.AddMinutes(30),
                StartUtc.AddHours(2));
            Assert.Equal(ProjectRecordMutationStatus.Success, touching.Status);
            Assert.Equal(StartUtc.AddHours(2), touching.Record!.EndUtc);
        }

        [Fact]
        public void UpdateClosedInterval_RejectsOverlapWithCurrentOpenInterval()
        {
            var history = new ProjectTimeHistory();
            Guid timerId = Guid.NewGuid();
            history.StartTracking(timerId, "Closed", StartUtc);
            history.StopTracking(timerId, StartUtc.AddHours(1));
            history.StartTracking(timerId, "Running", StartUtc.AddHours(2));
            ProjectWorkIntervalView closed = history.CreateView(StartUtc.AddHours(3)).Intervals[0];

            ProjectRecordMutationResult result = history.UpdateClosedInterval(
                closed.Id,
                "Closed",
                StartUtc,
                StartUtc.AddHours(2).AddTicks(1));

            Assert.Equal(ProjectRecordMutationStatus.Overlap, result.Status);
            Assert.Equal(StartUtc.AddHours(1),
                history.CreateView(StartUtc.AddHours(3)).Intervals[0].EndUtc);
        }

        [Fact]
        public void DeleteClosedInterval_RemovesOnlyRequestedRecordAndKeepsProject()
        {
            var history = new ProjectTimeHistory();
            ProjectWorkIntervalView first = history.AddManualInterval(
                "Navid",
                StartUtc,
                StartUtc.AddMinutes(30));
            ProjectWorkIntervalView second = history.AddManualInterval(
                "Navid",
                StartUtc.AddHours(1),
                StartUtc.AddHours(2));

            ProjectRecordMutationResult result =
                history.DeleteClosedInterval(first.Id);

            Assert.Equal(ProjectRecordMutationStatus.Success, result.Status);
            Assert.Equal(first, result.Record);
            Assert.Equal(second, Assert.Single(
                history.CreateView(StartUtc.AddHours(3)).Intervals));
            Assert.Equal(new[] { "Navid" }, history.ProjectNames);
        }

        [Fact]
        public void DeleteClosedInterval_MissingOpenAndEmptyIdsHaveNoSideEffects()
        {
            var history = new ProjectTimeHistory();
            Guid runningTimer = Guid.NewGuid();
            history.StartTracking(runningTimer, "Running", StartUtc);
            ProjectWorkIntervalView open = history.GetOpenInterval(runningTimer)!;
            ProjectHistoryView before = history.CreateView(StartUtc.AddMinutes(5));

            Assert.Throws<ArgumentException>(() =>
                history.DeleteClosedInterval(Guid.Empty));
            ProjectRecordMutationResult missing =
                history.DeleteClosedInterval(Guid.NewGuid());
            ProjectRecordMutationResult openResult =
                history.DeleteClosedInterval(open.Id);

            Assert.Equal(ProjectRecordMutationStatus.NotFound, missing.Status);
            Assert.Null(missing.Record);
            Assert.Equal(ProjectRecordMutationStatus.OpenInterval, openResult.Status);
            Assert.Null(openResult.Record);
            ProjectHistoryView after = history.CreateView(StartUtc.AddMinutes(5));
            Assert.Equal(before.Projects, after.Projects);
            Assert.Equal(before.Intervals, after.Intervals);
        }

        [Fact]
        public void DeleteClosedInterval_LeavesNewerOpenIntervalForSameTimerIntact()
        {
            var history = new ProjectTimeHistory();
            Guid timerId = Guid.NewGuid();
            history.StartTracking(timerId, "First", StartUtc);
            history.StopTracking(timerId, StartUtc.AddMinutes(30));
            history.StartTracking(timerId, "Second", StartUtc.AddHours(1));
            ProjectWorkIntervalView[] before = history
                .CreateView(StartUtc.AddHours(2))
                .Intervals
                .ToArray();
            ProjectWorkIntervalView closed = Assert.Single(before, record => !record.IsOpen);
            ProjectWorkIntervalView open = Assert.Single(before, record => record.IsOpen);

            ProjectRecordMutationResult result =
                history.DeleteClosedInterval(closed.Id);

            Assert.Equal(ProjectRecordMutationStatus.Success, result.Status);
            ProjectWorkIntervalView remaining = Assert.Single(
                history.CreateView(StartUtc.AddHours(2)).Intervals);
            Assert.Equal(open, remaining);
            Assert.Equal(timerId, remaining.TimerSessionId);
        }

        [Fact]
        public void DeleteClosedInterval_RoundTripsThroughStore()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "project-history.json");
            var store = new ProjectTimeStore(path);
            var history = new ProjectTimeHistory();
            ProjectWorkIntervalView deleted = history.AddManualInterval(
                "Delete me",
                StartUtc,
                StartUtc.AddMinutes(30));
            ProjectWorkIntervalView retained = history.AddManualInterval(
                "Keep me",
                StartUtc.AddHours(1),
                StartUtc.AddHours(2));
            Assert.True(store.Save(history, StartUtc.AddHours(3)));

            Assert.Equal(
                ProjectRecordMutationStatus.Success,
                history.DeleteClosedInterval(deleted.Id).Status);
            Assert.True(store.Save(history, StartUtc.AddHours(4)));
            Assert.True(store.TryLoad(out ProjectTimeHistory? restored));

            ProjectHistoryView view = restored!.CreateView(StartUtc.AddHours(5));
            Assert.Equal(retained, Assert.Single(view.Intervals));
            Assert.DoesNotContain(view.Intervals, record => record.Id == deleted.Id);
            Assert.Contains("Delete me", restored.ProjectNames);
            Assert.Contains("Keep me", restored.ProjectNames);
        }

        [Fact]
        public void ManualInterval_AddAndUpdateRoundTripThroughStore()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "project-history.json");
            var store = new ProjectTimeStore(path);
            var history = new ProjectTimeHistory();
            ProjectWorkIntervalView original = history.AddManualInterval(
                "Navid",
                StartUtc,
                StartUtc.AddMinutes(30));
            ProjectRecordMutationResult edit = history.UpdateClosedInterval(
                original.Id,
                "Website",
                StartUtc.AddMinutes(5),
                StartUtc.AddMinutes(55));

            Assert.Equal(ProjectRecordMutationStatus.Success, edit.Status);
            Assert.True(store.Save(history, StartUtc.AddHours(1)));
            Assert.True(store.TryLoad(out ProjectTimeHistory? restored));

            ProjectWorkIntervalView record = Assert.Single(
                restored!.CreateView(StartUtc.AddHours(2)).Intervals);
            Assert.Equal(original.Id, record.Id);
            Assert.Equal(original.TimerSessionId, record.TimerSessionId);
            Assert.Equal("Website", record.ProjectName);
            Assert.Equal(StartUtc.AddMinutes(5), record.StartUtc);
            Assert.Equal(StartUtc.AddMinutes(55), record.EndUtc);
        }

        [Fact]
        public void StartTracking_SameProjectIsIdempotentAcrossCasing()
        {
            var history = new ProjectTimeHistory();
            Guid timerId = Guid.NewGuid();

            Assert.Equal(
                ProjectTrackingChange.Started,
                history.StartTracking(timerId, "Navid", StartUtc));
            Assert.Equal(
                ProjectTrackingChange.NoChange,
                history.StartTracking(timerId, "NAVID", StartUtc.AddMinutes(10)));

            ProjectWorkIntervalView interval = Assert.Single(
                history.CreateView(StartUtc.AddHours(1)).Intervals);
            Assert.Equal("Navid", interval.ProjectName);
            Assert.Equal(StartUtc, interval.StartUtc);
            Assert.Null(interval.EndUtc);
        }

        [Fact]
        public void StartTracking_SwitchClosesAndOpensAtExactSameInstant()
        {
            var history = new ProjectTimeHistory();
            Guid timerId = Guid.NewGuid();
            DateTime switchedUtc = StartUtc.AddHours(1);
            history.StartTracking(timerId, "Navid", StartUtc);

            Assert.Equal(
                ProjectTrackingChange.Switched,
                history.StartTracking(timerId, "Website", switchedUtc));

            ProjectWorkIntervalView[] intervals = history.CreateView(switchedUtc).Intervals.ToArray();
            Assert.Equal(2, intervals.Length);
            Assert.Equal(switchedUtc, intervals[0].EndUtc);
            Assert.Equal(switchedUtc, intervals[1].StartUtc);
            Assert.Null(intervals[1].EndUtc);
            Assert.Equal("Website", intervals[1].ProjectName);
        }

        [Fact]
        public void RunningProjectSwitch_SplitsHistoryAndRestartsSameTimerFromZero()
        {
            var history = new ProjectTimeHistory();
            var timer = new TimerSession(7)
            {
                Name = "Navid",
                IsRunning = true
            };
            timer.RestoreElapsed(TimeSpan.FromMinutes(25), start: true);
            history.StartTracking(timer.Id, timer.Name, StartUtc);
            DateTime switchedUtc = StartUtc.AddMinutes(25);

            timer.Name = "Website";
            timer.ResetForProjectSwitch();
            history.StartTracking(timer.Id, timer.Name, switchedUtc);

            ProjectWorkIntervalView[] intervals = history
                .CreateView(switchedUtc.AddMinutes(1))
                .Intervals
                .ToArray();
            Assert.Equal(2, intervals.Length);
            Assert.Equal("Navid", intervals[0].ProjectName);
            Assert.Equal(switchedUtc, intervals[0].EndUtc);
            Assert.Equal("Website", intervals[1].ProjectName);
            Assert.Equal(switchedUtc, intervals[1].StartUtc);
            Assert.Null(intervals[1].EndUtc);
            Assert.Equal(7, timer.Number);
            Assert.True(timer.IsRunning);
            Assert.True(timer.Stopwatch.IsRunning);
            Assert.Equal(TimeSpan.Zero, timer.ElapsedOffset);
        }

        [Fact]
        public void PausedProjectSwitch_ResetsWithoutOpeningReplacementUntilResume()
        {
            var history = new ProjectTimeHistory();
            var timer = new TimerSession(3)
            {
                Name = "Before",
                IsRunning = true
            };
            timer.RestoreElapsed(TimeSpan.FromMinutes(10), start: true);
            history.StartTracking(timer.Id, timer.Name, StartUtc);
            DateTime pausedUtc = StartUtc.AddMinutes(10);
            timer.Stopwatch.Stop();
            timer.IsRunning = false;
            history.StopTracking(timer.Id, pausedUtc);

            timer.Name = "After";
            timer.ResetForProjectSwitch();
            Assert.False(history.StopTracking(timer.Id, pausedUtc));

            Assert.Equal(TimeSpan.Zero, timer.Elapsed);
            Assert.False(timer.IsRunning);
            ProjectWorkIntervalView before = Assert.Single(
                history.CreateView(pausedUtc).Intervals);
            Assert.Equal("Before", before.ProjectName);
            Assert.Equal(pausedUtc, before.EndUtc);

            DateTime resumedUtc = pausedUtc.AddMinutes(5);
            timer.RestoreElapsed(TimeSpan.Zero, start: true);
            timer.IsRunning = true;
            history.StartTracking(timer.Id, timer.Name, resumedUtc);
            ProjectWorkIntervalView after = Assert.Single(
                history.CreateView(resumedUtc).Intervals,
                interval => interval.ProjectName == "After");
            Assert.Equal(resumedUtc, after.StartUtc);
            Assert.True(after.IsOpen);
        }

        [Fact]
        public void RunningProjectClear_ClosesHistoryAndRestartsAsUnnamed()
        {
            var history = new ProjectTimeHistory();
            var timer = new TimerSession(4)
            {
                Name = "Tracked",
                IsRunning = true
            };
            timer.RestoreElapsed(TimeSpan.FromMinutes(8), start: true);
            history.StartTracking(timer.Id, timer.Name, StartUtc);
            DateTime clearedUtc = StartUtc.AddMinutes(8);

            timer.Name = "";
            timer.ResetForProjectSwitch();
            Assert.True(history.StopTracking(timer.Id, clearedUtc));

            ProjectWorkIntervalView record = Assert.Single(
                history.CreateView(clearedUtc.AddMinutes(1)).Intervals);
            Assert.Equal("Tracked", record.ProjectName);
            Assert.Equal(clearedUtc, record.EndUtc);
            Assert.Null(history.GetOpenInterval(timer.Id));
            Assert.True(timer.IsRunning);
            Assert.True(timer.Stopwatch.IsRunning);
            Assert.Equal(TimeSpan.Zero, timer.ElapsedOffset);
        }

        [Fact]
        public void RunningUnnamedAssignment_StartsNamedHistoryFromResetBoundary()
        {
            var history = new ProjectTimeHistory();
            var timer = new TimerSession(5)
            {
                IsRunning = true
            };
            timer.RestoreElapsed(TimeSpan.FromMinutes(6), start: true);
            DateTime assignedUtc = StartUtc.AddMinutes(6);

            timer.Name = "Assigned";
            timer.ResetForProjectSwitch();
            history.StartTracking(timer.Id, timer.Name, assignedUtc);

            ProjectWorkIntervalView record = Assert.Single(
                history.CreateView(assignedUtc.AddMinutes(1)).Intervals);
            Assert.Equal("Assigned", record.ProjectName);
            Assert.Equal(assignedUtc, record.StartUtc);
            Assert.True(record.IsOpen);
            Assert.True(timer.IsRunning);
            Assert.True(timer.Stopwatch.IsRunning);
            Assert.Equal(TimeSpan.Zero, timer.ElapsedOffset);
        }

        [Fact]
        public void StopTracking_ClosesOnlyOpenInterval()
        {
            var history = new ProjectTimeHistory();
            Guid timerId = Guid.NewGuid();
            history.StartTracking(timerId, "Navid", StartUtc);
            DateTime stoppedUtc = StartUtc.AddMinutes(64);

            Assert.True(history.StopTracking(timerId, stoppedUtc));
            Assert.False(history.StopTracking(timerId, stoppedUtc.AddSeconds(1)));

            ProjectWorkIntervalView interval = Assert.Single(
                history.CreateView(stoppedUtc.AddHours(1)).Intervals);
            Assert.Equal(stoppedUtc, interval.EndUtc);
            Assert.Equal(TimeSpan.FromMinutes(64), interval.Duration(stoppedUtc.AddDays(1)));
        }

        [Fact]
        public void DifferentTimersMayTrackOverlappingProjects()
        {
            var history = new ProjectTimeHistory();
            Guid firstTimer = Guid.NewGuid();
            Guid secondTimer = Guid.NewGuid();
            history.StartTracking(firstTimer, "Navid", StartUtc);
            history.StartTracking(secondTimer, "Website", StartUtc.AddMinutes(5));

            ProjectHistoryView view = history.CreateView(StartUtc.AddMinutes(20));

            Assert.Equal(2, view.Intervals.Count);
            Assert.All(view.Intervals, interval => Assert.True(interval.IsOpen));
            Assert.Equal(TimeSpan.FromMinutes(20), view.Intervals[0].Duration(view.AsOfUtc));
            Assert.Equal(TimeSpan.FromMinutes(15), view.Intervals[1].Duration(view.AsOfUtc));
        }

        [Fact]
        public void OpenIntervalDurationIncludesOfflineTimeAtQueryTime()
        {
            var history = new ProjectTimeHistory();
            history.StartTracking(Guid.NewGuid(), "Navid", StartUtc);

            ProjectHistoryView afterRestart = history.CreateView(StartUtc.AddDays(2));

            Assert.Equal(
                TimeSpan.FromDays(2),
                Assert.Single(afterRestart.Intervals).Duration(afterRestart.AsOfUtc));
        }

        [Fact]
        public void ClockRollback_ClampsSwitchAndStopToMonotonicIntervalBoundary()
        {
            var history = new ProjectTimeHistory();
            Guid timerId = Guid.NewGuid();
            history.StartTracking(timerId, "Navid", StartUtc);

            Assert.Equal(
                ProjectTrackingChange.Switched,
                history.StartTracking(timerId, "Other", StartUtc.AddSeconds(-1)));
            Assert.True(history.StopTracking(timerId, StartUtc.AddMinutes(-2)));

            ProjectWorkIntervalView[] intervals = history.CreateView(StartUtc).Intervals.ToArray();
            Assert.Equal(2, intervals.Length);
            Assert.All(intervals, interval =>
            {
                Assert.Equal(StartUtc, interval.StartUtc);
                Assert.Equal(StartUtc, interval.EndUtc);
            });

            // A later start under the same rolled-back clock must also remain
            // after the last persisted boundary, so the file stays valid.
            history.StartTracking(timerId, "Third", StartUtc.AddHours(-1));
            Assert.Equal(
                StartUtc,
                history.GetOpenInterval(timerId)!.StartUtc);
        }

        [Fact]
        public void Reconcile_ClosesStaleSwitchesRenamedAndStartsMissingIntervals()
        {
            var history = new ProjectTimeHistory();
            Guid paused = Guid.NewGuid();
            Guid renamed = Guid.NewGuid();
            Guid missing = Guid.NewGuid();
            history.StartTracking(paused, "Paused", StartUtc);
            history.StartTracking(renamed, "Old name", StartUtc);
            DateTime reconcileUtc = StartUtc.AddHours(1);

            bool changed = history.Reconcile(
                new[]
                {
                    new ProjectTimerState(paused, "Paused", IsRunning: false),
                    new ProjectTimerState(renamed, "New name", IsRunning: true),
                    new ProjectTimerState(missing, "Missing", IsRunning: true)
                },
                reconcileUtc);

            Assert.True(changed);
            ProjectWorkIntervalView[] intervals = history.CreateView(reconcileUtc).Intervals.ToArray();
            Assert.Equal(4, intervals.Length);
            Assert.Equal(reconcileUtc, intervals.Single(item => item.ProjectName == "Paused").EndUtc);
            Assert.Equal(reconcileUtc, intervals.Single(item => item.ProjectName == "Old name").EndUtc);
            Assert.Equal(reconcileUtc, intervals.Single(item => item.ProjectName == "New name").StartUtc);
            Assert.Equal(reconcileUtc, intervals.Single(item => item.ProjectName == "Missing").StartUtc);
            Assert.Equal(2, intervals.Count(item => item.IsOpen));
        }

        [Fact]
        public void Reconcile_KeepingMatchingOpenIntervalIsIdempotent()
        {
            var history = new ProjectTimeHistory();
            Guid timerId = Guid.NewGuid();
            history.StartTracking(timerId, "Navid", StartUtc);

            Assert.False(history.Reconcile(
                new[] { new ProjectTimerState(timerId, "navid", IsRunning: true) },
                StartUtc.AddHours(1)));
            Assert.Single(history.CreateView(StartUtc.AddHours(1)).Intervals);
        }

        [Fact]
        public void Reconcile_ClockRollbackKeepsRenameBoundaryValidAndExact()
        {
            var history = new ProjectTimeHistory();
            Guid timerId = Guid.NewGuid();
            history.StartTracking(timerId, "Before", StartUtc);

            Assert.True(history.Reconcile(
                new[] { new ProjectTimerState(timerId, "After", IsRunning: true) },
                StartUtc.AddMinutes(-30)));

            ProjectWorkIntervalView[] intervals = history.CreateView(StartUtc).Intervals.ToArray();
            Assert.Equal(2, intervals.Length);
            ProjectWorkIntervalView before = intervals.Single(item => item.ProjectName == "Before");
            ProjectWorkIntervalView after = intervals.Single(item => item.ProjectName == "After");
            Assert.Equal(StartUtc, before.EndUtc);
            Assert.Equal(StartUtc, after.StartUtc);
            Assert.True(after.IsOpen);

            using var directory = new TemporaryDirectory();
            var store = new ProjectTimeStore(Path.Combine(directory.Path, "history.json"));
            Assert.True(store.Save(history, StartUtc.AddMinutes(-30)));
            Assert.True(store.TryLoad(out _));
        }

        [Fact]
        public void CreateView_IsDetachedFromLaterHistoryChanges()
        {
            var history = new ProjectTimeHistory();
            Guid timerId = Guid.NewGuid();
            history.StartTracking(timerId, "Navid", StartUtc);
            ProjectHistoryView before = history.CreateView(StartUtc.AddMinutes(1));

            history.StopTracking(timerId, StartUtc.AddMinutes(2));
            history.RegisterProject("Later");

            Assert.Single(before.Projects);
            Assert.Null(Assert.Single(before.Intervals).EndUtc);
        }

        [Fact]
        public void SaveAndLoad_RoundTripsRegistryAndIntervals()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "project-history.json");
            var store = new ProjectTimeStore(path);
            var source = new ProjectTimeHistory();
            Guid first = Guid.NewGuid();
            Guid second = Guid.NewGuid();
            source.RegisterProject("Unused project");
            source.StartTracking(first, "Navid", StartUtc);
            source.StopTracking(first, StartUtc.AddMinutes(64));
            source.StartTracking(second, "Website", StartUtc.AddMinutes(2));

            Assert.True(store.Save(source, StartUtc.AddHours(2)));
            Assert.True(store.TryLoad(out ProjectTimeHistory? restored));

            Assert.Equal(ProjectTimeReadStatus.Success, store.LastReadStatus);
            Assert.Equal(ProjectTimeReadStatus.Success, store.LastPrimaryReadStatus);
            Assert.Equal(ProjectTimeReadStatus.None, store.LastBackupReadStatus);
            Assert.False(store.LoadedFromBackup);
            Assert.False(store.NeedsPrimaryRepair);
            Assert.Equal(StartUtc.AddHours(2), store.LastLoadedSavedAtUtc);

            ProjectHistoryView view = restored!.CreateView(StartUtc.AddHours(2));
            Assert.Equal(new[] { "Unused project", "Navid", "Website" },
                view.Projects.Select(project => project.Name));
            Assert.Equal(2, view.Intervals.Count);
            Assert.Contains(view.Intervals, interval =>
                interval.TimerSessionId == first
                && interval.EndUtc == StartUtc.AddMinutes(64));
            Assert.Contains(view.Intervals, interval =>
                interval.TimerSessionId == second
                && interval.EndUtc == null);
        }

        [Fact]
        public void CorruptPrimaryFallsBackToPreviousValidGeneration()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "project-history.json");
            var store = new ProjectTimeStore(path);
            var history = new ProjectTimeHistory();
            history.RegisterProject("Previous");
            Assert.True(store.Save(history, StartUtc));

            history.RegisterProject("Latest");
            Assert.True(store.Save(history, StartUtc.AddMinutes(1)));
            File.WriteAllText(path, "{ broken json");

            Assert.True(store.TryLoad(out ProjectTimeHistory? restored));
            Assert.Equal(new[] { "Previous" }, restored!.ProjectNames);
            Assert.Equal(ProjectTimeReadStatus.Success, store.LastReadStatus);
            Assert.Equal(ProjectTimeReadStatus.Corrupt, store.LastPrimaryReadStatus);
            Assert.Equal(ProjectTimeReadStatus.Success, store.LastBackupReadStatus);
            Assert.True(store.LoadedFromBackup);
            Assert.True(store.NeedsPrimaryRepair);
            Assert.Equal(StartUtc, store.LastLoadedSavedAtUtc);
        }

        [Fact]
        public void SaveAfterBackupRecoveryRepairsPrimaryAndPreservesGoodBackup()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "project-history.json");
            var store = new ProjectTimeStore(path);
            var history = new ProjectTimeHistory();
            history.RegisterProject("Known good backup");
            Assert.True(store.Save(history, StartUtc));
            history.RegisterProject("Corrupt generation");
            Assert.True(store.Save(history, StartUtc.AddMinutes(1)));
            File.WriteAllText(path, "corrupt");
            Assert.True(store.TryLoad(out ProjectTimeHistory? recovered));

            recovered!.RegisterProject("Repaired primary");
            Assert.True(store.Save(recovered, StartUtc.AddMinutes(2)));
            Assert.False(store.LoadedFromBackup);
            Assert.False(store.NeedsPrimaryRepair);

            var primaryReader = new ProjectTimeStore(path);
            Assert.True(primaryReader.TryLoad(out ProjectTimeHistory? primary));
            Assert.Contains("Repaired primary", primary!.ProjectNames);

            File.WriteAllText(path, "corrupt again");
            Assert.True(primaryReader.TryLoad(out ProjectTimeHistory? backup));
            Assert.Equal(new[] { "Known good backup" }, backup!.ProjectNames);
        }

        [Fact]
        public void FailedSaveAfterBackupRecoveryKeepsRepairState()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "project-history.json");
            var store = new ProjectTimeStore(path);
            var history = new ProjectTimeHistory();
            history.RegisterProject("Known good backup");
            Assert.True(store.Save(history, StartUtc));
            history.RegisterProject("Latest");
            Assert.True(store.Save(history, StartUtc.AddMinutes(1)));
            File.WriteAllText(path, "corrupt");
            Assert.True(store.TryLoad(out ProjectTimeHistory? recovered));

            File.Delete(path);
            Directory.CreateDirectory(path);
            Assert.False(store.Save(recovered!, StartUtc.AddMinutes(2)));

            Assert.True(store.LoadedFromBackup);
            Assert.True(store.NeedsPrimaryRepair);
            Assert.Equal(StartUtc, store.LastLoadedSavedAtUtc);
        }

        [Fact]
        public void UnavailablePrimaryIsNotReportedAsCorruptOrReplacedByBackup()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "project-history.json");
            Directory.CreateDirectory(path);
            var backupStore = new ProjectTimeStore(path + ".bak");
            var backupHistory = new ProjectTimeHistory();
            backupHistory.RegisterProject("Stale backup");
            Assert.True(backupStore.Save(backupHistory, StartUtc));
            var store = new ProjectTimeStore(path);

            Assert.False(store.TryLoad(out ProjectTimeHistory? history));

            Assert.Null(history);
            Assert.Equal(ProjectTimeReadStatus.Unavailable, store.LastReadStatus);
            Assert.Equal(ProjectTimeReadStatus.Unavailable, store.LastPrimaryReadStatus);
            Assert.Equal(ProjectTimeReadStatus.None, store.LastBackupReadStatus);
            Assert.False(store.LoadedFromBackup);
            Assert.False(store.NeedsPrimaryRepair);
            Assert.Null(store.LastLoadedSavedAtUtc);
        }

        [Fact]
        public void UnavailableBackupTakesPrecedenceOverCorruptPrimarySoCallerCanRetry()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "project-history.json");
            File.WriteAllText(path, "corrupt");
            Directory.CreateDirectory(path + ".bak");
            var store = new ProjectTimeStore(path);

            Assert.False(store.TryReadDocument(out ProjectHistoryDocument? document));

            Assert.Null(document);
            Assert.Equal(ProjectTimeReadStatus.Unavailable, store.LastReadStatus);
            Assert.Equal(ProjectTimeReadStatus.Corrupt, store.LastPrimaryReadStatus);
            Assert.Equal(ProjectTimeReadStatus.Unavailable, store.LastBackupReadStatus);
            Assert.False(store.LoadedFromBackup);
            Assert.False(store.NeedsPrimaryRepair);
            Assert.Null(store.LastLoadedSavedAtUtc);
        }

        [Fact]
        public void MissingPrimaryRecoversBackupAndRequestsPrimaryRepair()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "project-history.json");
            var backupStore = new ProjectTimeStore(path + ".bak");
            var backupHistory = new ProjectTimeHistory();
            backupHistory.RegisterProject("Only backup");
            Assert.True(backupStore.Save(backupHistory, StartUtc));
            var store = new ProjectTimeStore(path);

            Assert.True(store.TryLoad(out ProjectTimeHistory? history));

            Assert.Equal(new[] { "Only backup" }, history!.ProjectNames);
            Assert.Equal(ProjectTimeReadStatus.Success, store.LastReadStatus);
            Assert.Equal(ProjectTimeReadStatus.NotFound, store.LastPrimaryReadStatus);
            Assert.Equal(ProjectTimeReadStatus.Success, store.LastBackupReadStatus);
            Assert.True(store.LoadedFromBackup);
            Assert.True(store.NeedsPrimaryRepair);
            Assert.Equal(StartUtc, store.LastLoadedSavedAtUtc);
        }

        [Fact]
        public void MissingFilesHaveNotFoundStatusAndNoRecoveryMetadata()
        {
            using var directory = new TemporaryDirectory();
            var store = new ProjectTimeStore(
                Path.Combine(directory.Path, "project-history.json"));

            Assert.False(store.TryLoad(out ProjectTimeHistory? history));

            Assert.Null(history);
            Assert.Equal(ProjectTimeReadStatus.NotFound, store.LastReadStatus);
            Assert.Equal(ProjectTimeReadStatus.NotFound, store.LastPrimaryReadStatus);
            Assert.Equal(ProjectTimeReadStatus.NotFound, store.LastBackupReadStatus);
            Assert.False(store.LoadedFromBackup);
            Assert.False(store.NeedsPrimaryRepair);
            Assert.Null(store.LastLoadedSavedAtUtc);
        }

        [Fact]
        public void SavingInvalidDocumentDoesNotReplaceValidGeneration()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "project-history.json");
            var store = new ProjectTimeStore(path);
            var history = new ProjectTimeHistory();
            history.RegisterProject("Keep me");
            Assert.True(store.Save(history, StartUtc));
            string original = File.ReadAllText(path);
            var invalid = ValidDocument();
            invalid.Intervals[0].EndUtc = invalid.Intervals[0].StartUtc.AddSeconds(-1);

            Assert.False(store.Save(invalid));
            Assert.Equal(original, File.ReadAllText(path));
        }

        [Theory]
        [MemberData(nameof(InvalidDocuments))]
        public void TryLoad_RejectsSemanticallyInvalidDocuments(ProjectHistoryDocument invalid)
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "project-history.json");
            File.WriteAllText(path, JsonSerializer.Serialize(invalid));
            var store = new ProjectTimeStore(path);

            Assert.False(store.TryLoad(out ProjectTimeHistory? history));
            Assert.Null(history);
            Assert.Equal(ProjectTimeReadStatus.Corrupt, store.LastReadStatus);
        }

        [Fact]
        public void NewerVersionDoesNotFallBackToOlderBackup()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "project-history.json");
            ProjectHistoryDocument newer = ValidDocument();
            newer.Version = ProjectTimeStore.CurrentVersion + 1;
            string newerJson = JsonSerializer.Serialize(newer);
            File.WriteAllText(path, newerJson);

            var backup = new ProjectTimeStore(path + ".bak");
            var oldHistory = new ProjectTimeHistory();
            oldHistory.RegisterProject("Old backup");
            Assert.True(backup.Save(oldHistory, StartUtc));

            var store = new ProjectTimeStore(path);
            Assert.False(store.TryLoad(out _));
            Assert.Equal(ProjectTimeReadStatus.UnsupportedVersion, store.LastReadStatus);
            Assert.Equal(ProjectTimeReadStatus.UnsupportedVersion, store.LastPrimaryReadStatus);
            Assert.Equal(ProjectTimeReadStatus.None, store.LastBackupReadStatus);
            Assert.False(store.LoadedFromBackup);
            Assert.False(store.NeedsPrimaryRepair);
            Assert.Null(store.LastLoadedSavedAtUtc);
            Assert.Equal(newerJson, File.ReadAllText(path));
        }

        [Fact]
        public void SaveNew_CreatesPrimaryButNeverReplacesIt()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "project-history.json");
            var first = new ProjectTimeHistory();
            first.RegisterProject("First");
            var store = new ProjectTimeStore(path);

            Assert.True(store.SaveNew(first, StartUtc));
            Assert.False(store.LastSaveNewConflictDetected);
            string firstGeneration = File.ReadAllText(path);
            Assert.Equal(firstGeneration, File.ReadAllText(path + ".bak"));

            var second = new ProjectTimeHistory();
            second.RegisterProject("Second");
            Assert.False(store.SaveNew(second, StartUtc.AddMinutes(1)));
            Assert.True(store.LastSaveNewConflictDetected);
            Assert.Equal(firstGeneration, File.ReadAllText(path));
        }

        [Fact]
        public void SaveNew_ExistingBackupWinsWithoutCreatingPrimary()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "project-history.json");
            byte[] backup = [2, 4, 6, 8];
            File.WriteAllBytes(path + ".bak", backup);
            var history = new ProjectTimeHistory();
            history.RegisterProject("New");
            var store = new ProjectTimeStore(path);

            Assert.False(store.SaveNew(history, StartUtc));
            Assert.True(store.LastSaveNewConflictDetected);

            Assert.False(File.Exists(path));
            Assert.Equal(backup, File.ReadAllBytes(path + ".bak"));
        }

        public static IEnumerable<object[]> InvalidDocuments()
        {
            ProjectHistoryDocument unknownProject = ValidDocument();
            unknownProject.Intervals[0].ProjectKey = "UNKNOWN";
            yield return new object[] { unknownProject };

            ProjectHistoryDocument duplicateInterval = ValidDocument();
            duplicateInterval.Intervals.Add(Clone(duplicateInterval.Intervals[0]));
            yield return new object[] { duplicateInterval };

            ProjectHistoryDocument twoOpen = ValidDocument();
            var secondOpen = Clone(twoOpen.Intervals[0]);
            secondOpen.Id = Guid.NewGuid();
            secondOpen.StartUtc = secondOpen.StartUtc.AddHours(1);
            twoOpen.Intervals.Add(secondOpen);
            yield return new object[] { twoOpen };

            ProjectHistoryDocument overlapping = ValidDocument();
            overlapping.Intervals[0].EndUtc = StartUtc.AddHours(2);
            var overlap = Clone(overlapping.Intervals[0]);
            overlap.Id = Guid.NewGuid();
            overlap.StartUtc = StartUtc.AddHours(1);
            overlap.EndUtc = StartUtc.AddHours(3);
            overlapping.Intervals.Add(overlap);
            yield return new object[] { overlapping };

            ProjectHistoryDocument backward = ValidDocument();
            backward.Intervals[0].EndUtc = StartUtc.AddSeconds(-1);
            yield return new object[] { backward };

            ProjectHistoryDocument badKey = ValidDocument();
            badKey.Projects[0].Key = "wrong";
            yield return new object[] { badKey };

            ProjectHistoryDocument emptyTimer = ValidDocument();
            emptyTimer.Intervals[0].TimerSessionId = Guid.Empty;
            yield return new object[] { emptyTimer };
        }

        private static ProjectHistoryDocument ValidDocument()
            => new()
            {
                Version = ProjectTimeStore.CurrentVersion,
                SavedAtUtc = StartUtc,
                Projects = new()
                {
                    new ProjectDocumentEntry { Key = "NAVID", Name = "Navid" }
                },
                Intervals = new()
                {
                    new WorkIntervalDocumentEntry
                    {
                        Id = Guid.NewGuid(),
                        TimerSessionId = Guid.NewGuid(),
                        ProjectKey = "NAVID",
                        ProjectName = "Navid",
                        StartUtc = StartUtc
                    }
                }
            };

        private static WorkIntervalDocumentEntry Clone(WorkIntervalDocumentEntry value)
            => new()
            {
                Id = value.Id,
                TimerSessionId = value.TimerSessionId,
                ProjectKey = value.ProjectKey,
                ProjectName = value.ProjectName,
                StartUtc = value.StartUtc,
                EndUtc = value.EndUtc
            };

        private sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "StopwatchOverlayProjectTests-" + Guid.NewGuid().ToString("N"));
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
