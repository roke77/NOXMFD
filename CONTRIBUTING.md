# Contributing to NO XMFD

Thanks for considering it. This covers how to get a dev environment running, how the
codebase is laid out, and what's expected of a change before it's ready for review.

## Before you start

- **Bugs and small fixes** — just open a PR, or an issue if you'd rather not write the
  fix yourself.
- **Anything non-trivial** (a new feature, a UI change, a behavior change) — open an
  [issue](https://github.com/roke77/NOXMFD/issues) first so the approach can be agreed
  on before code gets written. This avoids a finished PR going in a direction that
  doesn't fit the mod.
- **Security issues** don't go through a public issue — see
  [SECURITY.md](SECURITY.md#verifying-and-reporting).
- **Mod-compatibility reports** (a conflict with another BepInEx mod) are also welcome
  as issues — see the [README's mod compatibility section](README.md#mod-compatibility)
  for what to include.

## Development setup

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

Use the shared color tokens in `src/web/shared/theme.css` (`var(--no-green)`,
`--no-red`, `--no-bg`, etc.) instead of hardcoding hex values, unless there's a specific
reason not to (call it out if so).

## Testing

- `tools/ci-check.ps1` runs everything below in one command — run it before opening a
  PR:
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
  meaningfully. If your change touches that kind of code, note in the PR description
  what still needs a manual in-game check.
- For a UI-visible change, a screenshot or short clip in the PR description helps a lot.

## Code style

- Keep diffs focused — a bug fix doesn't need surrounding cleanup, a small feature
  doesn't need a new abstraction layer "for later." Prefer deleting code over adding it
  when a simpler path exists.
- Comments explain **why**, not what: a non-obvious constraint, a workaround for a
  specific engine/API quirk, an invariant that has to hold. Skip comments that just
  restate the code.
- User-facing docs (README, NETWORKING.md, SECURITY.md, in-app help text) describe the
  *current* state only — no "this used to work differently" asides. Design docs under
  `docs/` are the exception: they're kept as a historical record of how a feature was
  planned, even after it ships.
- Match the structure already in play for the file/folder you're touching rather than
  introducing a new pattern alongside it. `src/plugin/README.md` has a file-by-file map
  of the backend if you're not sure where something belongs.

## Submitting a change

- Fork, branch, commit, open a PR against `main`.
- Keep commits reasonably scoped — one logical change per commit is easier to review
  and easier to `git blame` later than one giant diff.
- Describe *why* in the PR description, not just what changed — the diff already shows
  what.
- By contributing, you agree your changes are licensed under this repo's
  [MIT license](LICENSE).
