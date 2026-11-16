# Contributing to NO XMFD

NO XMFD is open source under the [MIT license](LICENSE). This covers how to report a problem
and how to build the mod from source.

## Reporting

- **Bugs and feature requests** — open an
  [issue](https://github.com/roke77/NOXMFD/issues). Include your game and NO XMFD versions,
  and steps to reproduce for bugs.
- **Mod-compatibility reports** (a conflict with another BepInEx mod) — open an issue too;
  see the [README's mod compatibility section](README.md#mod-compatibility) for what to
  include.
- **Security issues** don't go through a public issue — see
  [SECURITY.md](SECURITY.md#verifying-and-reporting).

## Building from source

NO XMFD is a single BepInEx 5 plugin: a C# backend (`src/plugin/`) that reads game
telemetry and serves a browser-based UI (`src/web/`) embedded in the built DLL.

**Requirements:**

- [.NET SDK](https://dotnet.microsoft.com/) matching `NOXMFD.csproj`'s target
  (`netstandard2.1`).
- A local install of **Nuclear Option**, since the project builds against the game's
  own managed DLLs (`Assembly-CSharp`, `Mirage`, `Rewired_Core`, etc.) — there's no way
  around this for the C# side.
- [BepInEx 5](https://docs.bepinex.dev/) (x64) installed into that game, to actually run
  and test the built plugin.
- Python 3, for the frontend preview harness (`tools/serve_web.py`) and release
  tooling — no extra packages required, just the standard library.
- [Node.js](https://nodejs.org/), to run the frontend's `*.test.js` self-checks.
- PowerShell (`pwsh`), to run `tools/ci-check.ps1`.

**Point the build at your game install.** `NOXMFD.csproj` defaults to the standard Steam
path; if yours differs, don't edit the `.csproj` — create a `GameDir.props` file next to
it (gitignored, machine-specific):

```xml
<Project><PropertyGroup>
  <GameDir>D:\SteamLibrary\steamapps\common\Nuclear Option</GameDir>
</PropertyGroup></Project>
```

**Build:**

```
dotnet build -c Release
```

A successful build also deploys the DLL straight into `$(GameDir)\BepInEx\plugins\`
(the `DeployToGame` MSBuild target), so `dotnet build` and relaunching the game is the
whole edit/test loop for the plugin side.

## Working on the frontend

The web UI lives as real files under `src/web/` (`shell/`, `pages/<name>/`, `services/`,
`shared/`) — not embedded in C# strings. `dotnet build` only validates the C#
routes/embedded-resource manifest; it does **not** check JS/CSS.

Preview it without the game running via the bundled HTTP harness:

```
python tools/serve_web.py --port 8782
```

Then open `http://localhost:8782/`. It serves the real page files and mocks the
telemetry stream (optionally replaying a real capture — see `tools/capture_assets.py`),
so most UI work can be iterated on without Nuclear Option open at all. It reads files
off disk per request, so edits show up on refresh with no restart needed.

## Testing

- `tools/ci-check.ps1` runs everything below in one command:
  ```
  pwsh tools/ci-check.ps1
  ```
- **Frontend**: every `*.test.js` file under `src/web/` and `tools/` (usually next to
  the code it covers) is a plain `node` script with asserts (no framework) — run it
  directly, or let `ci-check.ps1` find them all.
- **Plugin (C#)**: `tools/tests/` is a standalone xUnit project covering the plugin's
  pure-logic files (ones with no direct Unity/BepInEx/game-object coupling). Most of
  `src/plugin/` isn't unit-tested by design — it reflects into live Unity/game state or
  subclasses `MonoBehaviour`, which needs the game actually running to exercise
  meaningfully.
