# Implement Independent Floating Clock Themes from Figma

## Mission

Implement a separate theme system for the floating clock overlay.

The application must support two independent visual systems:

1. Application panel theme
2. Floating clock theme

Example:

| Panel Theme | Floating Clock Theme |
|---|---|
| Pixel Deck Night | Acanthus Dark |
| Daylight | Midnight |
| Acanthus | Pixel Deck Night |
| Midnight | Acanthus Dark |

Changing the floating clock theme must not change the panel theme.
Changing the panel theme must not overwrite the floating clock theme unless the user selected "Follow Application Theme".

## Scope

Only modify:

- floating clock rendering
- floating clock styles
- overlay resources
- overlay theme selection
- overlay settings

Do not redesign:

- Controller
- Settings layout
- Records
- Analytics
- Dialogs

Do not change the visual appearance of:

- Midnight
- Daylight
- Pixel Deck Night
- Pixel Deck Day
- Acanthus panel theme

## Figma source

Use this read-only file:

https://www.figma.com/design/q1GhB2cTVZiVWhbajEGT4K/StopWatch?node-id=13-6&t=zBkEwoSPfi2Eitki1-1

Only use:

Theme Study / Acanthus Dark / Floating Overlay

as the visual source for the new dark overlay themes.

Use these concepts:

- Overlay / Acanthus Dark / 01 Elegant Olive
- Overlay / Acanthus Dark / 02 Gold Crest
- Overlay / Acanthus Dark / 03 Minimal Botanical

Do not copy styling from other Figma pages.

## New settings architecture

Separate:

ApplicationTheme

from:

OverlayTheme

ApplicationTheme controls:

- Controller
- Settings
- Records
- Analytics
- Dialogs

OverlayTheme controls:

- floating clock
- overlay toolbar
- overlay border
- overlay text
- overlay ornaments

Possible OverlayTheme values:

- Follow Application Theme
- Midnight
- Daylight
- Pixel Deck Night
- Pixel Deck Day
- Acanthus Light
- Acanthus Dark Elegant Olive
- Acanthus Dark Gold Crest
- Acanthus Dark Minimal Botanical

## Settings

Add a separate floating clock theme selector.

Example:

Application Theme:
Pixel Deck Night

Floating Clock Theme:
Acanthus Dark Elegant Olive

These must work independently.

Existing users should migrate safely:

Old behavior:
Overlay follows application theme.

New behavior:
OverlayTheme = Follow Application Theme

Do not reset:

- overlay position
- opacity
- font
- size
- border width
- colors
- light ring
- custom settings

## Overlay resources

Create a separate overlay theme resource system.

Example:

Themes/Overlay/

Containing:

- MidnightOverlay.xaml
- DaylightOverlay.xaml
- PixelDeckNightOverlay.xaml
- PixelDeckDayOverlay.xaml
- AcanthusLightOverlay.xaml
- AcanthusDarkElegantOliveOverlay.xaml
- AcanthusDarkGoldCrestOverlay.xaml
- AcanthusDarkMinimalBotanicalOverlay.xaml

Overlay resources control:

- surface
- border
- text
- project name
- hover toolbar
- icons
- hover states
- ornaments
- shadows

Do not put overlay-only resources into panel theme dictionaries.

## Acanthus Dark concepts

Implement:

### Elegant Olive

- charcoal surface
- olive undertone
- antique gold border
- restrained corner ornaments
- professional everyday style

### Gold Crest

- richer dark surface
- stronger classical identity
- small crest accent
- more gold detail
- premium feeling

### Minimal Botanical

- minimal dark surface
- subtle sage accents
- smallest ornament footprint
- strongest readability

## Overlay structure

Preserve:

Timer value

Project name below timer

Hover:

Close
Pause/Resume
Reset

Do not add:

- project header above timer
- permanent status labels
- mode labels
- shortcut labels
- multi-row timer lists

## Opacity behavior

Background opacity affects only:

- overlay background surface

Never fade:

- timer digits
- project name
- border
- ornament
- hover toolbar
- toolbar icons

## Preserve behavior

Do not break:

- always-on-top
- dragging
- click-through
- multi-monitor support
- combined mode
- REC
- light ring
- capture exclusion
- custom position

## Validation

Test combinations:

All panel themes:

- Midnight
- Daylight
- Pixel Deck Night
- Pixel Deck Day
- Acanthus

with all overlay themes.

Verify:

- independent switching
- persistence after restart
- no panel theme changes when overlay changes
- no overlay changes when panel changes

## Tests

Add tests for:

- overlay theme catalog
- independent persistence
- migration from old settings
- overlay switching
- resource loading

Run:

dotnet build

dotnet test

## Completion report

Report:

1. files changed
2. Figma frames used
3. overlay themes created
4. settings changes
5. migration behavior
6. confirmation that other themes were untouched
7. build results
8. test results
