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
        //
        // Also covers F1: an own-radar detection stays selectable even while the picture is jammed
        // (Radar.IsJammed() is a separate mechanic), but a plain faction-known/datalink track is not.
        [Theory]
        [InlineData(false, false, false, false)]
        [InlineData(true, false, false, true)]
        [InlineData(false, true, false, true)]
        [InlineData(true, true, false, true)]
        [InlineData(true, false, true, false)]   // faction-known alone: jammed, no radar -> blocked
        [InlineData(false, true, true, true)]    // own radar: stays selectable even while jammed
        [InlineData(true, true, true, true)]     // own radar wins regardless of faction-known
        public void IsSelectable_requires_own_radar_or_unjammed_faction_known(
            bool factionKnown, bool ownRadarDetected, bool pictureJamActive, bool expected)
        {
            Assert.Equal(expected, TargetSelectionPolicy.IsSelectable(factionKnown, ownRadarDetected, pictureJamActive));
        }
    }
}
