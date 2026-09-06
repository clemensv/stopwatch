# README screenshot renderer

Run on Windows with the .NET 10 SDK, from the repository root:

```powershell
dotnet run --project tools/ReadmeScreenshots/ReadmeScreenshots.csproj -c Release
```

Creates eleven compact images plus a size/hash manifest in `docs/screenshots/`. The entire gallery has a 1.25 MB size budget. PNG keeps the flat interface captures crisp; high-quality JPEG keeps the three textured Pixel Deck captures small. No additional image libraries are required. Pixel Deck panels use the bundled Autumn Patchwork background at 30% strength; other panel backgrounds stay unchanged.

`floating-clock-transparency.png` compares real clock surfaces at 100%, 50%, and 0% background opacity over a sample checkerboard. The renderer checks that text, border, and toolbar do not inherit that opacity.

All examples use fictional projects and synthetic activity ending September 4, 2026. The dashboard calculates the real totals, timelines and heatmap from 846 varied in-memory records across six projects. The sample dataset is never saved into the application. In the sample controller, the overlay-toggle shortcut is unbound (a supported customization); the other shortcuts use their defaults.

Safety: this helper initializes WPF resources but does not run `App`, construct `ControllerWindow`, show native windows, load or save user stores, register shortcuts, or invoke record-editing actions. Controller screenshots use the production XAML with event handlers removed in memory and a sample timer rail/laps. Settings and analytics use their compiled production controls. This is documentation rendering, not an end-to-end interaction test.

The original screenshot files at the repository root are retained. The README gallery uses the smaller current images instead. Inspect every generated image after a layout change; the nonblank assertions do not detect clipping or typography defects.
