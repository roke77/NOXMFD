using System;
using System.Collections.Generic;

namespace NOXMFD
{
    // The web client POSTs JSON commands to /command; CommandEndpoint parses + queues them on a
    // server thread, and this dispatcher drains the queue on the Unity main thread (called from
    // TelemetryReader.Update) and runs the matching handler.
    //
    // Handlers MUST only run on the main thread (they touch game state), keep themselves
    // idempotent, validate against live state, and prefer the game's own high-level input methods
    // (e.g. CombatHUD.SelectUnit) over low-level setters so the in-cockpit side effects — marker
    // colour, audio, map sync — come along for free.

    // Wire envelope: { "cmd": "target.select", "id": 1234 }. Deliberately FLAT — every field is a
    // top-level primitive. Unity's JsonUtility reliably populates top-level fields of a
    // [Serializable] class but is flaky deserializing nested [Serializable] objects in the game's
    // Mono runtime (it silently leaves a nested args.id at 0). So all command params live here as a
    // flat union; each handler reads the fields it cares about, and absent ones default to 0.
    // Every field below is populated by UnityEngine.JsonUtility.FromJson via reflection, which the
    // compiler can't see through — hence CS0649 ("never assigned"), suppressed for the whole class
    // rather than per-field.
#pragma warning disable CS0649
    [Serializable]
    internal class CommandEnvelope
    {
        public string? cmd;
        public long   id;      // target unit persistentID (target.select / target.deselect)
        public string? wname;  // weapon type name (weapon.select) — matches LoadoutEntry.Name
                                // wpt.* : route/waypoint display name
                                // preset.save / preset.rename : preset name
                                // rates.set TGP resolution/quality groups: their stable wire names
                                // rates.set (group "tgpSuppressNative") : "on" | anything else
                                // soi.page : the page name the reported pane is showing
        public string? group;  // tgt.set / tgt.only : "faction" | "category" | "vehicle"
                                // combat-mode.set : "all" | "aa" | "ag"
                                // avn.toggle : "gear" | "radar" | "guns" | "eng" | "assist" | "nvg" |
                                //              "lights" | "turret"
        public int    index;   // tgt.set / tgt.only : toggle index within the group
                                // wpt.* : waypoint index, or a +-1 direction (cycle-route/step-waypoint)
                                // preset.rename / preset.delete / preset.load : slot number 1-5
        public bool   on;      // tgt.set / tgt.laser / tgt.hud : desired toggle state
                                // tgp.manual.set : desired ManualMode state
                                // tgp.ir.set : desired IR state (true = IR, false = COLOR)
        public string? bind;   // keybind.* : BindDef id ("flares", "gear-up", ...)
                                // wpt.* : route id ("" = clear active route)
        public string? key;    // keybind.set-key : Unity KeyCode name ("" or "None" clears)
        public string? cid;    // soi.panes / soi.page : which instance is reporting (a POST isn't tied to its /stream)
        public int    n;       // soi.panes : how many focusable surfaces that instance now shows
                                // soi.page : which of that instance's surfaces (pane index)
                                // wpt.reorder-waypoint : the "to" index
        public float  hz;      // rates.set : desired rate in Hz (group picks which — "fast" | "contact" | "tgp")
        public float  x;       // cursor.set : live cursor velocity X [-1,1]
        public float  y;       // cursor.set : live cursor velocity Y [-1,1]
        public string peer;    // sqd.invite / sqd.relinquish / sqd.accept / sqd.decline : a SteamID64,
                               // as text — a string, not a long, because a 17-digit SteamID64 exceeds
                               // what JavaScript's Number can represent exactly
                               // (docs/squadron-transport.md). Empty on sqd.relinquish means
                               // "auto-pick the oldest member." sqd.accept/sqd.decline use it to pick
                               // which queued incoming invite to act on (Squad.cs's _pendingReceived).
        public string name;    // sqd.invite : the target's display name (for the invite envelope
                                // and pendingSent list — cosmetic, the target's own client is
                                // authoritative about its own name)
                                // sqd.create / sqd.set-callsign : the squadron's chosen callsign
        public string type;    // sqd.send : payload type ("wpt.route", ...)
        public string payload; // sqd.send : the payload itself (small text only)
        public float  wx;      // wpt.add-waypoint : world X (floating-origin corrected)
        public float  wz;      // wpt.add-waypoint : world Z
        public string? text;   // wpt.import : the pasted route-export JSON blob
    }
#pragma warning restore CS0649

