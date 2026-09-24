using System.Collections.Generic;
using System.Reflection;
using Mirage.Events;
using NuclearOption.Networking;
using Steamworks;

namespace NOXMFD
{
    // Squad designations as in-game player names (docs/squad-callsign-names.md). Every client
    // resolves every player's name itself (Player.GetPlayerName → Steam) and caches the resulting
    // PlayerName on the Player and in UnitRegistry.cachedPlayerNames. Swapping that cached object
    // for one named after the designation renames the player everywhere the game reads a name —
    // map, chat, scoreboard, HUD markers — with the game's own sanitizing, filters, player number
    // and server tag still applied. RawSteamName keeps the real Steam name, so vote-kick is untouched.
    // No Harmony patch: two private-field reads/writes, failing safe (names stay Steam names) with
    // one logged warning if a game update renames either field.
    internal static class PlayerNameOverride
    {
        private static readonly FieldInfo? CacheField =
            typeof(Player).GetField("_playerNameCache", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? ResolvedField =
            typeof(Player).GetField("_onNameResolved", BindingFlags.NonPublic | BindingFlags.Instance);
        private static bool _loggedFailure;

        // ponytail: temporary field diagnostics for the squad-designation release, so players can
        // send logs of renames and slot changes. Remove this and every SquadLog call once reports
        // settle (grep SquadLog).
        internal static void SquadLog(string msg) => Plugin.Log?.LogInfo("[NOXMFD squad-names] " + msg);

        private sealed class Applied
        {
            internal PlayerName Original = null!;   // the game's own object, restored on leave
            internal PlayerName Renamed = null!;
            internal string Designation = string.Empty;
        }

        // SteamID → the rename currently in force. Main thread only.
        private static Dictionary<ulong, Applied> _applied = new Dictionary<ulong, Applied>();

        // Makes every player's cached name match `wanted` (SteamID → designation): renames players
        // in it, restores anyone renamed earlier who isn't any more, and keeps renamed players'
        // aircraft labels in step. ponytail: called at 1 Hz from TelemetryReader's slow tick rather
        // than on each roster event, so a rename lands up to a second late; the same pass also
        // re-applies after the game clears its name cache on a mission change.
        internal static void Reconcile(IReadOnlyDictionary<ulong, string> wanted)
        {
            if (CacheField == null || ResolvedField == null)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Plugin.Log?.LogWarning("[NOXMFD] Player name fields not found; squad designations won't replace in-game names.");
                }
                return;
            }
            if (wanted.Count == 0 && _applied.Count == 0) return;

