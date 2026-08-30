using NOXMFD;

namespace NOXMFD.Tests
{
    public class TargetSelectionPolicyTests
    {
        // Regression test for F3 (docs/jamming-contact-telemetry-hardening.md): target.select
        // previously accepted any live persistentID with no visibility check at all, letting a
        // caller enumerate the sequential id counter and select units never disclosed to the
        // player — confirmed live against a hidden carrier and hidden SAM sites. Locks in that a
        // unit must be reachable through at least one channel the player already has.
        [Theory]
        [InlineData(false, false, false)]
        [InlineData(true, false, true)]
        [InlineData(false, true, true)]
        [InlineData(true, true, true)]
        public void IsSelectable_requires_faction_known_or_own_radar(bool factionKnown, bool ownRadarDetected, bool expected)
        {
            Assert.Equal(expected, TargetSelectionPolicy.IsSelectable(factionKnown, ownRadarDetected));
        }
    }
}