    internal static class CommandDispatcher
    {
        private static readonly Dictionary<string, Action<CommandEnvelope>> _handlers =
            new Dictionary<string, Action<CommandEnvelope>>(StringComparer.Ordinal)
            {
                { "target.select",   TargetSelect },
                { "target.deselect", TargetDeselect },
                { "weapon.select",   WeaponSelect },
                { "weapon.cycle",    WeaponCycle },
                { "cm.deploy",       CountermeasureDeploy },
                { "gear.set",        GearSet },
                { "tgt.set",         TgtSet },
                { "tgt.only",        TgtOnly },
                { "tgt.reset",       TgtReset },
                { "tgt.clear",       TgtClear },
                { "tgt.clear-datalink", TgtClearDatalink },
                { "tgt.clear-stale",    TgtClearStale },
                { "tgt.laser",       TgtLaser },
                { "tgt.hud",         TgtHud },
                { "hud.set",         HudSet },
                { "hud.mode",        HudMode },
                { "declutter.set",   DeclutterSet },
                { "avn.toggle",      AvnToggle },
                { "avn.set",         AvnSet },
                // MAP CFG's and TGP CFG's controls. The old tgpQuality group remains a resolution
                // alias so an older page can still switch between native and the mirror feed.
                { "rates.set",       e => {
                    if (e.group == "tgp") RatesConfig.SetTgpHz(e.hz);
                    else if (e.group == "contact") RatesConfig.SetContactHz(e.hz);
                    else if (e.group == "tgpResolution" || e.group == "tgpQuality") RatesConfig.SetTgpResolution(e.wname ?? "native");
                    else if (e.group == "tgpJpegQuality") RatesConfig.SetTgpJpegQuality(e.wname ?? "mid");
                    else if (e.group == "tgpSuppressNative") RatesConfig.SetTgpSuppressNative(e.wname == "on" || e.on);
                    else RatesConfig.SetFastHz(e.hz);
                } },
                { "master-arms.set", e => ImmersionState.MasterArmsOn = e.on },
                // Squad protocol (docs/squadron-transport.md) — leader/member squad state built on
                // the Squadron transport. Parsing the peer id here (not in Squad) keeps the
                // wire-format tolerance at the trust boundary, where every other command's
                // validation already lives.
                { "sqd.create",     e => Squad.CreateSquad(e.name ?? string.Empty) },
                { "sqd.invite",     e => { if (TryPeer(e.peer, out ulong p)) Squad.Invite(p, e.name ?? string.Empty); } },
                { "sqd.set-callsign", e => Squad.SetCallsign(e.name ?? string.Empty) },
                { "sqd.accept",     e => { if (TryPeer(e.peer, out ulong p)) Squad.AcceptInvite(p); } },
                { "sqd.decline",    e => { if (TryPeer(e.peer, out ulong p)) Squad.DeclineInvite(p); } },
                { "sqd.leave",      e => Squad.Leave() },
                { "sqd.relinquish", e => Squad.RelinquishLeadership(TryPeer(e.peer, out ulong p) ? (ulong?)p : null) },
                { "sqd.disband",    e => Squad.Disband() },
                { "sqd.kick",       e => { if (TryPeer(e.peer, out ulong p)) Squad.Kick(p); } },
                { "sqd.send",       e => Squad.SendData(e.type, e.payload) },
                // TGP page's TGT/MAN and CLR/IR button pairs (docs/tgp-manual-control.md's NAV
                // additions) — explicit-state twins of the tgp-manual-toggle/tgp-manual-ir-toggle
                // keybinds, same "set" shape as master-arms.set above rather than a blind flip.
                { "tgp.manual.set", e => TgpManualControl.SetManual(e.on) },
                { "tgp.ir.set",     e => TgpManualControl.SetIR(e.on) },
                // Routes through Keybinds.SetCombatMode (not a bare assignment) so the WPN page's own
                // A/A / A/G controls get the same weapon auto-switch as the physical keybind — one
                // behavior, one source, not a second copy that could drift from it.
                { "combat-mode.set", e => Keybinds.SetCombatMode(e.group switch
                    {
                        "aa" => CombatMode.AirToAir,
                        "ag" => CombatMode.AirToGround,
                        _    => CombatMode.All,
                    }) },
                { "keybind.set-key",    e => Log("set-key",    e.bind, Keybinds.SetKeyBind(e.bind ?? string.Empty, e.key ?? string.Empty)) },
                { "keybind.arm-joy",    e => Log("arm-joy",    e.bind, Keybinds.ArmJoyCapture(e.bind ?? string.Empty)) },
                { "keybind.cancel-joy", e => Keybinds.CancelJoyCapture() },
                { "keybind.clear-joy",  e => Log("clear-joy",  e.bind, Keybinds.ClearJoyBind(e.bind ?? string.Empty)) },
                // Analog axis capture/clear/invert — the MAP cursor's Horizontal/Vertical rows only;
                // same arm/cancel/clear shape as the joystick-button commands above.
                { "keybind.arm-axis",       e => Log("arm-axis",       e.bind, Keybinds.ArmAxisCapture(e.bind ?? string.Empty)) },
                { "keybind.cancel-axis",    e => Keybinds.CancelAxisCapture() },
                { "keybind.clear-axis",     e => Log("clear-axis",     e.bind, Keybinds.ClearAxisBind(e.bind ?? string.Empty)) },
                { "keybind.set-axis-invert", e => Log("set-axis-invert", e.bind, Keybinds.SetAxisInvert(e.bind ?? string.Empty, e.on)) },
                // Input-while-unfocused toggle — the /keybinds page's first entry, not a bind (no
                // key/joy/axis source of its own).
                { "keybind.set-bg-input", e => Keybinds.SetBackgroundInput(e.on) },
                // Immersion start-state toggles — the KEY page's other non-bind rows, same shape as
                // keybind.set-bg-input.
                { "keybind.set-radar-on-start",       e => ImmersionConfig.SetRadarOnOnStart(e.on) },
                { "keybind.set-engine-on-start",      e => ImmersionConfig.SetEngineOnOnStart(e.on) },
                { "keybind.set-master-arms-on-start", e => ImmersionConfig.SetMasterArmsOnOnStart(e.on) },
                { "keybind.set-power-on-start",       e => ImmersionConfig.SetPowerOnOnStart(e.on) },
                // HudCombatModeFilters' own on/off switch — default OFF, unlike the four start-state
                // settings above.
                { "keybind.set-hud-filters-on-combat-mode", e => ImmersionConfig.SetHudFiltersOnCombatMode(e.on) },
                // SOI focus — driven from a browser with no controller and no aircraft.
                { "soi.next",           e => TelemetryServer.SoiCycle(1) },
                { "soi.prev",           e => TelemetryServer.SoiCycle(-1) },
                { "soi.action",         e => TelemetryServer.SoiAction(e.wname ?? string.Empty) },
                { "map.action",         MapAction },
                { "cursor.select",      e => TelemetryServer.CursorSelect() },
                { "cursor.set",         CursorSet },
                { "fire.set",           FireSet },
                // A client reports its current surface count so SOI can cycle surfaces, not documents.
                // Carries its own cid — a POST isn't tied to the /stream connection the count belongs to.
                { "soi.panes",          e => TelemetryServer.SetPaneCount(e.cid ?? string.Empty, e.n) },
                // The SOI-focused shell reports which page its focused surface is showing (n : pane
                // index, wname : page name) — the plugin has no way to know a pane's content on its
                // own. Lets the manual TGP camera also receive PAD Cursor input when the pilot is
                // looking at the external TGP page directly (docs/tgp-manual-control.md's PAD
                // Cursor consolidation plan, TelemetryServer.IsTgpSoi).
                { "soi.page",           e => TelemetryServer.ReportSoiPage(e.cid ?? string.Empty, e.n, e.wname ?? string.Empty) },
                // Waypoint/route editing — RouteStore is the plugin's own authoritative route library.
                { "wpt.create",           e => LogWpt("create",           RouteStore.CreateRoute(e.wname ?? string.Empty) != null) },
                { "wpt.rename",           e => LogWpt("rename",           RouteStore.RenameRoute(e.bind ?? string.Empty, e.wname ?? string.Empty)) },
                { "wpt.delete",           e => LogWpt("delete",           RouteStore.DeleteRoute(e.bind ?? string.Empty)) },
                { "wpt.set-active",       e => RouteStore.SetActiveRoute(e.bind ?? string.Empty) },
                { "wpt.clear",            e => RouteStore.ClearRoutes() },
                { "wpt.reset-route",      e => LogWpt("reset-route",      RouteStore.ResetRoute(e.bind ?? string.Empty)) },
                { "wpt.import",           e => LogWpt("import",           RouteStore.ImportRoute(e.text ?? string.Empty)) },
                { "wpt.rename-waypoint",  e => LogWpt("rename-waypoint",  RouteStore.RenameWaypoint(e.index, e.wname ?? string.Empty)) },
                { "wpt.reorder-waypoint", e => LogWpt("reorder-waypoint", RouteStore.ReorderWaypoint(e.index, e.n)) },
                { "wpt.reset-waypoint",   e => LogWpt("reset-waypoint",   RouteStore.ResetWaypoint(e.index)) },
                { "wpt.remove-waypoint",  e => LogWpt("remove-waypoint",  RouteStore.RemoveWaypoint(e.index)) },
                { "wpt.cycle-route",      e => RouteStore.CycleActiveRoute(e.index) },
                { "wpt.step-waypoint",    e => RouteStore.StepWaypoint(e.index) },
                { "wpt.add-waypoint",     e => RouteStore.AddWaypoint(e.wx, e.wz, e.wname ?? string.Empty) },
                // Squad share (docs/squadron-transport.md) — see RouteStore.cs's own header comment
                // on this group for why it's a separate path from wpt.import. `e.bind` carries the
                // shared route's id for accept/reject (same field wpt.set-active/wpt.delete use for
                // an ordinary route id).
                { "wpt.receive-shared",   e => LogWpt("receive-shared", RouteStore.ReceiveSharedRoute(e.text, Squad.LeaderName)) },
                { "wpt.accept-shared",    e => LogWpt("accept-shared",  RouteStore.AcceptShared(e.bind)) },
                { "wpt.reject-shared",    e => LogWpt("reject-shared",  RouteStore.RejectShared(e.bind)) },
                // The leader deleted a route this pilot had pending or accepted (BroadcastDeleteIfShared).
                { "wpt.remove-shared",    e => LogWpt("remove-shared",  RouteStore.RemoveSharedRoute(e.bind)) },
                // WPT's share button. Marks the route auto-resharing (RouteStore's own mutators push
                // a fresh copy after any future edit) and sends the first copy now.
                { "wpt.share",            e => LogWpt("share",          RouteStore.ShareRoute(e.bind)) },
                // SAVE LAYOUT is a browser-side keyboard shortcut only; LOAD reads GET /layout-options
                // and applies a picked layout entirely client-side, so there's no matching "load"
                // command here.
                //   wname : layout name       group : shell ("classic"/"f35")
                //   text  : the browser's own serialized arrangement, as JSON text (opaque here)
                { "layout.save",   e => LogLayout("save",   LayoutStore.SaveLayout(e.wname ?? string.Empty, e.group ?? string.Empty, e.text ?? string.Empty)) },
                // LOAD's picker manages the library — id : "bind" (matches wpt.*'s own route-id
                // reuse of the field), new name : "wname".
                { "layout.rename", e => LogLayout("rename", LayoutStore.RenameLayout(e.bind ?? string.Empty, e.wname ?? string.Empty)) },
                { "layout.delete", e => LogLayout("delete", LayoutStore.DeleteLayout(e.bind ?? string.Empty)) },
                // HUD filter presets — 5 fixed numbered slots (HudPresetStore), not an arbitrary list
                // like layout.* above: `index` (1-5) addresses a slot directly. save always targets
                // whichever slot is server-side CURRENT, never a client-picked index — the client
                // only ever supplies a name.
                //   wname : preset name (save/rename)     index : slot number 1-5 (rename/delete/load)
                { "preset.save",   e => LogPreset("save",   HudPresetStore.Save(e.wname ?? string.Empty)) },
                { "preset.rename", e => LogPreset("rename", HudPresetStore.Rename(e.index, e.wname ?? string.Empty)) },
                { "preset.delete", e => LogPreset("delete", HudPresetStore.Delete(e.index)) },
                { "preset.load",   e => LogPreset("load",   HudPresetStore.LoadPreset(e.index)) },
            };

