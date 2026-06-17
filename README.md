# NOTelemetryReader

A minimal [Nuclear Option](https://store.steampowered.com/app/2168680/) mod that reads
live telemetry from the running game and prints it to the BepInEx console. It's a
learning scaffold derived from how [NOBlackBox](https://github.com/KopterBuzz/NOBlackBox)
works — stripped down to just the "read data and print it" part.

## What it does

When you're in a mission it logs (about once per second) to the BepInEx console:

```
[ME] FQ-106 Kestrel | pos=(1234.5, 980.0, -4521.3) | TAS=210.4 m/s | AGL=305.0 m | gear=up
[WORLD] 42 units total (6 aircraft)
```

## How it works (the 4 layers)

1. **BepInEx 5** injects this DLL into the game process at startup.
2. The `.csproj` references the game's own `Assembly-CSharp.dll`, so we can call
   `MissionManager`, `GameManager`, `Unit`, `Aircraft`, etc. directly.
3. `Plugin.Update()` watches `MissionManager.IsRunning` and spawns a `TelemetryReader`
   MonoBehaviour while a mission is active.
4. `TelemetryReader.Update()` reads fields off the game's live objects
   (`GameManager.GetLocalAircraft(...)`, `FindObjectsByType<Unit>()`) and logs them.

## Prerequisites (on the Windows build machine)

- [.NET SDK](https://dotnet.microsoft.com/download) (6.0+; any recent SDK builds netstandard2.1).
- Nuclear Option installed via Steam.
- [BepInEx 5 (BepInEx_win_x64)](https://github.com/BepInEx/BepInEx/releases/latest)
  installed into the game folder, and the game launched once so BepInEx generates
  its folders. (See the NOBlackBox INSTALL.md for the full BepInEx setup.)

## Build

1. Open `NOTelemetryReader.csproj` and set `<GameDir>` to your Nuclear Option install
   path if it isn't the default Steam location.
2. From this folder:

   ```sh
   dotnet build -c Release
   ```

   Output DLL: `bin/Release/netstandard2.1/NOTelemetryReader.dll`.

## Install & run

1. Copy `NOTelemetryReader.dll` into
   `Nuclear Option/BepInEx/plugins/` (a subfolder like `plugins/NOTelemetryReader/` is fine).
2. Launch the game.
3. Open the BepInEx console to see the output. Enable it by setting, in
   `Nuclear Option/BepInEx/config/BepInEx.cfg`:

   ```ini
   [Logging.Console]
   Enabled = true
   ```

   You can also read the log afterward at `Nuclear Option/BepInEx/LogOutput.log`.
4. Start any mission — telemetry lines should start appearing.

## Where to go next

- Convert raw `transform.position` to real-world lat/lon (see `Helpers.CartesianToGeodetic`
  and `transform.GlobalX()` in NOBlackBox).
- Add more aircraft fields (AOA, throttle, control inputs) — see `ACMIAircraft_mono.cs`.
- Instead of logging, push the data over a local socket/HTTP endpoint so a separate
  UI application can consume it (your eventual goal).
- Add a BepInEx config file (`BepInEx.Configuration`) to make the log interval tunable.
