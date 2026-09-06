# Project logo

`project-logo-source.png` is the canonical project artwork supplied by the project owner. Keep its composition, palette, transparency, and pixel-art treatment unchanged.

Run `generate-project-logo-assets.ps1` to create:

- `png/project-logo-16.png` through `png/project-logo-256.png` for inspection and reuse;
- `png/project-logo-400.png`, which is byte-identical to the canonical source;
- `../../StopwatchOverlay/project-logo-24.png` for the controller header;
- `../../StopwatchOverlay/project-logo.ico`, containing 16, 24, 32, 48, 64, 128, and 256 px Windows frames.

The smaller exports use nearest-neighbor scaling so the supplied pixel artwork remains crisp. The app uses the dedicated 24 px PNG in XAML because WPF otherwise selects the first ICO frame, while the executable and tray use the multi-frame ICO.