        // Keybind writes just delegate to the Keybinds registry; log rejections (unknown id / bad key).
        private static void Log(string op, string? bind, bool ok)
        {
            if (!ok) Plugin.Log?.LogInfo($"[NOXMFD] keybind.{op} '{bind}': rejected.");
        }

        // A SteamID64 arrives as text (see CommandEnvelope.peer). Reject anything that isn't a plain
        // unsigned integer rather than coercing it — this is a trust boundary, and a malformed id
        // should no-op visibly in the log instead of silently becoming peer 0.
        private static bool TryPeer(string s, out ulong id)
        {
            id = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (!ulong.TryParse(s.Trim(), System.Globalization.NumberStyles.None,
                                System.Globalization.CultureInfo.InvariantCulture, out id) || id == 0)
            {
                Plugin.Log?.LogInfo($"[NOXMFD] squadron: rejected peer id '{s}'.");
                return false;
            }
            return true;
        }

        // Same shape as Log() above, for the wpt.* family — kept separate rather than
        // generalizing Log() since its "keybind." prefix is baked into every existing call site.
        private static void LogWpt(string op, bool ok)
        {
            if (!ok) Plugin.Log?.LogInfo($"[NOXMFD] wpt.{op}: rejected.");
        }

        // Same shape as LogWpt above, for the layout.* family.
        private static void LogLayout(string op, bool ok)
        {
            if (!ok) Plugin.Log?.LogInfo($"[NOXMFD] layout.{op}: rejected.");
        }

