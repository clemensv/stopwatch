# Developer Guide

This document covers building, developing, and contributing to Stopwatch Overlay.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows 10 or 11
- Visual Studio 2022, VS Code, or JetBrains Rider

## Project Structure

```
StopwatchOverlay/
├── App.xaml / App.xaml.cs          # Application entry point and global styles
├── AppBackgroundManager.cs         # Preset/custom tiled backgrounds and safe image import
├── ControllerWindow.xaml / .cs     # Main control panel UI and logic
├── OverlayWindow.xaml / .cs        # Transparent always-on-top overlay
├── TimerSession.cs                 # Independent runtime state for one logical timer
├── TimerSessionManager.cs          # Timer collection and active-timer selection
├── TimerWorkspaceStore.cs          # Versioned, atomic workspace persistence and recovery
├── ProjectTimeStore.cs             # Crash-safe project work-session history and aggregation
├── ProjectDashboardWindow.xaml/.cs # Date-filtered project totals, charts, timeline, and sessions
├── ProjectRecordsWindow.xaml/.cs   # Filterable detailed history with add/edit entry points
├── ProjectRecordEditorWindow.xaml/.cs # Local-time manual record editor and validation
├── TimerNameWindow.xaml / .cs      # Select, create, or clear the active timer's project
└── StopwatchOverlay.csproj         # Project configuration
```

## Architecture

| Component | Responsibility |
|---|---|
| **ControllerWindow** | Main control panel — ticks all timer sessions, routes commands to the active timer, manages separate and combined overlay replicas, project-history transitions, global hotkeys, settings, and lap views |
| **TimerSession** | Runtime state for one logical timer, including its stopwatch/countdown state, mode, name, lap times, visibility, and custom position |
| **TimerSessionManager** | Owns the ordered timer collection and its single logical active timer; creates, activates, cycles, and closes sessions without WPF dependencies |
| **TimerWorkspaceStore** | Captures and restores the versioned timer workspace and writes crash-safe atomic checkpoints under `%APPDATA%\StopwatchOverlay` |
| **ProjectTimeStore** | Owns named-project work intervals, UTC persistence, crash-safe primary/backup files, startup reconciliation, and date-range aggregation |
| **ProjectDashboardWindow** | Read-only visualization window with an all-project/single-project filter, Today, Last 7 days, Last 30 days, and All time summaries, charts, daily timelines, and session details |
| **ProjectRecordsWindow** | Detailed project-history page. It renders immutable history views, keeps active records read-only, and delegates add/edit/delete requests back to the controller |
| **ProjectRecordEditorWindow** | Modal local-time form for creating or correcting closed records; validates project names, positive duration, future endpoints, and invalid daylight-saving wall times before returning UTC values |
| **ProjectRecordDeleteWindow** | Themed, owner-bound confirmation that identifies a closed record before permanent deletion |
| **TimerNameWindow** | Compact dialog used before new-timer creation and by Win+F10 to select or add a project; edit mode can also clear the active timer's project |
| **OverlayWindow** | Transparent, always-on-top display with outlined text rendering, active-state indication, drag selection, and animated hover controls. Supports click-through mode |
| **App.xaml** | Global WPF styles (ModernButton, StartButton, StopButton) |
| **AppBackgroundManager** | Stable preset catalog, validated managed custom-image imports, tiled theme composition, and floating-clock surface brushes |

### Key Design Decisions

