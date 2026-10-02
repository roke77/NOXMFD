using NOXMFD;

namespace NOXMFD.Tests
{
    public class SquadDesignationsTests
    {
        [Fact]
        public void Format_joins_callsign_flight_and_number()
        {
            Assert.Equal("TALON 3-4", SquadDesignations.Format("TALON", 3, 4));
        }

        [Fact]
        public void Format_falls_back_to_SQD_without_a_callsign()
        {
            Assert.Equal("SQD 1-3", SquadDesignations.Format("", 1, 3));
        }

        [Fact]
        public void Label_adds_the_steam_name_to_a_callsign_and_falls_back_to_either()
        {
            Assert.Equal("VIPER 2-1 (DeckJockey)", SquadDesignations.Label("VIPER 2-1", "DeckJockey"));
            Assert.Equal("DeckJockey", SquadDesignations.Label("", "DeckJockey"));
            Assert.Equal("VIPER 2-1", SquadDesignations.Label("VIPER 2-1", ""));
            Assert.Equal("", SquadDesignations.Label("", ""));
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
    }
}
