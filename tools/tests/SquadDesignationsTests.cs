using System.Collections.Generic;
using NOXMFD;

namespace NOXMFD.Tests
{
    public class SquadDesignationsTests
    {
        [Fact]
        public void Build_numbers_the_leader_1_and_members_from_2_in_order()
        {
            var d = SquadDesignations.Build("TALON", 3, 100, new List<ulong> { 200, 300 });
            Assert.Equal("TALON 3-1", d[100]);
            Assert.Equal("TALON 3-2", d[200]);
            Assert.Equal("TALON 3-3", d[300]);
            Assert.Equal(3, d.Count);
        }

        [Fact]
        public void Build_skips_zero_ids_and_falls_back_to_SQD_without_a_callsign()
        {
            var d = SquadDesignations.Build("", 1, 0, new List<ulong> { 0, 200 });
            Assert.False(d.ContainsKey(0));
            Assert.Equal("SQD 1-3", d[200]);   // position still counts: the number is the slot, not the id
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
        public void TrySwap_moves_one_step_and_refuses_to_leave_the_list()
        {
            var list = new List<int> { 1, 2, 3 };
            Assert.True(SquadDesignations.TrySwap(list, 0, 1));
            Assert.Equal(new[] { 2, 1, 3 }, list);
            Assert.True(SquadDesignations.TrySwap(list, 2, -1));
            Assert.Equal(new[] { 2, 3, 1 }, list);

            Assert.False(SquadDesignations.TrySwap(list, 0, -1));   // already first
            Assert.False(SquadDesignations.TrySwap(list, 2, 1));    // already last
            Assert.False(SquadDesignations.TrySwap(list, -1, 1));   // member not found
            Assert.False(SquadDesignations.TrySwap(list, 1, 0));
            Assert.Equal(new[] { 2, 3, 1 }, list);
        }
    }
}
