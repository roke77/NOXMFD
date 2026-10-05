using System;
using System.Collections.Generic;

namespace NOXMFD
{
    // Who gets the presence beat, pure part (docs/squadron-transport.md, "Vanilla players and the
    // presence beat"). BCL-only so tools/tests links it directly; Presence.cs is the live glue.
    //
    // A faction-mate we have heard from (or a squadmate) gets every beat. Anyone else may not run
    // NOXMFD, and a SteamNetworkingMessages session to a player without it is never accepted: their
    // Steam client holds the connection half-open, and a second connect request landing on it trips
    // Steam's symmetric-connect asserts under the networking lock the game's own connection also
    // needs, stalling their match traffic. So an unknown peer gets only a few probes, spaced well
    // past Steam's ~10 s connect timeout, then nothing for as long as they stay in the roster.
    // Discovery still completes: a NOXMFD peer that hears a probe starts beating back, and one that
    // joins later probes us itself.
    internal sealed class PresenceProbes
    {
        // Seconds after a peer is first seen unknown in the roster.
        internal static readonly float[] ProbeOffsets = { 0f, 60f, 300f };

        private struct State { public float FirstSeen; public int Sent; }
        private readonly Dictionary<ulong, State> _state = new Dictionary<ulong, State>();
        private readonly HashSet<ulong> _present = new HashSet<ulong>();
        private readonly List<ulong> _gone = new List<ulong>();

        // Splits this tick's roster into `beat` (known peers: send when the beat is due) and `probe`
        // (unknown peers whose next probe is due now). `close` is the subset of `probe` whose earlier
        // probe went unanswered: its failed session has to be closed so the probe opens a fresh one.
        // A peer leaving the roster forgets its schedule, so rejoining starts it over.
        internal void Plan(IEnumerable<ulong> peers, Func<ulong, bool> known, float now,
                           List<ulong> beat, List<ulong> probe, List<ulong> close)
        {
            beat.Clear(); probe.Clear(); close.Clear(); _present.Clear();
            foreach (ulong p in peers)
            {
                _present.Add(p);
                if (known(p)) { _state.Remove(p); beat.Add(p); continue; }
                if (!_state.TryGetValue(p, out State s)) s = new State { FirstSeen = now };
                if (s.Sent < ProbeOffsets.Length && now - s.FirstSeen >= ProbeOffsets[s.Sent])
                {
                    if (s.Sent > 0) close.Add(p);
                    probe.Add(p);
                    s.Sent++;
                }
                _state[p] = s;
            }
            _gone.Clear();
            foreach (ulong p in _state.Keys) if (!_present.Contains(p)) _gone.Add(p);
            foreach (ulong p in _gone) _state.Remove(p);
        }
    }
}