        // Same shape again, for the preset.* family.
        private static void LogPreset(string op, bool ok)
        {
            if (!ok) Plugin.Log?.LogInfo($"[NOXMFD] preset.{op}: rejected.");
        }

        // True for a cmd we have a handler for — lets the server reject unknown commands at the
        // boundary (422) instead of silently queueing them.
        public static bool IsKnown(string cmd) => cmd != null && _handlers.ContainsKey(cmd);

        // Drained once per frame on the main thread.
        public static void Drain()
        {
            while (CommandEndpoint.TryDequeueCommand(out CommandEnvelope? env))
            {
                if (env == null) continue;
                if (_handlers.TryGetValue(env.cmd ?? string.Empty, out Action<CommandEnvelope> handler))
                {
                    try { handler(env); }
                    catch (Exception ex) { Plugin.Log?.LogWarning($"[NOXMFD] command '{env.cmd}' threw: {ex.Message}"); }
                }
                else
                {
                    Plugin.Log?.LogInfo($"[NOXMFD] unknown command '{env.cmd}' — dropped.");
                }
            }
        }

        // ── Handlers ─────────────────────────────────────────────────────────────

        // Select-only: never deselects, and no-ops if the unit is already targeted (AddTargetList
        // has no de-dup). Routes through CombatHUD.SelectUnit so the cockpit marker recolours, the
        // select beep plays, and the DynamicMap icon syncs; falls back to the bare weaponManager op
        // for a contact the HUD isn't tracking.
        private static void TargetSelect(CommandEnvelope env)
        {
            uint id = unchecked((uint)env.id);
            if (id == 0) { Plugin.Log?.LogInfo("[NOXMFD] target.select: id=0 (missing/unparsed) — ignored."); return; }

            if (!UnitRegistry.TryGetUnit(new PersistentID { Id = id }, out Unit unit) || unit == null || unit.disabled)
            {
                Plugin.Log?.LogInfo($"[NOXMFD] target.select id={id}: no live unit (stale) — ignored.");
                return;
            }

            // This is the external entry point: the browser only ever sends ids it rendered from
            // telemetry, but a direct POST can carry any id — persistentIDs are a plain sequential
            // counter, so enumerating 1..N reaches every live unit regardless of fog of war
            // (confirmed live, docs/jamming-contact-telemetry-hardening.md F3). Gate here rather
            // than in TrySelectTarget, which the manual TGP also calls after its own line-of-sight
            // acquisition — that path must stay unaffected.
            GameManager.GetLocalAircraft(out Aircraft ac);
            if (ac == null || ac.NetworkHQ == null)
            {
                Plugin.Log?.LogInfo($"[NOXMFD] target.select id={id}: no local aircraft/HQ — ignored.");
                return;
            }
            bool factionKnown     = ac.NetworkHQ.TryGetKnownPosition(unit, out _);
            bool ownRadarDetected = ac.radar is Radar radar && radar.detectedTargets.Contains(unit);
            // F1: while the native picture is jammed, a plain faction-known/datalink track is no
            // more selectable than it is disclosed on MAP/HSD — only an active own-radar detection
            // (a separate mechanic from picture jamming) keeps it eligible.
            CombatHUD? hud = SceneSingleton<CombatHUD>.i;
            bool pictureJamActive = hud != null && hud.jamAccumulation > 0f;
            if (!TargetSelectionPolicy.IsSelectable(factionKnown, ownRadarDetected, pictureJamActive))
            {
                Plugin.Log?.LogInfo($"[NOXMFD] target.select id={id}: not visible to player — ignored.");
                return;
            }

            TrySelectTarget(unit, "target.select");
        }

