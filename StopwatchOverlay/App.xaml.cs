using System;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace StopwatchOverlay
{
    public partial class App : Application
    {
        private const string SingleInstanceMutexName = @"Local\StopwatchOverlay.SingleInstance";
        private const string ShowExistingEventName = @"Local\StopwatchOverlay.ShowExisting";

        private Mutex? _singleInstanceMutex;
        private EventWaitHandle? _showExistingEvent;
        private RegisteredWaitHandle? _showExistingRegistration;
        private bool _ownsSingleInstanceMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            try
            {
                _singleInstanceMutex = new Mutex(
                    initiallyOwned: true,
                    SingleInstanceMutexName,
                    out _ownsSingleInstanceMutex);
            }
            catch (Exception exception) when (
                IsExpectedSingleInstanceBoundaryFailure(exception))
            {
                CrashLogger.LogRecoverable(exception, "SingleInstanceMutexCreate");
                // If the OS cannot create a named mutex, continue normally. The
                // workspace store still protects each individual write atomically.
                _singleInstanceMutex = null;
            }

            if (_singleInstanceMutex != null && !_ownsSingleInstanceMutex)
            {
                SignalExistingInstance();
                Shutdown();
                return;
            }

            StartExistingInstanceListener();
            SessionEnding += App_SessionEnding;
            base.OnStartup(e);
        }

        private void StartExistingInstanceListener()
        {
            try
            {
                _showExistingEvent = new EventWaitHandle(
                    initialState: false,
                    EventResetMode.AutoReset,
                    ShowExistingEventName);
                _showExistingRegistration = ThreadPool.RegisterWaitForSingleObject(
                    _showExistingEvent,
                    (_, timedOut) =>
                    {
                        if (timedOut || Dispatcher.HasShutdownStarted)
                            return;

                        try
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (MainWindow is ControllerWindow controller)
                                    controller.ShowController();
                            }));
                        }
                        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted)
                        {
                            // Shutdown won the race with the single-instance signal.
                        }
                    },
                    state: null,
                    millisecondsTimeOutInterval: Timeout.Infinite,
                    executeOnlyOnce: false);
            }
            catch (Exception exception) when (
                IsExpectedSingleInstanceBoundaryFailure(exception))
            {
                CrashLogger.LogRecoverable(exception, "SingleInstanceListenerStart");
                _showExistingRegistration = null;
                _showExistingEvent?.Dispose();
                _showExistingEvent = null;
            }
        }

        private static void SignalExistingInstance()
        {
            try
            {
                using var showEvent = EventWaitHandle.OpenExisting(ShowExistingEventName);
                showEvent.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // The first process may still be between mutex and event creation.
            }
            catch (Exception exception) when (
                IsExpectedSingleInstanceBoundaryFailure(exception))
            {
                CrashLogger.LogRecoverable(exception, "SingleInstanceSignal");
            }
        }

        private void App_SessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            try
            {
                if (MainWindow is ControllerWindow controller)
                    controller.PrepareForSystemExit();
            }
            catch (Exception exception)
            {
                CrashLogger.LogRecoverable(exception, "SessionEndingCheckpoint");
                // Shutdown must continue even if the final best-effort save fails.
            }
        }

        private void App_DispatcherUnhandledException(
            object sender,
            DispatcherUnhandledExceptionEventArgs e)
        {
            CrashLogger.LogFatal(
                e.Exception,
                "Application.DispatcherUnhandledException",
                isTerminating: true);
            TryCheckpoint();
            // Deliberately leave e.Handled false: persistence must not hide a
            // real application failure.
        }

        private void CurrentDomain_UnhandledException(
            object sender,
            UnhandledExceptionEventArgs e)
            => CrashLogger.LogUnhandledObject(
                e.ExceptionObject,
                "AppDomain.CurrentDomain.UnhandledException",
                e.IsTerminating);

        private void TaskScheduler_UnobservedTaskException(
            object? sender,
            UnobservedTaskExceptionEventArgs e)
        {
            CrashLogger.LogFatal(
                e.Exception,
                "TaskScheduler.UnobservedTaskException",
                isTerminating: false);
            // Do not call SetObserved: logging must not change exception policy.
        }

        protected override void OnExit(ExitEventArgs e)
        {
            TryCheckpoint();
            DispatcherUnhandledException -= App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;

            _showExistingRegistration?.Unregister(null);
            _showExistingRegistration = null;
            _showExistingEvent?.Dispose();
            _showExistingEvent = null;

            if (_ownsSingleInstanceMutex)
            {
                try { _singleInstanceMutex?.ReleaseMutex(); }
                catch { }
            }
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
            base.OnExit(e);
        }

        private void TryCheckpoint()
        {
            try
            {
                if (MainWindow is ControllerWindow controller)
                    controller.CheckpointStateNow();
            }
            catch
            {
                // The periodic atomic checkpoint remains available for recovery.
            }
        }

        private static bool IsExpectedSingleInstanceBoundaryFailure(
            Exception exception)
            => exception is IOException
                or UnauthorizedAccessException
                or SecurityException
                or WaitHandleCannotBeOpenedException
                or PlatformNotSupportedException;
    }
}
