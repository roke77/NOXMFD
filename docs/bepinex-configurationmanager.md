# BepInEx.ConfigurationManager's F1 menu doing nothing

## Symptom

`BepInEx.ConfigurationManager` is installed and loads cleanly (confirmed in `LogOutput.log`,
no exceptions), but its hotkey — default `F1` — never opens the settings window. Rebinding the
hotkey in its own `.cfg` file (`com.bepis.bepinex.configurationmanager.cfg`) to a different key
doesn't help either: no key opens it.

This surfaced while troubleshooting
[NO-VanillaIconsPLUS](https://github.com/xHellcat92x/NO-VanillaIconsPLUS), a third-party mod
whose colors/toggles are plain `ConfigEntry` values with no UI of their own — normally edited
through ConfigurationManager's menu — but the same root cause affects any mod that depends on
that menu.

## Root cause

`ConfigurationManager`'s `Update()` method — the one that polls its hotkey every frame and
toggles the window — lives on the `GameObject` BepInEx creates to host every plugin. **Nuclear
Option destroys that `GameObject` on the boot → MainMenu scene transition**, in this Unity/game
version. Once that happens, `ConfigurationManager`'s component is gone; nothing is polling the
hotkey anymore, so no key binding — old or new — will ever open the menu again for the rest of
that session.

This is the exact same engine quirk NO XMFD's own `Plugin.cs` already works around (see the
comment above `Plugin.Awake` and `MissionLifecycle.cs`): BepInEx's own `DontDestroyOnLoad` on its
manager object doesn't survive this specific transition, so NO XMFD spawns its own fresh,
persistent `GameObject` reactively on the first `SceneManager.sceneLoaded` event instead of
trying to keep the original one alive. `ConfigurationManager` is generic third-party code with
no awareness of this game-specific behavior, so it has no such workaround built in.

### How this was actually diagnosed

Confirmed with a throwaway diagnostic BepInEx plugin (not checked in) that:

1. Logged `Chainloader.PluginInfos["com.bepis.bepinex.configurationmanager"].Instance` from a
   plain `Awake()` on the BepInEx-hosted `GameObject` — found it fine, during boot.
2. Logged the same lookup again from a component on its own persistent `GameObject`, spawned via
   the `SceneManager.sceneLoaded` pattern above, once the MainMenu scene had loaded — `Instance`
   was already Unity "fake-null" by then, meaning the `GameObject` had already been destroyed.
3. Logged raw `Input.GetKeyDown(KeyCode.F1)` / `F9` from that same persistent component every
   frame — these fired correctly on every keypress, proving Unity's input system sees the keys
   fine in-game. (Pressing F1 on the bare Windows desktop separately opens the OS's own "Get
   Help" app — a real but unrelated coincidence that briefly looked like the cause.)

Together this ruled out an input-system conflict, an `OverrideHotkey` flag, and OS-level key
interception, and pinned it on the `GameObject` lifecycle.

## Fix

Set, in `BepInEx/config/BepInEx.cfg`:

```ini
[Chainloader]
HideManagerGameObject = true
```

This keeps BepInEx's manager `GameObject` (and everything hosted on it, including
`ConfigurationManager`) alive across the boot → MainMenu transition. After this, `Configuration
Manager`'s hotkey works normally for the rest of the session.

This repo hit and fixed this exact issue once before, independently of the VanillaIconsPLUS
investigation — see `908f9d5` (misdiagnosed as an Input System conflict, worked around with a
custom `Ctrl+H` hotkey component), then `f347068` (found the real cause and reverted to CM's own
native hotkey once `HideManagerGameObject` was set). NO XMFD itself needs no such setting — its
own settings are all `Browsable=false` (see `f148038`) and it survives the transition via its own
persistent worker regardless — this setting is only needed for *other* plugins that depend on
ConfigurationManager, like VanillaIconsPLUS.

## Notes

- **Avoid binding ConfigurationManager's hotkey to `F1`** — it's a native in-game camera-view
  key, so it will double-fire in a mission even once the menu itself works. `F9` was free at the
  time of writing.
- If a specific mod's own toggles/colors are all that's needed and installing/troubleshooting
  ConfigurationManager isn't worth it, every BepInEx mod's settings are also plain text in its own
  `BepInEx/config/<plugin guid>.cfg` — editing that directly and saving applies changes live for
  any setting wired to `ConfigEntry.SettingChanged` (VanillaIconsPLUS's colors and toggles all
  are), no restart needed.