        // Shared by command-driven MAP/RDR selection and the manual TGP's point-track handoff.
        // Select-only: AddTargetList does not de-duplicate, so callers must come through here.
        internal static bool TrySelectTarget(Unit unit, string source)
        {
            if (unit == null || unit.disabled) return false;

            GameManager.GetLocalAircraft(out Aircraft ac);
            if (ac == null || ac.weaponManager == null) return false;
            if (ReferenceEquals(unit, ac)) return false;   // can't target yourself

            string name = unit.definition?.unitName ?? "?";

            // Neutral (no-faction) units are never selectable. The game's own TGT filter panel has
            // no toggle for them at all — TargetListSelector_ToggleButton.CheckFactions only gates
            // Friendly/Enemy, so a no-faction contact always passes it — which would let a MAP tap
            // or RDR lock weapon-lock something the pilot's own filters were never built to offer.
            if (DynamicMap.GetFactionMode(unit.NetworkHQ) == FactionMode.NoFaction)
            {
                Plugin.Log?.LogInfo($"[NOXMFD] {source} '{name}' (id={unit.persistentID.Id}): no-faction unit — ignored.");
                return false;
            }

            // Respect the TGT filter panel's current faction/category/vehicle/laser toggles — a MAP
            // tap or RDR lock shouldn't be able to select anything the pilot has filtered out there.
            // Reuses the game's own exclusion check; if the singleton isn't up yet, fail open rather
            // than block every selection.
            TargetListSelector tgtSel = SceneSingleton<TargetListSelector>.i;
            if (tgtSel != null && tgtSel.CheckExclusions(unit))
            {
                Plugin.Log?.LogInfo($"[NOXMFD] {source} '{name}' (id={unit.persistentID.Id}): excluded by TGT filters — ignored.");
                return false;
            }

            WeaponManager wm = ac.weaponManager;
            if (wm.CheckIsTarget(unit))
            {
                Plugin.Log?.LogInfo($"[NOXMFD] {source} '{name}' (id={unit.persistentID.Id}): already targeted — no-op.");
                return false;
            }

            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            bool viaHud = hud != null && ReferenceEquals(hud.aircraft, ac) && hud.MarkerExists(unit);
            if (hud != null && viaHud) hud.SelectUnit(unit);
            else                       wm.AddTargetList(unit);
            Plugin.Log?.LogInfo($"[NOXMFD] {source} → '{name}' (id={unit.persistentID.Id}, viaHud={viaHud}).");
            return true;
        }

        // Mirrors the in-cockpit deselect via CombatHUD.DeSelectUnit, which reverts the marker
        // colour, plays the deselect beep, and syncs the DynamicMap icon; falls back to the bare
        // weaponManager op when the HUD isn't tracking the contact. No-ops if not currently a target.
        private static void TargetDeselect(CommandEnvelope env)
        {
            uint id = unchecked((uint)env.id);
            if (id == 0) { Plugin.Log?.LogInfo("[NOXMFD] target.deselect: id=0 (missing/unparsed) — ignored."); return; }

            if (!UnitRegistry.TryGetUnit(new PersistentID { Id = id }, out Unit unit) || unit == null)
            {
                Plugin.Log?.LogInfo($"[NOXMFD] target.deselect id={id}: no such unit — ignored.");
                return;
            }

            GameManager.GetLocalAircraft(out Aircraft ac);
            if (ac == null || ac.weaponManager == null) return;

            WeaponManager wm = ac.weaponManager;
            string name = unit.definition?.unitName ?? "?";
            if (!wm.CheckIsTarget(unit))
            {
                Plugin.Log?.LogInfo($"[NOXMFD] target.deselect '{name}' (id={id}): not targeted — no-op.");
                return;
            }

            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            bool viaHud = hud != null && ReferenceEquals(hud.aircraft, ac) && hud.MarkerExists(unit);
            if (hud != null && viaHud) hud.DeSelectUnit(unit);
            else                       wm.RemoveTargetList(unit);
            Plugin.Log?.LogInfo($"[NOXMFD] target.deselect ← '{name}' (id={id}, viaHud={viaHud}).");
        }

        // Matches on the same weaponName/shortName BuildLoadout uses to pick the first visible
        // station of the requested type; the game cycles duplicate stations of the same type with
        // its own next/prev. Replays the game's own NextWeaponStation() sequence so the marker and
        // select beep come along. No-ops if that weapon is already selected.
        private static void WeaponSelect(CommandEnvelope env)
        {
            string wname = env.wname ?? string.Empty;
            if (string.IsNullOrEmpty(wname)) { Plugin.Log?.LogInfo("[NOXMFD] weapon.select: empty name — ignored."); return; }

            GameManager.GetLocalAircraft(out Aircraft ac);
            if (ac == null || ac.weaponManager == null || ac.weaponStations == null) return;
            WeaponManager wm = ac.weaponManager;

            WeaponStation? target = WeaponSelectors.FindStationByName(ac, wname);
            if (target == null) { Plugin.Log?.LogInfo($"[NOXMFD] weapon.select '{wname}': no matching station — ignored."); return; }

            if (ReferenceEquals(wm.currentWeaponStation, target))
            {
                Plugin.Log?.LogInfo($"[NOXMFD] weapon.select '{wname}': already selected — no-op.");
                return;
            }

            WeaponSelectors.SelectStation(ac, target);
            Plugin.Log?.LogInfo($"[NOXMFD] weapon.select → '{wname}' (station {target.Number}).");
        }

        private static bool LocalAircraft(string op, out Aircraft ac)
        {
            GameManager.GetLocalAircraft(out ac);
            if (ac == null || ac.disabled)
            {
                Plugin.Log?.LogInfo($"[NOXMFD] {op}: no local aircraft — ignored.");
                return false;
            }
            return true;
        }

        private static void WeaponCycle(CommandEnvelope env)
        {
            if (!LocalAircraft("weapon.cycle", out Aircraft ac)) return;
            switch (env.group)
            {
                case "guns":     WeaponSelectors.CycleGun(ac);     break;
                case "missiles": WeaponSelectors.CycleMissile(ac); break;
                case "bombs":    WeaponSelectors.CycleBomb(ac);    break;
                default:
                    Plugin.Log?.LogInfo($"[NOXMFD] weapon.cycle: unknown group '{env.group}' — ignored.");
                    break;
            }
        }

        private static void CountermeasureDeploy(CommandEnvelope env)
        {
            if (!LocalAircraft("cm.deploy", out Aircraft ac)) return;
            switch (env.group)
            {
                case "flares": Keybinds.DriveCountermeasure(ac, 1); break;
                case "jammer": Keybinds.DriveCountermeasure(ac, 2); break;
                default:
                    Plugin.Log?.LogInfo($"[NOXMFD] cm.deploy: unknown group '{env.group}' — ignored.");
                    break;
            }
        }

