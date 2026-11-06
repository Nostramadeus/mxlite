# MxLite

A tiny replacement for Logitech Options+ on Windows. One ~40 KB program, no installer, about 30 MB of memory.

Not made by or affiliated with Logitech.

## Quick start

1. Download `MxLite.zip` from [Releases](../../releases) and unzip it to a folder, for example `C:\Tools\MxLite`.
2. Quit or uninstall Logi Options+ first. Otherwise every click does its action twice.
3. Double-click `MxLite.exe`. A mouse icon appears in the tray next to the clock. Double-click it for Settings.

## What it does

- **The button under the scroll wheel**
  - Click: Task View (see all open windows).
  - Hold + move the mouse: switch virtual desktops (configurable).
  - Hold + move further: jump 2 or more desktops in one hold (Settings: "Move further to jump 2 or more desktops").
  - Changed your mind mid-hold? Move back one swipe length and it jumps the other way.
- **The two thumb side buttons** (optional, off by default)
  - Rear = previous desktop, front = next desktop (configurable).
  - Or leave them as normal browser back/forward.
- **The scroll wheel's auto free-spin** (ratchety normally, spins free on a hard flick) keeps working. That part lives inside the mouse itself. MxLite can also set how hard a flick it needs.

## Which mice work

The rule: the mouse needs BOTH of these.

1. A scroll wheel that switches by itself from click-click to free-spin when you flick it hard. Logitech calls this "SmartShift".
2. A small button just below or behind the scroll wheel.

MxLite asks the mouse at startup whether it has both. If it does not, MxLite keeps searching and the tray tooltip says "searching for mouse...". Nothing breaks.

| Mouse family | Works? | Why |
|---|---|---|
| MX Anywhere 3, MX Anywhere 3S | **Yes**. The 3S is tested. | Has SmartShift and the button under the wheel. |
| MX Master, MX Master 2S, MX Master 3, MX Master 3S (incl. "for Mac" versions) | **Expected yes**, not tested yet | Same SmartShift wheel and the same button behind the wheel. |
| MX Master 4 | **Unknown**, not tested | Newer model. Please open an issue with your `mxlite.log` if you try it. |
| MX Anywhere 2, MX Anywhere 2S | No | Free-spin is switched by hand, not SmartShift. |
| MX Vertical, MX Ergo, Lift, Pebble, M-series (M185, M330, M720...) | No | No SmartShift wheel. |
| G-series gaming mice | No | Different wheel and software (G HUB). |
| Any non-Logitech mouse | No | MxLite only speaks Logitech's protocol. |

On the "Yes" and "Expected yes" mice, the two thumb side buttons can also be used (Settings, off by default).

Check yours: open `mxlite.log` next to the exe. A working mouse shows `Gesture button active on <your mouse name>`.

Works over Bluetooth or the Logi Bolt / Unifying USB receiver. Needs Windows 10 or 11 (64-bit). Nothing else to install.

## Setup

1. Put the folder anywhere. Do not run it from inside the zip.
2. Quit or uninstall Logi Options+.
3. Double-click `MxLite.exe`, then double-click the tray icon to open Settings. Every change applies instantly.
4. In Settings, tick "Start MxLite with Windows" so it is always on. (Run some apps "as administrator"? Use `install-elevated.cmd` instead, see below.)

Start Menu (optional): run `add-startmenu.cmd` once and MxLite shows up in the Start Menu.

## Settings file

Settings live in `mxlite.ini` next to the exe. The Settings window writes it; editing by hand is fine too.

| Key | Meaning |
|---|---|
| `click`, `left`, `right`, `up`, `down` | Keys sent by the wheel button: click and hold+move in each direction. Example: `Ctrl+Win+Right`. Empty = nothing. |
| `gesture_distance` | Mouse movement needed for the first desktop (default 300). |
| `repeat` | `1` = keep moving to jump several desktops in one hold. `0` = one per hold. |
| `extra_distance` | With `repeat = 1`: extra movement for each further desktop (default 450). 300 + 450: 300 = 1 desktop, 750 = 2, 1200 = 3. |
| `side_buttons` | `1` = thumb buttons send `back` / `forward` below. `0` = normal browser back/forward. |
| `back`, `forward` | Keys for the rear and front thumb buttons (default `Ctrl+Win+Left` / `Ctrl+Win+Right`). |
| `smartshift` | `auto` = leave the wheel as the mouse has it. `1`-`50` = lower spins free on a gentle flick, higher needs a hard flick. |

Key names: `Ctrl Shift Alt Win A-Z 0-9 F1-F24 Left Right Up Down Tab Space Enter Esc PgUp PgDn Home End PlayPause NextTrack PrevTrack VolUp VolDown Mute BrowserBack BrowserFwd PrtSc`.

## Stays alive when your PC is struggling

MxLite does two things at startup, nothing to set up:

- Runs at HIGH process priority, so the mouse button keeps responding when the CPU is maxed out.
- Pins its ~30 MB of memory in RAM, so Windows does not push it to disk when memory runs low (Logi Options+ does the same).

To check it worked, look for this line near the top of `mxlite.log`:
`priority=High, working set pinned (32 MB hard min)`.
If it says `pin FAILED` (rare, e.g. locked-down work PCs), MxLite still runs; it only loses this extra protection.

## Good to know

- **Wheel suddenly glides with no click-click, and pressing the wheel does not bring it back?** The mouse is stuck in permanent free-spin. That mode is stored inside the mouse, so restarting does not clear it. In Settings, set "Scroll wheel free-spin" to Custom (try 30). That writes ratchet mode back and the notches return at once. Nothing is broken and it is not the battery.
- **Button stops responding** (rare): turn the mouse off and on. MxLite reconnects within a few seconds.
- **Keys do nothing while an "as administrator" app has focus**: a Windows security rule (UIPI), not a bug. See the next section.
- `mxlite.log` (next to the exe) shows what the mouse sent. Useful if something misbehaves.

## Start as administrator (if you use "run as administrator" apps)

Windows drops keystrokes simulated by a NORMAL program while an ELEVATED ("run as administrator") window has focus. Your real keyboard is never affected. If some of your apps run elevated (VPN clients, some game tools), desktop switching feels dead while one of them is the active window, and nothing in `mxlite.log` looks wrong.

Fix: run `install-elevated.cmd` once.

- It creates a Task Scheduler task that starts MxLite as administrator when you log on. One UAC prompt during setup, none at boot.
- It removes the normal Startup-folder shortcut so MxLite does not start twice. Leave "Start MxLite with Windows" unticked.
- Undo any time with `uninstall-elevated.cmd`.

## Uninstall

1. If you used `install-elevated.cmd`, run `uninstall-elevated.cmd` first.
2. Untick "Start MxLite with Windows" in Settings.
3. Right-click the tray icon -> Exit MxLite, then delete the folder.
4. Turn the mouse off and on once; the buttons are back to factory behaviour. Nothing else is left behind.

## Build from source

No SDK needed: `build.cmd` uses the C# compiler that ships with Windows (.NET Framework 4.x).

```
build.cmd
```

It writes `MxLite.exe` next to `MxLite.cs`. Start with `MxLite.exe --debug` for a console with live log output.

MxLite talks to the mouse directly over Logitech's HID++ 2.0 protocol (features 0x1B04 reprogrammable controls and 0x2110/0x2111 SmartShift).

## License

MIT, see [LICENSE](LICENSE).