            var next = new Dictionary<ulong, Applied>();
            try
            {
                foreach (Player p in UnitRegistry.playerLookup.Values)
                {
                    if (p == null) continue;
                    ulong id = p.SteamID;
                    if (id == 0) continue;
                    _applied.TryGetValue(id, out Applied? cur);
                    if (!(CacheField.GetValue(p) is PlayerName live)) continue;   // not resolved yet — next tick
                    bool liveIsOurs = cur != null && ReferenceEquals(live, cur.Renamed);

                    if (wanted.TryGetValue(id, out string? designation))
                    {
                        if (liveIsOurs && cur!.Designation == designation)
                        {
                            UnitRegistry.cachedPlayerNames[p.CSteamID] = cur.Renamed;   // survives a cache clear
                            next[id] = cur;
                        }
                        else
                        {
                            PlayerName original = liveIsOurs ? cur!.Original : live;
                            var renamed = new PlayerName(original.RawSteamName, Sanitize(designation));
                            Set(p, renamed);
                            SquadLog($"rename {id} '{original.SanitizedName}' -> '{designation}'" +
                                     (cur != null && !liveIsOurs ? " (re-applied after the game reset its name cache)" : ""));
                            next[id] = new Applied { Original = original, Renamed = renamed, Designation = designation };
                        }
                    }
                    else if (liveIsOurs)
                    {
                        Set(p, cur!.Original);
                        SquadLog($"restore {id} -> '{cur.Original.SanitizedName}'");
                    }
                    else continue;

                    RelabelAircraft(p);
                }
            }
            catch (System.Exception ex)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Plugin.Log?.LogWarning($"[NOXMFD] Renaming players to squad designations failed: {ex}");
                }
            }

            // Anyone not settled above (left the match, name not resolved yet, or an exception)
            // keeps their entry while still wanted, so a rejoin — which picks the renamed object
            // back up from cachedPlayerNames — is still recognised as ours. Anyone no longer wanted
            // gets the original back in that cache (a player still here was restored by Set).
            foreach (var kv in _applied)
            {
                if (next.ContainsKey(kv.Key)) continue;
                if (wanted.ContainsKey(kv.Key)) { next[kv.Key] = kv.Value; continue; }
                var key = new CSteamID(kv.Key);
                if (UnitRegistry.cachedPlayerNames.TryGetValue(key, out PlayerName cached) && ReferenceEquals(cached, kv.Value.Renamed))
                {
                    UnitRegistry.cachedPlayerNames[key] = kv.Value.Original;
                    SquadLog($"restore {kv.Key} (not in match) -> '{kv.Value.Original.SanitizedName}'");
                }
            }
            _applied = next;
        }

        // The real Steam name of a player shown under a designation, or "" when they aren't renamed.
        internal static string SteamNameIfRenamed(ulong steamId) =>
            _applied.TryGetValue(steamId, out Applied? a) ? a.Original.SanitizedName : string.Empty;

        // The name the game would show without the rename — for NOXMFD's own lists (SQD invite
        // roster) that must keep listing people by Steam name.
        internal static string OriginalDisplayName(Player p) =>
            _applied.TryGetValue(p.SteamID, out Applied? a)
                ? a.Original.GetDisplayName(PlayerNameContext.Other)
                : p.GetDisplayName(PlayerNameContext.Other) ?? string.Empty;

        // "TALON 1-3 [F-16]" → "TALON 1-3 (SteamName) [F-16]" for a renamed player's unit label
        // (also "... pilot" after an ejection); anything else is returned unchanged.
        internal static string WithSteamName(string unitName)
        {
            if (string.IsNullOrEmpty(unitName)) return unitName;
            foreach (Applied a in _applied.Values)
            {
                string? withSteam = SquadDesignations.InsertSteamName(
                    unitName, a.Renamed.GetDisplayName(PlayerNameContext.Other), a.Original.SanitizedName);
                if (withSteam != null) return withSteam;
            }
            return unitName;
        }

        // The game's own name sanitizing (Player.GetPlayerName's resolve path).
        private static string Sanitize(string name) =>
            name.SanitizeRichText(32).ReplaceCharactersNotInFont(GameAssets.i.playerNameFont);

        private static void Set(Player p, PlayerName name)
        {
            CacheField!.SetValue(p, name);
            UnitRegistry.cachedPlayerNames[p.CSteamID] = name;
            p.RebuildNameCache();   // player number + server tag, per the viewer's own settings
            // AddLateEvent replays its last value to later listeners — an aircraft spawned after
            // this labels itself (Aircraft.OwnerNameResolved) with this name.
            (ResolvedField!.GetValue(p) as AddLateEvent<PlayerName>)?.Invoke(name);
        }

        // Aircraft.OwnerNameResolved builds "<name> [<type>]" once and unsubscribes, and the kill
        // feed prints that label, so a rename after spawn has to rewrite it here.
        private static void RelabelAircraft(Player p)
        {
            string label = p.GetDisplayName(PlayerNameContext.Other) + " [";
            foreach (Aircraft ac in UnitRegistry.allAircraft)
            {
                if (ac == null || ac.Player != p || ac.definition == null) continue;
                string want = label + ac.definition.unitName + "]";
                if (ac.unitName == want) continue;
                SquadLog($"relabel aircraft {ac.persistentID} '{ac.unitName}' -> '{want}'");
                ac.unitName = want;
                if (UnitRegistry.TryGetPersistentUnit(ac.persistentID, out PersistentUnit pu) && pu != null)
                    pu.unitName = want;
            }
        }
    }
}