        private static void GearSet(CommandEnvelope env)
        {
            if (!LocalAircraft("gear.set", out Aircraft ac)) return;
            switch (env.group)
            {
                case "up":   Keybinds.DriveGear(ac, up: true,  down: false); break;
                case "down": Keybinds.DriveGear(ac, up: false, down: true);  break;
                default:
                    Plugin.Log?.LogInfo($"[NOXMFD] gear.set: unknown group '{env.group}' — ignored.");
                    break;
            }
        }

        private static void CursorSet(CommandEnvelope env)
        {
            TelemetryServer.SetRemoteCursorState(ClampUnit(env.x), ClampUnit(env.y), env.on);
        }

        private static void FireSet(CommandEnvelope env)
        {
            TelemetryServer.SetRemoteFireState(env.group ?? string.Empty, env.on);
        }

        // tgt-next/prev/datalink/stale each need both halves of the physical keybind path: the
        // SOI-gated page action (tgt-next/prev hand Select to the focused row on the SOI-focused
        // TGT display), and the actual global effect that acts regardless of SOI — cycling the
        // shared target focus, or bulk-deselecting datalink-only/stale locks in the game itself.
        private static void MapAction(CommandEnvelope env)
        {
            string act = env.wname ?? string.Empty;
            TelemetryServer.MapAction(act);
            if (act == "tgt-next") Keybinds.CycleTargetFocus(1);
            else if (act == "tgt-prev") Keybinds.CycleTargetFocus(-1);
            else if (act == "tgt-datalink") ClearDatalinkTargets();
            else if (act == "tgt-stale") ClearStaleTargets();
        }

        private static float ClampUnit(float value)
        {
            if (value < -1f) return -1f;
            if (value > 1f) return 1f;
            return value;
        }

        // ── TGT filter panel ──────────────────────────────────────────────────────
        // These drive the game's own TargetListSelector singleton rather than reimplementing the
        // filter, so its prune of the current selection, its live gate on future selections, and
        // the map-icon recolour all come along for free. Every handler still null-guards so it
        // no-ops rather than throws if the game hasn't built the singleton yet.

        private static List<TargetListSelector_ToggleButton>? TgtGroup(TargetListSelector sel, string? group)
        {
            switch (group)
            {
                case "faction":  return sel.toggleFactionItems;
                case "category": return sel.toggleUnitTypesItems;
                case "vehicle":  return sel.toggleVehicleTypesItems;
                default:         return null;
            }
        }

        // Resolve the live singleton + a validated toggle for {group, index}; logs and returns null
        // on any miss (absent singleton, unknown group, out-of-range/null toggle). sel is always set.
        private static TargetListSelector_ToggleButton? TgtResolve(CommandEnvelope env, string op, out TargetListSelector sel)
        {
            sel = null!;
            TargetListSelector found = SceneSingleton<TargetListSelector>.i;
            if (found == null) { Plugin.Log?.LogInfo($"[NOXMFD] {op}: TargetListSelector absent — ignored."); return null; }
            sel = found;

            List<TargetListSelector_ToggleButton>? list = TgtGroup(sel, env.group);
            if (list == null) { Plugin.Log?.LogInfo($"[NOXMFD] {op}: unknown group '{env.group}' — ignored."); return null; }
            if (env.index < 0 || env.index >= list.Count)
            {
                Plugin.Log?.LogInfo($"[NOXMFD] {op}: index {env.index} out of range for '{env.group}' [{list.Count}] — ignored.");
                return null;
            }
            TargetListSelector_ToggleButton btn = list[env.index];
            if (btn == null) { Plugin.Log?.LogInfo($"[NOXMFD] {op}: null toggle at {env.group}[{env.index}] — ignored."); return null; }
            return btn;
        }

        // Set one filter toggle to an explicit state (not a blind flip — the page mirrors state, so an
        // explicit target is idempotent and survives a dropped click). Set() fires the game's own
        // NeedUpdateIcons → prune + recolour, and the toggle then gates future selections.
        private static void TgtSet(CommandEnvelope env)
        {
            TargetListSelector_ToggleButton? btn = TgtResolve(env, "tgt.set", out _);
            if (btn == null) return;
            if (btn.status == env.on) return;   // already there — no-op (avoids a needless prune pass)
            btn.Set(env.on);
            Plugin.Log?.LogInfo($"[NOXMFD] tgt.set {env.group}[{env.index}] = {env.on}.");
        }

        // Right-click "only this": turn every other toggle in the group off, this one on.
        private static void TgtOnly(CommandEnvelope env)
        {
            TargetListSelector_ToggleButton? btn = TgtResolve(env, "tgt.only", out TargetListSelector sel);
            if (btn == null) return;
            sel.SetOnlyItem(btn);
            Plugin.Log?.LogInfo($"[NOXMFD] tgt.only {env.group}[{env.index}].");
        }

        // RESET FILTER — all toggles back on. Does NOT re-select anything already cleared.
        private static void TgtReset(CommandEnvelope env)
        {
            TargetListSelector sel = SceneSingleton<TargetListSelector>.i;
            if (sel == null) { Plugin.Log?.LogInfo("[NOXMFD] tgt.reset: TargetListSelector absent — ignored."); return; }
            sel.ResetFilters();
            Plugin.Log?.LogInfo("[NOXMFD] tgt.reset — all filters on.");
        }

        // CLEAR TARGETS — deselect the whole current target list.
        private static void TgtClear(CommandEnvelope env)
        {
            TargetListSelector sel = SceneSingleton<TargetListSelector>.i;
            if (sel == null) { Plugin.Log?.LogInfo("[NOXMFD] tgt.clear: TargetListSelector absent — ignored."); return; }
            sel.DeselectAll();
            Plugin.Log?.LogInfo("[NOXMFD] tgt.clear — deselected all targets.");
        }

