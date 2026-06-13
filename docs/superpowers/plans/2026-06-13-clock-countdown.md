# Clock Countdown Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a fifth timer mode, "Clock Countdown", that counts down to a wall-clock target time (HH:MM:SS) instead of a fixed duration.

**Architecture:** Mode 4 in the existing `_currentMode` switch in `ControllerWindow`. A new `ClockCountdownPanel` (HH:MM:SS target inputs) prefilled to now+15min. On Start, the target is resolved to an absolute `DateTime` (rolling to tomorrow if already past). Each 50ms tick recomputes `_clockTarget - DateTime.Now` into the existing `_countdownRemaining`, so rendering, overlay, REC, and hotkey plumbing are all reused from Countdown mode.

**Tech Stack:** WPF (.NET 10, net10.0-windows), C#, no MVVM. No automated test suite — verification is `dotnet build` + manual run (per CLAUDE.md).

**Spec:** `docs/superpowers/specs/2026-06-13-clock-countdown-design.md`

---

## File Structure

- `StopwatchOverlay/ControllerWindow.xaml` — add the 5th radio button and the `ClockCountdownPanel`.
- `StopwatchOverlay/ControllerWindow.xaml.cs` — `_clockTarget` field, prefill helper, and mode-4 branches in `ModeRadio_Checked`, `StartStopButton_Click`, `Timer_Tick`, `GetFormattedTime`, `ResetButton_Click`.

Two files only. Each task ends with a `dotnet build` (and where relevant a manual check) before commit.

---

## Task 1: Add the mode radio button and target-time panel (XAML)

**Files:**
- Modify: `StopwatchOverlay/ControllerWindow.xaml` (mode row ~line 44-46; after CountdownPanel ~line 59)

- [ ] **Step 1: Give the Timecode radio a right margin and add the Clock Countdown radio**

In the Mode Selection `StackPanel`, replace the Timecode radio block:

```xml
            <RadioButton x:Name="TimecodeModeRadio" Content="Timecode" 
                         GroupName="Mode"
                         Checked="ModeRadio_Checked"/>
```

with:

```xml
            <RadioButton x:Name="TimecodeModeRadio" Content="Timecode" 
                         GroupName="Mode"
                         Checked="ModeRadio_Checked" Margin="0,0,15,0"/>
            <RadioButton x:Name="ClockCountdownModeRadio" Content="Clock Countdown" 
                         GroupName="Mode"
                         Checked="ModeRadio_Checked"/>
```

- [ ] **Step 2: Add the ClockCountdownPanel after the CountdownPanel**

Immediately after the closing `</StackPanel>` of `CountdownPanel` (the one ending ~line 59), add:

```xml
        <!-- Clock Countdown Target (only visible in clock-countdown mode) -->
        <StackPanel x:Name="ClockCountdownPanel" Grid.Row="2" Orientation="Horizontal" 
                    HorizontalAlignment="Center" Margin="0,0,0,10" Visibility="Collapsed">
            <TextBlock Text="Until: " Foreground="{DynamicResource {x:Static SystemColors.GrayTextBrushKey}}" VerticalAlignment="Center"/>
            <TextBox x:Name="ClockTargetHours" Width="40" Text="00" 
                     TextAlignment="Center" VerticalContentAlignment="Center"/>
            <TextBlock Text=" : " Foreground="{DynamicResource {x:Static SystemColors.GrayTextBrushKey}}" VerticalAlignment="Center"/>
            <TextBox x:Name="ClockTargetMinutes" Width="40" Text="00" 
                     TextAlignment="Center" VerticalContentAlignment="Center"/>
            <TextBlock Text=" : " Foreground="{DynamicResource {x:Static SystemColors.GrayTextBrushKey}}" VerticalAlignment="Center"/>
            <TextBox x:Name="ClockTargetSeconds" Width="40" Text="00" 
                     TextAlignment="Center" VerticalContentAlignment="Center"/>
        </StackPanel>
```

Both `CountdownPanel` and `ClockCountdownPanel` sit in `Grid.Row="2"`, each `Visibility="Collapsed"` by default — only one is shown at a time by `ModeRadio_Checked`.

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: Build succeeded (XAML names compile; handlers `ModeRadio_Checked` already exist). The new `ClockCountdownPanel`/`ClockTarget*` fields are referenced nowhere in code yet — that is fine, they generate fields.

- [ ] **Step 4: Commit**

```bash
git add StopwatchOverlay/ControllerWindow.xaml
git commit -m "feat: add Clock Countdown mode radio and target-time panel"
```

---

## Task 2: Field + prefill helper + mode wiring

**Files:**
- Modify: `StopwatchOverlay/ControllerWindow.xaml.cs` (field block ~line 48-49; `ModeRadio_Checked` ~line 254-269)

