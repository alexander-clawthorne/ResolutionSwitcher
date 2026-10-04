# ResolutionSwitcher

A controller-driven display resolution picker for Windows, built to be added to
Steam as a non-Steam game so you can change resolution from the couch without
reaching for a mouse.

Ships alongside a set of standalone `.bat` switchers for the same job from a
shortcut, hotkey or script, plus an HDR toggle.

No installer, no SDK, no dependencies. The exe builds with the C# compiler that
is already part of Windows.

## Why

Games and streaming clients often want a different resolution than the desktop.
Doing that through Settings needs a mouse, and the usual tools (QRes, NirCmd)
are extra downloads that Defender tends to dislike. This changes the mode
through the Win32 `ChangeDisplaySettings` API directly.

Every mode is validated with `CDS_TEST` before it is applied, so an unsupported
mode is greyed out in the menu and can never blank your panel.

## Build

```bat
src\build.bat
```

That uses `csc.exe` from the .NET Framework 4.x runtime shipped with Windows and
writes `ResolutionSwitcher.exe` to the repo root. Nothing to install.

## Use

Run `ResolutionSwitcher.exe`, or add it to Steam as a non-Steam game and launch
it from Big Picture.

| Button                   | Action                              |
| ------------------------ | ----------------------------------- |
| D-pad / Left Stick       | Move                                |
| A / Enter                | Apply the highlighted mode          |
| X / Delete               | Delete a custom row (press twice)   |
| B / Back / Start / Esc   | Quit                                |

Gamepad input is read straight from XInput rather than through Steam Input's
keyboard emulation, so it behaves the same from Steam, Big Picture or the
desktop.

## Configuration

### Adding your own resolutions

You do not need to edit any code. Pick **Add a resolution...** in the menu to
open an on-screen numpad, enter width then height, and the mode is validated,
applied and remembered.

Custom modes are stored as plain text, one `WIDTHxHEIGHT` per line, in:

```
%LOCALAPPDATA%\ResolutionSwitcher\custom-resolutions.txt
```

Edit or delete lines there by hand if you prefer.

### Changing the built-in list

The built-in modes are the first few lines of `BuildModes()` in
[`src/ResolutionSwitcher.cs`](src/ResolutionSwitcher.cs):

```csharp
list.Add(new Mode { Label = "Steam Deck", W = 1280, H = 800  });
list.Add(new Mode { Label = "1080p",      W = 1920, H = 1080 });
list.Add(new Mode { Label = "1200p",      W = 1920, H = 1200 });
list.Add(new Mode { Label = "1440p",      W = 3440, H = 1440 });
list.Add(new Mode { Label = "480p",       W = 640,  H = 480  });
```

Add, remove or relabel rows and re-run `src\build.bat`. The `1440p` entry is
3440x1440 ultrawide — change it if that is not your panel.

Unsupported modes are shown greyed out rather than hidden, so a wrong entry here
is harmless.

## Batch switchers

[`batch-files/`](batch-files) holds standalone switchers for when you want one
fixed resolution from a shortcut, a hotkey or another script, with no menu.

| File         | Sets                   |
| ------------ | ---------------------- |
| `480p.bat`   | 640 x 480              |
| `800p.bat`   | 1280 x 800             |
| `1080p.bat`  | 1920 x 1080            |
| `1200p.bat`  | 1920 x 1200            |
| `1440p.bat`  | 3440 x 1440 ultrawide  |
| `HDR.bat`    | HDR / Auto HDR toggle  |

Each is self-contained — the PowerShell that calls the Win32 API is read back
out of the `.bat` itself at runtime, so there is nothing else to ship.

### Making your own

Copy any of them and edit the three variables at the top:

```bat
set "RES_W=1920"
set "RES_H=1080"
set "RES_LABEL=1080p"
```

Refresh rate and colour depth are left untouched in all cases.

### Environment variables

| Variable          | Used by          | Effect                                           |
| ----------------- | ---------------- | ------------------------------------------------ |
| `RES_TESTONLY=1`  | resolution bats  | Check the mode is supported, change nothing       |
| `HDR_QUIET=1`     | `HDR.bat`        | Suppress output, for calling from another script  |

### HDR.bat

```bat
HDR.bat                 rem toggle
HDR.bat on | off | toggle | status
HDR.bat auto-on | auto-off        rem Auto HDR only
HDR.bat on+auto | off+auto        rem both together
```

HDR is switched through the `DisplayConfig` API (`SET_ADVANCED_COLOR_STATE`) —
the same thing Win+Alt+B does, but set to an explicit state rather than
blind-toggled.

Auto HDR is a registry value under
`HKCU\Software\Microsoft\DirectX\UserGpuPreferences`. It only applies to games
launched *after* the change, and only does anything while HDR itself is on. The
previous value is saved to `%LOCALAPPDATA%\hdr-res-scripts\autohdr.last`, so
turning it back on restores what Windows actually had instead of a guess.

## Requirements

Windows 10 or 11. .NET Framework 4.x, which is present by default. An XInput
controller is optional — keyboard works throughout.

## Licence

[MPL-2.0](LICENSE)