        // TGT page's DATALINK button: tap deselects datalink-only targets. Same check as
        // TelemetryReader.BuildUnits' Datalink field, duplicated here since it's a one-line read
        // with no shared game-query helper in this codebase yet.
        private static bool IsDatalinkOnly(FactionHQ playerHQ, Unit unit)
        {
            if (unit == null || unit.NetworkHQ == playerHQ) return false;   // only enemy/other-faction targets can be datalink-only
            return !(playerHQ.GetTrackingData(unit.persistentID)?.Observed() ?? false);
        }

        // TGT page's STALE button: tap deselects locked targets whose relayed position the game
        // itself no longer trusts — the same FactionHQ check (20m threshold) that swaps a locked
        // target's TGP box for the "?" (outdated) sprite. Duplicated from TelemetryReader.BuildUnits'
        // Stale field for the reason above.
        private static bool IsStale(FactionHQ playerHQ, Unit unit)
        {
            if (unit == null || unit.NetworkHQ == playerHQ) return false;
            return !playerHQ.IsTargetPositionAccurate(unit, 20f);
        }

        // Shared by TgtClearDatalink/TgtClearStale: bulk-deselect whichever currently-locked targets
        // match the given predicate.
        private static void TgtClearBy(string op, Func<FactionHQ, Unit, bool> predicate)
        {
            TargetListSelector sel = SceneSingleton<TargetListSelector>.i;
            if (sel == null) { Plugin.Log?.LogInfo($"[NOXMFD] {op}: TargetListSelector absent — ignored."); return; }

            GameManager.GetLocalAircraft(out Aircraft ac);
            if (ac == null || ac.weaponManager == null || ac.NetworkHQ == null) return;
            FactionHQ playerHQ = ac.NetworkHQ;

            List<Unit> targets = ac.weaponManager.GetTargetList();
            if (targets == null || targets.Count == 0) return;

            int cleared = 0;
            foreach (Unit unit in new List<Unit>(targets))   // copy: ForceDeselect mutates the live list
            {
                if (!predicate(playerHQ, unit)) continue;
                sel.ForceDeselect(unit);
                cleared++;
            }
            Plugin.Log?.LogInfo($"[NOXMFD] {op} — deselected {cleared} target(s).");
        }

        private static void TgtClearDatalink(CommandEnvelope env) => ClearDatalinkTargets();
        private static void TgtClearStale(CommandEnvelope env)    => ClearStaleTargets();

        // Internal, not private — same reasoning as Keybinds.CycleTargetFocus: the DATALINK/STALE
        // clear keybinds call these directly (Keybinds.cs) so they act regardless of SOI focus,
        // rather than only reaching the SOI-focused TGT display via the map-act browser round trip.
        internal static void ClearDatalinkTargets() => TgtClearBy("tgt.clear-datalink", IsDatalinkOnly);
        internal static void ClearStaleTargets()    => TgtClearBy("tgt.clear-stale", IsStale);

        // LASER toggle — keep only lased targets when on.
        private static void TgtLaser(CommandEnvelope env)
        {
            TargetListSelector sel = SceneSingleton<TargetListSelector>.i;
            if (sel == null || sel.toggleLaser == null) { Plugin.Log?.LogInfo("[NOXMFD] tgt.laser: unavailable — ignored."); return; }
            if (sel.toggleLaser.status == env.on) return;
            sel.toggleLaser.Set(env.on);
            Plugin.Log?.LogInfo($"[NOXMFD] tgt.laser = {env.on}.");
        }

        // HUD-follow toggle — mirror the filter to the HUD priority options. Set() fires the game's
        // OnToggleFollowHUD, which applies (on) or resets (off) the whole filter set.
        private static void TgtHud(CommandEnvelope env)
        {
            TargetListSelector sel = SceneSingleton<TargetListSelector>.i;
            if (sel == null || sel.toggleFollowHUD == null) { Plugin.Log?.LogInfo("[NOXMFD] tgt.hud: unavailable — ignored."); return; }
            if (sel.toggleFollowHUD.status == env.on) return;
            sel.toggleFollowHUD.Set(env.on);
            Plugin.Log?.LogInfo($"[NOXMFD] tgt.hud = {env.on}.");
        }

        // HUDOptions gates each unit's HUD icon: CheckMaximizeIcon() returns 0 when a unit's category
        // or type is off, so its marker shrinks to a minimized dot (enemy) or vanishes (friendly).
        // ApplyHUDSettings() fires OnApplyOptions so the HUD re-renders immediately rather than
        // after the ~1s idle refresh.
        //
        // hud.set flips one toggle within a group; the page reads the current state and names from
        // /hud-options (TelemetryServer.RefreshHudOptions), so indices here are always paired with
        // a list the page fetched from the same source — no hardcoded ordering.
        //   group "category" → listCategories[i]   (FRIENDLY/ENEMY/AIRCRAFT/MISSILES/VEHICLES/…)
        //   group "vehicle"  → listVehicleTypes[i] (TRUCK/UGV/LCV/…)
        //   group "building" → listBuildingTypes[i](CIV/FAC/RDR/…)
        private static void HudSet(CommandEnvelope env)
        {
            HUDOptions opt = SceneSingleton<HUDOptions>.i;
            if (opt == null) { Plugin.Log?.LogInfo("[NOXMFD] hud.set: HUDOptions unavailable — ignored."); return; }

            switch (env.group)
            {
                case "category":
                    if (!InList(opt.listCategories, env.index, "hud.set category")) return;
                    HUDOptions_Category cat = opt.listCategories[env.index];
                    if (cat == null || cat.maximized == env.on) return;   // null / already there
                    cat.Set(env.on);
                    break;
                case "vehicle":
                    if (!InList(opt.listVehicleTypes, env.index, "hud.set vehicle")) return;
                    HUDOptions_ToggleButton veh = opt.listVehicleTypes[env.index];
                    if (veh == null || veh.status == env.on) return;
                    veh.Set(env.on);
                    break;
                case "building":
                    if (!InList(opt.listBuildingTypes, env.index, "hud.set building")) return;
                    HUDOptions_ToggleButton bld = opt.listBuildingTypes[env.index];
                    if (bld == null || bld.status == env.on) return;
                    bld.Set(env.on);
                    break;
                default:
                    Plugin.Log?.LogInfo($"[NOXMFD] hud.set: unknown group '{env.group}' — ignored.");
                    return;
            }
            opt.ApplyHUDSettings();
            // Only updates the baseline while idle — an edit made mid A/A or A/G must not overwrite
            // what gets restored on exit.
            HudCombatModeFilters.CaptureIfIdle();
            Plugin.Log?.LogInfo($"[NOXMFD] hud.set {env.group}[{env.index}] = {env.on}.");
        }