- [ ] **Step 1: Add the `_clockTarget` field**

After the existing line:

```csharp
        private TimeSpan _countdownRemaining;
```

add:

```csharp
        private DateTime _clockTarget;
```

- [ ] **Step 2: Add a prefill helper**

Add this method to `ControllerWindow` (e.g. directly below `ModeRadio_Checked`):

```csharp
        private void PrefillClockTarget()
        {
            if (ClockTargetHours == null) return;
            var t = DateTime.Now.AddMinutes(15);
            ClockTargetHours.Text = t.Hour.ToString("D2");
            ClockTargetMinutes.Text = t.Minute.ToString("D2");
            ClockTargetSeconds.Text = "00";
        }
```

- [ ] **Step 3: Wire mode 4 into `ModeRadio_Checked`**

Replace the body of `ModeRadio_Checked` with:

```csharp
        private void ModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (CountdownPanel == null || ClockCountdownPanel == null) return;

            if (StopwatchModeRadio?.IsChecked == true) _currentMode = 0;
            else if (ClockModeRadio?.IsChecked == true) _currentMode = 1;
            else if (CountdownModeRadio?.IsChecked == true) _currentMode = 2;
            else if (TimecodeModeRadio?.IsChecked == true) _currentMode = 3;
            else if (ClockCountdownModeRadio?.IsChecked == true) _currentMode = 4;

            CountdownPanel.Visibility = _currentMode == 2 ? Visibility.Visible : Visibility.Collapsed;
            ClockCountdownPanel.Visibility = _currentMode == 4 ? Visibility.Visible : Visibility.Collapsed;

            if (_currentMode == 4) PrefillClockTarget();

            UpdateButtonStates();
            UpdateTimeDisplay();

            string[] modeNames = { "Stopwatch", "Clock", "Countdown", "Timecode", "Clock Countdown" };
            UpdateStatus($"{modeNames[_currentMode]} Mode", Brushes.DeepSkyBlue);
        }
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 5: Manual check**

Run: `dotnet run --project StopwatchOverlay`
Expected: Selecting "Clock Countdown" shows the "Until: HH : MM : SS" panel prefilled to ~15 minutes from now, seconds `00`. Status bar reads "Clock Countdown Mode". Switching to another mode hides the panel.

- [ ] **Step 6: Commit**

```bash
git add StopwatchOverlay/ControllerWindow.xaml.cs
git commit -m "feat: wire Clock Countdown mode selection and prefill"
```

---

## Task 3: Render mode 4 (GetFormattedTime)

**Files:**
- Modify: `StopwatchOverlay/ControllerWindow.xaml.cs` (`GetFormattedTime` ~line 222-234)

- [ ] **Step 1: Share the Countdown render block with mode 4**

In `GetFormattedTime()`, change the Countdown case label from:

```csharp
                case 2: // Countdown
```

to:

```csharp
                case 2: // Countdown
                case 4: // Clock Countdown (same render — reads _countdownRemaining)
```

The rest of the block (negative-sign handling + the four `_timeFormat` branches reading `_countdownRemaining`) is unchanged and now serves both modes.

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add StopwatchOverlay/ControllerWindow.xaml.cs
git commit -m "feat: render Clock Countdown using shared countdown formatting"
```

---

## Task 4: Start resolves the absolute target

**Files:**
- Modify: `StopwatchOverlay/ControllerWindow.xaml.cs` (`StartStopButton_Click` start branch ~line 291-301)

- [ ] **Step 1: Add the mode-4 resolve branch**

In `StartStopButton_Click`, inside the `else` (start) branch, after the existing mode-2 block:

```csharp
                if (_currentMode == 2) // Countdown
                {
                    if (!_isRunning)
                    {
                        int.TryParse(CountdownMinutes.Text, out int mins);
                        int.TryParse(CountdownSeconds.Text, out int secs);
                        _countdownDuration = TimeSpan.FromMinutes(mins) + TimeSpan.FromSeconds(secs);
                        _countdownRemaining = _countdownDuration;
                    }
                }
```

add:

```csharp
                if (_currentMode == 4) // Clock Countdown
                {
                    int.TryParse(ClockTargetHours.Text, out int h);
                    int.TryParse(ClockTargetMinutes.Text, out int m);
                    int.TryParse(ClockTargetSeconds.Text, out int s);
                    h = Math.Clamp(h, 0, 23);
                    m = Math.Clamp(m, 0, 59);
                    s = Math.Clamp(s, 0, 59);

                    var now = DateTime.Now;
                    var target = new DateTime(now.Year, now.Month, now.Day, h, m, s);
                    if (target <= now) target = target.AddDays(1); // roll to tomorrow
                    _clockTarget = target;
                    _countdownRemaining = _clockTarget - now;
                }
```

