using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace NOXMFD
{
    // ── NOXMFD presence ─────────────────────────────────────────────────────────
    // Detects which faction-mates in the current match are also running NOXMFD, so SQD's invite
    // roster (PlayerRoster.cs) can be limited to players who could actually receive an invite —
    // inviting someone without the mod just times out silently 15s later (Squad.cs's
    // InviteTimeoutSeconds), which reads as a bug rather than "they don't have it."
    //
    // Mechanism: a periodic broadcast, not a targeted ping-and-wait — there's no way to know in
    // advance who has the mod, so every instance just announces itself to the whole faction roster
    // on a timer, and every instance listens for the same announcement. A TTL on each received
    // announcement (rather than an explicit "goodbye") means someone who quits or force-closes
    // ages out naturally within a couple of missed beats, the same reasoning Squad.cs's own
    // leader-dropout gap accepts for the harder case.
    //
    // Rides Squadron.cs's transport (same channel, same trust model) with its own independent
    // drain cursor — Squadron.Since() is designed for exactly this: Squad.cs and this class each
    // read the shared inbox at their own pace, ignoring message types they don't own.
    internal static class Presence
    {
        private const string MessageType = "presence";

        // How often we announce ourselves, and how long a received announcement stays valid. TTL is
        // 3x the interval — tolerates a couple of missed/delayed beats before dropping someone,
        // without the roster lagging noticeably behind an actual disconnect.
        private const float BroadcastIntervalSeconds = 5f;
        private const float TtlSeconds = 3f * BroadcastIntervalSeconds;

        private static float _nextBroadcast;   // Time.unscaledTime; 0 forces an immediate first beat
        private static long  _drainedSeq;      // our own cursor into Squadron's shared inbox

        private static readonly Dictionary<ulong, float> _lastSeen = new Dictionary<ulong, float>();

        // ── LAN-roster investigation (temporary) ──────────────────────────────────────────────
        // A LAN-joined Player's SteamID SyncVar may never replicate (it normally rides Steam's own
        // lobby/matchmaking join, which raw UDP join skips), so PlayerRoster filters those players
        // out before Presence ever sees them — a peer id typed here bypasses that filter entirely,
        // to isolate whether Steam P2P itself still works over a LAN connection independent of
        // identity discovery. Hidden, empty by default; remove this block once the investigation
        // concludes either way.
        private static ConfigEntry<string>? _debugPeerIds;

        internal static void Bind(ConfigFile config)
        {
            var hidden = new ConfigurationManagerAttributes { Browsable = false };
            _debugPeerIds = config.Bind("Squad Debug", "PeerSteamIds", "",
                new ConfigDescription(
                    "Comma-separated SteamID64s to presence-ping directly, bypassing the faction roster. " +
                    "Temporary LAN-roster investigation aid — leave empty.", null, hidden));
        }

        private static IEnumerable<ulong> DebugPeers()
        {
            string raw = _debugPeerIds?.Value ?? "";
            if (raw.Length == 0) yield break;
            foreach (string part in raw.Split(','))
            {
                if (ulong.TryParse(part.Trim(), out ulong id) && id != 0) yield return id;
            }
        }

        private static bool IsDebugPeer(ulong id)
        {
            foreach (ulong debugId in DebugPeers()) { if (debugId == id) return true; }
            return false;
        }

        // Called once per second from TelemetryReader's slow tick, alongside PlayerRoster.Refresh —
        // same cadence, same caller, so the roster and the presence table it filters against never
        // drift more than a tick apart. `peers` is the current faction roster (self already
        // excluded by PlayerRoster) — broadcasting to exactly that set, not "everyone we've ever
        // seen," means someone who left the match stops being pinged immediately rather than
        // lingering.
        internal static void Tick(IEnumerable<ulong> peers)
        {
            if (!Squadron.Ready) return;
            if (Time.unscaledTime < _nextBroadcast) return;
            _nextBroadcast = Time.unscaledTime + BroadcastIntervalSeconds;
            Squadron.SendToAll(peers, MessageType, string.Empty);
            DebugPingConfiguredPeers();
        }

        // LAN-roster investigation only — sent and logged independently of the normal broadcast
        // above, since a debug peer is by definition NOT in `peers` (PlayerRoster never resolved
        // its SteamID from the faction roster in the first place; that's the gap being tested).
        private static void DebugPingConfiguredPeers()
        {
            bool any = false;
            foreach (ulong id in DebugPeers())
            {
                if (!any)
                {
                    any = true;
                    Plugin.Log?.LogInfo($"[SQD-TEST] self={Squadron.SelfId()} ready={Squadron.Ready}");
                }
                bool ok = Squadron.SendTo(id, MessageType, string.Empty);
                Plugin.Log?.LogInfo($"[SQD-TEST] presence send peer={id} result={(ok ? "OK" : "FAIL")}");
                Plugin.Log?.LogInfo($"[SQD-TEST] peer={id} present={HasNoxmfd(id)}");
            }
        }

        // Called once per frame from MissionLifecycle, right after Squadron.Poll() — same spot
        // Squad.Drain() is called from, an independent cursor into the same shared inbox.
        internal static void Drain()
        {
            var inbound = Squadron.Since(_drainedSeq);
            foreach (var m in inbound)
            {
                _drainedSeq = m.Seq;
                if (m.Type != MessageType) continue;
                _lastSeen[m.From] = Time.unscaledTime;
                if (IsDebugPeer(m.From)) Plugin.Log?.LogInfo($"[SQD-TEST] presence received peer={m.From}");
            }
        }

        // True if we've heard from this peer within the TTL — i.e. they're both in this match AND
        // running a live NOXMFD instance right now.
        internal static bool HasNoxmfd(ulong steamId) =>
            _lastSeen.TryGetValue(steamId, out float t) && Time.unscaledTime - t < TtlSeconds;
    }
}
