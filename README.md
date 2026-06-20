# NO Roks MFD

A [Nuclear Option](https://store.steampowered.com/app/2168680/) mod that streams live
telemetry from the running game and renders it in a browser — a real map with your plane
tracked on it in real time. It's a learning scaffold derived from how
[NOBlackBox](https://github.com/KopterBuzz/NOBlackBox) works.

## What it does

While the game is running, the mod hosts a small web server on `http://localhost:5005/`.
Open that in any browser to get a live HUD:

- The **actual in-game map**, pulled straight from the game's assets.
- Your aircraft drawn on the map, positioned and pointed by real telemetry, with a trail.
- A sidebar showing aircraft name, the in-game **grid coordinate** (e.g. `Hc87`),
  true airspeed, AGL altitude, heading, gear state, world unit counts, and raw position.

Endpoints, if you want to consume the data yourself:

| Path      | Description                                                        |
|-----------|--------------------------------------------------------------------|
| `/`       | The HUD page (HTML).                                               |
| `/stream` | Server-Sent Events stream of telemetry JSON (10 Hz in a mission).  |
| `/map`    | The extracted map image (PNG).                                     |

The stream sends `{"ping":true}` once a second when no mission is running, and full
telemetry frames at 10 Hz once you're flying.

## How it works

1. **BepInEx 5** injects this DLL into the game process at startup.
2. The `.csproj` references the game's own `Assembly-CSharp.dll` and `Mirage.dll`, so we
   can call `MissionManager`, `GameManager`, `Unit`, `Aircraft`, `Datum`, `LevelInfo`, etc.
   directly. (`Mirage.dll` is required because `Unit`/`Aircraft` inherit from its
   `NetworkBehaviour`.)
3. `Plugin.Awake()` starts the HTTP server (`TelemetryServer`). `Plugin.Update()` watches
   `MissionManager.IsRunning` and spawns/destroys a `TelemetryReader` MonoBehaviour per mission.
4. `TelemetryReader` reads the live aircraft and map each frame and pushes a snapshot to the
   server, which background threads serve over HTTP.

### Floating origin (the important bit)

Nuclear Option uses a **floating-origin** system: it re-centers the world around your
aircraft whenever you drift far from `(0,0,0)`, so `transform.position` constantly snaps
back toward zero and is **not** a usable world coordinate. The true world position is
`transform.position - Datum.originPosition`. The map is a square centered on the world
origin spanning `LevelInfo.LoadedMapSettings.MapSize`, so a world coordinate maps directly
to a map fraction — no calibration needed. The grid label is reproduced from the same
offsets the game uses, which doubles as a correctness check against the in-game readout.

## Prerequisites (on the Windows build machine)

- [.NET SDK](https://dotnet.microsoft.com/download) (6.0+; any recent SDK builds netstandard2.1).
- Nuclear Option installed via Steam.
- [BepInEx 5 (BepInEx_win_x64)](https://github.com/BepInEx/BepInEx/releases/latest)
  installed into the game folder, and the game launched once so BepInEx generates its folders.

## Build

1. Open `NORoksMFD.csproj` and set `<GameDir>` to your Nuclear Option install
   path if it isn't the default Steam location.
2. From this folder:

   ```sh
   dotnet build -c Release
   ```

   This builds `bin/Release/netstandard2.1/NORoksMFD.dll` and, via a post-build
   step, copies it into `<GameDir>/BepInEx/plugins/`.

## Install & run

1. Build (the DLL is auto-deployed to the plugins folder), or copy
   `NORoksMFD.dll` into `Nuclear Option/BepInEx/plugins/` manually.
2. Launch the game. (Restart the game after any rebuild to load the new DLL.)
3. Open `http://localhost:5005/` in a browser. You'll see "CONNECTED — no mission".
4. Start a mission and spawn in — your plane appears on the map immediately.

Optional: enable the BepInEx console to watch the mod's logs by setting, in
`Nuclear Option/BepInEx/config/BepInEx.cfg`:

```ini
[Logging.Console]
Enabled = true
```

Logs are also written to `Nuclear Option/BepInEx/LogOutput.log`.

## Where to go next

- Track other units (not just your own plane) on the map — the data is already scanned.
- Add more aircraft fields (AOA, throttle, control inputs) — see `ACMIAircraft_mono.cs`
  in NOBlackBox.
- Build the standalone UI app that consumes `/stream` instead of the embedded HUD.
- Add a BepInEx config file (`BepInEx.Configuration`) to make the port and rates tunable.
