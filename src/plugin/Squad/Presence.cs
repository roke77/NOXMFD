using System.Collections.Generic;
using UnityEngine;

namespace NOXMFD
{
    // ── NOXMFD presence ─────────────────────────────────────────────────────────
    // Detects which faction-mates in the current match are also running NOXMFD, so SQD's invite
    // roster (PlayerRoster.cs) can be limited to players who could actually receive an invite —
    // inviting someone without the mod just sits there forever unanswered (Squad.cs's invites have
    // no timeout), which reads as a bug rather than "they don't have it."
    //
    // Mechanism: a periodic broadcast, not a targeted ping-and-wait — there's no way to know in
    // advance who has the mod, so every instance just announces itself to the whole faction roster
    // on a timer, and every instance listens for the same announcement. A TTL on each received
    // announcement (rather than an explicit "goodbye") means someone who quits or force-closes
    // ages out naturally within a couple of missed beats. Squad.CheckLiveness uses the same beats, with
    // its own longer allowance (IsLost), to detect a leader/member who crashed or force-quit with no
    // graceful sqd.leave/kick/disband to send.
    //
    // The beat also carries the sender's own identity (FactionIdentity.cs, issues #106 and #107,
    // docs/faction-broadcast.md, docs/self-callsign.md): its own callsign, its squad's callsign/flight
    // and leader, its fuel and its squad protocol version. Every instance keeps what it hears in a
    // table, which is how a pilot sees every callsign and every squad. An older client sends and
    // ignores a payload this build can't read, so mixed versions still see each other as running
    // NOXMFD (and the table tells them apart, SquadVersion).
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

        // Squad liveness (IsLost) is deliberately slower than the invite-list TTL above: dropping a
        // squadmate is destructive, hiding them from the list for a moment is not. Beats only flow
        // while BOTH pilots sit in a faction, so silence is measured from the latest of: their last
        // beat, us entering a faction, and them appearing in our faction roster. A squadmate still
        // loading a mission (absent from the roster) gets the longer allowance.
        private const float InFactionLostSeconds = 30f;
        private const float AbsentLostSeconds    = 120f;

        private static float _lastRosterTick = -999f;   // Time.unscaledTime of the last NoteRoster()
        private static float _enteredAt;                // when the current in-faction stretch began

        private static readonly HashSet<ulong> _present = new HashSet<ulong>();   // for transition logging

        private static float _nextBroadcast;   // Time.unscaledTime; 0 forces an immediate first beat
        private static long  _drainedSeq;      // our own cursor into Squadron's shared inbox

        private static readonly Dictionary<ulong, float> _lastSeen = new Dictionary<ulong, float>();
        private static readonly FactionIdentity.Table _identities = new FactionIdentity.Table();

        // The identity last sent, minus fuel. A change (a new callsign, a join, leave or rename) is
        // announced on the next tick instead of waiting out the 5 s beat, so it reaches the faction
        // within a second.
        private static string _lastSquadKey = string.Empty;

        // Called once per second from TelemetryReader's slow tick, alongside PlayerRoster.Refresh —
        // same cadence, same caller, so the roster and the presence table it filters against never
        // drift more than a tick apart. `peers` is the current faction roster (self already
        // excluded by PlayerRoster) — broadcasting to exactly that set, not "everyone we've ever
        // seen," means someone who left the match stops being pinged immediately rather than
        // lingering.
        // `myFuel` is null with no local aircraft (main menu, between missions) — nothing real to say.
        internal static void Tick(IEnumerable<ulong> peers, float? myFuel)
        {
            if (!Squadron.Ready) return;
            FactionIdentity.Identity me = Squad.SelfIdentity(myFuel);
            string squadKey = me.Designation + "/" + me.SquadCallsign + me.SquadFlight + "/" + me.LeaderId;
            bool changed = squadKey != _lastSquadKey;
            if (!changed && Time.unscaledTime < _nextBroadcast) return;
            _lastSquadKey = squadKey;
            _nextBroadcast = Time.unscaledTime + BroadcastIntervalSeconds;
            Squadron.SendToAll(peers, MessageType, FactionIdentity.Serialize(me));
        }

        // Called by PlayerRoster.Refresh every tick it has a local faction. A gap of more than a few
        // ticks means we were out of a faction (between missions, loading) and beats stopped both
        // ways, so a new stretch starts.
        internal static void NoteRoster()
        {
            float now = Time.unscaledTime;
            if (now - _lastRosterTick > 3f) _enteredAt = now;
            _lastRosterTick = now;
        }

        // Logs peers appearing/disappearing from the presence table (1 Hz, from PlayerRoster.Refresh).
        internal static void LogTransitions()
        {
            foreach (var kv in _lastSeen)
            {
                bool now = HasNoxmfd(kv.Key);
                if (now && _present.Add(kv.Key))
                    Squadron.LinkLog($"presence: {kv.Key} is running NOXMFD");
                else if (!now && _present.Remove(kv.Key))
                    Squadron.LinkLog($"presence: no beat from {kv.Key} for {Time.unscaledTime - kv.Value:F0}s (inFaction={PlayerRoster.InFaction(kv.Key)})");
            }
        }

        // True when a squadmate should be treated as gone: no beat for long enough while beats should
        // be flowing. False whenever we are not in a faction ourselves — nothing arrives then.
        internal static bool IsLost(ulong steamId, out float silentFor)
        {
            silentFor = 0f;
            float now = Time.unscaledTime;
            if (now - _lastRosterTick > 3f) return false;
            _lastSeen.TryGetValue(steamId, out float seen);
            float since = PlayerRoster.FactionSince(steamId, out bool inFaction);
            silentFor = now - Mathf.Max(seen, Mathf.Max(_enteredAt, since));
            return silentFor >= (inFaction ? InFactionLostSeconds : AbsentLostSeconds);
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
                // An empty or unparseable payload (an older client, a bad record) is still a beat.
                if (FactionIdentity.TryParse(m.Payload, out FactionIdentity.Identity id))
                    _identities.Note(m.From, id, Time.unscaledTime);
            }
        }

        // SteamID -> designation for every pilot heard with a callsign. Squad.Designations() adds our own.
        internal static Dictionary<ulong, string> Designations() =>
            _identities.Designations(Time.unscaledTime, PlayerRoster.InFaction);

        // Every other squad heard of, for SQD's faction list (the viewer's own squad, led by
        // `excludeLeader`, comes from Squad.cs's roster instead).
        internal static List<FactionIdentity.FactionSquad> Squads(ulong excludeLeader) =>
            _identities.Squads(Time.unscaledTime, PlayerRoster.InFaction, excludeLeader);

        // The squad protocol a pilot announced; below FactionIdentity.SquadProtocolVersion (0 when never
        // heard from, or heard only through an older client) means they can't join a squad with us.
        internal static int SquadVersion(ulong steamId) => _identities.SquadVersion(steamId);

        // A faction-mate's fuel: its own broadcast in the identity record, else the `fuel` message an
        // older client still sends (FuelBroadcast.cs; removable once those clients are gone).
        internal static float? FuelFor(ulong steamId) =>
            _identities.FuelFor(steamId, Time.unscaledTime) ?? FuelBroadcast.FuelFor(steamId);

        // True if we've heard from this peer within the TTL — i.e. they're both in this match AND
        // running a live NOXMFD instance right now.
        internal static bool HasNoxmfd(ulong steamId) =>
            _lastSeen.TryGetValue(steamId, out float t) && Time.unscaledTime - t < TtlSeconds;
    }
}
