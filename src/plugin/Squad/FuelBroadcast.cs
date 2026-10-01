using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace NOXMFD
{
    // Receive side of the standalone `fuel` message (docs/atc-extension-support.md, item 1), kept for
    // older clients: a current client sends its fuel inside the Presence beat's identity record
    // (FactionIdentity.cs), and Presence.FuelFor prefers that. Remove this class once those clients
    // are gone.
    //
    // The game has no networked value for another aircraft's true current fuel:
    // Aircraft.GetFuelLevel() sums FuelTank.fuelMass, which only updates while aircraft.LocalSim is
    // true, i.e. only for the aircraft the reading client itself is flying.
    //
    // Peer-reported, not server-authoritative: a modified client could broadcast a fake value, the
    // same trust boundary squadron-transport.md calls out for every payload it carries. Drain clamps
    // to a valid ratio so a malformed or malicious payload can't smuggle NaN/Infinity/out-of-range
    // values into the UI — it can't stop someone lying that they're full when they're not, but this
    // is a display convenience, not something game-integrity-critical.
    internal static class FuelBroadcast
    {
        private const string MessageType = "fuel";

        // An older client broadcasts every 15 s; the TTL is 3x that.
        private const float TtlSeconds = 45f;

        private static long  _drainedSeq;      // our own cursor into Squadron's shared inbox

        private static readonly Dictionary<ulong, (float Fuel, float At)> _lastReported =
            new Dictionary<ulong, (float Fuel, float At)>();

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
