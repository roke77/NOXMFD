# Security & privacy

**Short version:** NO XMFD is an open-source BepInEx mod. Like *every* BepInEx mod, it runs as
unsandboxed code inside the game with your user account's privileges — there is no such thing as
a "safe" sandbox for a Unity mod. So the only real basis for trust is being able to read what it
does. This page tells you exactly what it can access, what it deliberately does **not** do, and
how to verify that yourself.

If you'd rather just read the code: the whole mod is in [`src/plugin/`](src/plugin/) (see its
[README](src/plugin/README.md) for a file-by-file map).

## The trust model (applies to all BepInEx mods)

BepInEx loads plugins as .NET DLLs directly into the game process. A plugin can do anything your
user account can do — read/write files, open network connections, run programs. **Nothing
sandboxes it**, and BepInEx doesn't verify or sign plugins. This is true of any mod you install,
not just this one. Only install mods whose source you (or someone you trust) can inspect, and
prefer downloading through a moderated channel such as [Thunderstore](https://thunderstore.io/c/nuclear-option/),
which virus-scans and manually reviews uploads.

## What NO XMFD does

- **Reads game state in memory** — your aircraft's position, speed, loadout, targets, the map
  image, unit icons, and the targeting-pod camera. This is the telemetry the display shows.
- **Runs a local web server** — an HTTP/SSE server on TCP port **5005** (configurable), bound to
  all network interfaces so a tablet/phone on your Wi-Fi can open the display. It serves the MFD
  web UI, the telemetry stream, and the captured images. See [docs/networking.md](docs/networking.md).
- **Sends input to the game, only for two explicit actions** — tap-to-target and the
  countermeasure/gear keybinds. Both go through the game's own APIs (the same calls the cockpit
  makes), so they behave exactly like normal input and replicate over multiplayer the same way.
  Everything else is strictly read-only.
- **Optionally runs `netsh` once** — *only* if the LAN bind is denied, the game is running as
  Administrator, and `AutoSetupLanAccess` is left on. It adds a Windows URL reservation and an
  inbound firewall rule **for its own port only**, so a tablet can connect. It never runs
  otherwise, never elevates on its own (no UAC prompt), and touches nothing but its own port's
  rules. Full detail + the manual alternative: [docs/networking.md](docs/networking.md).

## What NO XMFD does NOT do

- **No internet connections.** All traffic is localhost/LAN. The mod makes no outbound HTTP
  requests, has no analytics, telemetry, auto-update, or "phone-home" of any kind. (The one
  network syscall outside the web server is a UDP socket briefly opened toward `8.8.8.8` purely
  to ask Windows which local network interface would route outbound — **no data is ever sent**
  through it; it exists only to discover your LAN IP to show on the display. See `DetectLanIp` in
  [`TelemetryServer.cs`](src/plugin/TelemetryServer.cs).)
- **No file access beyond BepInEx.** It reads/writes only its own config file
  (`BepInEx/config/com.roque.NOXMFD.cfg`) and BepInEx's log. Captured game images are held in
  memory and served over HTTP — they are not written to disk. It reads no personal files.
- **No credential, keystroke, or clipboard capture.** It reads game state, not your system.

## The one real caveat: the LAN server is unauthenticated

The web server has **no password**. Anyone who can reach port 5005 on your machine can view your
in-game telemetry and send the same tap-to-target / deselect commands the UI sends (they cannot
touch anything outside the game's normal targeting). On a trusted home network this is fine — it's
the whole point of the second-screen feature. On an untrusted or public network, treat it as
exposed. To keep it strictly local, leave `AutoSetupLanAccess` **off** and don't open the firewall
port: the server then binds localhost-only and nothing on the LAN can reach it.

## Antivirus false positives

Your antivirus may flag **BepInEx** (not this mod specifically) — its loader injects a DLL into
the game, which looks like malware to a heuristic scanner even though it's benign. This is a
well-known false positive across the whole modding ecosystem
([BepInEx #219](https://github.com/BepInEx/BepInEx/issues/219),
[#1014](https://github.com/BepInEx/BepInEx/discussions/1014)). Separately, this mod's optional
`netsh` firewall call can trip behaviour-based heuristics, since firewall changes are a common
malware pattern — that's why it's opt-in, elevation-gated, and scoped to its own port. If you're
unsure, build the DLL yourself from this repo and compare.

## Verifying and reporting

- **Verify:** the source is the whole story — build `NOXMFD.csproj` and diff the DLL against a
  release, or just read [`src/plugin/`](src/plugin/).
- **Report a vulnerability:** open a GitHub issue, or for anything sensitive contact the
  maintainer directly rather than filing publicly.
