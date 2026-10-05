using System.Collections.Generic;
using NOXMFD;

namespace NOXMFD.Tests
{
    public class PresenceProbesTests
    {
        private readonly PresenceProbes _probes = new PresenceProbes();
        private readonly List<ulong> _beat = new List<ulong>();
        private readonly List<ulong> _probe = new List<ulong>();
        private readonly List<ulong> _close = new List<ulong>();
        private readonly HashSet<ulong> _known = new HashSet<ulong>();

        private void Plan(float now, params ulong[] peers) =>
            _probes.Plan(peers, _known.Contains, now, _beat, _probe, _close);

        [Fact]
        public void KnownPeerIsBeatenNeverProbed()
        {
            _known.Add(1);
            Plan(0f, 1);
            Assert.Equal(new ulong[] { 1 }, _beat);
            Assert.Empty(_probe);
        }

        [Fact]
        public void UnknownPeerGetsOnlyTheScheduledProbesThenSilence()
        {
            int probes = 0, closes = 0;
            for (int t = 0; t <= 3600; t++)
            {
                Plan(t, 2);
                Assert.Empty(_beat);
                if (_probe.Count > 0)
                {
                    Assert.Equal(PresenceProbes.ProbeOffsets[probes], (float)t);
                    probes++;
                }
                closes += _close.Count;
            }
            Assert.Equal(PresenceProbes.ProbeOffsets.Length, probes);
            Assert.Equal(PresenceProbes.ProbeOffsets.Length - 1, closes);   // every retry closes the unanswered session first
        }

        [Fact]
        public void LeavingTheRosterRestartsTheSchedule()
        {
            for (int t = 0; t <= 400; t++) Plan(t, 3);   // schedule exhausted
            Plan(401f);                                  // left the roster
            Plan(402f, 3);                               // rejoined
            Assert.Equal(new ulong[] { 3 }, _probe);
            Assert.Empty(_close);
        }

        [Fact]
        public void PeerHeardFromMidScheduleSwitchesToBeats()
        {
            Plan(0f, 4);
            _known.Add(4);
            Plan(60f, 4);
            Assert.Equal(new ulong[] { 4 }, _beat);
            Assert.Empty(_probe);
        }
    }
}