- **WPF + WinForms hybrid**: WPF for UI rendering, `System.Windows.Forms.Screen` for reliable multi-monitor enumeration.
- **Logical timers vs. windows**: A `TimerSession` is one independent timer. Separate `OverlayWindow` instances are screen-specific views of a session, while combined mode uses one shared logical view (replicated per selected screen) that dynamically displays the active session.
- **Chooser-first timer creation**: Every user-created timer opens the project chooser before `TimerSessionManager.Create()`. The neutral placeholder creates an unnamed timer, the adjacent `+` action adds a project, and Cancel consumes no timer number or state. Restored timers are never reprompted.
- **Project-switch boundary**: Win+F10 uses one UTC transition instant. A non-zero timer closes the old project record, resets elapsed/lap/countdown session state, and starts the new project while preserving running/paused state. A zero timer only changes assignment; same canonical project selections never reset or split history.
- **Single active command target**: Several sessions may run simultaneously, but `TimerSessionManager.Active` is the only session affected by Win+F5 through Win+F10. Win+F3 cycles sessions in creation order in both separate and combined views. Clicking a separate overlay activates its owning session; the shared overlay already represents the active session.
- **Presentation-only combining**: Win+F12 changes only how timers are displayed. It never changes their running state or project intervals. Individual overlay visibility and positions remain intact so separating restores the prior layout; the shared overlay has independent visibility and per-screen coordinates.
- **Persistent workspace state**: All sessions are checkpointed, including running/paused state, elapsed or remaining time, names, laps, modes, overlay visibility and positions, session order, active selection, and combined-overlay presentation. Global appearance and shortcut preferences continue to use `AppSettings`.
- **Independent theme and background**: Theme tokens and the optional tiled image are persisted separately. Theme resources are applied first; `AppBackgroundManager` then composites the chosen preset or managed custom image over the clean theme background and applies the same pattern to floating-clock chrome.
- **Managed custom backgrounds**: Imported JPG, JPEG, PNG, and BMP files are validated and copied atomically into `%LOCALAPPDATA%\StopwatchOverlay\Backgrounds`. Settings store only a generated ID, display name, and safe leaf filename, so the original image can be moved or deleted without breaking the app.
- **Recovery semantics**: Running timers account for UTC time elapsed while the process or PC was off; paused timers restore their exact saved value. State-changing actions are checkpointed immediately, while pending text, slider, and checkbox edits are flushed atomically by a one-second save timer. No idle writes occur when state is unchanged. Checkpoints live under `%APPDATA%\StopwatchOverlay`; a hard crash can lose at most roughly one second of the latest UI edits while retaining the previous valid checkpoint.
- **Project intervals**: A named running timer owns one open UTC work interval. Pause, stop, close, or clearing its name closes that interval. Renaming a running timer to another project closes the old interval and opens the new interval at the same instant. Each timer is independent, so intervals may overlap.
- **Project-history recovery**: Project history is stored in `%APPDATA%\StopwatchOverlay\project-history.json` with a crash-recovery backup. Workspace and history writes share one timestamp; a partial failure retries the exact same logical snapshot instead of moving a project boundary forward. If an older workspace backup and newer history are recovered together, a persisted guard prevents backward reconciliation until explicit timer actions make their open states agree. Startup reconciliation otherwise preserves valid open intervals for restored named/running timers, closes stale intervals, and creates missing ones. Dashboard calendar grouping is shown in local time.
- **Win32 interop**: `RegisterHotKey` for system-wide hotkeys (Win+F2 through Win+F12), `SetWindowLong` for click-through, no-activate, and tool-window styles.
- **Text outline rendering**: Four offset `TextBlock` layers beneath the main text create a border/outline effect that stays readable on any background.
- **Non-resizing hover toolbar**: Overlay actions live in a WPF `Popup`, allowing close, pause/resume, and reset controls to animate below the timer without changing its measured size or anchored position. Click-through closes and disables this mouse UI.
- **Framework-dependent deployment**: The standard published binary relies on an installed .NET 10 Desktop Runtime, keeping the download much smaller than the optional self-contained build.

## Building

```bash
# Restore dependencies and build (Debug)
dotnet build

# Build in Release mode
dotnet build -c Release

# Run the application
dotnet run --project StopwatchOverlay
```

## Publishing

The project is configured for framework-dependent single-file publishing:

```bash
# Publish a small single-file executable
dotnet publish StopwatchOverlay/StopwatchOverlay.csproj -c Release

# Output location:
# StopwatchOverlay/bin/Release/net10.0-windows/win-x64/publish/StopwatchOverlay.exe
```

The resulting executable requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0/runtime) to be installed. Publish with `--self-contained true` when a larger portable build is preferred.

## Modes

The application supports four display modes:

1. **Stopwatch** — Elapsed time counter with start/stop/reset
2. **Clock** — Real-time clock display with optional colon blink
3. **Countdown** — Countdown timer (continues into negative). A Duration / Until-clock-time toggle (`_useClockTarget`) switches between a fixed duration and counting down to a wall-clock target time (HH:MM:SS); the until-time variant resolves an absolute target on start (rolling to tomorrow if already past) and recomputes the remaining time from `DateTime.Now` each tick
4. **Timecode** — Frame-accurate timecode display (HH:MM:SS:FF)

## Hotkeys

| Key | Action |
|---|---|
| Win+F2 | Create a new timer and make it active |
| Win+F3 | Cycle to the next active timer |
| Win+F4 | Close the active timer |
| Win+F5 | Start / stop the active timer |
| Win+F6 | Reset the active timer |
| Win+F7 | Show / hide the active timer's overlay, or the shared overlay in combined mode |
| Win+F8 | Record a lap for the active timer |
| Win+F9 | Toggle the active timer between its current mode and Clock |
| Win+F10 | Select, create, change, or clear the active timer's project |
| Win+F11 | Open the project time dashboard |
| Win+F12 | Combine all open timers into one shared overlay / restore separate overlays |

The F5–F9 commands never broadcast to every running timer. Win+F12 is a presentation toggle, while Win+F3 remains active-timer selection in either view. An overlay click changes the logical active timer in separate view; click-through mode intentionally disables overlay mouse selection, dragging, and hover actions, while registered hotkeys remain available. All shortcut assignments, including dashboard and combine/separate, can be changed or cleared in the shortcut editor.

## CI/CD

The GitHub Actions workflow (`.github/workflows/release.yml`) automatically:

1. Triggers on version tag pushes (`v*`)
2. Builds a framework-dependent single-file executable
3. Packages it into a ZIP file
4. Creates a GitHub Release with the ZIP attached

To create a release:

```bash
git tag v1.0.0
git push origin v1.0.0
```

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/my-feature`)
3. Commit your changes
4. Push to your branch and open a Pull Request

## License

[MIT](LICENSE)