(Setting `_countdownRemaining` here makes the display correct on the very first frame before the next tick.)

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add StopwatchOverlay/ControllerWindow.xaml.cs
git commit -m "feat: resolve Clock Countdown target on start"
```

---

## Task 5: Tick recomputes remaining from wall clock

**Files:**
- Modify: `StopwatchOverlay/ControllerWindow.xaml.cs` (`Timer_Tick` ~line 156-168)

- [ ] **Step 1: Add the mode-4 tick branch**

In `Timer_Tick`, after the existing mode-2 block:

```csharp
            if (_currentMode == 2 && _isRunning) // Countdown mode
            {
                _countdownRemaining -= TimeSpan.FromMilliseconds(50);
                // Flash status when hitting zero
                if (_countdownRemaining <= TimeSpan.Zero && _countdownRemaining > TimeSpan.FromMilliseconds(-100))
                {
                    UpdateStatus("Time's up! (counting negative)", Brushes.Red);
                }
            }
```

add:

```csharp
            if (_currentMode == 4 && _isRunning) // Clock Countdown mode
            {
                _countdownRemaining = _clockTarget - DateTime.Now;
                if (_countdownRemaining <= TimeSpan.Zero && _countdownRemaining > TimeSpan.FromMilliseconds(-100))
                {
                    UpdateStatus("Time's up! (counting negative)", Brushes.Red);
                }
            }
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Manual check**

Run: `dotnet run --project StopwatchOverlay`
Expected: In Clock Countdown mode, set a target ~2 min ahead, click Start → the display counts down and reaches `00:00:00` at the target wall-clock time, then goes negative with a `-` sign and the status flashes "Time's up!".

- [ ] **Step 4: Commit**

```bash
git add StopwatchOverlay/ControllerWindow.xaml.cs
git commit -m "feat: tick Clock Countdown from wall-clock target"
```

---

## Task 6: Reset re-prefills the target panel

**Files:**
- Modify: `StopwatchOverlay/ControllerWindow.xaml.cs` (`ResetButton_Click` ~line 321-349)

- [ ] **Step 1: Add the mode-4 reset branch**

In `ResetButton_Click`, after the existing mode-2 block:

```csharp
            if (_currentMode == 2)
            {
                int.TryParse(CountdownMinutes.Text, out int mins);
                int.TryParse(CountdownSeconds.Text, out int secs);
                _countdownDuration = TimeSpan.FromMinutes(mins) + TimeSpan.FromSeconds(secs);
                _countdownRemaining = _countdownDuration;
            }
```

add:

```csharp
            if (_currentMode == 4)
            {
                PrefillClockTarget();
                _countdownRemaining = TimeSpan.Zero;
            }
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Manual check**

Run: `dotnet run --project StopwatchOverlay`
Expected: After a Clock Countdown run, click Reset → the "Until:" fields re-prefill to ~15 min ahead (SS `00`) and the display clears to zero.

- [ ] **Step 4: Commit**

```bash
git add StopwatchOverlay/ControllerWindow.xaml.cs
git commit -m "feat: reset Clock Countdown re-prefills target"
```

---

## Task 7: Full manual verification pass

**Files:** none (verification only)

- [ ] **Step 1: Build release-style and run**

Run: `dotnet build` then `dotnet run --project StopwatchOverlay`

- [ ] **Step 2: Walk the spec test checklist**

Verify each:
1. Select Clock Countdown → panel shows now+15min, SS=00.
2. Set target 2 min ahead, Start → counts down, reaches `00:00:00` at the wall-clock target.
3. Let it pass zero → goes negative with `-`, "Time's up!" status flashes.
4. Set a target earlier than now (e.g. 1 minute ago) → Start → rolls to tomorrow (large remaining ~23h59m).
5. Switch formats (HH:MM:SS.t / HH:MM:SS / MM:SS.t / MM:SS) → render matches Countdown style.
6. Toggle overlay on → overlay shows the same string as the controller; REC indicator + Win+F5/F6/F7/F8 hotkeys work.
7. Reset → panel re-prefills to now+15min, display clears.
8. Buttons: Start / Reset / Lap are all enabled in Clock Countdown mode (unlike Clock mode where they are disabled).

- [ ] **Step 3: Confirm no regressions in other modes**

Quickly cycle Stopwatch / Clock / Countdown / Timecode → each still behaves as before; only one input panel shows at a time.

- [ ] **Step 4: (Optional) Update README/DEVELOPERS docs**

If user wants user-facing docs updated, add "Clock Countdown" to the mode list in `README.md`. Not required for functionality — confirm with user before doing.
