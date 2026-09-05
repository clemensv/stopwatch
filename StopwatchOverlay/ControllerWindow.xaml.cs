using System;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StopwatchOverlay
{
    public partial class ControllerWindow : Window
    {
        // Win32 API for global hotkeys
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

        [DllImport("Shcore.dll")]
        private static extern int GetDpiForMonitor(
            IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
        private const int MDT_EFFECTIVE_DPI = 0;

        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _blinkTimer;
        private readonly DispatcherTimer _stateSaveTimer;
        private readonly DispatcherTimer _backgroundApplyTimer;
        private readonly DispatcherTimer _dedicatedSettingsApplyTimer;
        private sealed record OverlayInstance(TimerSession Session, Screen Screen, OverlayWindow Window);
        private sealed record CombinedOverlayInstance(Screen Screen, OverlayWindow Window);
        private sealed record WorkspaceLoadRetryResult(
            TimerWorkspaceStore Store,
            TimerSessionManager Manager,
            DateTime LoadedAtUtc,
            bool Loaded);

        private readonly TimerSessionManager _timerManager = new();
        private TimerWorkspaceStore _workspaceStore = new();
        private readonly ProjectTimeStore _projectTimeStore = new();
        private ProjectTimeHistory _projectHistory = new();
        private IReadOnlyList<TimerSession> _timers => _timerManager.Sessions;
        private readonly List<OverlayInstance> _overlayInstances = new();
        private readonly List<CombinedOverlayInstance> _combinedOverlayInstances = new();
        private readonly Dictionary<string, (double Left, double Top)> _combinedPositionsByScreen = new();
        private readonly TimerSession _emptyTimer = new(1);
        private TimerSession? _activeTimer => _timerManager.Active;
        private bool _combinedOverlayMode;
        private bool _combinedOverlayVisible = true;
        private bool _combinedHasCustomPosition;
        private bool _restoringTimerUi;
        private bool _workspaceWasRestored;
        private bool _freshWorkspaceTimerDefaultsPending;
        private bool _workspaceRecoveredFromBackup;
        private bool _workspacePersistenceDisabled;
        private bool _workspaceRequiresCreateOnlySave;
        private bool _workspaceCreateOnlySaveConflict;
        private bool _workspaceLoadUnavailable;
        private bool _workspaceLoadRetryPending;
        private bool _workspaceLoadRetryInProgress;
        private DateTime _nextWorkspaceLoadRetryUtc;
        private DateTime _workspaceStartupRetryDeadlineUtc;
        private bool _workspaceRecoveryEvidenceObserved;
        private TimerWorkspaceSnapshot? _pendingWorkspaceSnapshot;
        private DateTime _pendingCheckpointUtc;
        private bool _pendingWorkspaceSaved;
        private bool _initializationComplete;
        private bool _checkpointInProgress;
        private bool _stateDirty = true;
        private bool _projectHistoryDirty;
        private bool _projectHistoryPersistenceDisabled;
        private bool _projectHistoryRequiresCreateOnlySave;
        private string? _projectHistoryWarning;
        private bool _projectHistoryLoadRetryPending;
        private DateTime _nextProjectHistoryLoadRetryUtc;
        private DateTime? _projectHistoryNotFoundSinceUtc;
        private ProjectTimerState[] _projectHistoryRetryBaseline = Array.Empty<ProjectTimerState>();
        private DateTime _projectHistoryRetryBaselineUtc;
        private bool _projectHistoryRecoveredFromBackup;
        private bool _skipProjectHistoryReconciliation;
        private readonly List<LightRingWindow> _lightRingWindows = new();
        private ProjectDashboardWindow? _projectDashboardWindow;
        private SettingsWindow? _settingsWindow;
        private bool _projectWindowsRefreshPending;
        private bool _updatingTimerRail;
        private Screen? _selectedScreen;

        // These compatibility properties keep the controller event handlers concise while
        // routing every operation to the selected logical timer. The empty timer makes the
        // XAML initialization path and the zero-timer state safe.
        private TimerSession CurrentTimer => _activeTimer ?? _emptyTimer;
        private bool _isRunning { get => CurrentTimer.IsRunning; set => CurrentTimer.IsRunning = value; }
        private int _currentMode { get => CurrentTimer.Mode; set => CurrentTimer.Mode = value; }
        private int _lastNonClockMode { get => CurrentTimer.LastNonClockMode; set => CurrentTimer.LastNonClockMode = value; }
        private TimeSpan _countdownDuration { get => CurrentTimer.CountdownDuration; set => CurrentTimer.CountdownDuration = value; }
        private TimeSpan _countdownRemaining { get => CurrentTimer.CountdownRemaining; set => CurrentTimer.CountdownRemaining = value; }
        private DateTime _clockTarget { get => CurrentTimer.ClockTarget; set => CurrentTimer.ClockTarget = value; }
        private bool _useClockTarget { get => CurrentTimer.UseClockTarget; set => CurrentTimer.UseClockTarget = value; }
        private bool _colonVisible { get => CurrentTimer.ColonVisible; set => CurrentTimer.ColonVisible = value; }
        private int _timeFormat = 0; // 0=HH:MM:SS.t, 1=HH:MM:SS, 2=MM:SS.t, 3=MM:SS, 4=HH:MM
        private int _frameRate = 30;
        private int _timerRailRefreshTick;

        private ObservableCollection<string> _lapTimes => CurrentTimer.LapTimes;
        private int _lapCount { get => CurrentTimer.LapCount; set => CurrentTimer.LapCount = value; }
        private HwndSource? _hwndSource;
        private AppSettings _settings = new();
        private Dictionary<ShortcutAction, Shortcut> _shortcuts = new();
        private NotifyIcon? _trayIcon;
        private ContextMenuStrip? _trayMenu;
        private bool _isExiting;
        private bool _changingStartWithWindows;
        private bool _appliedStartWithWindows;
        private bool _isNamingTimer;
        private TimerNameWindow? _projectChooserWindow;
        private bool _persistenceFailureNotified;
        private bool _updatingPanelBackgroundSelector;
        private bool _syncingDedicatedSettings;
        private bool _applyingDedicatedSettings;
        private bool _settingsInteractionInProgress;
        private bool _settingsCompletionQueued;
        private SettingsChangeKind _pendingDedicatedSettingsChanges;
        private string? _backgroundWarning;
        private IReadOnlyList<AppBackgroundChoice> _backgroundChoices =
            Array.Empty<AppBackgroundChoice>();

        // Custom overlay position (absolute, device-independent) set by dragging the overlay.
        private bool _hasCustomPosition { get => CurrentTimer.HasCustomPosition; set => CurrentTimer.HasCustomPosition = value; }
        private double _customLeft { get => CurrentTimer.CustomLeft; set => CurrentTimer.CustomLeft = value; }
        private double _customTop { get => CurrentTimer.CustomTop; set => CurrentTimer.CustomTop = value; }
        private bool _suppressReposition = false;

        public ControllerWindow()
        {
            // Capture this before SettingsStore or the first checkpoint can create
            // files of their own. A genuinely pristine first run may proceed after
            // repeated NotFound reads; any timer/history generation already present
            // at process start is recovery evidence and is never overwritten.
            _workspaceRecoveryEvidenceObserved = DetectInitialWorkspaceRecoveryEvidence();

            // Load and apply the persisted app theme before the visual tree is
            // created so startup never flashes the other theme.
            _settings = SettingsStore.Load();
            AppThemeManager.Apply(_settings.ThemeMode);
            _settings.ThemeMode = AppThemeManager.CurrentTheme;
            _appliedStartWithWindows = _settings.StartWithWindows;
            AppBackgroundManager.Apply(_settings, out _backgroundWarning);
            if (_backgroundWarning != null)
                SettingsStore.Save(_settings);

            InitializeComponent();

            _shortcuts = new Dictionary<ShortcutAction, Shortcut>(_settings.Shortcuts);

            DateTime startupUtc = DateTime.UtcNow;
            _workspaceWasRestored = _workspaceStore.TryLoad(
                _timerManager, startupUtc, startupUtc.ToLocalTime());
            _freshWorkspaceTimerDefaultsPending = !_workspaceWasRestored;
            _workspaceRecoveredFromBackup = _workspaceWasRestored
                && _workspaceStore.LastLoadUsedBackup;
            _skipProjectHistoryReconciliation = _workspaceWasRestored
                && _workspaceStore.LastLoadedSkipProjectHistoryReconciliation;
            _combinedOverlayMode = _workspaceWasRestored
                && _workspaceStore.LastLoadedCombinedOverlayMode;
            _combinedOverlayVisible = !_workspaceWasRestored
                || _workspaceStore.LastLoadedCombinedOverlayVisible;
            _combinedHasCustomPosition = _workspaceWasRestored
                && _workspaceStore.LastLoadedCombinedHasCustomPosition;
            if (_workspaceWasRestored)
            {
                foreach (var pair in _workspaceStore.LastLoadedCombinedPositionsByScreen)
                    _combinedPositionsByScreen[pair.Key] = (pair.Value.Left, pair.Value.Top);
            }
            // A startup read can briefly surface as NotFound or Corrupt while a
            // recovery/copy operation swaps whole files, not only as a sharing
            // violation. Protect and retry every non-terminal failure before an
            // empty workspace is allowed to become writable.
            _workspaceLoadRetryPending = !_workspaceWasRestored
                && _workspaceStore.LastReadStatus != TimerWorkspaceReadStatus.UnsupportedVersion;
            _workspaceLoadUnavailable = _workspaceLoadRetryPending;
            _workspacePersistenceDisabled = !_workspaceWasRestored;
            _nextWorkspaceLoadRetryUtc = startupUtc;
            _workspaceStartupRetryDeadlineUtc = startupUtc.AddSeconds(5);
            if (!_workspaceWasRestored
                && _workspaceStore.LastReadStatus is
                    TimerWorkspaceReadStatus.Corrupt
                    or TimerWorkspaceReadStatus.UnsupportedVersion
                    or TimerWorkspaceReadStatus.Unavailable)
            {
                LogWorkspaceStartupReadResult();
            }
            InitializeProjectHistory(startupUtc);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _timer.Tick += Timer_Tick;

            _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _blinkTimer.Tick += BlinkTimer_Tick;

            _stateSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _stateSaveTimer.Tick += (_, _) =>
            {
                RetryUnavailableWorkspace();
                RetryUnavailableProjectHistory();
                if (_stateDirty && !_settingsInteractionInProgress)
                    CheckpointState();
            };

            _backgroundApplyTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(140)
            };
            _backgroundApplyTimer.Tick += (_, _) => ApplyPendingPanelBackgroundStrength();

            _dedicatedSettingsApplyTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(40)
            };
            _dedicatedSettingsApplyTimer.Tick += (_, _) => ApplyPendingDedicatedSettings();

            LapListBox.ItemsSource = CurrentTimer.LapTimes;
            TimerRailList.ItemsSource = _timers;

            InitializeTrayIcon();

            PopulateScreens();
            ApplySettingsToUi();
            if (_workspaceWasRestored)
                RestoreActiveTimerEditorState();
            UpdateButtonStates();
            UpdateShortcutLabels();
            _initializationComplete = true;
            if (_settings.LightRingEnabled)
            {
                Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        if (!_isExiting)
                            ShowLightRing();
                    }),
                    DispatcherPriority.Loaded);
            }
            _timer.Start();
            _blinkTimer.Start();
            _stateSaveTimer.Start();
        }

        private void InitializeProjectHistory(DateTime startupUtc)
        {
            if (_projectTimeStore.TryLoad(out ProjectTimeHistory? loaded) && loaded != null)
            {
                _projectHistory = loaded;
                if (_projectTimeStore.NeedsPrimaryRepair)
                {
                    _projectHistoryRecoveredFromBackup = true;
                    _projectHistoryDirty = true;
                    _stateDirty = true;
                    _projectHistoryWarning =
                        "Project history was recovered from backup and will be repaired";
                }
            }
            else
            {
                _projectHistory = new ProjectTimeHistory();
                if (_projectTimeStore.LastReadStatus == ProjectTimeReadStatus.UnsupportedVersion)
                {
                    _projectHistoryPersistenceDisabled = true;
                    _projectHistoryWarning =
                        "Project history is from a newer app version and was not overwritten";
                }
                else if (_projectTimeStore.LastReadStatus == ProjectTimeReadStatus.Corrupt)
                {
                    _projectHistoryPersistenceDisabled = true;
                    _projectHistoryWarning =
                        "Project history could not be read and was not overwritten";
                }
                else if (_projectTimeStore.LastReadStatus == ProjectTimeReadStatus.Unavailable)
                {
                    _projectHistoryPersistenceDisabled = true;
                    _projectHistoryLoadRetryPending = true;
                    _nextProjectHistoryLoadRetryUtc = startupUtc.AddSeconds(5);
                    _projectHistoryWarning =
                        "Project history is temporarily unavailable and was not overwritten";
                }
                else if (_projectTimeStore.LastReadStatus == ProjectTimeReadStatus.NotFound)
                {
                    // A history restored between this observation and the first
                    // checkpoint must win over a reconstructed empty generation.
                    _projectHistoryRequiresCreateOnlySave = true;
                }
            }

            if (_workspacePersistenceDisabled)
            {
                _projectHistoryPersistenceDisabled = true;
                _projectHistoryLoadRetryPending = false;
                _projectHistoryRetryBaseline = Array.Empty<ProjectTimerState>();
                return;
            }

            if (_projectHistoryLoadRetryPending)
                CaptureProjectHistoryRetryBaseline(startupUtc);

            if (_projectHistoryPersistenceDisabled)
                return;

            DateTime reconciliationUtc = _workspaceWasRestored
                ? _workspaceStore.LastLoadedSavedAtUtc ?? startupUtc
                : startupUtc;

            if (_workspaceWasRestored
                && _projectTimeStore.LastLoadedSavedAtUtc is DateTime historySavedAtUtc
                && historySavedAtUtc > reconciliationUtc)
            {
                _skipProjectHistoryReconciliation = true;
                _projectHistoryWarning =
                    "A newer project history was preserved; automatic reconciliation is paused until the timers agree";
            }

            if (_skipProjectHistoryReconciliation
                && !ProjectTrackingMatchesWorkspace())
            {
                return;
            }

            _skipProjectHistoryReconciliation = false;
            ReconcileProjectHistory(reconciliationUtc);
        }

        private void CaptureProjectHistoryRetryBaseline(DateTime startupUtc)
        {
            _projectHistoryRetryBaselineUtc = _workspaceWasRestored
                ? _workspaceStore.LastLoadedSavedAtUtc ?? startupUtc
                : startupUtc;
            _projectHistoryRetryBaselineUtc =
                ProjectTimeHistory.NormalizeUtc(_projectHistoryRetryBaselineUtc);
            _projectHistoryRetryBaseline = _timers
                .Select(timer => new ProjectTimerState(
                    timer.Id,
                    timer.Name,
                    timer.IsRunning))
                .ToArray();
        }

        private void ReconcileProjectHistory(DateTime utcNow)
        {
            try
            {
                int projectCountBefore = _projectHistory.ProjectNames.Count;
                foreach (var timer in _timers.Where(timer =>
                    !string.IsNullOrWhiteSpace(timer.Name)))
                {
                    string canonicalName = _projectHistory.RegisterProject(timer.Name);
                    if (!string.Equals(timer.Name, canonicalName, StringComparison.Ordinal))
                    {
                        timer.Name = canonicalName;
                        _stateDirty = true;
                    }
                }

                bool changed = _projectHistory.ProjectNames.Count != projectCountBefore;
                changed |= _projectHistory.Reconcile(
                    _timers.Select(timer => new ProjectTimerState(
                        timer.Id,
                        timer.Name,
                        timer.IsRunning)),
                    utcNow);

                if (changed)
                {
                    _projectHistoryDirty = true;
                    _stateDirty = true;
                }
            }
            catch (ArgumentException exception)
            {
                CrashLogger.LogRecoverable(exception, "ProjectHistoryReconciliation");
                _projectHistoryWarning = "Project tracking could not be reconciled";
            }
        }

        private bool ProjectTrackingMatchesWorkspace()
        {
            ProjectWorkIntervalView[] openIntervals = _projectHistory
                .CreateView(DateTime.UtcNow)
                .Intervals
                .Where(interval => interval.IsOpen)
                .ToArray();
            var statesById = _timers.ToDictionary(timer => timer.Id);

            foreach (ProjectWorkIntervalView interval in openIntervals)
            {
                if (!statesById.TryGetValue(interval.TimerSessionId, out TimerSession? timer)
                    || !timer.IsRunning
                    || string.IsNullOrWhiteSpace(timer.Name)
                    || !string.Equals(
                        timer.Name.Trim(),
                        interval.ProjectName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            foreach (TimerSession timer in _timers.Where(timer =>
                timer.IsRunning && !string.IsNullOrWhiteSpace(timer.Name)))
            {
                if (!openIntervals.Any(interval =>
                    interval.TimerSessionId == timer.Id
                    && string.Equals(
                        interval.ProjectName,
                        timer.Name.Trim(),
                        StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
            }

            return true;
        }

        private void TryClearProjectReconciliationGuard()
        {
            if (!_skipProjectHistoryReconciliation
                || !ProjectTrackingMatchesWorkspace())
            {
                return;
            }

            _skipProjectHistoryReconciliation = false;
            _projectHistoryWarning = null;
            _stateDirty = true;
            UpdateStatus("Timer state and project history are synchronized", Brushes.DeepSkyBlue);
        }

        private bool ProjectTransitionIsTemporarilyBlocked(
            TimerSession? timer,
            bool alwaysBlock = false)
        {
            if (_workspaceLoadRetryPending || _workspacePersistenceDisabled)
            {
                UpdateStatus(
                    _workspaceLoadRetryPending
                        ? "Timer recovery is still checking the existing workspace"
                        : "Timer recovery is read-only; restart after resolving access to the data file",
                    Brushes.OrangeRed);
                return true;
            }

            bool historyLoadBlocksWorkspace = _projectHistoryLoadRetryPending;
            bool exactCheckpointPending = _pendingWorkspaceSnapshot != null;
            bool timerTracksProject = timer != null
                && !string.IsNullOrWhiteSpace(timer.Name);
            if (!historyLoadBlocksWorkspace
                && (!exactCheckpointPending || (!alwaysBlock && !timerTracksProject)))
            {
                return false;
            }

            UpdateStatus(
                "Project recovery is finishing; try this command again in a moment",
                Brushes.OrangeRed);
            return true;
        }

        private async void RetryUnavailableWorkspace()
        {
            if (!_workspaceLoadRetryPending
                || _workspaceLoadRetryInProgress
                || _isExiting
                || DateTime.UtcNow < _nextWorkspaceLoadRetryUtc)
            {
                return;
            }

            _workspaceLoadRetryInProgress = true;
            _nextWorkspaceLoadRetryUtc = DateTime.UtcNow.AddSeconds(2);
            try
            {
                string workspacePath = _workspaceStore.FilePath;
                WorkspaceLoadRetryResult result = await Task.Run(() =>
                {
                    DateTime loadedAtUtc = DateTime.UtcNow;
                    var retryStore = new TimerWorkspaceStore(workspacePath);
                    var retryManager = new TimerSessionManager();
                    bool loaded = retryStore.TryLoad(
                        retryManager,
                        loadedAtUtc,
                        loadedAtUtc.ToLocalTime());
                    return new WorkspaceLoadRetryResult(
                        retryStore,
                        retryManager,
                        loadedAtUtc,
                        loaded);
                });

                if (_isExiting || !_workspaceLoadRetryPending)
                    return;

                if (!result.Loaded)
                {
                    TimerWorkspaceReadStatus retryStatus = result.Store.LastReadStatus;
                    if (retryStatus != TimerWorkspaceReadStatus.NotFound)
                        _workspaceRecoveryEvidenceObserved = true;
                    if (retryStatus == TimerWorkspaceReadStatus.UnsupportedVersion)
                    {
                        _workspaceLoadRetryPending = false;
                        _workspaceLoadUnavailable = false;
                        UpdateStatus(
                            "Timer recovery file is from a newer app version and was not overwritten",
                            Brushes.OrangeRed);
                    }
                    else if (retryStatus == TimerWorkspaceReadStatus.Corrupt
                        && DateTime.UtcNow >= _workspaceStartupRetryDeadlineUtc)
                    {
                        _workspaceLoadRetryPending = false;
                        _workspaceLoadUnavailable = false;
                        UpdateStatus(
                            "Timer recovery file could not be read and was not overwritten",
                            Brushes.OrangeRed);
                    }
                    else if (retryStatus == TimerWorkspaceReadStatus.NotFound
                        && DateTime.UtcNow >= _workspaceStartupRetryDeadlineUtc
                        && !WorkspaceRecoveryEvidenceExists())
                    {
                        CompleteConfirmedNewWorkspace(result.LoadedAtUtc);
                    }
                    else
                    {
                        UpdateStatus(
                            "Timer recovery is checking the existing data files; nothing has been overwritten",
                            Brushes.OrangeRed);
                    }
                    return;
                }

                // The unavailable startup path deliberately blocks timer creation,
                // so applying the recovered manager cannot discard user work.
                if (_timerManager.Count != 0
                    || _pendingWorkspaceSnapshot != null
                    || _projectChooserWindow != null
                    || _overlayInstances.Count != 0
                    || _combinedOverlayInstances.Count != 0)
                {
                    _workspaceLoadRetryPending = false;
                    CrashLogger.LogRecoverable(
                        new InvalidOperationException(
                            "Automatic timer recovery was stopped because the in-memory workspace was no longer empty."),
                        "TimerWorkspaceRetryConflict");
                    UpdateStatus(
                        "Timer recovery needs a restart because this workspace changed while recovery was pending",
                        Brushes.OrangeRed);
                    return;
                }

                CompleteUnavailableWorkspaceRetry(result);
            }
            finally
            {
                _workspaceLoadRetryInProgress = false;
            }
        }

        private void CompleteConfirmedNewWorkspace(DateTime confirmedAtUtc)
        {
            // Only a stable NotFound result with no prior app-data evidence is
            // treated as a genuine first run. An existing workspace/history file,
            // backup, temporary generation, or directory at an expected file path
            // keeps this process in protected recovery instead.
            _workspaceLoadRetryPending = false;
            _workspaceLoadUnavailable = false;
            _workspacePersistenceDisabled = false;
            _workspaceRequiresCreateOnlySave = true;
            _freshWorkspaceTimerDefaultsPending = true;

            _projectHistory = new ProjectTimeHistory();
            _projectHistoryDirty = false;
            _projectHistoryPersistenceDisabled = false;
            _projectHistoryRequiresCreateOnlySave = true;
            _projectHistoryWarning = null;
            _projectHistoryLoadRetryPending = false;
            _nextProjectHistoryLoadRetryUtc = default;
            _projectHistoryNotFoundSinceUtc = null;
            _projectHistoryRetryBaseline = Array.Empty<ProjectTimerState>();
            _projectHistoryRetryBaselineUtc = default;
            _projectHistoryRecoveredFromBackup = false;
            InitializeProjectHistory(confirmedAtUtc);

            UpdateButtonStates();
            UpdateStatus("No existing timer workspace was found", Brushes.DeepSkyBlue);
            CreateNewTimer();
        }

        private void ResumeProtectedWorkspaceRecoveryAfterFirstRunRace()
        {
            _workspaceLoadRetryPending = true;
            _workspaceLoadUnavailable = true;
            _workspacePersistenceDisabled = true;
            _projectHistoryPersistenceDisabled = true;
            _projectHistoryLoadRetryPending = false;
            _nextWorkspaceLoadRetryUtc = DateTime.UtcNow;
            UpdateButtonStates();
            UpdateStatus(
                "Existing recovery data appeared; the new timer was not created",
                Brushes.OrangeRed);
            RetryUnavailableWorkspace();
        }

        private bool WorkspaceRecoveryEvidenceExists()
        {
            if (_workspaceRecoveryEvidenceObserved)
                return true;

            string? dataDirectory = System.IO.Path.GetDirectoryName(_workspaceStore.FilePath);
            string[] expectedPaths =
            [
                _workspaceStore.FilePath,
                _workspaceStore.BackupPath,
                _projectTimeStore.FilePath,
                _projectTimeStore.BackupPath
            ];
            if (expectedPaths.Any(path =>
                System.IO.File.Exists(path) || System.IO.Directory.Exists(path)))
            {
                _workspaceRecoveryEvidenceObserved = true;
                return true;
            }

            if (string.IsNullOrWhiteSpace(dataDirectory)
                || !System.IO.Directory.Exists(dataDirectory))
            {
                return false;
            }

            try
            {
                bool found = System.IO.Directory
                    .EnumerateFileSystemEntries(dataDirectory, "workspace.json*")
                    .Concat(System.IO.Directory.EnumerateFileSystemEntries(
                        dataDirectory,
                        "project-history.json*"))
                    .Any();
                _workspaceRecoveryEvidenceObserved |= found;
                return found;
            }
            catch (Exception exception) when (exception is
                System.IO.IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException)
            {
                // An unreadable data directory is itself recovery evidence.
                _workspaceRecoveryEvidenceObserved = true;
                return true;
            }
        }

        private bool DetectInitialWorkspaceRecoveryEvidence()
        {
            string? dataDirectory = System.IO.Path.GetDirectoryName(_workspaceStore.FilePath);
            if (string.IsNullOrWhiteSpace(dataDirectory)
                || !System.IO.Directory.Exists(dataDirectory))
            {
                return false;
            }

            try
            {
                return System.IO.Directory
                    .EnumerateFileSystemEntries(dataDirectory, "workspace.json*")
                    .Concat(System.IO.Directory.EnumerateFileSystemEntries(
                        dataDirectory,
                        "project-history.json*"))
                    .Any();
            }
            catch (Exception exception) when (exception is
                System.IO.IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException)
            {
                return true;
            }
        }

        private void LogWorkspaceStartupReadResult()
        {
            TimerWorkspaceReadStatus status = _workspaceStore.LastReadStatus;
            string details = string.Join(
                Environment.NewLine,
                $"Loaded={_workspaceWasRestored}",
                $"InitialStatus={status}",
                $"PrimaryStatus={_workspaceStore.LastPrimaryReadStatus}",
                $"BackupStatus={_workspaceStore.LastBackupReadStatus}",
                $"ManagerCount={_timerManager.Count}",
                $"ActiveTimerPresent={_timerManager.Active != null}",
                $"PrimaryExists={System.IO.File.Exists(_workspaceStore.FilePath)}",
                $"BackupExists={System.IO.File.Exists(_workspaceStore.BackupPath)}",
                $"DataDirectoryExists={System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(_workspaceStore.FilePath))}",
                $"HistoryPrimaryExists={System.IO.File.Exists(_projectTimeStore.FilePath)}",
                $"HistoryBackupExists={System.IO.File.Exists(_projectTimeStore.BackupPath)}",
                $"PrimaryMetadata={DescribeStartupFile(_workspaceStore.FilePath)}",
                $"BackupMetadata={DescribeStartupFile(_workspaceStore.BackupPath)}",
                $"Theme={_settings.ThemeMode}",
                $"Is64BitProcess={Environment.Is64BitProcess}",
                $"ApplicationData={Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}",
                $"WorkspacePath={_workspaceStore.FilePath}",
                $"CurrentDirectory={Environment.CurrentDirectory}",
                $"BaseDirectory={AppContext.BaseDirectory}");
            CrashLogger.LogRecoverable(
                new InvalidOperationException(details),
                $"TimerWorkspaceStartup{status}Count{_timerManager.Count}");
        }

        private static string DescribeStartupFile(string path)
        {
            try
            {
                var info = new System.IO.FileInfo(path);
                if (!info.Exists)
                    return "Missing";
                return $"Length={info.Length};LastWriteUtc={info.LastWriteTimeUtc:O}";
            }
            catch (Exception exception) when (exception is
                System.IO.IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException
                or NotSupportedException
                or ArgumentException)
            {
                return $"ReadError={exception.GetType().Name};HResult=0x{exception.HResult:X8}";
            }
        }

        private void CompleteUnavailableWorkspaceRetry(WorkspaceLoadRetryResult result)
        {
            _workspaceStore = result.Store;
            _timerManager.Restore(
                result.Manager.Sessions,
                result.Manager.Active?.Id,
                result.Manager.NextNumber);

            _workspaceWasRestored = true;
            _freshWorkspaceTimerDefaultsPending = false;
            _workspaceRecoveredFromBackup = result.Store.LastLoadUsedBackup;
            _workspaceLoadUnavailable = false;
            _workspaceLoadRetryPending = false;
            _workspacePersistenceDisabled = false;
            _workspaceRequiresCreateOnlySave = false;
            _skipProjectHistoryReconciliation =
                result.Store.LastLoadedSkipProjectHistoryReconciliation;
            _combinedOverlayMode = result.Store.LastLoadedCombinedOverlayMode;
            _combinedOverlayVisible = result.Store.LastLoadedCombinedOverlayVisible;
            _combinedHasCustomPosition = result.Store.LastLoadedCombinedHasCustomPosition;
            _combinedPositionsByScreen.Clear();
            foreach (var pair in result.Store.LastLoadedCombinedPositionsByScreen)
                _combinedPositionsByScreen[pair.Key] = (pair.Value.Left, pair.Value.Top);

            // History may also have been inaccessible during the same short-lived
            // file conflict. Reload it against the recovered workspace rather than
            // reconciling an empty placeholder into the user's records.
            _projectHistory = new ProjectTimeHistory();
            _projectHistoryDirty = false;
            _projectHistoryPersistenceDisabled = false;
            _projectHistoryRequiresCreateOnlySave = false;
            _projectHistoryWarning = null;
            _projectHistoryLoadRetryPending = false;
            _nextProjectHistoryLoadRetryUtc = default;
            _projectHistoryRetryBaseline = Array.Empty<ProjectTimerState>();
            _projectHistoryRetryBaselineUtc = default;
            _projectHistoryRecoveredFromBackup = false;
            InitializeProjectHistory(result.LoadedAtUtc);

            RestoreActiveTimerEditorState();
            UpdateButtonStates();
            UpdateShortcutLabels();
            RestorePersistedOverlays();
            QueueProjectWindowsRefresh();
            _stateDirty = true;
            if (_projectHistoryWarning != null)
                UpdateStatus(_projectHistoryWarning, Brushes.OrangeRed);
            else
                UpdateStatus("Timer workspace recovered", Brushes.DeepSkyBlue);
            CheckpointState();
        }

        private void RetryUnavailableProjectHistory()
        {
            if (_workspacePersistenceDisabled)
            {
                _projectHistoryLoadRetryPending = false;
                return;
            }

            if (!_projectHistoryLoadRetryPending
                || DateTime.UtcNow < _nextProjectHistoryLoadRetryUtc)
            {
                return;
            }

            _nextProjectHistoryLoadRetryUtc = DateTime.UtcNow.AddSeconds(5);
            if (!_projectTimeStore.TryLoad(out ProjectTimeHistory? loaded) || loaded == null)
            {
                switch (_projectTimeStore.LastReadStatus)
                {
                    case ProjectTimeReadStatus.Unavailable:
                        _projectHistoryNotFoundSinceUtc = null;
                        break;
                    case ProjectTimeReadStatus.NotFound:
                        DateTime observedAtUtc = DateTime.UtcNow;
                        _projectHistoryNotFoundSinceUtc ??= observedAtUtc;
                        if (observedAtUtc - _projectHistoryNotFoundSinceUtc.Value
                            >= TimeSpan.FromSeconds(5))
                        {
                            CompleteProjectHistoryRetry(
                                new ProjectTimeHistory(),
                                needsPrimaryRepair: false,
                                createOnly: true,
                                "A new project history was created");
                        }
                        else
                        {
                            _nextProjectHistoryLoadRetryUtc = observedAtUtc.AddSeconds(1);
                        }
                        break;
                    case ProjectTimeReadStatus.UnsupportedVersion:
                        _projectHistoryNotFoundSinceUtc = null;
                        _projectHistoryLoadRetryPending = false;
                        _projectHistoryWarning =
                            "Project history is from a newer app version and was not overwritten";
                        UpdateStatus(_projectHistoryWarning, Brushes.OrangeRed);
                        break;
                    default:
                        _projectHistoryNotFoundSinceUtc = null;
                        _projectHistoryLoadRetryPending = false;
                        _projectHistoryWarning =
                            "Project history could not be read and was not overwritten";
                        UpdateStatus(_projectHistoryWarning, Brushes.OrangeRed);
                        break;
                }
                return;
            }

            CompleteProjectHistoryRetry(
                loaded,
                _projectTimeStore.NeedsPrimaryRepair,
                createOnly: false,
                "Project history is available again");
        }

        private void CompleteProjectHistoryRetry(
            ProjectTimeHistory loaded,
            bool needsPrimaryRepair,
            bool createOnly,
            string statusMessage)
        {
            _projectHistory = loaded;
            _projectHistoryPersistenceDisabled = false;
            _projectHistoryLoadRetryPending = false;
            _projectHistoryNotFoundSinceUtc = null;
            _projectHistoryRequiresCreateOnlySave = createOnly;
            _projectHistoryWarning = needsPrimaryRepair
                ? "Project history was recovered from backup and will be repaired"
                : null;

            bool historyIsNewerThanBaseline = _workspaceWasRestored
                && _projectTimeStore.LastLoadedSavedAtUtc is DateTime historySavedAtUtc
                && historySavedAtUtc > _projectHistoryRetryBaselineUtc;
            if (historyIsNewerThanBaseline)
            {
                _skipProjectHistoryReconciliation = true;
                _projectHistoryWarning =
                    "A newer project history was preserved; automatic reconciliation is paused until the timers agree";
            }

            bool changed = ApplyProjectHistoryRetryBaseline(
                applyBaseline: !_skipProjectHistoryReconciliation);
            if (needsPrimaryRepair)
            {
                _projectHistoryRecoveredFromBackup = true;
                changed = true;
            }
            if (changed)
                MarkProjectHistoryDirty();

            TryClearProjectReconciliationGuard();
            if (!_skipProjectHistoryReconciliation)
                ReconcileProjectHistory(DateTime.UtcNow);
            QueueProjectWindowsRefresh();
            UpdateStatus(statusMessage, Brushes.DeepSkyBlue);
            CheckpointState();
        }

        private bool ApplyProjectHistoryRetryBaseline(bool applyBaseline)
        {
            bool changed = false;
            if (applyBaseline)
            {
                int projectCountBefore = _projectHistory.ProjectNames.Count;
                foreach (ProjectTimerState state in _projectHistoryRetryBaseline)
                {
                    if (ProjectTimeHistory.TryNormalizeProjectName(
                        state.ProjectName,
                        out string? baselineName))
                    {
                        _projectHistory.RegisterProject(baselineName!);
                    }
                }
                changed |= _projectHistory.ProjectNames.Count != projectCountBefore;
                changed |= _projectHistory.Reconcile(
                    _projectHistoryRetryBaseline,
                    _projectHistoryRetryBaselineUtc);
            }

            _projectHistoryRetryBaseline = Array.Empty<ProjectTimerState>();
            return changed;
        }

        private string RegisterProjectName(string projectName)
        {
            string normalized = (projectName ?? "").Trim();
            if (normalized.Length == 0 || _projectHistoryPersistenceDisabled)
                return normalized;

            int countBefore = _projectHistory.ProjectNames.Count;
            string registered = _projectHistory.RegisterProject(normalized);
            if (_projectHistory.ProjectNames.Count != countBefore)
                MarkProjectHistoryDirty();
            return registered;
        }

        private void SynchronizeProjectTracking(TimerSession timer, DateTime utcNow)
        {
            if (_projectHistoryPersistenceDisabled)
                return;

            try
            {
                bool changed;
                if (timer.IsRunning && !string.IsNullOrWhiteSpace(timer.Name))
                {
                    timer.Name = RegisterProjectName(timer.Name);
                    changed = _projectHistory.StartTracking(timer.Id, timer.Name, utcNow)
                        != ProjectTrackingChange.NoChange;
                }
                else
                {
                    changed = _projectHistory.StopTracking(timer.Id, utcNow);
                }

                if (changed)
                    MarkProjectHistoryDirty();

                TryClearProjectReconciliationGuard();
            }
            catch (ArgumentException exception)
            {
                CrashLogger.LogRecoverable(exception, "ProjectTrackingSynchronization");
                _projectHistoryWarning = "Project time could not be updated";
                UpdateStatus(_projectHistoryWarning, Brushes.OrangeRed);
            }
        }

        private void MarkProjectHistoryDirty()
        {
            _projectHistoryDirty = true;
            _stateDirty = true;
            QueueProjectWindowsRefresh();
        }

        private void QueueProjectWindowsRefresh()
        {
            bool dashboardVisible = _projectDashboardWindow?.IsVisible == true;
            if (_projectWindowsRefreshPending || !dashboardVisible)
                return;

            _projectWindowsRefreshPending = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _projectWindowsRefreshPending = false;
                if (_projectDashboardWindow?.IsVisible == true)
                    _projectDashboardWindow.RefreshFromHistory();
            }), DispatcherPriority.Background);
        }

        private TimerSession CreateTimerModel()
        {
            bool applyFreshWorkspaceDefaults = _freshWorkspaceTimerDefaultsPending;
            var timer = _timerManager.Create();
            timer.Mode = applyFreshWorkspaceDefaults ? _emptyTimer.Mode : 0;
            timer.LastNonClockMode = applyFreshWorkspaceDefaults
                ? _emptyTimer.LastNonClockMode
                : 0;
            timer.CountdownRemaining = TimeSpan.FromMinutes(5);
            timer.CascadeIndex = timer.Number - 1;
            string selectedPosition = PositionSelector == null
                ? "Top Center" : SelectedContent(PositionSelector, "Top Center");
            timer.LastPresetPosition = selectedPosition == "Custom"
                ? "Top Center" : selectedPosition;
            if (applyFreshWorkspaceDefaults)
            {
                timer.HasCustomPosition = _emptyTimer.HasCustomPosition;
                timer.CustomLeft = _emptyTimer.CustomLeft;
                timer.CustomTop = _emptyTimer.CustomTop;
                _freshWorkspaceTimerDefaultsPending = false;
            }
            return timer;
        }

        private IEnumerable<OverlayInstance> ActiveOverlayInstances()
            => _activeTimer == null
                ? Enumerable.Empty<OverlayInstance>()
                : _overlayInstances.Where(instance => ReferenceEquals(instance.Session, _activeTimer));

        private IEnumerable<OverlayWindow> ActiveOverlayWindows()
            => _combinedOverlayMode
                ? _combinedOverlayInstances.Select(instance => instance.Window)
                : ActiveOverlayInstances().Select(instance => instance.Window);

        private bool ActiveOverlayIsVisible()
            => _activeTimer != null && ActiveOverlayWindows().Any();

        private int ActiveOverlayWindowCount()
            => _activeTimer == null ? 0 : ActiveOverlayWindows().Count();

        private void SaveActiveTimerEditorState()
        {
            if (_activeTimer == null || CountdownMinutes == null) return;

            _activeTimer.CountdownMinutesText = CountdownMinutes.Text;
            _activeTimer.CountdownSecondsText = CountdownSeconds.Text;
            _activeTimer.ClockTargetHoursText = ClockTargetHours.Text;
            _activeTimer.ClockTargetMinutesText = ClockTargetMinutes.Text;
            _activeTimer.ClockTargetSecondsText = ClockTargetSeconds.Text;
            _activeTimer.SmartInputText = SmartInputBox.Text;
            ApplyResponsiveLayout(ActualWidth);
        }

        private void RestoreActiveTimerEditorState()
        {
            _restoringTimerUi = true;
            try
            {
                if (_activeTimer == null)
                {
                    string createShortcut = ShortcutText(ShortcutAction.NewTimer);
                    TimeDisplay.Text = "--:--";
                    LapListBox.ItemsSource = null;
                    LapPlaceholder.Text = createShortcut.Length > 0
                        ? $"No timers — press {createShortcut} to create one"
                        : "No timers — use Timers > New timer";
                    LapPlaceholder.Visibility = Visibility.Visible;
                    CountdownPanel.Visibility = Visibility.Collapsed;
                    return;
                }

                CountdownMinutes.Text = _activeTimer.CountdownMinutesText;
                CountdownSeconds.Text = _activeTimer.CountdownSecondsText;
                ClockTargetHours.Text = _activeTimer.ClockTargetHoursText;
                ClockTargetMinutes.Text = _activeTimer.ClockTargetMinutesText;
                ClockTargetSeconds.Text = _activeTimer.ClockTargetSecondsText;
                SmartInputBox.Text = _activeTimer.SmartInputText;

                if (_activeTimer.UseClockTarget)
                    CountdownUntilRadio.IsChecked = true;
                else
                    CountdownDurationRadio.IsChecked = true;

                SelectMode(_activeTimer.Mode);
                LapListBox.ItemsSource = _activeTimer.LapTimes;
                LapPlaceholder.Visibility = _activeTimer.LapTimes.Count == 0
                    ? Visibility.Visible : Visibility.Collapsed;
            }
            finally
            {
                _restoringTimerUi = false;
            }

            UpdateSmartPreview();
            UpdateTimeDisplay();
        }

        private void ActivateTimer(
            TimerSession timer,
            bool announce = true,
            bool checkpoint = true)
        {
            if (!_timers.Contains(timer)) return;
            if (!ReferenceEquals(_activeTimer, timer))
                SaveActiveTimerEditorState();

            if (!_timerManager.Activate(timer)) return;
            RestoreActiveTimerEditorState();
            RefreshOverlayActiveStates();
            UpdateButtonStates();
            UpdateShortcutLabels();

            if (announce)
            {
                string label = string.IsNullOrWhiteSpace(timer.Name)
                    ? $"Timer {timer.Number}" : timer.Name;
                UpdateStatus($"{label} active", Brushes.DeepSkyBlue);
            }

            if (checkpoint)
                CheckpointState();
        }

        private void TimerRailList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingTimerRail || TimerRailList.SelectedItem is not TimerSession timer)
                return;
            ActivateTimer(timer);
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
            => ApplyResponsiveLayout(e.NewSize.Width);

        private void ApplyResponsiveLayout(double width)
        {
            if (TimerRailColumn == null || TimerRail == null)
                return;
            bool compact = ControllerLayoutPolicy.UseCompactLayout(width);
            TimerRailColumn.Width = compact ? new GridLength(0) : new GridLength(260);
            TimerRail.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        }

        private void RefreshOverlayActiveStates()
        {
            foreach (var instance in _overlayInstances)
            {
                instance.Window.SetActive(ReferenceEquals(instance.Session, _activeTimer));
                instance.Window.SetRunning(instance.Session.IsRunning);
                instance.Window.SetPauseResumeEnabled(instance.Session.Mode != 1);
            }

            RefreshCombinedOverlayState();
        }

        private async void CreateNewTimer()
        {
            if (_workspaceLoadRetryPending || _workspacePersistenceDisabled)
            {
                UpdateStatus(
                    _workspaceLoadRetryPending
                        ? "Timer recovery is still checking the existing workspace"
                        : "Timer recovery is read-only; restart after resolving access to the data file",
                    Brushes.OrangeRed);
                return;
            }

            if (_isNamingTimer
                || ProjectTransitionIsTemporarilyBlocked(null, alwaysBlock: true))
            {
                return;
            }

            string? projectName = await ChooseProjectAsync(
                currentName: "",
                isCreatingTimer: true);
            if (projectName == null || _isExiting)
                return;

            // The chooser leaves overlays interactive. Recheck after it closes in case
            // another shortcut established an exact recovery checkpoint meanwhile.
            if (ProjectTransitionIsTemporarilyBlocked(null, alwaysBlock: true))
                return;

            if (_workspaceRequiresCreateOnlySave && WorkspaceRecoveryEvidenceExists())
            {
                ResumeProtectedWorkspaceRecoveryAfterFirstRunRace();
                return;
            }

            SaveActiveTimerEditorState();
            var timer = CreateTimerModel();
            timer.Name = RegisterProjectName(projectName);
            RestoreActiveTimerEditorState();

            timer.OverlayVisible = true;
            if (_combinedOverlayMode)
            {
                _combinedOverlayVisible = true;
                ShowCombinedOverlay();
                RefreshCombinedOverlayState();
            }
            else
            {
                ShowTimerOverlays(timer);
            }
            if (AutoStartCheckBox?.IsChecked == true && timer.Mode != 1)
                StartStopButton_Click(this, new RoutedEventArgs());
            RefreshOverlayActiveStates();
            UpdateButtonStates();
            UpdateShortcutLabels();
            string createdLabel = string.IsNullOrWhiteSpace(timer.Name)
                ? $"Timer {timer.Number}"
                : timer.Name;
            UpdateStatus($"{createdLabel} created", Brushes.DeepSkyBlue);
            CheckpointState();
        }

        private async Task<string?> ChooseProjectAsync(
            string currentName,
            bool isCreatingTimer)
        {
            if (_isNamingTimer)
                return null;

            var dialog = new TimerNameWindow(
                currentName,
                _projectHistory.ProjectNames,
                isCreatingTimer,
                ShortcutText(ShortcutAction.RenameTimer));
            if (IsActive && IsVisible && WindowState != WindowState.Minimized)
            {
                dialog.Owner = this;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            // This dialog can be opened by a global shortcut while another app is
            // in front of the controller. Ownership only keeps it above this window,
            // so make the short-lived chooser topmost to keep the prompt visible.
            dialog.Topmost = true;

            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void OnDialogClosed(object? sender, EventArgs args)
                => completion.TrySetResult(dialog.WasAccepted);

            dialog.Closed += OnDialogClosed;
            _isNamingTimer = true;
            _projectChooserWindow = dialog;
            try
            {
                // ShowDialog disables all other WPF windows on this dispatcher,
                // including the floating timers. A modeless window plus this awaited
                // completion keeps the controller flow linear without blocking overlays.
                dialog.Show();
                bool accepted = await completion.Task;
                return accepted ? dialog.TimerName : null;
            }
            finally
            {
                dialog.Closed -= OnDialogClosed;
                if (ReferenceEquals(_projectChooserWindow, dialog))
                    _projectChooserWindow = null;
                _isNamingTimer = false;
            }
        }

        private void CancelProjectChooser()
        {
            TimerNameWindow? dialog = _projectChooserWindow;
            if (dialog?.IsVisible == true)
                dialog.Close();
        }

        private void CycleActiveTimer()
        {
            if (_timers.Count == 0) return;
            SaveActiveTimerEditorState();
            var next = _timerManager.CycleNext();
            if (next != null) ActivateTimer(next);
        }

        private void CloseActiveTimer()
        {
            if (_activeTimer == null) return;

            var closing = _activeTimer;
            if (ProjectTransitionIsTemporarilyBlocked(closing)) return;
            DateTime transitionUtc = DateTime.UtcNow;
            if (!_combinedOverlayMode)
                CloseTimerOverlays(closing);
            _timerManager.CloseActive();
            SynchronizeProjectTracking(closing, transitionUtc);

            if (_timers.Count == 0)
            {
                CloseCombinedOverlayWindows();
                RestoreActiveTimerEditorState();
                RefreshOverlayActiveStates();
                UpdateButtonStates();
                UpdateShortcutLabels();
                string createShortcut = ShortcutText(ShortcutAction.NewTimer);
                UpdateStatus(createShortcut.Length > 0
                    ? $"No timers — press {createShortcut}"
                    : "No timers — use Timers > New timer", Brushes.Gray);
                CheckpointState();
                return;
            }

            ActivateTimer(_activeTimer!);
        }

        private async void RenameActiveTimer()
        {
            if (_activeTimer == null || _isNamingTimer) return;

            var timer = _activeTimer;
            if (ProjectTransitionIsTemporarilyBlocked(timer, alwaysBlock: true)) return;
            string? projectName = await ChooseProjectAsync(
                timer.Name,
                isCreatingTimer: false);
            if (projectName == null || _isExiting || !_timers.Contains(timer))
            {
                return;
            }

            // Persistence may have entered exact-checkpoint recovery while the
            // non-blocking chooser was open.
            if (ProjectTransitionIsTemporarilyBlocked(timer, alwaysBlock: true))
                return;

            string requestedName = projectName.Trim();
            bool assignmentChanged = !ProjectAssignmentsEqual(timer.Name, requestedName);
            bool wasRunning = timer.IsRunning;
            bool hadAccumulatedTime = timer.HasAccumulatedTime;
            int runningMode = timer.Mode == 1 ? timer.LastNonClockMode : timer.Mode;

            // Prepare a fresh countdown before mutating the project/history pair.
            // An invalid smart expression leaves the old project untouched.
            if (assignmentChanged
                && hadAccumulatedTime
                && runningMode == 2
                && !InitializeCountdownFromEditor(timer))
            {
                return;
            }

            DateTime transitionUtc = DateTime.UtcNow;
            string registeredName = RegisterProjectName(requestedName);
            assignmentChanged = !ProjectAssignmentsEqual(timer.Name, registeredName);
            timer.Name = registeredName;

            bool resetForNewProject = assignmentChanged && hadAccumulatedTime;
            if (resetForNewProject)
            {
                timer.ResetForProjectSwitch();
                if (runningMode == 2)
                {
                    timer.CountdownInitialized = wasRunning;
                    timer.LastCountdownUpdateUtc = wasRunning ? transitionUtc : default;
                }

                timer.RecBlinkVisible = wasRunning
                    && ShowRecIndicatorCheckBox?.IsChecked == true;
                if (ReferenceEquals(_activeTimer, timer))
                    LapPlaceholder.Visibility = Visibility.Visible;
            }

            // IsRunning is preserved by ResetForProjectSwitch, so this closes the
            // old project and opens the replacement at the same exact timestamp.
            SynchronizeProjectTracking(timer, transitionUtc);
            foreach (var instance in _overlayInstances.Where(item => ReferenceEquals(item.Session, timer)))
            {
                instance.Window.SetTimerName(timer.Name);
                if (resetForNewProject)
                {
                    instance.Window.SetRecIndicatorVisible(timer.RecBlinkVisible);
                    instance.Window.SetRunning(timer.IsRunning);
                }
            }
            RefreshCombinedOverlayState();
            RepositionAllOverlays();
            if (resetForNewProject)
                UpdateTimeDisplay();
            RecIndicator.Visibility = _activeTimer?.RecBlinkVisible == true
                ? Visibility.Visible
                : Visibility.Collapsed;
            UpdateButtonStates();
            UpdateShortcutLabels();

            string label = string.IsNullOrWhiteSpace(timer.Name) ? $"Timer {timer.Number}" : timer.Name;
            UpdateStatus(string.IsNullOrWhiteSpace(timer.Name)
                ? resetForNewProject
                    ? $"Timer {timer.Number} unassigned; timer reset to zero"
                    : $"Timer {timer.Number} is not assigned to a project"
                : resetForNewProject
                    ? $"Project set to {label}; timer reset to zero"
                    : $"Project set to {label}", Brushes.DeepSkyBlue);
            CheckpointState();
        }

        private static bool ProjectAssignmentsEqual(string? left, string? right)
            => StringComparer.OrdinalIgnoreCase.Equals(
                (left ?? "").Trim(),
                (right ?? "").Trim());

        private void NewTimerMenuItem_Click(object sender, RoutedEventArgs e) => CreateNewTimer();
        private void NextTimerMenuItem_Click(object sender, RoutedEventArgs e) => CycleActiveTimer();
        private void CloseTimerMenuItem_Click(object sender, RoutedEventArgs e) => CloseActiveTimer();
        private void RenameTimerMenuItem_Click(object sender, RoutedEventArgs e) => RenameActiveTimer();
        private void ToggleCombinedOverlayMenuItem_Click(object sender, RoutedEventArgs e) => ToggleCombinedOverlayMode();
        private void ProjectDashboardMenuItem_Click(object sender, RoutedEventArgs e) => ShowProjectDashboard();

        private void ThemeModeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeModeSelector?.SelectedItem is not ComboBoxItem item)
                return;

            // InitializeComponent selects the first ComboBox item before the saved
            // selection is pushed into the UI. The persisted theme was already
            // applied above, so this construction-time event must not overwrite it.
            if (!_initializationComplete || _syncingDedicatedSettings)
                return;

            string theme = AppThemeCatalog.Normalize(item.Content?.ToString());
            _settings.ThemeMode = theme;

            // Commit the small preference first. If the process is terminated at
            // any later point in this UI update, the next launch still restores
            // the user's explicit selection.
            PopulateSettingsFromUi();
            _settings.ThemeMode = theme;
            SettingsStore.Save(_settings);

            _backgroundApplyTimer.Stop();
            AppThemeManager.Apply(theme);
            AppBackgroundManager.Apply(_settings, out string? backgroundWarning);
            _backgroundWarning = backgroundWarning;
            if (backgroundWarning != null)
            {
                SettingsStore.Save(_settings);
                PopulatePanelBackgroundChoices(_settings.PanelBackgroundId);
            }
            UpdatePanelBackgroundPreview();

            // Existing overlays contain a user-configurable content layer and a
            // theme-controlled chrome layer, so reapply both without discarding
            // the user's text, outline, font, size, or opacity choices.
            ApplyAllOverlaySettings();
            UpdateButtonStates();
            _projectDashboardWindow?.RefreshFromHistory();

            // The normal checkpoint remains as a retry and crash-recovery backstop
            // for the rest of the application state.
            CheckpointState();
            UpdateStatus(
                backgroundWarning ?? $"{theme} theme applied",
                backgroundWarning == null
                    ? (Brush)FindResource("AccentBrush")
                    : Brushes.OrangeRed);
        }

        private void PopulatePanelBackgroundChoices(string? preferredId = null)
        {
            _updatingPanelBackgroundSelector = true;
            try
            {
                _backgroundChoices = AppBackgroundCatalog.GetAvailableChoices(_settings);
                PanelBackgroundSelector.ItemsSource = _backgroundChoices;

                string requested = preferredId ?? _settings.PanelBackgroundId;
                AppBackgroundChoice selected = _backgroundChoices.FirstOrDefault(choice =>
                    choice.Id.Equals(requested, StringComparison.OrdinalIgnoreCase))
                    ?? _backgroundChoices[0];
                PanelBackgroundSelector.SelectedItem = selected;
                if (selected.IsAvailable)
                    _settings.PanelBackgroundId = selected.Id;

                if (!selected.Id.Equals(requested, StringComparison.OrdinalIgnoreCase))
                {
                    _backgroundWarning =
                        "The saved custom background is missing; Theme default is being used.";
                    SettingsStore.Save(_settings);
                }
            }
            finally
            {
                _updatingPanelBackgroundSelector = false;
            }

            UpdatePanelBackgroundPreview();
        }

        private void UpdatePanelBackgroundPreview()
        {
            if (PanelBackgroundPreview == null)
                return;

            AppBackgroundChoice? selected =
                PanelBackgroundSelector?.SelectedItem as AppBackgroundChoice;
            PanelBackgroundPreview.Background = selected == null
                ? (Brush)FindResource("AppBackgroundBrush")
                : AppBackgroundManager.CreatePreviewBrush(
                    selected,
                    _settings.PanelBackgroundStrength);
            PanelBackgroundPreview.ToolTip = selected == null
                ? "Background preview"
                : selected.IsAvailable
                    ? $"Preview: {selected.DisplayName}"
                    : $"{selected.DisplayName} is unavailable. Remove it and add the image again.";
            if (PanelBackgroundStrengthSlider != null)
                PanelBackgroundStrengthSlider.IsEnabled = selected is
                    { IsThemeDefault: false, IsAvailable: true };
            if (RemovePanelBackgroundButton != null)
                RemovePanelBackgroundButton.IsEnabled = selected?.IsCustom == true;
        }

        private void PanelBackgroundSelector_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (_updatingPanelBackgroundSelector
                || !_initializationComplete
                || _syncingDedicatedSettings
                || PanelBackgroundSelector.SelectedItem is not AppBackgroundChoice choice)
            {
                return;
            }

            if (!choice.IsAvailable)
            {
                _backgroundApplyTimer.Stop();
                PopulateSettingsFromUi();
                _settings.PanelBackgroundId = AppBackgroundCatalog.ThemeDefault;
                bool fallbackSaved = SettingsStore.Save(_settings);
                AppBackgroundManager.Apply(_settings, out _);
                UpdatePanelBackgroundPreview();
                RefreshOverlayBackgroundSurfaces();
                MarkStateDirty();
                UpdateStatus(
                    fallbackSaved
                        ? $"{choice.DisplayName} is unavailable; remove it or add the image again"
                        : $"{choice.DisplayName} is unavailable, and the fallback could not be saved",
                    Brushes.OrangeRed);
                return;
            }

            PopulateSettingsFromUi();
            _settings.PanelBackgroundId = choice.Id;
            bool saved = SettingsStore.Save(_settings);

            _backgroundApplyTimer.Stop();
            AppBackgroundManager.Apply(_settings, out string? warning);
            _backgroundWarning = warning;
            if (warning != null)
            {
                choice.IsAvailable = false;
                PanelBackgroundSelector.Items.Refresh();
                SettingsStore.Save(_settings);
            }
            UpdatePanelBackgroundPreview();
            RefreshOverlayBackgroundSurfaces();
            MarkStateDirty();

            string message = warning
                ?? (saved
                    ? $"{choice.DisplayName} background applied"
                    : $"{choice.DisplayName} applied, but the choice could not be saved");
            UpdateStatus(
                message,
                warning == null && saved
                    ? (Brush)FindResource("AccentBrush")
                    : Brushes.OrangeRed);
        }

        private void PanelBackgroundStrengthSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (PanelBackgroundStrengthLabel != null)
            {
                PanelBackgroundStrengthLabel.Text =
                    $"{(int)Math.Round(PanelBackgroundStrengthSlider.Value)}%";
            }

            if (!_initializationComplete || _syncingDedicatedSettings)
                return;

            _settings.PanelBackgroundStrength = PanelBackgroundStrengthSlider.Value;
            _backgroundApplyTimer.Stop();
            _backgroundApplyTimer.Start();
            MarkStateDirty();
        }

        private void ApplyPendingPanelBackgroundStrength()
        {
            _backgroundApplyTimer.Stop();
            if (_isExiting)
                return;

            AppBackgroundManager.Apply(_settings, out string? warning);
            _backgroundWarning = warning;
            if (warning != null)
            {
                SettingsStore.Save(_settings);
                PopulatePanelBackgroundChoices(_settings.PanelBackgroundId);
            }
            UpdatePanelBackgroundPreview();
            RefreshOverlayBackgroundSurfaces();
            _settingsWindow?.SchedulePreviewFromAppliedSettings();
            if (warning != null)
                UpdateStatus(warning, Brushes.OrangeRed);
        }

        private void AddPanelBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Add a background image",
                Filter = "Background images (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|All files (*.*)|*.*",
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) != true)
                return;

            _backgroundApplyTimer.Stop();
            PopulateSettingsFromUi();
            string previousSelection = _settings.PanelBackgroundId;
            if (!AppBackgroundCatalog.TryImport(
                    dialog.FileName,
                    _settings.CustomBackgrounds,
                    out CustomAppBackground? imported,
                    out string? error)
                || imported == null)
            {
                System.Windows.MessageBox.Show(
                    error ?? "The selected image could not be added.",
                    "Add background",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _settings.CustomBackgrounds.Add(imported);
            string selectionId = AppBackgroundCatalog.CustomSelectionId(imported.Id);
            _settings.PanelBackgroundId = selectionId;
            if (!SettingsStore.Save(_settings))
            {
                _settings.CustomBackgrounds.RemoveAll(item =>
                    item.Id.Equals(imported.Id, StringComparison.OrdinalIgnoreCase));
                _settings.PanelBackgroundId = previousSelection;
                AppBackgroundCatalog.DeleteManagedCopy(imported);
                System.Windows.MessageBox.Show(
                    "The background could not be saved. Your existing settings were not changed.",
                    "Add background",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            PopulatePanelBackgroundChoices(selectionId);
            AppBackgroundManager.Apply(_settings, out string? warning);
            _backgroundWarning = warning;
            if (warning != null)
            {
                SettingsStore.Save(_settings);
                PopulatePanelBackgroundChoices(_settings.PanelBackgroundId);
            }
            UpdatePanelBackgroundPreview();
            RefreshOverlayBackgroundSurfaces();
            MarkStateDirty();
            UpdateStatus(
                warning ?? $"{imported.DisplayName} added and applied",
                warning == null
                    ? (Brush)FindResource("AccentBrush")
                    : Brushes.OrangeRed);
        }

        private void RemovePanelBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            if (PanelBackgroundSelector.SelectedItem is not AppBackgroundChoice
                { IsCustom: true } choice)
            {
                return;
            }

            string customId = choice.Id["custom:".Length..];
            int index = _settings.CustomBackgrounds.FindIndex(item =>
                item.Id.Equals(customId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return;

            MessageBoxResult confirmation = System.Windows.MessageBox.Show(
                $"Remove “{choice.DisplayName}” from your background library?\n\n" +
                "The app-managed copy will be deleted. This cannot be undone.",
                "Remove background",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
                return;

            _backgroundApplyTimer.Stop();
            PopulateSettingsFromUi();
            CustomAppBackground removed = _settings.CustomBackgrounds[index];
            string previousSelection = _settings.PanelBackgroundId;
            _settings.CustomBackgrounds.RemoveAt(index);
            _settings.PanelBackgroundId = AppBackgroundCatalog.ThemeDefault;

            if (!SettingsStore.Save(_settings))
            {
                _settings.CustomBackgrounds.Insert(
                    Math.Min(index, _settings.CustomBackgrounds.Count),
                    removed);
                _settings.PanelBackgroundId = previousSelection;
                System.Windows.MessageBox.Show(
                    "The background could not be removed because settings could not be saved.",
                    "Remove background",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            bool deleted = AppBackgroundCatalog.DeleteManagedCopy(removed);
            PopulatePanelBackgroundChoices(AppBackgroundCatalog.ThemeDefault);
            AppBackgroundManager.Apply(_settings, out string? warning);
            _backgroundWarning = warning;
            UpdatePanelBackgroundPreview();
            RefreshOverlayBackgroundSurfaces();
            MarkStateDirty();
            UpdateStatus(
                warning
                ?? (deleted
                    ? $"{choice.DisplayName} removed"
                    : $"{choice.DisplayName} removed from the library; its file could not be deleted"),
                warning == null && deleted
                    ? (Brush)FindResource("AccentBrush")
                    : Brushes.OrangeRed);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // Register global hotkeys
            var helper = new WindowInteropHelper(this);
            _hwndSource = HwndSource.FromHwnd(helper.Handle);
            _hwndSource?.AddHook(HwndHook);

            var failures = ApplyShortcuts(_shortcuts);
            UpdateShortcutLabels();
            if (failures.Count > 0)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    string names = string.Join(", ", failures.Select(action =>
                        _shortcuts.TryGetValue(action, out var shortcut)
                            ? shortcut.Format() : action.ToString()));
                    UpdateStatus($"Shortcut unavailable: {names}", Brushes.OrangeRed);
                    _trayIcon?.ShowBalloonTip(
                        5000,
                        "Some shortcuts are unavailable",
                        $"Windows could not register: {names}. Change them in Shortcuts.",
                        ToolTipIcon.Warning);
                }), DispatcherPriority.ContextIdle);
            }
        }

        // Unregisters all hotkey ids, then registers each non-unbound shortcut.
        // Returns the actions whose RegisterHotKey failed (combo held by another app).
        private List<ShortcutAction> ApplyShortcuts(Dictionary<ShortcutAction, Shortcut> shortcuts)
        {
            var failures = new List<ShortcutAction>();
            var helper = new WindowInteropHelper(this);
            if (helper.Handle == IntPtr.Zero) return failures; // HWND not ready yet

            foreach (ShortcutAction action in Enum.GetValues<ShortcutAction>())
                UnregisterHotKey(helper.Handle, (int)action);

            foreach (var (action, shortcut) in shortcuts)
            {
                if (shortcut.VirtualKey == 0) continue; // unbound
                bool ok = RegisterHotKey(helper.Handle, (int)action,
                    shortcut.Modifiers | MOD_NOREPEAT, shortcut.VirtualKey);
                if (!ok) failures.Add(action);
            }
            return failures;
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                ShortcutAction action = (ShortcutAction)wParam.ToInt32();
                if (action != ShortcutAction.OpenDashboard
                    && (_workspaceLoadRetryPending || _workspacePersistenceDisabled))
                {
                    ProjectTransitionIsTemporarilyBlocked(null, alwaysBlock: true);
                    handled = true;
                    return IntPtr.Zero;
                }

                switch (action)
                {
                    case ShortcutAction.StartStop:
                        StartStopButton_Click(this, new RoutedEventArgs());
                        handled = true;
                        break;
                    case ShortcutAction.Reset:
                        ResetButton_Click(this, new RoutedEventArgs());
                        handled = true;
                        break;
                    case ShortcutAction.ToggleOverlay:
                        ToggleOverlayButton_Click(this, new RoutedEventArgs());
                        handled = true;
                        break;
                    case ShortcutAction.Lap:
                        LapButton_Click(this, new RoutedEventArgs());
                        handled = true;
                        break;
                    case ShortcutAction.ToggleClock:
                        ToggleClockMode();
                        handled = true;
                        break;
                    case ShortcutAction.NewTimer:
                        CreateNewTimer();
                        handled = true;
                        break;
                    case ShortcutAction.NextTimer:
                        CycleActiveTimer();
                        handled = true;
                        break;
                    case ShortcutAction.CloseTimer:
                        CloseActiveTimer();
                        handled = true;
                        break;
                    case ShortcutAction.RenameTimer:
                        Dispatcher.BeginInvoke(new Action(RenameActiveTimer), DispatcherPriority.Input);
                        handled = true;
                        break;
                    case ShortcutAction.OpenDashboard:
                        ShowProjectDashboard();
                        handled = true;
                        break;
                    case ShortcutAction.ToggleCombinedOverlay:
                        ToggleCombinedOverlayMode();
                        handled = true;
                        break;
                }
            }
            return IntPtr.Zero;
        }

        private void PopulateScreens()
        {
            ScreenSelector.Items.Clear();
            ScreenSelector.Items.Add(new ComboBoxItem { Content = "All Screens", Tag = null });

            var screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                var screen = screens[i];
                string name = screen.Primary ? $"Screen {i + 1} (Primary)" : $"Screen {i + 1}";
                name += $" - {screen.Bounds.Width}x{screen.Bounds.Height}";
                ScreenSelector.Items.Add(new ComboBoxItem { Content = name, Tag = screen });
            }

            ScreenSelector.SelectedIndex = screens.Length > 1 ? 1 : 0;
            _selectedScreen = screens.Length > 0 ? screens[0] : null;
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            DateTime utcNow = DateTime.UtcNow;
            DateTime localNow = DateTime.Now;

            AdvanceRunningCountdowns(utcNow, localNow, announceExpiry: true);
            UpdateTimeDisplay();
            if (++_timerRailRefreshTick >= 10)
            {
                _timerRailRefreshTick = 0;
                TimerRailList?.Items.Refresh();
            }
        }

        private void AdvanceRunningCountdowns(
            DateTime utcNow,
            DateTime localNow,
            bool announceExpiry)
        {
            foreach (var timer in _timers)
            {
                int runningMode = timer.Mode == 1 ? timer.LastNonClockMode : timer.Mode;
                if (!timer.IsRunning || runningMode != 2) continue;

                TimeSpan before = timer.CountdownRemaining;
                if (timer.UseClockTarget)
                {
                    timer.CountdownRemaining = timer.ClockTarget - localNow;
                }
                else
                {
                    if (timer.LastCountdownUpdateUtc == default)
                        timer.LastCountdownUpdateUtc = utcNow;
                    timer.CountdownRemaining -= utcNow - timer.LastCountdownUpdateUtc;
                    timer.LastCountdownUpdateUtc = utcNow;
                }

                if (announceExpiry
                    && ReferenceEquals(timer, _activeTimer)
                    && before > TimeSpan.Zero && timer.CountdownRemaining <= TimeSpan.Zero)
                    UpdateStatus("Time's up! (counting negative)", Brushes.Red);
            }
        }

        internal void CheckpointStateNow()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(CheckpointStateNow, DispatcherPriority.Send);
                return;
            }

            bool wasRetryingExactCheckpoint = _pendingWorkspaceSnapshot != null;
            CheckpointState();
            if (wasRetryingExactCheckpoint
                && _pendingWorkspaceSnapshot == null
                && _stateDirty)
            {
                // The first pass completed an older exact recovery pair. Capture
                // the current in-memory state before a clean exit, shutdown, or
                // best-effort crash checkpoint can terminate the process.
                CheckpointState();
            }
        }

        private void CheckpointState()
        {
            if (!_initializationComplete || _checkpointInProgress)
                return;

            if (_workspaceCreateOnlySaveConflict)
            {
                // The unsaved in-memory first-run state must never be written over
                // recovery data that appeared concurrently. Preferences remain
                // independent and may still be saved before the required restart.
                PopulateSettingsFromUi();
                bool settingsSaved = SettingsStore.Save(_settings);
                _stateDirty = !settingsSaved;
                return;
            }

            _stateDirty = true;
            _checkpointInProgress = true;
            try
            {
                bool retryingExactCheckpoint = _pendingWorkspaceSnapshot != null;
                bool deferringForHistoryLoad = _projectHistoryLoadRetryPending
                    && !_workspacePersistenceDisabled;
                DateTime checkpointUtc;
                TimerWorkspaceSnapshot? workspaceSnapshot;

                if (retryingExactCheckpoint)
                {
                    // A previous workspace/history pair did not fully commit.
                    // Reuse the exact logical snapshot and timestamp: moving this
                    // watermark forward would make a crash repair a pause, start,
                    // rename, or close at the wrong time.
                    checkpointUtc = _pendingCheckpointUtc;
                    workspaceSnapshot = _pendingWorkspaceSnapshot;
                }
                else
                {
                    SaveActiveTimerEditorState();
                    checkpointUtc = DateTime.UtcNow;
                    AdvanceRunningCountdowns(
                        checkpointUtc,
                        checkpointUtc.ToLocalTime(),
                        announceExpiry: false);
                    workspaceSnapshot = _workspacePersistenceDisabled
                        || deferringForHistoryLoad
                        ? null
                        : TimerWorkspaceStore.Capture(_timerManager, checkpointUtc);
                    if (workspaceSnapshot != null)
                    {
                        workspaceSnapshot.SkipProjectHistoryReconciliation =
                            _skipProjectHistoryReconciliation;
                        workspaceSnapshot.CombinedOverlayMode = _combinedOverlayMode;
                        workspaceSnapshot.CombinedOverlayVisible = _combinedOverlayVisible;
                        workspaceSnapshot.CombinedHasCustomPosition = _combinedHasCustomPosition;
                        workspaceSnapshot.CombinedPositionsByScreen =
                            _combinedPositionsByScreen.ToDictionary(
                                pair => pair.Key,
                                pair => new TimerScreenPositionSnapshot
                                {
                                    Left = pair.Value.Left,
                                    Top = pair.Value.Top
                                });
                        _pendingWorkspaceSnapshot = workspaceSnapshot;
                        _pendingCheckpointUtc = checkpointUtc;
                        _pendingWorkspaceSaved = false;
                    }
                }

                PopulateSettingsFromUi();
                bool settingsSaved = SettingsStore.Save(_settings);
                bool attemptedWorkspaceCreateOnlySave = !_workspacePersistenceDisabled
                    && !deferringForHistoryLoad
                    && !_pendingWorkspaceSaved
                    && _workspaceRequiresCreateOnlySave;
                bool workspaceSaved = _workspacePersistenceDisabled
                    || deferringForHistoryLoad
                    || _pendingWorkspaceSaved
                    || (_workspaceRequiresCreateOnlySave
                        ? _workspaceStore.SaveNew(workspaceSnapshot!)
                        : _workspaceStore.Save(workspaceSnapshot!));
                if (workspaceSaved
                    && !_workspacePersistenceDisabled
                    && !deferringForHistoryLoad)
                {
                    _pendingWorkspaceSaved = true;
                    _workspaceRequiresCreateOnlySave = false;
                }

                // Never let history advance beyond a failed workspace write. With
                // workspace first and both files stamped with the same UTC value,
                // startup reconciliation can safely repair a crash between them.
                bool historyNeededRepair = _projectTimeStore.NeedsPrimaryRepair;
                bool projectHistoryWasDirty = _projectHistoryDirty;
                bool attemptedHistoryCreateOnlySave = !_projectHistoryPersistenceDisabled
                    && _projectHistoryDirty
                    && workspaceSaved
                    && _projectHistoryRequiresCreateOnlySave;
                bool projectHistorySaved = _projectHistoryPersistenceDisabled
                    || !_projectHistoryDirty
                    || (workspaceSaved
                        && (_projectHistoryRequiresCreateOnlySave
                            ? _projectTimeStore.SaveNew(_projectHistory, checkpointUtc)
                            : _projectTimeStore.Save(_projectHistory, checkpointUtc)));
                if (projectHistorySaved)
                {
                    _projectHistoryDirty = false;
                    if (projectHistoryWasDirty && !_projectHistoryPersistenceDisabled)
                        _projectHistoryRequiresCreateOnlySave = false;
                    if (historyNeededRepair && !_projectTimeStore.NeedsPrimaryRepair)
                        _projectHistoryWarning = null;
                }

                if ((attemptedWorkspaceCreateOnlySave
                        && !workspaceSaved
                        && _workspaceStore.LastSaveNewConflictDetected)
                    || (attemptedHistoryCreateOnlySave
                        && !projectHistorySaved
                        && _projectTimeStore.LastSaveNewConflictDetected))
                {
                    EnterCreateOnlySaveConflict();
                    return;
                }

                bool recoveryPairSaved = workspaceSaved && projectHistorySaved;
                if (recoveryPairSaved)
                {
                    _pendingWorkspaceSnapshot = null;
                    _pendingWorkspaceSaved = false;
                }

                bool allSaved = settingsSaved && workspaceSaved && projectHistorySaved;
                if (allSaved)
                {
                    // A successful retry commits the older exact snapshot first.
                    // Keep the dirty flag for one fresh follow-up checkpoint so
                    // harmless UI edits made during the retry are not discarded.
                    _stateDirty = retryingExactCheckpoint || deferringForHistoryLoad;
                    if (_persistenceFailureNotified)
                        UpdateStatus("Recovery data saved", Brushes.DeepSkyBlue);
                    _persistenceFailureNotified = false;
                }
                else
                {
                    ReportPersistenceFailure(
                        settingsSaved,
                        workspaceSaved,
                        projectHistorySaved);
                }
            }
            finally
            {
                _checkpointInProgress = false;
            }
        }

        private void ReportPersistenceFailure(
            bool settingsSaved,
            bool workspaceSaved,
            bool projectHistorySaved)
        {
            var failed = new List<string>(3);
            if (!settingsSaved) failed.Add("settings");
            if (!workspaceSaved) failed.Add("timer recovery");
            if (!projectHistorySaved) failed.Add("project history");

            bool settingsRequireRestart = !settingsSaved
                && SettingsStore.IsWriteProtected(SettingsStore.SettingsPath);
            string message = settingsRequireRestart
                ? "Settings file was unavailable at startup; close any other instance and restart to reload it safely"
                : $"Could not save {string.Join(", ", failed)}; retrying";
            UpdateStatus(message, Brushes.OrangeRed);

            if (_persistenceFailureNotified)
                return;

            _persistenceFailureNotified = true;
            _trayIcon?.ShowBalloonTip(
                5000,
                "Stopwatch data was not saved",
                message,
                ToolTipIcon.Warning);
        }

        private void MarkStateDirty()
        {
            if (_initializationComplete && !_restoringTimerUi)
                _stateDirty = true;
        }

        private void BlinkTimer_Tick(object? sender, EventArgs e)
        {
            foreach (var timer in _timers)
            {
                timer.ColonVisible = BlinkColonCheckBox?.IsChecked == true && timer.Mode == 1
                    ? !timer.ColonVisible : true;
                timer.RecBlinkVisible = timer.IsRunning
                    && ShowRecIndicatorCheckBox?.IsChecked == true
                    && !timer.RecBlinkVisible;

                foreach (var instance in _overlayInstances.Where(item => ReferenceEquals(item.Session, timer)))
                    instance.Window.SetRecIndicatorVisible(timer.RecBlinkVisible);
            }

            if (_combinedOverlayMode && _activeTimer != null)
            {
                foreach (var instance in _combinedOverlayInstances)
                    instance.Window.SetRecIndicatorVisible(_activeTimer.RecBlinkVisible);
            }

            RecIndicator.Visibility = _activeTimer?.RecBlinkVisible == true
                ? Visibility.Visible : Visibility.Collapsed;
            UpdateTimeDisplay();
        }

        private void UpdateTimeDisplay()
        {
            foreach (var timer in _timers)
            {
                string timerText = GetFormattedTime(timer);
                foreach (var instance in _overlayInstances.Where(item => ReferenceEquals(item.Session, timer)))
                    instance.Window.UpdateTime(timerText);
            }

            if (_combinedOverlayMode && _activeTimer != null)
            {
                string timerText = GetFormattedTime(_activeTimer);
                foreach (var instance in _combinedOverlayInstances)
                    instance.Window.UpdateTime(timerText);
            }

            TimeDisplay.Text = _activeTimer == null ? "--:--" : GetFormattedTime(_activeTimer);
        }

        private string GetFormattedTime() => GetFormattedTime(CurrentTimer);

        private string GetFormattedTime(TimerSession timer)
        {
            string colon = timer.ColonVisible ? ":" : " ";
            
            switch (timer.Mode)
            {
                case 1: // Clock
                    var now = DateTime.Now;
                    return _timeFormat switch
                    {
                        0 => $"{now.Hour:D2}{colon}{now.Minute:D2}{colon}{now.Second:D2}.{now.Millisecond / 100:D1}",
                        1 => $"{now.Hour:D2}{colon}{now.Minute:D2}{colon}{now.Second:D2}",
                        2 => $"{now.Minute:D2}{colon}{now.Second:D2}.{now.Millisecond / 100:D1}",
                        3 => $"{now.Minute:D2}{colon}{now.Second:D2}",
                        4 => $"{now.Hour:D2}{colon}{now.Minute:D2}",
                        _ => now.ToString("HH:mm:ss")
                    };

                case 2: // Countdown (both fixed-duration and until-clock-time)
                    var remaining = timer.CountdownRemaining;
                    bool isNegative = remaining < TimeSpan.Zero;
                    var absRemaining = isNegative ? remaining.Negate() : remaining;
                    string sign = isNegative ? "-" : "";
                    long countdownHours = (long)absRemaining.TotalHours;
                    long countdownMinutes = (long)absRemaining.TotalMinutes;
                    return _timeFormat switch
                    {
                        0 => $"{sign}{countdownHours:D2}:{absRemaining.Minutes:D2}:{absRemaining.Seconds:D2}.{absRemaining.Milliseconds / 100:D1}",
                        1 => $"{sign}{countdownHours:D2}:{absRemaining.Minutes:D2}:{absRemaining.Seconds:D2}",
                        2 => $"{sign}{countdownMinutes:D2}:{absRemaining.Seconds:D2}.{absRemaining.Milliseconds / 100:D1}",
                        3 => $"{sign}{countdownMinutes:D2}:{absRemaining.Seconds:D2}",
                        4 => $"{sign}{countdownHours:D2}:{absRemaining.Minutes:D2}",
                        _ => $"{sign}{absRemaining.Hours:D2}:{absRemaining.Minutes:D2}:{absRemaining.Seconds:D2}.{absRemaining.Milliseconds / 100:D1}"
                    };

                case 3: // Timecode (with frames)
                    var tc = timer.Stopwatch.Elapsed;
                    int frames = (int)(tc.Milliseconds / (1000.0 / _frameRate));
                    return $"{(long)tc.TotalHours:D2}:{tc.Minutes:D2}:{tc.Seconds:D2}:{frames:D2}";

                default: // Stopwatch
                    var elapsed = timer.Stopwatch.Elapsed;
                    long elapsedHours = (long)elapsed.TotalHours;
                    long elapsedMinutes = (long)elapsed.TotalMinutes;
                    return _timeFormat switch
                    {
                        0 => $"{elapsedHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}.{elapsed.Milliseconds / 100:D1}",
                        1 => $"{elapsedHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}",
                        2 => $"{elapsedMinutes:D2}:{elapsed.Seconds:D2}.{elapsed.Milliseconds / 100:D1}",
                        3 => $"{elapsedMinutes:D2}:{elapsed.Seconds:D2}",
                        4 => $"{elapsedHours:D2}:{elapsed.Minutes:D2}",
                        _ => $"{elapsedHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}.{elapsed.Milliseconds / 100:D1}"
                    };
            }
        }

        private void ModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (CountdownPanel == null) return;
            if (_activeTimer == null && IsLoaded) return;

            if (StopwatchModeRadio?.IsChecked == true) _currentMode = 0;
            else if (ClockModeRadio?.IsChecked == true) _currentMode = 1;
            else if (CountdownModeRadio?.IsChecked == true) _currentMode = 2;
            else if (TimecodeModeRadio?.IsChecked == true) _currentMode = 3;

            if (_currentMode != 1)
                _lastNonClockMode = _currentMode;

            CountdownPanel.Visibility = _currentMode == 2 ? Visibility.Visible : Visibility.Collapsed;

            // Refresh the prefilled target whenever the user enters until-clock-time countdown
            if (_currentMode == 2 && _useClockTarget && !_restoringTimerUi) PrefillClockTarget();

            UpdateButtonStates();
            UpdateTimeDisplay();

            if (!_restoringTimerUi)
            {
                string[] modeNames = { "Stopwatch", "Clock", "Countdown", "Timecode" };
                UpdateStatus($"{modeNames[_currentMode]} Mode", Brushes.DeepSkyBlue);
            }

            if (_currentMode == 2 && !_restoringTimerUi) FocusCountdownInput();
            if (!_restoringTimerUi) CheckpointState();
        }

        private void ToggleClockMode()
        {
            if (_activeTimer == null) return;
            if (_currentMode == 1)
            {
                SelectMode(_lastNonClockMode);
            }
            else
            {
                _lastNonClockMode = _currentMode;
                SelectMode(1);
            }
        }

        private void SelectMode(int mode)
        {
            var radio = mode switch
            {
                0 => StopwatchModeRadio,
                1 => ClockModeRadio,
                2 => CountdownModeRadio,
                3 => TimecodeModeRadio,
                _ => null
            };

            if (radio != null)
                radio.IsChecked = true;
        }

        private void CountdownTypeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (CountdownDurationPanel == null || CountdownUntilPanel == null) return;
            if (_activeTimer == null && IsLoaded) return;

            _useClockTarget = CountdownUntilRadio?.IsChecked == true;
            CountdownDurationPanel.Visibility = _useClockTarget ? Visibility.Collapsed : Visibility.Visible;
            CountdownUntilPanel.Visibility = _useClockTarget ? Visibility.Visible : Visibility.Collapsed;

            if (_useClockTarget && !_restoringTimerUi) PrefillClockTarget();
            UpdateTimeDisplay();
            if (!_restoringTimerUi) FocusCountdownInput();
            if (!_restoringTimerUi) CheckpointState();
        }

        // Focuses the active countdown input so the user can type immediately:
        // the smart text box in smart mode, else the first classic field for the
        // current sub-mode (until-clock-time vs fixed duration). No-op off countdown.
        private void FocusCountdownInput()
        {
            if (_currentMode != 2) return;

            System.Windows.Controls.Control? target =
                _settings.UseSmartCountdownInput ? SmartInputBox
                : _useClockTarget ? ClockTargetHours
                : CountdownMinutes;
            if (target == null) return;

            // Defer so focus lands after layout/visibility changes are applied.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                target.Focus();
                if (target is System.Windows.Controls.TextBox tb) tb.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            FitControllerToWorkingArea();

            // Refresh the command on every launch in case a portable build was moved.
            // This is best-effort here; an interactive checkbox change reports errors.
            try
            {
                StartupRegistration.SetEnabled(_settings.StartWithWindows);
            }
            catch (Exception exception) when (exception is
                UnauthorizedAccessException
                or System.Security.SecurityException
                or System.IO.IOException
                or InvalidOperationException
                or ArgumentException)
            {
                CrashLogger.LogRecoverable(exception, "StartupRegistrationRefresh");
                UpdateStatus("Could not update Windows startup", Brushes.OrangeRed);
            }

            if (_workspaceWasRestored)
            {
                // Recreate exactly the overlays that were visible at the last valid
                // checkpoint. Do not auto-start restored paused timers.
                RestorePersistedOverlays();
            }
            // A brand-new workspace asks for a project before it mutates the timer
            // manager. Cancel leaves the supported zero-timer state intact.
            else if (!_workspaceLoadRetryPending)
            {
                CreateNewTimer();
            }
            else
            {
                // Keep the manager pristine while the background retry reads the
                // existing workspace. This lets recovery apply without replacing
                // any timer the user created in the meantime.
                RetryUnavailableWorkspace();
            }

            // Settings now participate in the controller's page-level scrolling. Reset
            // any automatic bring-into-view offset produced while restoring controls.
            Dispatcher.BeginInvoke(new Action(() => ControllerScrollViewer.ScrollToTop()),
                System.Windows.Threading.DispatcherPriority.Loaded);

            FocusCountdownInput();
            CheckpointState();

            if (_persistenceFailureNotified)
            {
                // Keep the more actionable save failure already shown by the
                // checkpoint rather than replacing it with a recovery notice.
            }
            else if (_workspaceLoadUnavailable)
                UpdateStatus(
                    "Timer recovery is validating the existing data files; nothing has been overwritten",
                    Brushes.OrangeRed);
            else if (_workspacePersistenceDisabled)
                UpdateStatus(
                    "Timer recovery file is from a newer app version and was not overwritten",
                    Brushes.OrangeRed);
            else if (_projectHistoryWarning != null)
                UpdateStatus(_projectHistoryWarning, Brushes.OrangeRed);
            else if (_projectHistoryRecoveredFromBackup)
                UpdateStatus("Project history recovered from backup", Brushes.DeepSkyBlue);
            else if (_workspaceRecoveredFromBackup)
                UpdateStatus("Timer state recovered from backup", Brushes.DeepSkyBlue);
            else if (_backgroundWarning != null)
                UpdateStatus(_backgroundWarning, Brushes.OrangeRed);
        }

        private void RestorePersistedOverlays()
        {
            if (_combinedOverlayMode)
            {
                if (_combinedOverlayVisible)
                    ShowCombinedOverlay();
            }
            else
            {
                foreach (var timer in _timers.Where(timer => timer.OverlayVisible))
                    ShowTimerOverlays(timer);
            }

            RefreshOverlayActiveStates();
            UpdateShortcutLabels();
        }

        private void StartWithWindowsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || _changingStartWithWindows || _syncingDedicatedSettings) return;

            bool enabled = StartWithWindowsCheckBox.IsChecked == true;
            if (ApplyStartWithWindowsSetting(enabled, showWarning: true))
                CheckpointState();
        }

        private bool ApplyStartWithWindowsSetting(bool enabled, bool showWarning)
        {
            bool previousValue = _appliedStartWithWindows;
            try
            {
                StartupRegistration.SetEnabled(enabled);
                _settings.StartWithWindows = enabled;
                if (!SettingsStore.Save(_settings))
                {
                    try
                    {
                        StartupRegistration.SetEnabled(previousValue);
                    }
                    catch (Exception rollbackException) when (rollbackException is
                        UnauthorizedAccessException
                        or System.Security.SecurityException
                        or System.IO.IOException
                        or InvalidOperationException
                        or ArgumentException)
                    {
                        CrashLogger.LogRecoverable(
                            rollbackException,
                            "StartupRegistrationRollback");
                    }

                    RestoreStartWithWindowsSetting(previousValue);
                    if (showWarning)
                    {
                        System.Windows.MessageBox.Show(
                            "Windows startup was changed, but the preference could not be saved. The change was rolled back.",
                            "Start with Windows",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                    return false;
                }

                _appliedStartWithWindows = enabled;
                UpdateStatus(enabled ? "Starts with Windows" : "Windows startup disabled",
                    Brushes.DeepSkyBlue);
                return true;
            }
            catch (Exception ex) when (ex is
                UnauthorizedAccessException
                or System.Security.SecurityException
                or System.IO.IOException
                or InvalidOperationException
                or ArgumentException)
            {
                CrashLogger.LogRecoverable(ex, "StartupRegistration");
                RestoreStartWithWindowsSetting(previousValue);

                if (showWarning)
                {
                    System.Windows.MessageBox.Show(
                        "Windows startup could not be updated. Check access to your Windows startup settings and try again.",
                        "Start with Windows",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                return false;
            }
        }

        private void EnterCreateOnlySaveConflict()
        {
            _workspaceCreateOnlySaveConflict = true;
            _workspacePersistenceDisabled = true;
            _projectHistoryPersistenceDisabled = true;
            _workspaceLoadRetryPending = false;
            _workspaceLoadUnavailable = false;
            _workspaceRequiresCreateOnlySave = false;
            _projectHistoryRequiresCreateOnlySave = false;
            _pendingWorkspaceSnapshot = null;
            _pendingWorkspaceSaved = false;
            _stateDirty = false;
            UpdateButtonStates();
            UpdateStatus(
                "Existing recovery data appeared; restart to load it safely. New unsaved timer changes were not written",
                Brushes.OrangeRed);
            CrashLogger.LogRecoverable(
                new System.IO.IOException(
                    "A create-only first workspace checkpoint lost a race with restored recovery data; no existing file was replaced."),
                "FirstWorkspaceCreateConflict");
        }

        private void RestoreStartWithWindowsSetting(bool enabled)
        {
            _settings.StartWithWindows = enabled;
            _changingStartWithWindows = true;
            try
            {
                StartWithWindowsCheckBox.IsChecked = enabled;
            }
            finally
            {
                _changingStartWithWindows = false;
            }
            _settingsWindow?.ReloadFromSettings();
        }

        private void InitializeTrayIcon()
        {
            _trayMenu = new ContextMenuStrip();

            var openItem = new ToolStripMenuItem("Open Stopwatch Controller")
            {
                Font = new System.Drawing.Font(
                    System.Drawing.SystemFonts.MenuFont!, System.Drawing.FontStyle.Bold)
            };
            openItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(ShowController));

            var dashboardItem = new ToolStripMenuItem("Project dashboard");
            dashboardItem.Click += (_, _) =>
                Dispatcher.BeginInvoke(new Action(ShowProjectDashboard));

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(ExitApplication));

            _trayMenu.Items.Add(openItem);
            _trayMenu.Items.Add(dashboardItem);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(exitItem);

            _trayIcon = new NotifyIcon
            {
                Icon = LoadTrayIcon(),
                Text = "Stopwatch Overlay",
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _trayIcon.DoubleClick += (_, _) => Dispatcher.BeginInvoke(new Action(ShowController));
        }

        private static System.Drawing.Icon LoadTrayIcon()
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/StopwatchOverlay;component/project-logo.ico"));
            if (resource?.Stream == null)
                return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();

            using (resource.Stream)
            using (var icon = new System.Drawing.Icon(resource.Stream, SystemInformation.SmallIconSize))
                return (System.Drawing.Icon)icon.Clone();
        }

        internal void ShowController()
        {
            if (_isExiting || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return;

            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            Focus();
        }

        private void ShowProjectDashboard()
        {
            CheckpointState();

            if (_projectDashboardWindow == null)
            {
                _projectDashboardWindow = new ProjectDashboardWindow(
                    () => _projectHistory.CreateView(DateTime.UtcNow),
                    AddManualProjectRecord,
                    UpdateProjectRecord,
                    DeleteProjectRecord,
                    CanMutateProjectRecords,
                    GetProjectRecordsWarning);
                _projectDashboardWindow.Closed += (_, _) => _projectDashboardWindow = null;
            }

            _projectDashboardWindow.RefreshFromHistory();

            if (!_projectDashboardWindow.IsVisible)
                _projectDashboardWindow.Show();
            if (_projectDashboardWindow.WindowState == WindowState.Minimized)
                _projectDashboardWindow.WindowState = WindowState.Normal;

            _projectDashboardWindow.Activate();
            _projectDashboardWindow.Focus();
        }

        private bool CanMutateProjectRecords()
        {
            if (!Dispatcher.CheckAccess())
                return Dispatcher.Invoke(CanMutateProjectRecords);

            return !_projectHistoryPersistenceDisabled
                && !_projectHistoryLoadRetryPending
                && _pendingWorkspaceSnapshot == null;
        }

        private string? GetProjectRecordsWarning()
        {
            if (!Dispatcher.CheckAccess())
                return Dispatcher.Invoke(GetProjectRecordsWarning);

            if (_projectHistoryPersistenceDisabled)
            {
                return _projectHistoryWarning
                    ?? "Project history is unavailable, so records cannot be changed.";
            }

            if (_projectHistoryLoadRetryPending)
                return "Project history is still loading. Editing will be available when recovery finishes.";

            if (_pendingWorkspaceSnapshot != null)
                return "Project recovery is finishing. Try the change again in a moment.";

            return _projectHistoryWarning;
        }

        private void EnsureProjectRecordsCanMutate()
        {
            if (CanMutateProjectRecords())
                return;

            throw new InvalidOperationException(
                GetProjectRecordsWarning()
                ?? "Project records cannot be changed while recovery is in progress.");
        }

        private ProjectRecordMutationResult AddManualProjectRecord(
            string projectName,
            DateTime startUtc,
            DateTime endUtc)
        {
            if (!Dispatcher.CheckAccess())
            {
                return Dispatcher.Invoke(() =>
                    AddManualProjectRecord(projectName, startUtc, endUtc));
            }

            // The records dialog can remain open while an older exact checkpoint
            // begins retrying. Recheck at commit time so a manual edit can never
            // advance project history beyond its paired timer workspace.
            EnsureProjectRecordsCanMutate();
            ProjectWorkIntervalView record = _projectHistory.AddManualInterval(
                projectName,
                startUtc,
                endUtc);
            MarkProjectHistoryDirty();
            CheckpointStateNow();
            return new ProjectRecordMutationResult(
                ProjectRecordMutationStatus.Success,
                record);
        }

        private ProjectRecordMutationResult UpdateProjectRecord(
            Guid recordId,
            string projectName,
            DateTime startUtc,
            DateTime endUtc)
        {
            if (!Dispatcher.CheckAccess())
            {
                return Dispatcher.Invoke(() =>
                    UpdateProjectRecord(recordId, projectName, startUtc, endUtc));
            }

            EnsureProjectRecordsCanMutate();
            ProjectRecordMutationResult result = _projectHistory.UpdateClosedInterval(
                recordId,
                projectName,
                startUtc,
                endUtc);
            if (result.Status == ProjectRecordMutationStatus.Success)
            {
                MarkProjectHistoryDirty();
                CheckpointStateNow();
            }

            return result;
        }

        private ProjectRecordMutationResult DeleteProjectRecord(Guid recordId)
        {
            if (!Dispatcher.CheckAccess())
            {
                return Dispatcher.Invoke(() => DeleteProjectRecord(recordId));
            }

            EnsureProjectRecordsCanMutate();
            ProjectRecordMutationResult result =
                _projectHistory.DeleteClosedInterval(recordId);
            if (result.Status == ProjectRecordMutationStatus.Success)
            {
                MarkProjectHistoryDirty();
                CheckpointStateNow();
            }

            return result;
        }

        private void ExitApplication()
        {
            FlushPendingSettingsBeforeExit(showWarnings: true);
            _isExiting = true;
            Close();
        }

        internal void PrepareForSystemExit()
        {
            FlushPendingSettingsBeforeExit(showWarnings: false);
            _isExiting = true;
            CancelProjectChooser();
            CheckpointStateNow();
        }

        private void FlushPendingSettingsBeforeExit(bool showWarnings)
        {
            // A tray exit can race the short Settings coalescing window. Finish the
            // last discrete change (notably Start with Windows) before shutdown
            // starts, otherwise its value could be saved without its side effect.
            _settingsInteractionInProgress = false;
            _settingsCompletionQueued = false;
            if (_pendingDedicatedSettingsChanges != SettingsChangeKind.None
                || _dedicatedSettingsApplyTimer.IsEnabled)
            {
                ApplyPendingDedicatedSettings(showWarnings);
            }

            if (_backgroundApplyTimer.IsEnabled)
                ApplyPendingPanelBackgroundStrength();
        }

        // WPF's CenterScreen placement can put the title bar outside the desktop when
        // the requested DIP size becomes taller than the monitor after display scaling.
        // Clamp and center in native pixels so this also works on mixed-DPI monitors.
        private void FitControllerToWorkingArea()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var windowRect)) return;

            var workArea = Screen.FromHandle(hwnd).WorkingArea;
            const int edgeMargin = 8;

            int availableWidth = Math.Max(1, workArea.Width - edgeMargin * 2);
            int availableHeight = Math.Max(1, workArea.Height - edgeMargin * 2);
            int windowWidth = Math.Min(windowRect.Right - windowRect.Left, availableWidth);
            int windowHeight = Math.Min(windowRect.Bottom - windowRect.Top, availableHeight);
            int left = workArea.Left + (workArea.Width - windowWidth) / 2;
            int top = workArea.Top + (workArea.Height - windowHeight) / 2;

            SetWindowPos(hwnd, IntPtr.Zero, left, top, windowWidth, windowHeight,
                SWP_NOZORDER | SWP_NOACTIVATE);
        }

        private void PrefillClockTarget()
        {
            if (ClockTargetHours == null) return;
            var t = DateTime.Now.AddMinutes(15);
            ClockTargetHours.Text = t.Hour.ToString("D2");
            ClockTargetMinutes.Text = t.Minute.ToString("D2");
            ClockTargetSeconds.Text = "00";
        }

        // Switches the countdown panel between the classic spinners and the smart text box.
        private void ApplyCountdownInputMode()
        {
            if (CountdownClassicPanel == null || CountdownSmartPanel == null) return;

            bool smart = _settings.UseSmartCountdownInput;
            SmartInputMenuItem.Header = smart ? "Switch to classic _input" : "Switch to smart _input";
            CountdownClassicPanel.Visibility = smart ? Visibility.Collapsed : Visibility.Visible;
            CountdownSmartPanel.Visibility = smart ? Visibility.Visible : Visibility.Collapsed;

            if (smart)
            {
                UpdateSmartPreview();
            }
            else
            {
                // Smart Start/Reset may have written _useClockTarget as a side effect of
                // parsing; re-sync it to the visible classic radio so the classic Start/Reset
                // paths read the correct sub-mode. During initial workspace recovery the
                // saved timer is authoritative and its radio has not been restored yet.
                if (!_restoringTimerUi && (!_workspaceWasRestored || _initializationComplete))
                    _useClockTarget = CountdownUntilRadio?.IsChecked == true;
            }
        }

        private void SmartInputMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _settings.UseSmartCountdownInput = !_settings.UseSmartCountdownInput;
            ApplyCountdownInputMode();
            SettingsStore.Save(_settings);
            FocusCountdownInput();
            CheckpointState();
        }

        private void SmartInputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSmartPreview();
            MarkStateDirty();
        }

        private void TimerEditor_TextChanged(object sender, TextChangedEventArgs e)
            => MarkStateDirty();

        private void OptionCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_initializationComplete || _syncingDedicatedSettings)
                return;

            MarkStateDirty();
        }

        // Enter in the smart box starts the countdown (no effect while already running).
        private void SmartInputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter && !_isRunning)
            {
                e.Handled = true;
                StartStopButton_Click(StartStopButton, new RoutedEventArgs());
            }
        }

        // Renders the parsed interpretation (or the error) below the smart text box.
        private void UpdateSmartPreview()
        {
            if (SmartPreview == null || SmartInputBox == null) return;

            var text = SmartInputBox.Text?.Trim() ?? "";
            if (text.Length == 0)
            {
                SmartPreview.Text = "e.g. 5m, 1h30m, 2 pm, tomorrow 9 am";
                SmartPreview.Foreground = (Brush)FindResource("SecondaryTextBrush");
                return;
            }

            var now = DateTime.Now;
            var result = CountdownParser.Parse(text, now);
            if (!result.Success)
            {
                SmartPreview.Text = result.Error;
                SmartPreview.Foreground = (Brush)FindResource("DangerTextBrush");
                return;
            }

            TimeSpan remaining = result.Duration ?? (result.Target!.Value - now);
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

            string span = FormatApproxSpan(remaining);
            string target = (now + remaining).ToString("ddd HH:mm");
            SmartPreview.Text = $"in {span}  →  {target}";
            SmartPreview.Foreground = (Brush)FindResource("SecondaryTextBrush");
        }

        // Compact human duration like "1h 30m" / "2d 3h" / "45s".
        private static string FormatApproxSpan(TimeSpan t)
        {
            if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h";
            if (t.TotalHours >= 1) return $"{t.Hours}h {t.Minutes}m";
            if (t.TotalMinutes >= 1) return $"{t.Minutes}m {t.Seconds}s";
            return $"{t.Seconds}s";
        }

        private bool InitializeCountdownFromEditor(TimerSession timer)
        {
            SaveActiveTimerEditorState();
            DateTime now = DateTime.Now;

            if (_settings.UseSmartCountdownInput)
            {
                var parsed = CountdownParser.Parse(timer.SmartInputText, now);
                if (!parsed.Success)
                {
                    UpdateSmartPreview();
                    UpdateStatus(parsed.Error ?? "Invalid input", Brushes.OrangeRed);
                    return false;
                }

                if (parsed.Target.HasValue)
                {
                    timer.UseClockTarget = true;
                    timer.ClockTarget = parsed.Target.Value;
                    timer.CountdownRemaining = timer.ClockTarget - now;
                }
                else
                {
                    timer.UseClockTarget = false;
                    timer.CountdownDuration = parsed.Duration!.Value;
                    timer.CountdownRemaining = timer.CountdownDuration;
                }
            }
            else if (timer.UseClockTarget)
            {
                int.TryParse(timer.ClockTargetHoursText, out int h);
                int.TryParse(timer.ClockTargetMinutesText, out int m);
                int.TryParse(timer.ClockTargetSecondsText, out int s);
                h = Math.Clamp(h, 0, 23);
                m = Math.Clamp(m, 0, 59);
                s = Math.Clamp(s, 0, 59);

                var target = new DateTime(now.Year, now.Month, now.Day, h, m, s);
                if (target <= now) target = target.AddDays(1);
                timer.ClockTarget = target;
                timer.CountdownRemaining = target - now;
            }
            else
            {
                int.TryParse(timer.CountdownMinutesText, out int mins);
                int.TryParse(timer.CountdownSecondsText, out int secs);
                timer.CountdownDuration = TimeSpan.FromMinutes(Math.Max(0, mins))
                    + TimeSpan.FromSeconds(Math.Max(0, secs));
                timer.CountdownRemaining = timer.CountdownDuration;
            }

            timer.CountdownInitialized = true;
            timer.LastCountdownUpdateUtc = DateTime.UtcNow;
            return true;
        }

        private void StartStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTimer == null || _currentMode == 1) return;
            var timer = _activeTimer;
            if (ProjectTransitionIsTemporarilyBlocked(timer)) return;
            DateTime transitionUtc;

            if (timer.IsRunning)
            {
                // Capture the final countdown fraction at the pause instant rather
                // than waiting for the next 50 ms display tick.
                transitionUtc = DateTime.UtcNow;
                AdvanceRunningCountdowns(
                    transitionUtc, transitionUtc.ToLocalTime(), announceExpiry: false);
                timer.Stopwatch.Stop();
                timer.IsRunning = false;
                timer.LastCountdownUpdateUtc = default;
                UpdateStatus("Paused", Brushes.Orange);
            }
            else
            {
                int runningMode = timer.Mode == 1 ? timer.LastNonClockMode : timer.Mode;
                if (runningMode == 2)
                {
                    if (!timer.CountdownInitialized && !InitializeCountdownFromEditor(timer))
                        return;

                    transitionUtc = DateTime.UtcNow;
                    if (timer.UseClockTarget)
                        timer.ClockTarget = transitionUtc.ToLocalTime() + timer.CountdownRemaining;
                    timer.LastCountdownUpdateUtc = transitionUtc;
                }
                else
                {
                    transitionUtc = DateTime.UtcNow;
                }

                timer.Stopwatch.Start();
                timer.IsRunning = true;
                UpdateStatus("Running", Brushes.LimeGreen);
            }

            SynchronizeProjectTracking(timer, transitionUtc);

            timer.RecBlinkVisible = timer.IsRunning && ShowRecIndicatorCheckBox?.IsChecked == true;
            RecIndicator.Visibility = timer.RecBlinkVisible ? Visibility.Visible : Visibility.Collapsed;
            foreach (var overlay in ActiveOverlayWindows())
            {
                overlay.SetRecIndicatorVisible(timer.RecBlinkVisible);
                overlay.SetRunning(timer.IsRunning);
            }

            UpdateButtonStates();
            UpdateShortcutLabels();
            CheckpointState();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTimer == null || _currentMode == 1) return;
            var timer = _activeTimer;
            if (ProjectTransitionIsTemporarilyBlocked(timer)) return;
            DateTime resetUtc = DateTime.UtcNow;

            timer.Stopwatch.Reset();
            timer.IsRunning = false;
            timer.CountdownInitialized = false;
            timer.LastCountdownUpdateUtc = default;

            if (timer.Mode == 2)
            {
                if (!InitializeCountdownFromEditor(timer))
                    timer.CountdownRemaining = TimeSpan.Zero;
                timer.CountdownInitialized = false;
            }

            SynchronizeProjectTracking(timer, resetUtc);

            timer.LapTimes.Clear();
            timer.LapCount = 0;
            timer.RecBlinkVisible = false;
            LapPlaceholder.Visibility = Visibility.Visible;
            RecIndicator.Visibility = Visibility.Collapsed;
            foreach (var overlay in ActiveOverlayWindows())
            {
                overlay.SetRecIndicatorVisible(false);
                overlay.SetRunning(false);
            }

            UpdateTimeDisplay();
            UpdateButtonStates();
            UpdateShortcutLabels();
            UpdateStatus("Reset", Brushes.Gray);
            CheckpointState();
        }

        private void LapButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTimer == null || _currentMode == 1) return; // No lap for clock mode

            _lapCount++;
            string lapTime = $"Lap {_lapCount}: {GetFormattedTime()}";
            _lapTimes.Insert(0, lapTime);
            LapPlaceholder.Visibility = Visibility.Collapsed;
            CheckpointState();
        }

        private void ToggleOverlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTimer == null) return;
            var timer = _activeTimer;

            if (_combinedOverlayMode)
            {
                if (ActiveOverlayIsVisible())
                {
                    CloseCombinedOverlayWindows();
                    _combinedOverlayVisible = false;
                    UpdateStatus(timer.IsRunning ? "Running (combined overlay hidden)" : "Combined overlay hidden",
                        timer.IsRunning ? Brushes.LimeGreen : Brushes.Gray);
                }
                else
                {
                    _combinedOverlayVisible = true;
                    ShowCombinedOverlay();
                    if (AutoStartCheckBox?.IsChecked == true && !timer.IsRunning && timer.Mode != 1)
                        StartStopButton_Click(sender, e);
                    UpdateStatus($"Combined overlay visible on {ActiveOverlayWindowCount()} screen(s)",
                        Brushes.DeepSkyBlue);
                }

                UpdateShortcutLabels();
                CheckpointState();
                return;
            }

            if (ActiveOverlayIsVisible())
            {
                CloseTimerOverlays(timer);
                timer.OverlayVisible = false;
                UpdateStatus(timer.IsRunning ? "Running (overlay hidden)" : "Overlay hidden",
                    timer.IsRunning ? Brushes.LimeGreen : Brushes.Gray);
            }
            else
            {
                timer.OverlayVisible = true;
                ShowTimerOverlays(timer);

                if (AutoStartCheckBox?.IsChecked == true && !timer.IsRunning && timer.Mode != 1)
                    StartStopButton_Click(sender, e);

                UpdateStatus($"Overlay visible on {ActiveOverlayWindowCount()} screen(s)", Brushes.DeepSkyBlue);
            }

            UpdateShortcutLabels();
            CheckpointState();
        }

        private void ToggleCombinedOverlayMode()
        {
            if (_combinedOverlayMode)
            {
                CloseCombinedOverlayWindows();
                _combinedOverlayMode = false;

                foreach (var timer in _timers.Where(timer => timer.OverlayVisible))
                    ShowTimerOverlays(timer);

                RefreshOverlayActiveStates();
                UpdateStatus("Timers restored to separate overlays", Brushes.DeepSkyBlue);
            }
            else
            {
                if (_timers.Count == 0)
                {
                    string createShortcut = ShortcutText(ShortcutAction.NewTimer);
                    UpdateStatus(createShortcut.Length > 0
                        ? $"No timers — press {createShortcut}"
                        : "No timers — use Timers > New timer", Brushes.Gray);
                    return;
                }

                SeedCombinedPositionFromSeparateOverlays();
                CloseAllSeparateOverlayWindows();
                _combinedOverlayMode = true;
                _combinedOverlayVisible = true;
                ShowCombinedOverlay();
                RefreshOverlayActiveStates();
                string nextShortcut = ShortcutText(ShortcutAction.NextTimer);
                UpdateStatus(nextShortcut.Length > 0
                    ? $"Timers combined — use {nextShortcut} to switch"
                    : "Timers combined — use Timers > Next active timer to switch",
                    Brushes.DeepSkyBlue);
            }

            UpdateButtonStates();
            UpdateShortcutLabels();
            CheckpointState();
        }

        private void SeedCombinedPositionFromSeparateOverlays()
        {
            if (_combinedHasCustomPosition
                || _overlayInstances.Count == 0
                || SelectedContent(PositionSelector, "Top Center") != "Custom")
                return;

            var sources = ActiveOverlayInstances().ToList();
            if (sources.Count == 0)
            {
                sources = _overlayInstances
                    .GroupBy(instance => instance.Screen.DeviceName, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
            }

            foreach (var instance in sources)
            {
                _combinedPositionsByScreen[instance.Screen.DeviceName] =
                    (instance.Window.Left, instance.Window.Top);
            }

            _combinedHasCustomPosition = _combinedPositionsByScreen.Count > 0;
        }

        private void CloseAllSeparateOverlayWindows()
        {
            foreach (var instance in _overlayInstances.ToList())
                instance.Window.Close();
            _overlayInstances.Clear();
        }

        private void ShowCombinedOverlay()
        {
            if (!_combinedOverlayMode || !_combinedOverlayVisible || _activeTimer == null)
                return;

            if (_combinedOverlayInstances.Count == 0)
            {
                foreach (var screen in SelectedScreens())
                    CreateCombinedOverlayForScreen(screen);
            }

            RefreshCombinedOverlayState();
        }

        private void CreateCombinedOverlayForScreen(Screen screen)
        {
            var overlay = new OverlayWindow { Tag = screen };
            overlay.PositionChangedByUser += () => OnCombinedOverlayMoved(overlay);
            overlay.ActivationRequested += () =>
            {
                CancelProjectChooser();
                if (_activeTimer != null)
                    ActivateTimer(_activeTimer, announce: false);
            };
            overlay.ClockToggleRequested += () =>
            {
                if (_activeTimer != null)
                    ToggleClockMode();
            };
            overlay.CloseRequested += () =>
            {
                if (_activeTimer != null)
                    CloseActiveTimer();
            };
            overlay.PauseResumeRequested += () =>
            {
                if (_activeTimer != null)
                    StartStopButton_Click(overlay, new RoutedEventArgs());
            };
            overlay.ResetRequested += () =>
            {
                if (_activeTimer != null)
                    ResetButton_Click(overlay, new RoutedEventArgs());
            };

            ApplyOverlaySettings(overlay);
            overlay.Show();
            overlay.UpdateLayout();
            PositionCombinedOverlay(overlay, screen);

            if (ClickThroughCheckBox?.IsChecked == true)
                overlay.SetClickThrough(true);

            _combinedOverlayInstances.Add(new CombinedOverlayInstance(screen, overlay));
        }

        private void RefreshCombinedOverlayState()
        {
            if (!_combinedOverlayMode)
                return;

            TimerSession? timer = OverlayPresentationPolicy.SelectCombinedTimer(
                _timers,
                _activeTimer);
            if (timer == null)
                return;
            foreach (var instance in _combinedOverlayInstances)
            {
                instance.Window.SetTimerName(timer.Name);
                instance.Window.SetActive(true);
                instance.Window.SetRunning(timer.IsRunning);
                instance.Window.SetPauseResumeEnabled(timer.Mode != 1);
                instance.Window.SetRecIndicatorVisible(timer.RecBlinkVisible);
                instance.Window.UpdateTime(GetFormattedTime(timer));
                instance.Window.UpdateLayout();
                PositionCombinedOverlay(instance.Window, instance.Screen);
            }
        }

        private void CloseCombinedOverlayWindows()
        {
            foreach (var instance in _combinedOverlayInstances.ToList())
                instance.Window.Close();
            _combinedOverlayInstances.Clear();
        }

        private void CaptureCombinedOverlayPositions()
        {
            foreach (var instance in _combinedOverlayInstances)
            {
                _combinedPositionsByScreen[instance.Screen.DeviceName] =
                    (instance.Window.Left, instance.Window.Top);
            }

            if (_combinedOverlayInstances.Count > 0)
                _combinedHasCustomPosition = true;
        }

        private void OnCombinedOverlayMoved(OverlayWindow overlay)
        {
            if (overlay.Tag is not Screen screen)
                return;

            _combinedHasCustomPosition = true;
            _combinedPositionsByScreen[screen.DeviceName] = (overlay.Left, overlay.Top);
            PositionCombinedOverlay(overlay, screen);
            _combinedPositionsByScreen[screen.DeviceName] = (overlay.Left, overlay.Top);
            CheckpointState();
        }

        private IEnumerable<Screen> SelectedScreens()
        {
            var selectedItem = ScreenSelector.SelectedItem as ComboBoxItem;
            if (selectedItem?.Tag is Screen selected)
                return new[] { selected };
            return Screen.AllScreens;
        }

        private void ShowTimerOverlays(TimerSession timer)
        {
            if (_combinedOverlayMode) return;
            if (_overlayInstances.Any(item => ReferenceEquals(item.Session, timer))) return;
            foreach (var screen in SelectedScreens())
                CreateOverlayForScreen(timer, screen);
            timer.OverlayVisible = _overlayInstances.Any(item => ReferenceEquals(item.Session, timer));
        }

        private void CloseTimerOverlays(TimerSession timer)
        {
            var instances = _overlayInstances
                .Where(item => ReferenceEquals(item.Session, timer)).ToList();
            foreach (var instance in instances)
                instance.Window.Close();
            _overlayInstances.RemoveAll(item => ReferenceEquals(item.Session, timer));
        }

        private void CreateOverlayForScreen(TimerSession timer, Screen screen)
        {
            var overlay = new OverlayWindow { Tag = screen };
            overlay.PositionChangedByUser += () => OnOverlayMoved(timer, overlay);
            overlay.ActivationRequested += () =>
            {
                CancelProjectChooser();
                ActivateTimer(timer);
            };
            overlay.ClockToggleRequested += () =>
            {
                ActivateTimer(timer, announce: false, checkpoint: false);
                ToggleClockMode();
            };
            overlay.CloseRequested += () =>
            {
                ActivateTimer(timer, announce: false, checkpoint: false);
                CloseActiveTimer();
            };
            overlay.PauseResumeRequested += () =>
            {
                ActivateTimer(timer, announce: false, checkpoint: false);
                StartStopButton_Click(overlay, new RoutedEventArgs());
            };
            overlay.ResetRequested += () =>
            {
                ActivateTimer(timer, announce: false, checkpoint: false);
                ResetButton_Click(overlay, new RoutedEventArgs());
            };

            ApplyOverlaySettings(overlay);
            overlay.SetTimerName(timer.Name);
            overlay.SetActive(ReferenceEquals(timer, _activeTimer));
            overlay.SetRunning(timer.IsRunning);
            overlay.SetPauseResumeEnabled(timer.Mode != 1);
            overlay.Show();
            overlay.UpdateLayout();
            PositionOverlay(timer, overlay, screen);
            overlay.UpdateTime(GetFormattedTime(timer));
            
            if (ClickThroughCheckBox?.IsChecked == true)
                overlay.SetClickThrough(true);
            
            overlay.SetRecIndicatorVisible(timer.IsRunning
                && ShowRecIndicatorCheckBox?.IsChecked == true);
            
            _overlayInstances.Add(new OverlayInstance(timer, screen, overlay));
        }

        private void ScreenSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ScreenSelector.SelectedItem is ComboBoxItem item && item.Tag is Screen screen)
            {
                _selectedScreen = screen;
            }

            if (!_initializationComplete || _syncingDedicatedSettings)
                return;
            
            RebuildVisibleOverlays();
            CheckpointState();
        }

        private void PositionSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressReposition || !_initializationComplete || _syncingDedicatedSettings)
                return; // selection flipped to "Custom" by a drag, or a settings mirror update

            string selectedPosition = SelectedContent(PositionSelector, "Top Center");
            if (selectedPosition == "Custom")
            {
                if (_combinedOverlayMode)
                    CaptureCombinedOverlayPositions();
                else
                    CaptureVisibleOverlayPositions();
            }
            else
            {
                foreach (var timer in _timers)
                    timer.LastPresetPosition = selectedPosition;
                _combinedHasCustomPosition = false;
            }

            RepositionAllOverlays();
            CheckpointState();
        }

        private void CaptureVisibleOverlayPositions()
        {
            foreach (var instance in _overlayInstances)
            {
                var timer = instance.Session;
                timer.HasCustomPosition = true;
                timer.CustomPositionsByScreen[instance.Screen.DeviceName] =
                    (instance.Window.Left, instance.Window.Top);
                timer.CustomLeft = instance.Window.Left;
                timer.CustomTop = instance.Window.Top;
            }
        }

        // The user dragged an overlay: capture its spot as the custom position and switch the selector to "Custom".
        private void OnOverlayMoved(TimerSession timer, OverlayWindow overlay)
        {
            ActivateTimer(timer, announce: false, checkpoint: false);
            if (SelectedContent(PositionSelector, "Top Center") != "Custom")
                CaptureVisibleOverlayPositions();
            timer.CustomLeft = overlay.Left;
            timer.CustomTop = overlay.Top;
            timer.HasCustomPosition = true;
            if (overlay.Tag is Screen screen)
                timer.CustomPositionsByScreen[screen.DeviceName] = (overlay.Left, overlay.Top);

            _suppressReposition = true;
            SelectByContent(PositionSelector, "Custom");
            _suppressReposition = false;

            if (overlay.Tag is Screen movedScreen)
                PositionOverlay(timer, overlay, movedScreen);
            CheckpointState();
        }

        private void RepositionAllOverlays()
        {
            // Overlays use SizeToContent, so changing format/font/size changes their width.
            // Re-run positioning (after a layout pass) to keep them anchored correctly.
            foreach (var instance in _overlayInstances)
                PositionOverlay(instance.Session, instance.Window, instance.Screen);
            foreach (var instance in _combinedOverlayInstances)
                PositionCombinedOverlay(instance.Window, instance.Screen);
        }

        private void RebuildVisibleOverlays()
        {
            if (!_initializationComplete && !IsLoaded)
                return;

            if (_combinedOverlayMode)
            {
                CloseCombinedOverlayWindows();
                if (_combinedOverlayVisible)
                    ShowCombinedOverlay();
                RefreshOverlayActiveStates();
                UpdateShortcutLabels();
                return;
            }

            if (_overlayInstances.Count == 0) return;
            var visibleTimers = _timers.Where(timer => timer.OverlayVisible).ToList();
            foreach (var instance in _overlayInstances.ToList())
                instance.Window.Close();
            _overlayInstances.Clear();
            foreach (var timer in visibleTimers)
                ShowTimerOverlays(timer);
            RefreshOverlayActiveStates();
            UpdateShortcutLabels();
        }

        private void PositionOverlay(TimerSession timer, OverlayWindow overlay, Screen screen)
        {
            var bounds = screen.Bounds;
            var position = (PositionSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Top Center";

            overlay.UpdateLayout();
            var dpiScale = GetDpiScaleForScreen(screen);
            
            double overlayWidth = overlay.ActualWidth > 0 ? overlay.ActualWidth : 300;
            double overlayHeight = overlay.ActualHeight > 0 ? overlay.ActualHeight : 80;

            double screenLeft = bounds.Left / dpiScale;
            double screenTop = bounds.Top / dpiScale;
            double screenWidth = bounds.Width / dpiScale;
            double screenHeight = bounds.Height / dpiScale;
            double screenRight = screenLeft + screenWidth;
            double screenBottom = screenTop + screenHeight;

            int margin = 10;
            int toolbarClearance = 42;

            // Each all-screen replica keeps its own custom coordinates. Older saved
            // settings contain one coordinate pair, so only apply that pair to the
            // screen that actually contains it; other replicas stay at a safe anchor.
            if (position == "Custom" && timer.HasCustomPosition)
            {
                bool hasPerScreen = timer.CustomPositionsByScreen.TryGetValue(
                    screen.DeviceName, out var custom);
                bool savedPointIsOnScreen = timer.CustomLeft >= screenLeft
                    && timer.CustomLeft < screenRight
                    && timer.CustomTop >= screenTop
                    && timer.CustomTop < screenBottom;
                if (hasPerScreen || savedPointIsOnScreen)
                {
                    double customLeft = hasPerScreen ? custom.Left : timer.CustomLeft;
                    double customTop = hasPerScreen ? custom.Top : timer.CustomTop;
                    overlay.Left = Math.Clamp(customLeft, screenLeft + margin,
                        Math.Max(screenLeft + margin, screenRight - overlayWidth - margin));
                    overlay.Top = Math.Clamp(customTop, screenTop + margin,
                        Math.Max(screenTop + margin,
                            screenBottom - overlayHeight - margin - toolbarClearance));
                    return;
                }

                position = timer.LastPresetPosition;
            }

            (double left, double top) = position switch
            {
                "Top Left" => (screenLeft + margin, screenTop + margin),
                "Top Center" => (screenLeft + (screenWidth - overlayWidth) / 2, screenTop + margin),
                "Top Right" => (screenRight - overlayWidth - margin, screenTop + margin),
                "Bottom Left" => (screenLeft + margin, screenBottom - overlayHeight - margin - toolbarClearance),
                "Bottom Center" => (screenLeft + (screenWidth - overlayWidth) / 2, screenBottom - overlayHeight - margin - toolbarClearance),
                "Bottom Right" => (screenRight - overlayWidth - margin, screenBottom - overlayHeight - margin - toolbarClearance),
                _ => (screenLeft + (screenWidth - overlayWidth) / 2, screenTop + margin)
            };

            double cascade = 24 * (timer.CascadeIndex % 8);
            left += cascade;
            top += position.StartsWith("Bottom", StringComparison.Ordinal) ? -cascade : cascade;
            overlay.Left = Math.Clamp(left, screenLeft + margin, Math.Max(screenLeft + margin, screenRight - overlayWidth - margin));
            overlay.Top = Math.Clamp(top, screenTop + margin, Math.Max(screenTop + margin, screenBottom - overlayHeight - margin));
        }

        private void PositionCombinedOverlay(OverlayWindow overlay, Screen screen)
        {
            var bounds = screen.Bounds;
            overlay.UpdateLayout();
            double dpiScale = GetDpiScaleForScreen(screen);

            double overlayWidth = overlay.ActualWidth > 0 ? overlay.ActualWidth : 300;
            double overlayHeight = overlay.ActualHeight > 0 ? overlay.ActualHeight : 80;
            double screenLeft = bounds.Left / dpiScale;
            double screenTop = bounds.Top / dpiScale;
            double screenWidth = bounds.Width / dpiScale;
            double screenHeight = bounds.Height / dpiScale;
            double screenRight = screenLeft + screenWidth;
            double screenBottom = screenTop + screenHeight;
            const int margin = 10;
            const int toolbarClearance = 42;

            if (_combinedHasCustomPosition
                && _combinedPositionsByScreen.TryGetValue(screen.DeviceName, out var custom))
            {
                overlay.Left = Math.Clamp(custom.Left, screenLeft + margin,
                    Math.Max(screenLeft + margin, screenRight - overlayWidth - margin));
                overlay.Top = Math.Clamp(custom.Top, screenTop + margin,
                    Math.Max(screenTop + margin,
                        screenBottom - overlayHeight - margin - toolbarClearance));
                return;
            }

            string position = SelectedContent(PositionSelector, "Top Center");
            if (position == "Custom")
                position = _activeTimer?.LastPresetPosition ?? "Top Center";

            (double left, double top) = position switch
            {
                "Top Left" => (screenLeft + margin, screenTop + margin),
                "Top Center" => (screenLeft + (screenWidth - overlayWidth) / 2, screenTop + margin),
                "Top Right" => (screenRight - overlayWidth - margin, screenTop + margin),
                "Bottom Left" => (screenLeft + margin, screenBottom - overlayHeight - margin - toolbarClearance),
                "Bottom Center" => (screenLeft + (screenWidth - overlayWidth) / 2,
                    screenBottom - overlayHeight - margin - toolbarClearance),
                "Bottom Right" => (screenRight - overlayWidth - margin,
                    screenBottom - overlayHeight - margin - toolbarClearance),
                _ => (screenLeft + (screenWidth - overlayWidth) / 2, screenTop + margin)
            };

            overlay.Left = Math.Clamp(left, screenLeft + margin,
                Math.Max(screenLeft + margin, screenRight - overlayWidth - margin));
            overlay.Top = Math.Clamp(top, screenTop + margin,
                Math.Max(screenTop + margin, screenBottom - overlayHeight - margin));
        }

        private double GetDpiScaleForScreen(Screen screen)
        {
            try
            {
                var center = new NativePoint
                {
                    X = screen.Bounds.Left + screen.Bounds.Width / 2,
                    Y = screen.Bounds.Top + screen.Bounds.Height / 2
                };
                IntPtr monitor = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);
                if (monitor != IntPtr.Zero
                    && GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0
                    && dpiX > 0)
                    return dpiX / 96.0;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }

            try
            {
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                    return source.CompositionTarget.TransformToDevice.M11;
            }
            catch (InvalidOperationException exception)
            {
                CrashLogger.LogRecoverable(exception, "OverlayDpiFallback");
            }
            return 1.0;
        }

        private void AppearanceChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_initializationComplete || _syncingDedicatedSettings)
                return;

            ApplyAllOverlaySettings();
            CheckpointState();
        }

        private void AppearanceSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TextSizeLabel != null) TextSizeLabel.Text = ((int)TextSizeSlider.Value).ToString();
            if (BorderWidthLabel != null) BorderWidthLabel.Text = ((int)BorderWidthSlider.Value).ToString();
            if (BackgroundOpacityLabel != null) BackgroundOpacityLabel.Text = $"{(int)BackgroundOpacitySlider.Value}%";

            if (!_initializationComplete || _syncingDedicatedSettings)
                return;
            
            ApplyAllOverlaySettings();
            MarkStateDirty();
        }

        private void TimeFormatSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _timeFormat = TimeFormatSelector?.SelectedIndex ?? 0;

            if (!_initializationComplete || _syncingDedicatedSettings)
                return;

            UpdateTimeDisplay();
            RepositionAllOverlays();
            CheckpointState();
        }

        private void ShowRecIndicatorCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_initializationComplete || _syncingDedicatedSettings)
                return;

            _settings.ShowRecIndicator = ShowRecIndicatorCheckBox?.IsChecked == true;
            UpdateRecIndicatorVisibility();
            CheckpointState();
        }

        private void UpdateRecIndicatorVisibility()
        {
            foreach (var timer in _timers)
            {
                timer.RecBlinkVisible = _settings.ShowRecIndicator && timer.IsRunning;
                foreach (var instance in _overlayInstances.Where(item => ReferenceEquals(item.Session, timer)))
                    instance.Window.SetRecIndicatorVisible(timer.RecBlinkVisible);
            }
            if (_combinedOverlayMode && _activeTimer != null)
            {
                foreach (var instance in _combinedOverlayInstances)
                    instance.Window.SetRecIndicatorVisible(_activeTimer.RecBlinkVisible);
            }
            RecIndicator.Visibility = _activeTimer?.RecBlinkVisible == true
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ClickThroughCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_initializationComplete || _syncingDedicatedSettings)
                return;

            _settings.ClickThrough = ClickThroughCheckBox?.IsChecked == true;
            ApplyOverlayInteractionSettings();
            CheckpointState();
        }

        private void ApplyOverlayInteractionSettings()
        {
            foreach (var instance in _overlayInstances)
            {
                instance.Window.SetClickThrough(_settings.ClickThrough);
                instance.Window.SetHideFromCapture(_settings.HideOverlayFromCapture);
            }
            foreach (var instance in _combinedOverlayInstances)
            {
                instance.Window.SetClickThrough(_settings.ClickThrough);
                instance.Window.SetHideFromCapture(_settings.HideOverlayFromCapture);
            }
        }

        #region Light Ring

        private void LightRingCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_initializationComplete || _syncingDedicatedSettings)
                return;

            _settings.LightRingEnabled = LightRingCheckBox?.IsChecked == true;
            if (LightRingCheckBox?.IsChecked == true)
            {
                ShowLightRing();
            }
            else
            {
                HideLightRing();
            }
            CheckpointState();
        }

        private void LightRingSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LightRingBrightnessLabel != null)
                LightRingBrightnessLabel.Text = $"{(int)LightRingBrightnessSlider.Value}%";
            if (LightRingWidthLabel != null)
                LightRingWidthLabel.Text = $"{(int)LightRingWidthSlider.Value}px";

            if (!_initializationComplete || _syncingDedicatedSettings)
                return;

            _settings.LightRingBrightness = LightRingBrightnessSlider.Value;
            _settings.LightRingWidth = LightRingWidthSlider.Value;
            
            UpdateLightRingSettings();
            MarkStateDirty();
        }

        private void LightRingSliderChanged(object sender, RoutedEventArgs e)
        {
            // Overload for checkbox events
            if (!_initializationComplete || _syncingDedicatedSettings)
                return;

            _settings.LightRingHideFromCapture = LightRingHideFromCaptureCheckBox.IsChecked == true;
            UpdateLightRingSettings();
            CheckpointState();
        }

        private void ShowLightRing()
        {
            HideLightRing();

            if (_isExiting || !_settings.LightRingEnabled)
                return;

            var selectedItem = ScreenSelector.SelectedItem as ComboBoxItem;
            
            if (selectedItem?.Tag == null) // "All Screens"
            {
                foreach (var screen in Screen.AllScreens)
                {
                    CreateLightRingForScreen(screen);
                }
            }
            else if (selectedItem.Tag is Screen screen)
            {
                CreateLightRingForScreen(screen);
            }
        }

        private void CreateLightRingForScreen(Screen screen)
        {
            var lightRing = new LightRingWindow();
            double brightness = _settings.LightRingBrightness / 100.0;
            int width = (int)Math.Round(_settings.LightRingWidth);
            bool hideFromCapture = _settings.LightRingHideFromCapture;
            
            lightRing.Show();
            lightRing.PositionOnScreen(screen);
            lightRing.ApplySettings(brightness, width, hideFromCapture);
            
            _lightRingWindows.Add(lightRing);
        }

        private void HideLightRing()
        {
            foreach (var lightRing in _lightRingWindows)
            {
                lightRing.Close();
            }
            _lightRingWindows.Clear();
        }

        private void UpdateLightRingSettings()
        {
            _lightRingWindows.RemoveAll(window => !window.IsLoaded);
            if (_lightRingWindows.Count == 0)
                return;

            double brightness = _settings.LightRingBrightness / 100.0;
            int width = (int)Math.Round(_settings.LightRingWidth);
            bool hideFromCapture = _settings.LightRingHideFromCapture;

            foreach (var lightRing in _lightRingWindows)
            {
                lightRing.ApplySettings(brightness, width, hideFromCapture);
            }
        }

        #endregion

        private void ApplyAllOverlaySettings()
        {
            foreach (var instance in _overlayInstances)
                ApplyOverlaySettings(instance.Window);
            foreach (var instance in _combinedOverlayInstances)
                ApplyOverlaySettings(instance.Window);
            RepositionAllOverlays();
        }

        private void RefreshOverlayBackgroundSurfaces()
        {
            foreach (var instance in _overlayInstances)
                ApplyOverlaySettings(instance.Window);
            foreach (var instance in _combinedOverlayInstances)
                ApplyOverlaySettings(instance.Window);
        }

        private void ApplyOverlaySettings(OverlayWindow overlay)
        {
            if (TextColorSelector == null) return;

            var textColor = GetColorFromSelection(TextColorSelector);
            var borderColor = GetColorFromSelection(BorderColorSelector);
            var fontSize = (int)(TextSizeSlider?.Value ?? 48);
            var borderWidth = (int)(BorderWidthSlider?.Value ?? 2);
            var fontFamily = (FontSelector?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Consolas";
            var bgOpacity = (BackgroundOpacitySlider?.Value ?? 50) / 100.0;

            overlay.ApplySettings(textColor, borderColor, fontSize, borderWidth, fontFamily, bgOpacity);
            overlay.SetHideFromCapture(_settings.HideOverlayFromCapture);
        }

        private Color GetColorFromSelection(System.Windows.Controls.ComboBox comboBox)
        {
            var selection = (comboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "White";
            return selection switch
            {
                "White" => Colors.White,
                "Charcoal" => Color.FromRgb(44, 41, 36),
                "Yellow" => Colors.Yellow,
                "Cyan" => Colors.Cyan,
                "Lime" => Colors.Lime,
                "Orange" => Colors.Orange,
                "Red" => Colors.Red,
                "Magenta" => Colors.Magenta,
                "Black" => Colors.Black,
                "Dark Gray" => Colors.DarkGray,
                "Blue" => Colors.Blue,
                _ => Colors.White
            };
        }

        private void UpdateButtonStates()
        {
            bool hasTimer = _activeTimer != null;
            bool workspaceMutable = !_workspaceLoadRetryPending
                && !_workspacePersistenceDisabled;
            bool isClockMode = hasTimer && _currentMode == 1;
            NewTimerButton.IsEnabled = workspaceMutable;
            RailNewTimerButton.IsEnabled = workspaceMutable;
            NewTimerMenuItem.IsEnabled = workspaceMutable;
            StartStopButton.IsEnabled = workspaceMutable && hasTimer && !isClockMode;
            ResetButton.IsEnabled = workspaceMutable && hasTimer && !isClockMode;
            LapButton.IsEnabled = workspaceMutable && hasTimer && !isClockMode;
            ToggleOverlayButton.IsEnabled = workspaceMutable && hasTimer;
            StopwatchModeRadio.IsEnabled = workspaceMutable && hasTimer;
            ClockModeRadio.IsEnabled = workspaceMutable && hasTimer;
            CountdownModeRadio.IsEnabled = workspaceMutable && hasTimer;
            TimecodeModeRadio.IsEnabled = workspaceMutable && hasTimer;
            NextTimerMenuItem.IsEnabled = workspaceMutable && _timers.Count > 1;
            CloseTimerMenuItem.IsEnabled = workspaceMutable && hasTimer;
            RenameTimerMenuItem.IsEnabled = workspaceMutable && hasTimer;
            ToggleCombinedOverlayMenuItem.IsEnabled = workspaceMutable
                && (_combinedOverlayMode || hasTimer);
            StartStopButton.Style = (Style)FindResource(
                hasTimer && _isRunning ? "StopButton" : "StartButton");
            RecIndicator.Visibility = _activeTimer?.RecBlinkVisible == true
                ? Visibility.Visible : Visibility.Collapsed;
            RefreshOverlayActiveStates();
            if (TimerRailList != null)
            {
                _updatingTimerRail = true;
                try
                {
                    TimerRailList.Items.Refresh();
                    TimerRailList.SelectedItem = _activeTimer;
                }
                finally
                {
                    _updatingTimerRail = false;
                }
            }
            if (CombinedRailStatus != null)
                CombinedRailStatus.Text = _combinedOverlayMode
                    ? "Combined overlay · active timer only"
                    : "Separate overlays";
            if (ActiveWorkspaceTitle != null)
                ActiveWorkspaceTitle.Text = _activeTimer?.DisplayName ?? "No active timer";
        }

        private void UpdateStatus(string text, Brush color)
        {
            StatusText.Text = text;
            StatusIndicator.Fill = ResolveStatusBrush(color);
        }

        private Brush ResolveStatusBrush(Brush requested)
        {
            if (requested is not SolidColorBrush solid)
                return requested;

            if (solid.Color == Colors.LimeGreen)
                return (Brush)FindResource("SuccessBrush");
            if (solid.Color == Colors.Orange)
                return (Brush)FindResource("WarningBrush");
            if (solid.Color == Colors.OrangeRed || solid.Color == Colors.Red)
                return (Brush)FindResource("DangerTextBrush");
            if (solid.Color == Colors.Gray)
                return (Brush)FindResource("MutedStatusBrush");
            if (solid.Color == Colors.DeepSkyBlue)
                return (Brush)FindResource("AccentBrush");

            return requested;
        }

        // "  ·  Win+F5" suffix for button captions; "" if the action is unbound.
        private string ComboSuffix(ShortcutAction action)
        {
            if (_shortcuts.TryGetValue(action, out var s))
            {
                var text = s.Format();
                if (text.Length > 0) return $"  ·  {text}";
            }
            return "";
        }

        private string ShortcutText(ShortcutAction action)
            => _shortcuts.TryGetValue(action, out var shortcut) ? shortcut.Format() : "";

        // Rewrites every caption/hint that mentions a hotkey combo.
        private void UpdateShortcutLabels()
        {
            string startVerb = _isRunning ? "Stop" : "Start";
            StartStopButton.Content = startVerb + ComboSuffix(ShortcutAction.StartStop);
            ResetButton.Content = "Reset" + ComboSuffix(ShortcutAction.Reset);
            ToggleOverlayButton.Content = (ActiveOverlayIsVisible() ? "Hide overlay" : "Show overlay")
                + ComboSuffix(ShortcutAction.ToggleOverlay);
            LapButton.Content = "Add lap" + ComboSuffix(ShortcutAction.Lap);

            string s(ShortcutAction a) => (_shortcuts.TryGetValue(a, out var v) ? v : new Shortcut(0, 0)).Format();
            NewTimerMenuItem.InputGestureText = s(ShortcutAction.NewTimer);
            NextTimerMenuItem.InputGestureText = s(ShortcutAction.NextTimer);
            CloseTimerMenuItem.InputGestureText = s(ShortcutAction.CloseTimer);
            RenameTimerMenuItem.InputGestureText = s(ShortcutAction.RenameTimer);
            ProjectDashboardMenuItem.InputGestureText = s(ShortcutAction.OpenDashboard);
            ToggleCombinedOverlayMenuItem.InputGestureText = s(ShortcutAction.ToggleCombinedOverlay);
            ToggleCombinedOverlayMenuItem.Header = _combinedOverlayMode
                ? "_Separate overlays"
                : "_Combine overlays";
            ShortcutHintText.Text =
                $"{s(ShortcutAction.NewTimer)} New  {s(ShortcutAction.NextTimer)} Next  " +
                $"{s(ShortcutAction.CloseTimer)} Close  {s(ShortcutAction.RenameTimer)} Project  " +
                $"{s(ShortcutAction.OpenDashboard)} Dashboard";

            var lapCombo = (_shortcuts.TryGetValue(ShortcutAction.Lap, out var lv) ? lv : new Shortcut(0, 0)).Format();
            var newTimerCombo = s(ShortcutAction.NewTimer);
            NewTimerButton.ToolTip = newTimerCombo.Length > 0
                ? $"Create a new timer and choose its project ({newTimerCombo})"
                : "Create a new timer and choose its project";
            LapPlaceholder.Text = _activeTimer == null
                ? (newTimerCombo.Length > 0
                    ? $"No timers — press {newTimerCombo} to create one"
                    : "No timers — use Timers > New timer")
                : lapCombo.Length > 0
                ? $"Press {lapCombo} or click Lap to record split times"
                : "Click Lap to record split times";
        }

        // Pushes persisted settings into the UI controls. Their change handlers fire as a
        // side effect (labels update, theme applies); overlays don't exist yet so those calls are no-ops.
        private void ApplySettingsToUi()
        {
            _timeFormat = _settings.TimeFormat;
            SelectByContent(ThemeModeSelector, _settings.ThemeMode);
            PopulatePanelBackgroundChoices(_settings.PanelBackgroundId);
            PanelBackgroundStrengthSlider.Value = _settings.PanelBackgroundStrength;
            SelectByContent(TextColorSelector, _settings.TextColor);
            SelectByContent(BorderColorSelector, _settings.BorderColor);
            SelectByContent(FontSelector, _settings.FontFamily);
            if (_settings.TimeFormat >= 0 && _settings.TimeFormat < TimeFormatSelector.Items.Count)
                TimeFormatSelector.SelectedIndex = _settings.TimeFormat;

            TextSizeSlider.Value = _settings.TextSize;
            BorderWidthSlider.Value = _settings.BorderWidth;
            BackgroundOpacitySlider.Value = _settings.BackgroundOpacity;
            HideOverlayFromCaptureCheckBox.IsChecked = _settings.HideOverlayFromCapture;

            // Layout (screen before light ring, which reads the screen selection).
            // The legacy coordinates in settings.json seed only a brand-new
            // workspace; restored sessions own their individual positions.
            if (!_workspaceWasRestored)
            {
                _hasCustomPosition = _settings.HasCustomPosition;
                _customLeft = _settings.CustomLeft;
                _customTop = _settings.CustomTop;
                SelectByContent(PositionSelector, _settings.Position);
            }
            else
            {
                _suppressReposition = true;
                try
                {
                    SelectByContent(PositionSelector, _settings.Position);
                }
                finally
                {
                    _suppressReposition = false;
                }
            }
            ScreenSelector.SelectedIndex = SettingsChangePolicy.ResolveScreenComboIndex(
                _settings.ScreenIndex,
                Screen.AllScreens.Length);

            LightRingBrightnessSlider.Value = _settings.LightRingBrightness;
            LightRingWidthSlider.Value = _settings.LightRingWidth;
            LightRingHideFromCaptureCheckBox.IsChecked = _settings.LightRingHideFromCapture;

            AutoStartCheckBox.IsChecked = _settings.AutoStart;
            ShowRecIndicatorCheckBox.IsChecked = _settings.ShowRecIndicator;
            ClickThroughCheckBox.IsChecked = _settings.ClickThrough;
            BlinkColonCheckBox.IsChecked = _settings.BlinkColon;
            StartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;

            // Last-used mode is a compatibility fallback for users upgrading
            // from settings-only releases. A restored workspace has its own mode.
            if (!_workspaceWasRestored)
            {
                switch (_settings.Mode)
                {
                    case 1: ClockModeRadio.IsChecked = true; break;
                    case 2: CountdownModeRadio.IsChecked = true; break;
                    case 3: TimecodeModeRadio.IsChecked = true; break;
                    default: StopwatchModeRadio.IsChecked = true; break;
                }
            }

            // Light ring last, after screen selection is settled.
            LightRingCheckBox.IsChecked = _settings.LightRingEnabled;

            // Countdown input mode (classic vs smart text).
            ApplyCountdownInputMode();
        }

        // Snapshots the current UI control values back into _settings (everything except shortcuts).
        private void PopulateSettingsFromUi()
        {
            _settings.ThemeMode = AppThemeCatalog.Normalize(
                SelectedContent(ThemeModeSelector, AppThemeCatalog.Midnight));
            if (PanelBackgroundSelector.SelectedItem is AppBackgroundChoice
                { IsAvailable: true } backgroundChoice)
            {
                _settings.PanelBackgroundId = backgroundChoice.Id;
            }
            _settings.PanelBackgroundStrength = PanelBackgroundStrengthSlider.Value;
            _settings.TextColor = SelectedContent(TextColorSelector, "White");
            _settings.BorderColor = SelectedContent(BorderColorSelector, "Black");
            _settings.FontFamily = SelectedContent(FontSelector, "Consolas");
            _settings.TimeFormat = TimeFormatSelector.SelectedIndex;
            _settings.TextSize = TextSizeSlider.Value;
            _settings.BorderWidth = BorderWidthSlider.Value;
            _settings.BackgroundOpacity = BackgroundOpacitySlider.Value;
            _settings.HideOverlayFromCapture = HideOverlayFromCaptureCheckBox.IsChecked == true;

            _settings.Position = SelectedContent(PositionSelector, "Top Center");
            _settings.ScreenIndex = ScreenSelector.SelectedIndex;
            _settings.HasCustomPosition = _hasCustomPosition;
            _settings.CustomLeft = _customLeft;
            _settings.CustomTop = _customTop;

            _settings.LightRingEnabled = LightRingCheckBox.IsChecked == true;
            _settings.LightRingBrightness = LightRingBrightnessSlider.Value;
            _settings.LightRingWidth = LightRingWidthSlider.Value;
            _settings.LightRingHideFromCapture = LightRingHideFromCaptureCheckBox.IsChecked == true;

            _settings.AutoStart = AutoStartCheckBox.IsChecked == true;
            _settings.ShowRecIndicator = ShowRecIndicatorCheckBox.IsChecked == true;
            _settings.ClickThrough = ClickThroughCheckBox.IsChecked == true;
            _settings.BlinkColon = BlinkColonCheckBox.IsChecked == true;
            _settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;

            _settings.Mode = _currentMode;
        }

        private static void SelectByContent(System.Windows.Controls.ComboBox cb, string content)
        {
            foreach (var obj in cb.Items)
                if (obj is ComboBoxItem item && item.Content?.ToString() == content)
                {
                    cb.SelectedItem = item;
                    return;
                }
        }

        private static string SelectedContent(System.Windows.Controls.ComboBox cb, string fallback)
            => (cb.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? fallback;

        // Opens the modal shortcut editor; commits the result if the user saves.
        private void OpenShortcuts_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ShortcutsWindow(_shortcuts) { Owner = this };
            if (dlg.ShowDialog() == true)
                CommitPendingShortcuts(dlg.Result);
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsWindow is { IsLoaded: true })
            {
                _settingsWindow.Activate();
                return;
            }

            _settingsWindow = new SettingsWindow(_settings) { Owner = this };
            _settingsWindow.SettingsChanged += QueueDedicatedSettingsChange;
            _settingsWindow.SettingsInteractionStarted += SettingsInteractionStarted;
            _settingsWindow.SettingsInteractionCompleted += SettingsInteractionCompleted;
            _settingsWindow.ShowOverlayRequested += SettingsShowOverlayRequested;
            _settingsWindow.Closed += SettingsWindowClosed;
            _settingsWindow.Show();
        }

        private void QueueDedicatedSettingsChange(SettingsChangeKind change)
        {
            if (_isExiting || change == SettingsChangeKind.None)
                return;

            SyncDedicatedSettingsControls(change);
            _pendingDedicatedSettingsChanges |= change;
            MarkStateDirty();

            if (!_dedicatedSettingsApplyTimer.IsEnabled)
                _dedicatedSettingsApplyTimer.Start();
            if (!_settingsInteractionInProgress)
                QueueSettingsCompletion();
        }

        private void SyncDedicatedSettingsControls(SettingsChangeKind change)
        {
            _syncingDedicatedSettings = true;
            try
            {
                if ((change & SettingsChangeKind.Theme) != 0)
                    SelectByContent(ThemeModeSelector, _settings.ThemeMode);

                if ((change & SettingsChangeKind.OverlayScreen) != 0)
                {
                    int screenIndex = SettingsChangePolicy.ResolveScreenComboIndex(
                        _settings.ScreenIndex,
                        Screen.AllScreens.Length);
                    ScreenSelector.SelectedIndex = screenIndex;
                    _selectedScreen = (ScreenSelector.SelectedItem as ComboBoxItem)?.Tag as Screen;
                }

                if ((change & SettingsChangeKind.OverlayPosition) != 0)
                    SelectByContent(PositionSelector, _settings.Position);

                if ((change & (SettingsChangeKind.OverlayAppearance
                               | SettingsChangeKind.OverlayGeometry)) != 0)
                {
                    SelectByContent(TextColorSelector, _settings.TextColor);
                    SelectByContent(BorderColorSelector, _settings.BorderColor);
                    SelectByContent(FontSelector, _settings.FontFamily);
                    TimeFormatSelector.SelectedIndex = _settings.TimeFormat;
                    TextSizeSlider.Value = _settings.TextSize;
                    BorderWidthSlider.Value = _settings.BorderWidth;
                    BackgroundOpacitySlider.Value = _settings.BackgroundOpacity;
                    _timeFormat = _settings.TimeFormat;
                }

                if ((change & SettingsChangeKind.BackgroundSelection) != 0)
                    PopulatePanelBackgroundChoices(_settings.PanelBackgroundId);

                if ((change & SettingsChangeKind.BackgroundStrength) != 0)
                    PanelBackgroundStrengthSlider.Value = _settings.PanelBackgroundStrength;

                if ((change & SettingsChangeKind.OverlayInteraction) != 0)
                {
                    ClickThroughCheckBox.IsChecked = _settings.ClickThrough;
                    HideOverlayFromCaptureCheckBox.IsChecked = _settings.HideOverlayFromCapture;
                }

                if ((change & (SettingsChangeKind.LightRingVisibility
                               | SettingsChangeKind.LightRingAppearance)) != 0)
                {
                    LightRingCheckBox.IsChecked = _settings.LightRingEnabled;
                    LightRingBrightnessSlider.Value = _settings.LightRingBrightness;
                    LightRingWidthSlider.Value = _settings.LightRingWidth;
                    LightRingHideFromCaptureCheckBox.IsChecked = _settings.LightRingHideFromCapture;
                }

                if ((change & SettingsChangeKind.Behavior) != 0)
                {
                    AutoStartCheckBox.IsChecked = _settings.AutoStart;
                    ShowRecIndicatorCheckBox.IsChecked = _settings.ShowRecIndicator;
                    BlinkColonCheckBox.IsChecked = _settings.BlinkColon;
                }

                if ((change & SettingsChangeKind.Startup) != 0)
                    StartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;
            }
            finally
            {
                _syncingDedicatedSettings = false;
            }
        }

        private void ApplyPendingDedicatedSettings(bool showStartupWarning = true)
        {
            _dedicatedSettingsApplyTimer.Stop();
            if (_isExiting || _applyingDedicatedSettings)
                return;

            SettingsChangeKind changes = _pendingDedicatedSettingsChanges;
            if (_settingsInteractionInProgress)
                changes &= ~SettingsChangeKind.BackgroundStrength;

            if (changes == SettingsChangeKind.None)
                return;

            _pendingDedicatedSettingsChanges &= ~changes;
            _applyingDedicatedSettings = true;
            try
            {
                bool themeChanged = SettingsChangePolicy.RequiresThemeApply(changes);
                bool backgroundChanged = SettingsChangePolicy.RequiresBackgroundApply(changes);
                bool screenChanged = (changes & SettingsChangeKind.OverlayScreen) != 0;
                bool geometryChanged = (changes & SettingsChangeKind.OverlayGeometry) != 0;
                bool rebuildLightRing = SettingsChangePolicy.RequiresLightRingRebuild(changes);

                if (themeChanged)
                {
                    AppThemeManager.Apply(_settings.ThemeMode);
                    if (!_settings.ThemeMode.Equals(
                            AppThemeManager.CurrentTheme,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        _settings.ThemeMode = AppThemeManager.CurrentTheme;
                        SyncDedicatedSettingsControls(SettingsChangeKind.Theme);
                        _settingsWindow?.ReloadFromSettings();
                    }
                }

                if (backgroundChanged)
                {
                    _backgroundApplyTimer.Stop();
                    AppBackgroundManager.Apply(_settings, out string? warning);
                    _backgroundWarning = warning;
                    if (warning != null)
                    {
                        PopulatePanelBackgroundChoices(_settings.PanelBackgroundId);
                        _settingsWindow?.ReloadFromSettings();
                    }
                    UpdatePanelBackgroundPreview();
                    UpdateStatus(
                        warning ?? "Appearance updated",
                        warning == null ? (Brush)FindResource("AccentBrush") : Brushes.OrangeRed);
                }

                if (screenChanged)
                {
                    RebuildVisibleOverlays();
                }
                else if ((changes & (SettingsChangeKind.Theme
                                     | SettingsChangeKind.BackgroundSelection
                                     | SettingsChangeKind.BackgroundStrength
                                     | SettingsChangeKind.OverlayAppearance
                                     | SettingsChangeKind.OverlayGeometry)) != 0)
                {
                    RefreshOverlayBackgroundSurfaces();
                    if (geometryChanged)
                        RepositionAllOverlays();
                }

                if ((changes & SettingsChangeKind.OverlayPosition) != 0)
                    RepositionAllOverlays();

                if ((changes & SettingsChangeKind.OverlayInteraction) != 0)
                    ApplyOverlayInteractionSettings();

                if (rebuildLightRing)
                {
                    if (_settings.LightRingEnabled)
                        ShowLightRing();
                    else
                        HideLightRing();
                }
                else if ((changes & SettingsChangeKind.LightRingAppearance) != 0)
                {
                    UpdateLightRingSettings();
                }

                if ((changes & SettingsChangeKind.Behavior) != 0)
                {
                    ApplyCountdownInputMode();
                    UpdateRecIndicatorVisibility();
                }

                if ((changes & SettingsChangeKind.Startup) != 0)
                    ApplyStartWithWindowsSetting(
                        _settings.StartWithWindows,
                        showWarning: showStartupWarning);

                if ((changes & (SettingsChangeKind.Theme
                                | SettingsChangeKind.BackgroundSelection
                                | SettingsChangeKind.BackgroundStrength)) != 0)
                {
                    _projectDashboardWindow?.RefreshFromHistory();
                }

                if ((changes & (SettingsChangeKind.Theme
                                | SettingsChangeKind.OverlayScreen
                                | SettingsChangeKind.Behavior)) != 0)
                {
                    UpdateButtonStates();
                }

                _settingsWindow?.SchedulePreviewFromAppliedSettings();
            }
            finally
            {
                _applyingDedicatedSettings = false;
            }

            if (_pendingDedicatedSettingsChanges != SettingsChangeKind.None
                && !_settingsInteractionInProgress)
            {
                _dedicatedSettingsApplyTimer.Start();
            }
        }

        private void SettingsInteractionStarted()
        {
            _settingsInteractionInProgress = true;
        }

        private void SettingsInteractionCompleted()
        {
            _settingsInteractionInProgress = false;
            QueueSettingsCompletion();
        }

        private void QueueSettingsCompletion()
        {
            if (_settingsCompletionQueued || _isExiting)
                return;

            _settingsCompletionQueued = true;
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    _settingsCompletionQueued = false;
                    if (_isExiting)
                        return;

                    ApplyPendingDedicatedSettings();
                    if (_backgroundApplyTimer.IsEnabled)
                        ApplyPendingPanelBackgroundStrength();
                    CheckpointStateNow();
                }),
                DispatcherPriority.ContextIdle);
        }

        private void SettingsShowOverlayRequested()
            => ToggleOverlayButton_Click(this, new RoutedEventArgs());

        private void SettingsWindowClosed(object? sender, EventArgs e)
        {
            if (sender is SettingsWindow window)
            {
                window.SettingsChanged -= QueueDedicatedSettingsChange;
                window.SettingsInteractionStarted -= SettingsInteractionStarted;
                window.SettingsInteractionCompleted -= SettingsInteractionCompleted;
                window.ShowOverlayRequested -= SettingsShowOverlayRequested;
                window.Closed -= SettingsWindowClosed;
            }

            _settingsWindow = null;
            _settingsInteractionInProgress = false;
            QueueSettingsCompletion();
        }

        // Validates a candidate set, warns+confirms on conflicts, then registers, saves, and refreshes.
        private void CommitPendingShortcuts(Dictionary<ShortcutAction, Shortcut> candidate)
        {
            var problems = new List<string>();

            // 1. In-app duplicates (ignore unbound).
            var seen = new Dictionary<(uint, uint), ShortcutAction>();
            var duplicateCombos = new HashSet<(uint, uint)>();
            foreach (var (action, s) in candidate)
            {
                if (s.VirtualKey == 0) continue;
                var key = (s.Modifiers, s.VirtualKey);
                if (seen.TryGetValue(key, out var other))
                {
                    problems.Add($"{action} and {other} share {s.Format()}.");
                    duplicateCombos.Add(key);
                }
                else
                    seen[key] = action;
            }

            // 2. OS-level rejection (combo held by another running app).
            var failures = ApplyShortcuts(candidate);
            foreach (var action in failures)
            {
                var s = candidate[action];
                if (duplicateCombos.Contains((s.Modifiers, s.VirtualKey)))
                    continue; // already reported as an in-app duplicate above
                problems.Add($"{action} ({s.Format()}) is already in use by another app.");
            }

            if (problems.Count > 0)
            {
                var msg = "Some shortcuts have conflicts:\n\n" + string.Join("\n", problems);
                var confirmation = new ConfirmationDialogWindow(
                    "Shortcut conflicts",
                    "Keep conflicting shortcuts?",
                    msg,
                    "Keep assignments")
                {
                    Owner = this
                };
                if (confirmation.ShowDialog() != true)
                {
                    // Revert registration to the last committed set; leave pending values in the boxes.
                    ApplyShortcuts(_shortcuts);
                    return;
                }
            }

            // Commit.
            _shortcuts = new Dictionary<ShortcutAction, Shortcut>(candidate);
            _settings.Shortcuts = new Dictionary<ShortcutAction, Shortcut>(candidate);
            PopulateSettingsFromUi(); // keep appearance/layout current in the same file
            SettingsStore.Save(_settings);
            UpdateShortcutLabels();
            UpdateStatus("Shortcuts updated", Brushes.DeepSkyBlue);
            CheckpointState();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _settingsWindow?.Close();
            // The project chooser is modeless so floating timers stay clickable.
            // Never strand that topmost window when hiding or exiting the controller.
            CancelProjectChooser();

            // Save both global preferences and the complete timer workspace before
            // either hiding to the tray or performing a real application exit.
            CheckpointStateNow();

            // The controller's close button hides it to the notification area. The
            // stopwatch, overlays, light ring, timer, and global shortcuts stay active.
            if (!_isExiting)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            // Unregister hotkeys
            var helper = new WindowInteropHelper(this);
            foreach (ShortcutAction action in Enum.GetValues<ShortcutAction>())
                UnregisterHotKey(helper.Handle, (int)action);

            foreach (var instance in _overlayInstances.ToList()) instance.Window.Close();
            foreach (var instance in _combinedOverlayInstances.ToList()) instance.Window.Close();
            HideLightRing();
            _projectDashboardWindow?.Close();
            _projectDashboardWindow = null;
            _timer.Stop();
            _blinkTimer.Stop();
            _stateSaveTimer.Stop();
            _backgroundApplyTimer.Stop();
            _dedicatedSettingsApplyTimer.Stop();

            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(HwndHook);
                _hwndSource = null;
            }

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Icon?.Dispose();
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            _trayMenu?.Dispose();
            _trayMenu = null;
        }
    }
}
