using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace NOXMFD
{
    // Peer-reported fuel (docs/atc-extension-support.md, item 1) — the game has no networked value
    // for another aircraft's true current fuel: Aircraft.GetFuelLevel() sums FuelTank.fuelMass,
    // which only updates while aircraft.LocalSim is true, i.e. only for the aircraft the reading
    // client itself is flying. So instead, each NOXMFD instance broadcasts its OWN accurate reading
    // to the whole faction, the same way Presence.cs already broadcasts "I'm running NOXMFD" —
    // same transport, same peer list (PlayerRoster.Refresh already builds one for Presence), same
    // TTL-based staleness model. Faction-wide, not squad-scoped — Presence's own broadcast already
    // proves this transport handles a whole-faction fan-out fine, so fuel doesn't need the narrower
    // relay-through-the-squad-leader shape a star-topology squad message would.
    //
    // Peer-reported, not server-authoritative: a modified client could broadcast a fake value, the
    // same trust boundary squadron-transport.md calls out for every payload it carries. Drain clamps
    // to a valid ratio so a malformed or malicious payload can't smuggle NaN/Infinity/out-of-range
    // values into the UI — it can't stop someone lying that they're full when they're not, but this
    // is a display convenience, not something game-integrity-critical.
    internal static class FuelBroadcast
    {
        private const string MessageType = "fuel";

        // Fuel changes slowly relative to Presence's "am I still here" beat — no need for the same
        // 5s cadence. TTL is 3x the interval, same reasoning as Presence's own.
        private const float BroadcastIntervalSeconds = 15f;
        private const float TtlSeconds = 3f * BroadcastIntervalSeconds;

        private static float _nextBroadcast;   // Time.unscaledTime; 0 forces an immediate first beat
        private static long  _drainedSeq;      // our own cursor into Squadron's shared inbox

        private static readonly Dictionary<ulong, (float Fuel, float At)> _lastReported =
            new Dictionary<ulong, (float Fuel, float At)>();

        // Called from PlayerRoster.Refresh, right alongside Presence.Tick — same peer list, same
        // 1 Hz caller, so this needs no scan of its own. myFuel is null when there's no local
        // aircraft right now (main menu, between missions) — nothing real to broadcast.
        internal static void Tick(IEnumerable<ulong> peers, float? myFuel)
        {
            if (!Squadron.Ready || myFuel == null) return;
            if (Time.unscaledTime < _nextBroadcast) return;
            _nextBroadcast = Time.unscaledTime + BroadcastIntervalSeconds;
            float ratio = Mathf.Clamp01(myFuel.Value);
            Squadron.SendToAll(peers, MessageType, ratio.ToString("0.000", CultureInfo.InvariantCulture));
        }

        // Called once per frame from MissionLifecycle, right after Squadron.Poll() — same spot
        // Presence.Drain()/Squad.Drain() are called from, an independent cursor into the same
        // shared inbox.
        internal static void Drain()
        {
            var inbound = Squadron.Since(_drainedSeq);
            foreach (var m in inbound)
            {
                _drainedSeq = m.Seq;
                if (m.Type != MessageType) continue;
                if (!float.TryParse(m.Payload, NumberStyles.Float, CultureInfo.InvariantCulture, out float ratio))
                    continue;   // malformed payload from a stale/mismatched mod version — drop it
                _lastReported[m.From] = (Mathf.Clamp01(ratio), Time.unscaledTime);
            }
        }

        // null if we've never heard from this peer, or not within the TTL (they left, closed
        // NOXMFD, or simply haven't broadcast yet) — the caller (TelemetryReader.BuildUnits) shows
        // "no data" rather than a stale last-known value.
        internal static float? FuelFor(ulong steamId) =>
            _lastReported.TryGetValue(steamId, out var r) && Time.unscaledTime - r.At < TtlSeconds
                ? r.Fuel : (float?)null;
    }
}