        // Mode tabs (NAV/GUN/A2A/A2G/EW/LOG) — radio buttons, each carrying a saved priority preset.
        // ToggleButtons() does the radio flip and, via the button's own Set(), applies that preset's
        // category/vehicle/building priorities; ApplyHUDSettings() then re-renders the HUD at once.
        private static void HudMode(CommandEnvelope env)
        {
            HUDOptions opt = SceneSingleton<HUDOptions>.i;
            if (opt == null) { Plugin.Log?.LogInfo("[NOXMFD] hud.mode: HUDOptions unavailable — ignored."); return; }
            if (!InList(opt.listModes, env.index, "hud.mode")) return;
            HUDOptions_ToggleButton btn = opt.listModes[env.index];
            if (btn == null) { Plugin.Log?.LogInfo($"[NOXMFD] hud.mode[{env.index}] is null — ignored."); return; }
            opt.ToggleButtons(btn);
            opt.ApplyHUDSettings();
            HudCombatModeFilters.CaptureIfIdle();
            Plugin.Log?.LogInfo($"[NOXMFD] hud.mode = {env.index}.");
        }

        // The mod's OWN HudDeclutter flags (HudDeclutterConfig), not the game's HUDOptions. env.group
        // is "weapon" (top-right weapon/ammo/CM cluster), "minimap" (bottom-left corner map), "boxes"
        // (boxed heading/speed/alt readouts) or "feed" (native kill-feed ticker).
        // Writing the ConfigEntry persists it and fires SettingChanged, so the in-game F1 checkbox
        // follows; HudDeclutter reads the flag each tick and hides/restores within ~0.5s (the minimap
        // within a frame), so there's nothing to apply here. env.on is the desired HIDE state.
        private static void DeclutterSet(CommandEnvelope env)
        {
            switch (env.group)
            {
                case "weapon":  HudDeclutterConfig.SetHideWeaponAmmo(env.on); break;
                case "minimap": HudDeclutterConfig.SetHideMinimap(env.on);    break;
                case "boxes":   HudDeclutterConfig.SetHideTopBoxes(env.on);   break;
                case "feed":    HudDeclutterConfig.SetHideKillFeed(env.on);   break;
                default:
                    Plugin.Log?.LogInfo($"[NOXMFD] declutter.set: unknown group '{env.group}' — ignored.");
                    return;
            }
            Plugin.Log?.LogInfo($"[NOXMFD] declutter.set {env.group} hide={env.on}.");
        }

        // F-35 master-strip status icons — toggles the same systems the AVN annunciators already
        // read. gear/radar/eng go through the game's own networked Cmd calls, the same path the
        // immersion keybinds use; guns/assist/lights/turret are local, client-only toggles; nvg is a
        // player-camera setting, not per-aircraft, but still gated on a live aircraft so the icon
        // only works in flight, matching the row it lives in. Each game-side call already self-guards
        // on the airframe's own capability, so this just fires it — no capability check duplicated.
        private static void AvnToggle(CommandEnvelope env)
        {
            GameManager.GetLocalAircraft(out Aircraft ac);
            if (ac == null || ac.disabled) return;

            switch (env.group)
            {
                case "gear":   ac.SetGear(!ac.gearDeployed); break;
                case "radar":  ac.CmdToggleRadar(); break;
                case "guns":   ac.weaponManager?.ToggleGunsLinked(); break;
                case "eng":    ac.CmdToggleIgnition(); break;
                case "assist": ac.TogglePitchLimiter(); break;
                case "nvg":    NightVision.Toggle(); break;
                case "lights": ac.ToggleNavLights(); break;
                case "turret": SceneSingleton<CombatHUD>.i?.ToggleAutoControl(); break;
                default:
                    Plugin.Log?.LogInfo($"[NOXMFD] avn.toggle: unknown group '{env.group}' — ignored.");
                    return;
            }
            Plugin.Log?.LogInfo($"[NOXMFD] avn.toggle {env.group}.");
        }

        private static void AvnSet(CommandEnvelope env)
        {
            if (!LocalAircraft("avn.set", out Aircraft ac)) return;

            switch (env.group)
            {
                case "radar": Keybinds.SetRadar(ac, env.on);  break;
                case "eng":   Keybinds.SetEngine(ac, env.on); break;
                default:
                    Plugin.Log?.LogInfo($"[NOXMFD] avn.set: unknown group '{env.group}' — ignored.");
                    return;
            }
            Plugin.Log?.LogInfo($"[NOXMFD] avn.set {env.group} = {env.on}.");
        }

        // Bounds guard shared by the HUD handlers: true when index addresses a live element.
        private static bool InList<T>(System.Collections.Generic.List<T> list, int index, string who)
        {
            if (list == null || index < 0 || index >= list.Count)
            {
                Plugin.Log?.LogInfo($"[NOXMFD] {who}: index {index} out of range — ignored.");
                return false;
            }
            return true;
        }
    }
}
