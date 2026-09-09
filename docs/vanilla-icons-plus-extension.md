# Vanilla Icons PLUS bridge — NOXMFD-side plan

## Status

NOXMFD's side (sections 1–4 below) is built, as API version 2 — see `docs/extensions-api.md`'s
"6. Icon color overrides" for the shipped surface. Section 5 (documentation) is this same update.
The extension itself, in its own repo, is not started; the rest of this document is still that
project's plan.

## Goal

[NO-VanillaIconsPLUS](https://github.com/xHellcat92x/NO-VanillaIconsPLUS) recolors the game's
native HUD and `DynamicMap` unit icons: a friendly/enemy/neutral tint plus an enemy-only
"AA"/"Special AA" override for a configurable set of unit types. A new extension plugin — its own
repo, its own release cycle, depending on both NOXMFD and (optionally) VanillaIconsPLUS — applies
those same colors to NOXMFD's own MAP page icons, so a pilot who customized VanillaIconsPLUS's
palette sees a matching MAP.

This document covers only what NOXMFD itself needs to expose for that extension to do its job.
The extension's own design — how it reads VanillaIconsPLUS's config, whether it depends on
VanillaIconsPLUS directly or lets a pilot type in colors independently, its own repo layout — is
that project's plan, not this one.

## Two independent rendering pipelines

VanillaIconsPLUS and NOXMFD's MAP page color unit icons through entirely separate paths, and
nothing links them today:

- **VanillaIconsPLUS** Harmony-patches `HUDUnitMarker`/`UnitMapIcon` directly and writes into
  `GameAssets.HUDFriendly`/`HUDHostile`/`HUDNeutral`, recoloring the game's own native HUD and
  map icons in place.
- **NOXMFD's MAP page** (`src/web/pages/map/map.js`) is a separate canvas renderer fed by NOXMFD's
  own telemetry stream. Each contact carries a type (`u.t`, matched against NOXMFD's own captured
  icon sprites at `/icon?type=`) and a faction (`u.f`: `0` neutral, `1` friendly, `2` enemy). Color
  comes from `factionColors[u.f]`, itself seeded once per frame from the telemetry payload's
  `colors` block (`map.js:118`, `map.js:639`, `map.js:779`).

A pilot who repaints VanillaIconsPLUS's HUD tints sees the native HUD change; NOXMFD's MAP is
unaffected unless something explicitly tells it to change too.

## What already works, by accident, and its limits

NOXMFD's `TelemetryReader.ReadFactionColors()` (`src/plugin/Telemetry/TelemetryReader.cs:700-714`)
reads `GameAssets.HUDFriendly/HUDHostile/HUDNeutral` — the exact same three fields
VanillaIconsPLUS's `ApplyHUDTints()` writes — and ships them to the browser every frame as
`colors.f/e/n` (`src/plugin/Telemetry/TelemetryJson.cs:95-97`). So MAP's three base faction tints
already end up matching VanillaIconsPLUS's Friendly/Enemy/Neutral Units settings, coincidentally,
with two real gaps:

- `ReadFactionColors()` reads once per session and caches (`_colorsRead`), so a live recolor
  through ConfigurationManager's F9 menu never reaches MAP for the rest of that session.
- It carries no equivalent for VanillaIconsPLUS's enemy-only AA/Special AA override — that tint
  is keyed by a whitelist of unit type names (`Unit.unitName`, e.g. `"AFV-6 AA"`,
  `"HLT CRAM"`), not by faction, and NOXMFD's telemetry has no per-type color concept at all today.

Both gaps need real API surface, not a bigger coincidence.

## NOXMFD's responsibilities

### 1. A live faction-color override

Add to `NOXMFD.Api` (`src/plugin/Extensions/Api.cs`):

```csharp
public static void SetFactionColorOverride(string? friendlyHex, string? enemyHex, string? neutralHex);
public static void ClearFactionColorOverride();
```

Backed by a small static store (new `IconColorRegistry`, alongside `ExtensionRegistry` — same
lock-around-a-dictionary pattern `ExtensionRegistry.PublishSlice` already uses). When an override
is set, it takes precedence over `ReadFactionColors()`'s once-cached game read; `Clear` reverts to
that cached read. This turns the existing accidental match into an explicit, live-updatable one —
the extension calls it once at startup and again from whatever `SettingChanged` hook it wires to
VanillaIconsPLUS's own `ConfigEntry<Color>`s, the same way VanillaIconsPLUS re-applies its own
tints today.

### 2. A per-unit-type color override, faction-scoped

```csharp
public static void SetUnitTypeColorOverride(string unitType, string hex, int? factionFilter = null);
public static void ClearUnitTypeColorOverride(string unitType);
```

`unitType` is the same key NOXMFD's telemetry already carries as a contact's `t` field and already
uses to look up that type's icon sprite (`CapturedAssetEndpoint.SetIcon`/`ServeIcon`) — no new
classification or name mapping needed on NOXMFD's side; the extension supplies whichever type
names it wants colored (VanillaIconsPLUS's own AA/Special AA whitelist entries, to start).
`factionFilter` is one of the existing faction ints (`0`/`1`/`2`) or `null` for "any faction" —
`2` reproduces VanillaIconsPLUS's enemy-only AA tint exactly; `null` covers a future type override
that isn't faction-gated, without inventing a bitmask for a case nothing needs yet.

### 3. Carry both overrides to the browser and consult them in MAP

- `TelemetrySnapshot` gains a `TypeColorOverrides` field (type → `(hex, factionFilter)`).
- `TelemetryJson.AppendFramePayload` (`src/plugin/Telemetry/TelemetryJson.cs:92-98`) extends the
  existing `"colors"` object additively — `f`/`e`/`n` unchanged, plus `"types":{"<unitType>":"#hex"}`
  for any override with no `factionFilter`, and a parallel per-faction map (or a `"types2":{...}`
  keyed by `"<faction>:<unitType>"`) for scoped ones. Exact shape is an implementation detail once
  this is built; the constraint is additive-only, since every existing NOXMFD page and the one
  known extension example already parse `colors` and must keep working unchanged.
- `map.js`'s color resolution (`map.js:639`, currently
  `const hex = u.sq ? SQUAD_COLOR : (factionColors[u.f] || factionColors[0]);`) gains one more
  lookup ahead of the faction fallback: a per-type override for `u.t` (respecting its faction
  scope) wins over the plain faction color, but the existing squad-color/selection precedence
  above it is untouched.

### 4. Versioning

This is new public surface, so bump `Api.ApiVersion` (`src/plugin/Extensions/Api.cs:8`) from `1`
to `2` when it lands, and document a `MinimumVersion` the new extension pins against in its own
`BepInDependency` attribute — the same mechanism every extension already uses
(`docs/extensions-api.md`'s "Dependency and lifecycle" section).

### 5. Documentation

Once built, extend `docs/extensions-api.md` with a sixth numbered surface ("Icon color
overrides") alongside the existing five, and add the equivalent walkthrough section to
`EXTENSIONS.md` for a modder writing against it — matching how both docs already cover pages,
telemetry, commands, navigation, and the MJPEG feed.

## What stays out of NOXMFD

- Any dependency on VanillaIconsPLUS itself, or any parsing of its `ConfigEntry`s/`.cfg` file —
  the extension owns that, not NOXMFD.
- AA/Special AA classification logic (VanillaIconsPLUS's `AAUnitHelper`, its whitelist file) —
  NOXMFD only ever sees "this type name, this color, this optional faction," supplied by the
  extension.
- Harmony patches on the game's native HUD/`DynamicMap` — those already exist in VanillaIconsPLUS
  and are untouched by any of this; NOXMFD's own MAP rendering is the only thing being colored
  here.
- Player name label colors/sizes/offsets (VanillaIconsPLUS's `FriendlyNameHUD`/`MapNameFontSize`
  etc.) — MAP draws no per-unit name labels today, so there is nothing on NOXMFD's side for those
  settings to apply to.

## Open questions

Left for the extension repo's own plan, not answered here:

- Whether the extension takes a hard `BepInDependency` on VanillaIconsPLUS (reading its
  `ConfigEntry<Color>`s directly) or works standalone with its own color settings.
- Whether it mirrors VanillaIconsPLUS's AA whitelist file format/location or keeps its own copy.
- Release/versioning cadence relative to VanillaIconsPLUS's own updates.
