# Clock Countdown Mode — Design

**Date:** 2026-06-13
**Status:** Approved (pending spec review)
**Branch:** feature/clock-countdown

## Summary

Add a fifth timer mode, **Clock Countdown**, that counts down to a wall-clock target
time (HH:MM:SS) instead of a fixed duration. Example: at 20:00:50 the user sets a
target of 20:03:00 and the overlay shows a 2m10s countdown that reaches zero exactly
at 20:03:00.

This complements the existing **Countdown** mode (mode 2), which counts a fixed
duration. Clock Countdown is mode 4.

## Goals

- Count down to an absolute wall-clock time the user picks.
- Prefill a sensible default (now + 15 minutes, seconds = 00) and let the user edit it.
- Reuse the existing countdown rendering, overlay, hotkey, and REC plumbing.
- Be drift-free and correct across PC sleep.

## Non-Goals

- No date picker — target is a time-of-day only.
- No recurring/scheduled alarms.
- No new overlay appearance options.

## Mode Model

The controller already keys all behavior off `_currentMode`
(0=Stopwatch, 1=Clock, 2=Countdown, 3=Timecode). Add:

- `_currentMode == 4` → **Clock Countdown**.

New radio button `ClockCountdownModeRadio` ("Clock Countdown") joins the existing
mode row, wired to the same `ModeRadio_Checked` handler.

`ModeRadio_Checked` updated to:

- Set `_currentMode = 4` when `ClockCountdownModeRadio.IsChecked == true`.
- Show `ClockCountdownPanel` only when mode 4 (and keep `CountdownPanel` logic for mode 2).
- Add `"Clock Countdown"` to the `modeNames` array used for the status message.

## State

| Field                 | Type                  | Purpose                                                                    |
| --------------------- | --------------------- | -------------------------------------------------------------------------- |
| `_clockTarget`        | `DateTime`            | Resolved absolute target instant. Set on Start.                            |
| `_countdownRemaining` | `TimeSpan` (existing) | Reused as the render value for mode 4, so formatting shares mode 2's code. |

No new format state — Clock Countdown uses the existing `_timeFormat` (0–3).

## UI

New panel `ClockCountdownPanel`, a sibling of `CountdownPanel`, placed in the same
grid region and visible only in mode 4. Layout mirrors the Countdown duration row:

```
Until:  [HH] : [MM] : [SS]
```

- Three `TextBox`es: `ClockTargetHours`, `ClockTargetMinutes`, `ClockTargetSeconds`
  (width 40, centered), separated by colon `TextBlock`s.
- "Until:" label uses the same `GrayTextBrushKey` styling as the Countdown panel labels.

**Prefill:** When mode 4 is selected (in `ModeRadio_Checked`), compute
`var t = DateTime.Now.AddMinutes(15);` and set:

- `ClockTargetHours.Text = t.Hour` (`D2`)
- `ClockTargetMinutes.Text = t.Minute` (`D2`)
- `ClockTargetSeconds.Text = "00"`

The user may edit any of the three fields, including seconds.

## Start

In `StartStopButton_Click`, add a mode-4 branch parallel to the existing mode-2 branch,
taken only when starting (not when stopping):

1. Parse `ClockTargetHours` / `ClockTargetMinutes` / `ClockTargetSeconds` with
   `int.TryParse`. On parse failure default the field to 0. Clamp: hours 0–23,
   minutes 0–59, seconds 0–59.
2. Build today's target:
   `var now = DateTime.Now;`
   `var target = new DateTime(now.Year, now.Month, now.Day, h, m, s);`
3. **Roll to tomorrow if already passed:** if `target <= now` then
   `target = target.AddDays(1);`
4. Store `_clockTarget = target;`
5. `_stopwatch.Start();` set `_isRunning = true;` (keeps REC indicator and running-state
   plumbing identical to other modes), update button content/style/status as the existing
   code does.

