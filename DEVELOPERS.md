# Developer Guide

This document covers building, developing, and contributing to Stopwatch Overlay.

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- Windows 10 or 11
- Visual Studio 2022, VS Code, or JetBrains Rider

## Project Structure

```
StopwatchOverlay/
├── App.xaml / App.xaml.cs          # Application entry point and global styles
├── ControllerWindow.xaml / .cs     # Main control panel UI and logic
├── OverlayWindow.xaml / .cs        # Transparent always-on-top overlay
└── StopwatchOverlay.csproj         # Project configuration
```

## Architecture

| Component | Responsibility |
|---|---|
| **ControllerWindow** | Main control panel — timer logic, global hotkeys, screen selection, appearance settings, presets, lap tracking |
| **OverlayWindow** | Transparent, always-on-top display with outlined text rendering. Supports click-through mode and drag repositioning |
| **App.xaml** | Global WPF styles (ModernButton, StartButton, StopButton) |

### Key Design Decisions

- **WPF + WinForms hybrid**: WPF for UI rendering, `System.Windows.Forms.Screen` for reliable multi-monitor enumeration.
- **Win32 interop**: `RegisterHotKey` for system-wide hotkeys (F9–F12), `SetWindowLong` for click-through and tool-window styles.
- **Text outline rendering**: Four offset `TextBlock` layers beneath the main text create a border/outline effect that stays readable on any background.
- **Framework-dependent deployment**: The published binary relies on the .NET Desktop Runtime that ships with Windows 11, keeping the download small (~1 MB vs ~150 MB self-contained).

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
# StopwatchOverlay/bin/Release/net8.0-windows/win-x64/publish/StopwatchOverlay.exe
```

The resulting executable requires the [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0/runtime) to be installed. Windows 11 24H2+ includes the .NET 8 runtime by default.

## Modes

The application supports four display modes:

1. **Stopwatch** — Elapsed time counter with start/stop/reset
2. **Clock** — Real-time clock display with optional colon blink
3. **Countdown** — Configurable countdown timer (continues into negative)
4. **Timecode** — Frame-accurate timecode display (HH:MM:SS:FF)

## Hotkeys

| Key | Action |
|---|---|
| F9 | Start / Stop |
| F10 | Reset |
| F11 | Show / Hide overlay |
| F12 | Record lap time |

## Presets

User presets are stored as JSON files in:

```
%APPDATA%\StopwatchOverlay\Presets\
```

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
