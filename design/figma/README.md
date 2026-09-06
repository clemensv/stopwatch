# Stopwatch Overlay — Pixel Deck Figma Pack

This folder contains the editable vector source for the pixelized Stopwatch Overlay theme. The visual language follows the nautical pixel UI in `sample-pages2` while preserving the clock application's current structure and behavior.

## Boards

| Board | Size | Purpose |
| --- | ---: | --- |
| `pixel-deck-foundations.svg` | 1280 × 960 | Night/Day palettes, typography, spacing, geometry, and interaction rules |
| `pixel-deck-components.svg` | 1280 × 960 | Buttons, fields, toggles, tabs, badges, progress, timer card, and data marks |
| `pixel-deck-settings.svg` | 1180 × 900 | Controller window, settings, appearance controls, lap table, and status footer |
| `pixel-deck-dashboard.svg` | 1280 × 900 | Filters, KPI cards, project bars, daily totals, heatmap, timeline, and project records |
| `pixel-deck-floating-clock.svg` | 1040 × 600 | Compact, click-through, and hover-toolbar overlay states |

Open `preview.html` to review all boards together. Run `node generate-pixel-deck.mjs` to regenerate every SVG from the checked-in source.

## Figma import

Import the five SVG files onto one Figma page, arrange them from left to right in the order above, and preserve their native dimensions. SVG import keeps the artwork as editable vector shapes and text.

Recommended frame names:

1. `00 — Foundations`
2. `01 — Components`
3. `02 — Controller + Settings / Night`
4. `03 — Project Reports / Night`
5. `04 — Floating Clock States / Night`

Use Inter for interface text and Cascadia Mono for labels, timers, shortcuts, statuses, and numeric data. If Figma substitutes a font during import, select all text on the board and reapply the corresponding family.

## Theme contract

- Base grid: 4 px
- Touch target: 48 px minimum
- Control border: 2 px
- Control radius: 3–5 px
- Panel geometry: stepped 6–10 px corners
- Control shadow: 3 px right / 4 px down
- Panel shadow: 6 px right / 7 px down
- Default theme: Night Deck
- Primary action: gold fill, dark text
- Secondary action: raised purple-black surface, gold border
- Danger action: coral fill, near-black text
- Status communication: color plus a visible text label

## Main Night Deck colors

| Token | Value |
| --- | --- |
| Sky | `#1D3F55` |
| Ocean | `#0D5260` |
| Deck | `#754426` |
| Surface | `#232334` |
| Raised | `#2C2B3E` |
| Inset | `#191A28` |
| Ink | `#FFF8E8` |
| Muted ink | `#C7C3C3` |
| Border | `#D7A24B` |
| Primary | `#37AEB0` |
| Gold | `#FFD46C` |
| Success | `#70D6A2` |
| Danger | `#FF8291` |

## Source alignment

- `ControllerWindow.xaml`: mode tabs, timer display, actions, overlay settings, appearance, light ring, application/report access, lap table, and shortcut footer.
- `ProjectDashboardWindow.xaml`: day/range controls, KPI summary, project allocation, daily totals, activity heatmap, timeline, and collapsed inline project records.
- `OverlayWindow.xaml`: compact timer, REC status, click-through state, and hover actions for pause, reset, and close.