Stop (the `_isRunning == true` branch) is unchanged and shared with all modes.

## Tick

In `Timer_Tick`, add a mode-4 case alongside the mode-2 case:

```
if (_currentMode == 4 && _isRunning)
{
    _countdownRemaining = _clockTarget - DateTime.Now;
    if (_countdownRemaining <= TimeSpan.Zero && _countdownRemaining > TimeSpan.FromMilliseconds(-100))
    {
        UpdateStatus("Time's up! (counting negative)", Brushes.Red);
    }
}
```

Recomputing from `DateTime.Now` each tick (rather than decrementing) makes the countdown
drift-free and correct after the machine sleeps/resumes.

**Zero behavior:** identical to Countdown — the value goes negative and keeps counting
with a `-` sign; no freeze. Matches the existing mode-2 overrun behavior the user
confirmed.

## Render

`GetFormattedTime()` gains `case 4:` whose body is identical to `case 2:` (negative-sign
handling + the four `_timeFormat` branches). Since both read `_countdownRemaining`,
combine the labels — `case 2:` and `case 4:` share one block (C# allows stacked case
labels with no statements between them; true fall-through is illegal because the block
ends in `return`).

## Reset

In `ResetButton_Click`, add a mode-4 branch parallel to the existing mode-2 branch:

- Re-prefill `ClockCountdownPanel` to `DateTime.Now.AddMinutes(15)` (HH/MM, SS = "00").
- Clear `_countdownRemaining = TimeSpan.Zero;`
- Stop and reset running/REC state as the shared reset code already does.

## Buttons

`UpdateButtonStates`: Start/Reset/Lap are **enabled** in mode 4 (only Clock mode 1
disables them). The existing `isClockMode = _currentMode == 1` guard already yields the
correct result, so no change is required — verify it still reads `== 1` and not a range.

Lap is allowed in mode 4 (only mode 1 returns early in `LapButton_Click`).

## Hotkeys / Overlay / Cleanup

No changes. Hotkeys already route to the button handlers, overlays render from
`GetFormattedTime()`, and `Window_Closing` cleanup is mode-agnostic.

## Edge Cases

| Case                                       | Behavior                                                                                     |
| ------------------------------------------ | -------------------------------------------------------------------------------------------- |
| Target == now exactly at Start             | `target <= now` true → rolls to tomorrow (~24h). Acceptable; user picked the current minute. |
| Non-numeric field text                     | `int.TryParse` fails → field treated as 0, then clamped.                                     |
| Out-of-range (e.g. 99)                     | Clamped to valid range before building target.                                               |
| PC sleeps mid-countdown                    | On resume, next tick recomputes `target - now`; display jumps to correct remaining.          |
| Overnight target (e.g. 06:00 set at 23:00) | Rolls to tomorrow → counts ~7h. Intended use.                                                |

## Testing

No automated test suite exists (per CLAUDE.md). Manual verification:

1. Select Clock Countdown → panel shows now+15min, SS=00.
2. Set target 2 min ahead, Start → counts down, reaches 00:00:00 at the wall-clock target.
3. Let it pass zero → goes negative with `-`, "Time's up!" status flashes.
4. Set a target earlier than now → Start → rolls to tomorrow (~large remaining).
5. Switch formats (HH:MM:SS.t / HH:MM:SS / MM:SS.t / MM:SS) → render matches Countdown.
6. Overlay on → shows same string as controller. REC indicator + hotkeys work.
7. Reset → panel re-prefills to now+15min, display clears.

## Files Touched

- `StopwatchOverlay/ControllerWindow.xaml` — add radio button + `ClockCountdownPanel`.
- `StopwatchOverlay/ControllerWindow.xaml.cs` — mode 4 in `ModeRadio_Checked`,
  `Timer_Tick`, `GetFormattedTime`, `StartStopButton_Click`, `ResetButton_Click`;
  new `_clockTarget` field; prefill helper.
