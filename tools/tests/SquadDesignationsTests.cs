using System.Collections.Generic;
using NOXMFD;

namespace NOXMFD.Tests
{
    public class SquadDesignationsTests
    {
        [Fact]
        public void Build_numbers_the_leader_1_and_members_by_their_own_slot()
        {
            var d = SquadDesignations.Build("TALON", 3, 100, new List<(ulong, int)> { (200, 2), (300, 4) });
            Assert.Equal("TALON 3-1", d[100]);
            Assert.Equal("TALON 3-2", d[200]);
            Assert.Equal("TALON 3-4", d[300]);   // slot 3 is a hole — 300 keeps its number
            Assert.Equal(3, d.Count);
        }

        [Fact]
        public void Build_skips_zero_ids_and_falls_back_to_SQD_without_a_callsign()
        {
            var d = SquadDesignations.Build("", 1, 0, new List<(ulong, int)> { (0, 2), (200, 3) });
            Assert.False(d.ContainsKey(0));
            Assert.Equal("SQD 1-3", d[200]);
        }

        [Fact]
        public void InsertSteamName_matches_only_the_whole_shown_name()
        {
            Assert.Equal("TALON 1-3 (Roke) [F-16]", SquadDesignations.InsertSteamName("TALON 1-3 [F-16]", "TALON 1-3", "Roke"));
            Assert.Equal("TALON 1-3 (Roke) pilot", SquadDesignations.InsertSteamName("TALON 1-3 pilot", "TALON 1-3", "Roke"));
            Assert.Null(SquadDesignations.InsertSteamName("TALON 1-30 [F-16]", "TALON 1-3", "Roke"));   // not a prefix of a longer name
            Assert.Null(SquadDesignations.InsertSteamName("Havoc [F-16]", "TALON 1-3", "Roke"));
            Assert.Null(SquadDesignations.InsertSteamName("TALON 1-3", "TALON 1-3", "Roke"));           // bare name, no label
            Assert.Null(SquadDesignations.InsertSteamName("x [F-16]", "", "Roke"));
        }

        [Fact]
        public void FirstFreeSlot_fills_the_lowest_hole_before_growing()
        {
            Assert.Equal(2, SquadDesignations.FirstFreeSlot(new int[0]));
            Assert.Equal(3, SquadDesignations.FirstFreeSlot(new[] { 2, 4 }));
            Assert.Equal(5, SquadDesignations.FirstFreeSlot(new[] { 4, 2, 3 }));
        }

        [Fact]
        public void MoveTarget_steps_into_any_neighbour_within_2_to_the_highest_held_slot()
        {
            Assert.Equal(3, SquadDesignations.MoveTarget(4, -1, 4));    // up into a hole or a member
            Assert.Equal(4, SquadDesignations.MoveTarget(3, 1, 4));
            Assert.Equal(-1, SquadDesignations.MoveTarget(2, -1, 4));   // -1 stays the leader's
            Assert.Equal(-1, SquadDesignations.MoveTarget(4, 1, 4));    // not past the last held slot
            Assert.Equal(-1, SquadDesignations.MoveTarget(3, 0, 4));
        }
    }
}
